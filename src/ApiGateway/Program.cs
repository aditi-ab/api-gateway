using System.Net;
using System.Text;
using ApiGateway;
using ApiGateway.Application;
using ApiGateway.Persistence;
using ApiGateway.Persistence.Sqlite;
using ApiGateway.Persistence.SqlServer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;

var builder = WebApplication.CreateBuilder(args);
var certificateKeyPath = builder.Configuration["CertificateProtection:KeysPath"] ??
                         Path.Combine(AppContext.BaseDirectory, "state", "certificate-keys");
var certificateProtector = new CertificateMaterialProtector(certificateKeyPath);
var certificateCachePath = builder.Configuration["Gateway:InboundTls:CertificateCachePath"] ??
                           Path.Combine(AppContext.BaseDirectory, "state", "inbound-certificates.json");
var inboundCertificates = new InboundCertificateRegistry(certificateProtector, certificateCachePath);
var httpPort = builder.Configuration.GetValue("Gateway:InboundTls:HttpPort", 8080);
var httpsPort = builder.Configuration.GetValue("Gateway:InboundTls:HttpsPort", 8443);
if (httpPort == httpsPort)
    throw new InvalidOperationException("Gateway inbound HTTP and HTTPS ports must be different.");
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.ListenAnyIP(httpPort, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(httpsPort, listen =>
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps(https => https.ServerCertificateSelector = (_, host) => inboundCertificates.Select(host));
    });
});
builder.Services.AddOptions<GatewayOptions>().Bind(builder.Configuration.GetSection("Gateway")).ValidateOnStart();
builder.Services.AddOptions<UpstreamTlsOptions>().Bind(builder.Configuration.GetSection("Gateway:UpstreamTls"));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("Gateway") ?? "Data Source=apigateway.db";
if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) builder.Services.AddGatewaySqlServer(connection);
else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) builder.Services.AddGatewaySqlite(connection);
else throw new InvalidOperationException("DatabaseProvider must be Sqlite or SqlServer.");
builder.Services.AddSingleton<GatewayConfigValidator>();
builder.Services.AddSingleton(certificateProtector);
builder.Services.AddSingleton(inboundCertificates);
builder.Services.AddSingleton<InboundSecurityStore>();
builder.Services.AddSingleton<AcmeHttpChallengeStore>();
builder.Services.AddHostedService<InboundCertificateRefreshService>();
builder.Services.AddSingleton<DynamicProxyConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());
builder.Services.AddSingleton<GatewayRuntimeState>();
builder.Services.AddSingleton<GatewayPolicyStore>();
builder.Services.AddSingleton<RouteRequestTracker>();
builder.Services.AddSingleton<ConsumerCredentialStore>();
builder.Services.AddSingleton<ApiKeyUsageQueue>();
builder.Services.AddHostedService<ApiKeyUsageWriter>();
builder.Services.AddSingleton<DynamicRateLimiter>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RouteResponseCache>();
builder.Services.AddSingleton<DynamicJwtValidator>();
builder.Services.AddSingleton<SecretReferenceValidator>();
builder.Services.AddSingleton<MirrorDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MirrorDispatcher>());
builder.Services.AddSingleton<ILoadBalancingPolicy, WeightedPoolPolicy>();
builder.Services.AddSingleton<IForwarderHttpClientFactory, ResilientHttpClientFactory>();
var dataProtectionPath = builder.Configuration["Gateway:DataProtectionKeysPath"] ??
                         Path.Combine(AppContext.BaseDirectory, "state", "keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection().SetApplicationName("ApiGateway")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services.AddSingleton<LastKnownGoodStore>();
builder.Services.AddHostedService<ConfigurationPoller>();
builder.Services.AddReverseProxy();
var useOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry().WithTracing(x =>
{
    x.AddSource(GatewayTelemetry.MeterName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (useOtlp) x.AddOtlpExporter();
}).WithMetrics(x =>
{
    x.AddMeter(GatewayTelemetry.MeterName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (useOtlp) x.AddOtlpExporter();
});
var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (!context.Request.IsHttps) return Task.CompletedTask;
        var policy = context.RequestServices.GetRequiredService<InboundSecurityStore>().Current;
        var host = context.Request.Host.Host;
        if (!policy.Enabled || !policy.Hosts.Any(pattern => InboundCertificateService.Covers(pattern, host)))
            return Task.CompletedTask;
        var value = $"max-age={policy.MaxAgeSeconds}";
        if (policy.IncludeSubDomains) value += "; includeSubDomains";
        if (policy.Preload) value += "; preload";
        context.Response.Headers.StrictTransportSecurity = value;
        return Task.CompletedTask;
    });
    await next(context);
});
app.MapGet("/healthz", () => Results.Ok(new { status = "live" }));
app.MapGet("/.well-known/acme-challenge/{token}", (string token, HttpRequest request,
        AcmeHttpChallengeStore challenges) =>
    challenges.TryGet(request.Host.Host, token, out var keyAuthorization)
        ? Results.Text(keyAuthorization, "text/plain", Encoding.UTF8)
        : Results.NotFound());
app.MapGet("/readyz",
    (GatewayRuntimeState state, IOptions<GatewayOptions> options) => state.RevisionId is null
        ? Results.Json(
            new { state = state.State, instanceId = options.Value.InstanceId, errorCode = state.LastErrorCode },
            statusCode: 503)
        : Results.Ok(new
        {
            state = state.State, instanceId = options.Value.InstanceId, revisionId = state.RevisionId,
            contentHash = state.ContentHash, activatedAtUtc = state.ActivatedAtUtc
        }));
app.MapReverseProxy(proxy => proxy.UseMiddleware<ProxyPolicyMiddleware>());
app.Run();

public partial class Program;