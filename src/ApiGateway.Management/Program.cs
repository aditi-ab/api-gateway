using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Aditify.Identity;
using ApiGateway.Application;
using ApiGateway.Domain;
using ApiGateway.Management;
using ApiGateway.Persistence;
using ApiGateway.Persistence.Sqlite;
using ApiGateway.Persistence.SqlServer;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Yarp.ReverseProxy.LoadBalancing;
using Path = System.IO.Path;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("Gateway") ?? "Data Source=apigateway.db";
var entraAuthority = builder.Configuration["Authentication:Entra:Authority"];
var entraAudience = builder.Configuration["Authentication:Entra:Audience"];
var entraClientId = builder.Configuration["Authentication:Entra:ClientId"];
var entraScope = builder.Configuration["Authentication:Entra:Scope"];
var entraConfigured = !string.IsNullOrWhiteSpace(entraAuthority) && !string.IsNullOrWhiteSpace(entraAudience) &&
                      !string.IsNullOrWhiteSpace(entraClientId) && !string.IsNullOrWhiteSpace(entraScope);
var entraConnectionState = new EntraConnectionState(new EntraConnectionSnapshot(entraConfigured,
    entraAuthority ?? string.Empty, entraAudience ?? string.Empty, entraClientId ?? string.Empty,
    entraScope ?? string.Empty, Guid.Empty));
builder.Services.AddSingleton(entraConnectionState);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(address));
});
if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddGatewaySqlServer(connection);
else if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddGatewaySqlite(connection);
else throw new InvalidOperationException("DatabaseProvider must be Sqlite or SqlServer.");
builder.Services.AddSingleton<GatewayConfigValidator>();
builder.Services.AddSingleton<IConfigurationPublicationValidator, YarpPublicationValidator>();
builder.Services.AddScoped<IConfigurationPublicationValidator, InboundCertificatePublicationValidator>();
builder.Services.AddScoped<GatewayLifecycleService>();
builder.Services.AddScoped<GatewayConfigurationService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddSingleton<ApiKeyUsageQueue>();
builder.Services.AddHostedService<ApiKeyUsageWriter>();
builder.Services.AddScoped<OpenApiImportService>();
builder.Services.AddScoped<RetentionMaintenanceService>();
builder.Services.AddOptions<RetentionOptions>().Bind(builder.Configuration.GetSection("Retention"));
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddScoped<LocalAdministratorService>();
builder.Services.AddScoped<EntraConnectionService>();
var certificateKeyPath = builder.Configuration["CertificateProtection:KeysPath"] ??
                         Path.Combine(AppContext.BaseDirectory, "state", "certificate-keys");
builder.Services.AddSingleton(new CertificateMaterialProtector(certificateKeyPath));
builder.Services.AddScoped<InboundCertificateService>();
builder.Services.AddScoped<InboundSecuritySettingsService>();
builder.Services.AddOptions<AcmeOptions>().Bind(builder.Configuration.GetSection("Acme"))
    .Validate(x => x.WorkerInterval > TimeSpan.Zero && x.MaxConcurrentOrders > 0 &&
                   x.RenewalInfoInterval > TimeSpan.Zero &&
                   x.HttpChallengePropagationDelay >= TimeSpan.Zero && x.DnsPropagationTimeout > TimeSpan.Zero &&
                   x.DnsPropagationPollInterval > TimeSpan.Zero &&
                   x.OrderFinalizationTimeout > TimeSpan.Zero && x.OrderFinalizationPollInterval > TimeSpan.Zero &&
                   x.InProgressTimeout > x.LeaseDuration && x.LeaseDuration > x.LeaseRenewalInterval &&
                   x.LeaseRenewalInterval > TimeSpan.Zero,
        "ACME concurrency and intervals must be positive, and the in-progress and lease durations must exceed their renewal intervals.")
    .ValidateOnStart();
builder.Services.AddHttpClient(nameof(AcmeDnsProviders));
builder.Services.AddScoped<AcmeAccountService>();
builder.Services.AddScoped<DnsProviderProfileService>();
builder.Services.AddScoped<ManagedCertificateService>();
builder.Services.AddScoped<AcmeWorkerLeaseService>();
builder.Services.AddScoped<AcmeOrderProcessor>();
builder.Services.AddSingleton<IAcmeDnsTxtLookup, AcmeDnsTxtLookup>();
builder.Services.AddSingleton<IDnsChallengeProvider, CloudflareDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, DigitalOceanDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, Route53DnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, AzureDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, GoogleCloudDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, LoopiaDnsProvider>();
builder.Services.AddSingleton<IDnsChallengeProvider, SimplyDnsProvider>();
builder.Services.AddSingleton<DnsChallengeProviderFactory>();
builder.Services.AddHostedService<AcmeCertificateWorker>();
builder.Services.AddScoped<IPasswordHasher<LocalAdministrator>, PasswordHasher<LocalAdministrator>>();
builder.Services.AddHttpContextAccessor();
var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"] ??
                         Path.Combine(AppContext.BaseDirectory, "data-protection-keys");
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection().SetApplicationName("ApiGateway.Management")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
builder.Services.AddAditifyIdentity(options =>
{
    options.BasePath = "/admin";
    options.CookieScheme = ManagementAuth.CookieScheme;
    options.RegisterCookieScheme = false;
    options.AdministratorPolicy = ManagementAuth.AdministratorPolicy;
    options.AdministratorRole = ManagementAuth.AdministratorRole;
    options.SecurityStampClaim = "apigateway.security-stamp";
    options.MustChangePasswordClaim = ManagementAuth.MustChangePasswordClaim;
});
builder.Services.AddScoped<IAdminIdentityStore, GatewayAdminIdentityStore>();
builder.Services.AddSingleton<IProductRoleCatalog, GatewayRoleCatalog>();
builder.Services.AddScoped<IAdminIdentityAuditSink, GatewayIdentityAuditSink>();
builder.Services.AddAntiforgery(x =>
{
    x.HeaderName = "X-CSRF-TOKEN";
    x.Cookie.Name = appCookieName(builder.Environment);
    x.Cookie.HttpOnly = true;
    x.Cookie.SameSite = SameSiteMode.Strict;
    x.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddAuthentication(ManagementAuth.PolicyScheme)
    .AddPolicyScheme(ManagementAuth.PolicyScheme, ManagementAuth.PolicyScheme,
        x => x.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("X-Management-Api-Key")
            ? ManagementAuth.ApiKeyScheme
            : entraConnectionState.Current.Configured && context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : ManagementAuth.CookieScheme)
    .AddCookie(ManagementAuth.CookieScheme, x =>
    {
        x.Cookie.Name = appCookieName(builder.Environment, true);
        x.Cookie.HttpOnly = true;
        x.Cookie.SameSite = SameSiteMode.Strict;
        x.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        x.Events.OnRedirectToLogin = c =>
        {
            c.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        x.Events.OnRedirectToAccessDenied = c =>
        {
            c.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
        x.Events.OnValidatePrincipal = async c =>
        {
            var local = c.HttpContext.RequestServices.GetRequiredService<LocalAdministratorService>();
            if (!await local.ValidatePrincipalAsync(c.Principal!, c.HttpContext.RequestAborted)) c.RejectPrincipal();
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ManagementApiKeyHandler>(ManagementAuth.ApiKeyScheme, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, x =>
    {
        if (entraConfigured)
        {
            x.Authority = entraAuthority;
            x.Audience = entraAudience;
        }

        x.RequireHttpsMetadata = true;
        x.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username", RoleClaimType = "roles", ValidateIssuer = true,
            ValidateAudience = true, ValidateLifetime = true
        };
    });
builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, DynamicEntraJwtOptions>();
builder.Services.AddAuthorization(x =>
{
    x.AddPolicy(ManagementAuth.ReaderPolicy,
        p => p.RequireAssertion(c => c.User.IsInRole("Reader") || c.User.IsInRole("ConfigurationEditor") ||
                                     c.User.IsInRole("Publisher") ||
                                     c.User.IsInRole(ManagementAuth.AdministratorRole) || c.User.Claims.Any(x =>
                                         x.Type == "apigateway.scope" && x.Value is "config:read" or "config:manage"
                                             or "config:write"
                                             or "config:publish" or "instances:read" or "credentials:read"
                                             or "credentials:write" or "audit:read" or "system:admin")));
    x.AddPolicy(ManagementAuth.WritePolicy,
        p => p.RequireAssertion(c => c.User.IsInRole("ConfigurationEditor") || c.User.IsInRole("Publisher") ||
                                     c.User.IsInRole(ManagementAuth.AdministratorRole) ||
                                     c.User.HasClaim("apigateway.scope", "config:write") ||
                                     c.User.HasClaim("apigateway.scope", "config:manage") ||
                                     c.User.HasClaim("apigateway.scope", "config:publish") ||
                                     c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.PublishPolicy,
        p => p.RequireAssertion(c =>
            c.User.IsInRole("Publisher") || c.User.IsInRole(ManagementAuth.AdministratorRole) ||
            c.User.HasClaim("apigateway.scope", "config:publish") ||
            c.User.HasClaim("apigateway.scope", "config:manage") ||
            c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.ManageConfigurationPolicy,
        p => p.RequireAssertion(c =>
            c.User.IsInRole("Publisher") || c.User.IsInRole(ManagementAuth.AdministratorRole) ||
            c.User.HasClaim("apigateway.scope", "config:manage") ||
            c.User.HasClaim("apigateway.scope", "config:publish") ||
            c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.AdministratorPolicy,
        p => p.RequireAssertion(c =>
            c.User.IsInRole(ManagementAuth.AdministratorRole) || c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.InstancesReadPolicy,
        p => p.RequireAssertion(c => c.User.IsInRole("Reader") || c.User.IsInRole("ConfigurationEditor") ||
                                     c.User.IsInRole("Publisher") ||
                                     c.User.IsInRole(ManagementAuth.AdministratorRole) ||
                                     c.User.HasClaim("apigateway.scope", "instances:read") ||
                                     c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.CredentialsReadPolicy,
        p => p.RequireAssertion(c =>
            c.User.IsInRole(ManagementAuth.AdministratorRole) ||
            c.User.HasClaim("apigateway.scope", "credentials:read") ||
            c.User.HasClaim("apigateway.scope", "credentials:write") ||
            c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.CredentialsWritePolicy,
        p => p.RequireAssertion(c =>
            c.User.IsInRole(ManagementAuth.AdministratorRole) ||
            c.User.HasClaim("apigateway.scope", "credentials:write") ||
            c.User.HasClaim("apigateway.scope", "system:admin")));
    x.AddPolicy(ManagementAuth.AuditReadPolicy,
        p => p.RequireAssertion(c => c.User.IsInRole("Reader") || c.User.IsInRole("ConfigurationEditor") ||
                                     c.User.IsInRole("Publisher") ||
                                     c.User.IsInRole(ManagementAuth.AdministratorRole) ||
                                     c.User.HasClaim("apigateway.scope", "audit:read") ||
                                     c.User.HasClaim("apigateway.scope", "system:admin")));
});
builder.Services.AddRateLimiter(x => x.AddPolicy("authentication",
    context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
var graphQl = builder.Services.AddGraphQLServer().AddType(new DurationType()).AddAuthorization().AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddErrorFilter<GatewayErrorFilter>().AddCostAnalyzer().ModifyCostOptions(x =>
    {
        x.EnforceCostLimits = true;
        x.MaxFieldCost = 5000;
        x.MaxTypeCost = 5000;
    }).ModifyRequestOptions(x => x.ExecutionTimeout = TimeSpan.FromSeconds(30))
    .ModifyServerOptions(x => x.Tool.Enable = builder.Environment.IsDevelopment());
builder.Services.AddReverseProxy();
builder.Services.AddSingleton<ILoadBalancingPolicy, WeightedPoolValidationPolicy>();
if (!builder.Environment.IsDevelopment()) graphQl.DisableIntrospection();
var useOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry().WithTracing(x =>
{
    x.AddSource(ManagementTelemetry.MeterName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (useOtlp) x.AddOtlpExporter();
}).WithMetrics(x =>
{
    x.AddMeter(ManagementTelemetry.MeterName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (useOtlp) x.AddOtlpExporter();
});
var app = builder.Build();

if (args is ["schema", "export", var schemaPath])
{
    var executor = await app.Services.GetRequiredService<IRequestExecutorProvider>().GetExecutorAsync();
    var output = Path.GetFullPath(schemaPath, app.Environment.ContentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, executor.Schema.ToString());
    return;
}

if (args is ["database", "migrate"])
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.MigrateAsync();
    return;
}

if (args is ["database", "status"])
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database;
    var pending = (await database.GetPendingMigrationsAsync()).ToArray();
    Console.WriteLine(pending.Length == 0
        ? "Database schema is current."
        : $"Pending migrations:{Environment.NewLine}{string.Join(Environment.NewLine, pending)}");
    Environment.ExitCode = pending.Length == 0 ? 0 : 2;
    return;
}

if (args is ["database", "seed-development"])
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Development seed data can only be created in the Development environment.");
    await using var scope = app.Services.CreateAsyncScope();
    var lifecycle = scope.ServiceProvider.GetRequiredService<GatewayLifecycleService>();
    var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
    foreach (var (slug, name) in new[]
                 { ("development", "Development"), ("staging", "Staging"), ("production", "Production") })
        if (!await db.Environments.AnyAsync(x => x.Slug == slug))
            await lifecycle.CreateEnvironmentAsync(slug, name, null, "system:development-seed",
                Guid.NewGuid().ToString("N"), CancellationToken.None);
    Console.WriteLine("Development environments are available.");
    return;
}

var applyMigrationsOnStartup = builder.Configuration.GetValue<bool>("Gateway:ApplyMigrationsOnStartup");
if (applyMigrationsOnStartup)
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "Gateway:ApplyMigrationsOnStartup is permitted only in the Development environment.");
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Database.MigrateAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<EntraConnectionService>().LoadAsync(CancellationToken.None);
}

app.UseForwardedHeaders();
DocumentationContentSecurityPolicy.Use(app);
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var forced = context.User.Identity?.AuthenticationType == ManagementAuth.CookieScheme &&
                 context.User.HasClaim(ManagementAuth.MustChangePasswordClaim, "true");
    var allowed = context.Request.Path.StartsWithSegments("/admin/auth/status") ||
                  context.Request.Path.StartsWithSegments("/admin/auth/change-password") ||
                  context.Request.Path.StartsWithSegments("/admin/auth/logout");
    if (forced && !allowed)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
app.UseRateLimiter();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    if (context.Request.Path != "/graphql")
    {
        await next(context);
        return;
    }

    if (context.Request.ContentLength > 1_048_576)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var operation = "anonymous";
    if (HttpMethods.IsPost(context.Request.Method))
    {
        context.Request.EnableBuffering();
        try
        {
            using var document =
                await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.TryGetProperty("operationName", out var name) &&
                name.ValueKind == JsonValueKind.String) operation = name.GetString() ?? operation;
        }
        catch (JsonException)
        {
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }

    var started = Stopwatch.GetTimestamp();
    using var activity = ManagementTelemetry.Activities.StartActivity("graphql.operation");
    activity?.SetTag("graphql.operation.name", operation);
    try
    {
        await next(context);
    }
    finally
    {
        ManagementTelemetry.GraphQlDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("outcome", context.Response.StatusCode < 400 ? "success" : "failure"));
    }
});
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/graphql" && HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Cookies.ContainsKey(appCookieName(app.Environment, true)))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next(context);
});
app.MapGet("/healthz", () => Results.Ok(new { status = "live" }));
app.MapGet("/readyz",
    async (GatewayDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "database-unavailable" }, statusCode: 503));
app.MapGet("/admin/config.json",
    (EntraConnectionState entraState) =>
    {
        var entra = entraState.Current;
        return Results.Ok(new
        {
            authenticationModes = entra.Configured ? new[] { "local", "entra" } : new[] { "local" },
            graphqlEndpoint = "/graphql", documentationUrl = "/docs/",
            entra = entra.Configured
                ? new { authority = entra.Authority, clientId = entra.ClientId, scope = entra.Scope }
                : null
        });
    }).AllowAnonymous();
var auth = app.MapGroup("/admin/auth").AllowAnonymous();
auth.MapGet("/status",
    async (HttpContext context, LocalAdministratorService local, IAdminIdentityStore identities, IAntiforgery antiforgery, CancellationToken ct) =>
    {
        var state = await local.StatusAsync(context.User, ct);
        return Results.Ok(new
        {
            state.BootstrapRequired, authenticated = state.Username is not null, state.Username,
            mustChangePassword = context.User.HasClaim(ManagementAuth.MustChangePasswordClaim, "true"),
            providers = (await identities.ListProvidersAsync(ct)).Where(x => x.Enabled).Select(x => new
                { x.Id, x.DisplayName, type = x.Type.ToString().ToLowerInvariant() }),
            antiforgeryToken = antiforgery.GetAndStoreTokens(context).RequestToken
        });
    });
auth.MapPost("/bootstrap",
    async (LoginRequest request, HttpContext context, LocalAdministratorService local, CancellationToken ct) =>
    {
        var admin = await local.BootstrapAsync(request.Username, request.Password, ct);
        await context.SignInAsync(ManagementAuth.CookieScheme, LocalAdministratorService.Principal(admin));
        ManagementTelemetry.Authentication.Add(1, new KeyValuePair<string, object?>("method", "bootstrap"),
            new KeyValuePair<string, object?>("outcome", "success"));
        return Results.NoContent();
    }).RequireRateLimiting("authentication").WithMetadata(new RequireAntiforgeryTokenAttribute());
auth.MapPost("/login",
    async (LoginRequest request, HttpContext context, LocalAdministratorService local, AdminIdentityService identity,
        IExternalIdentityService external, CancellationToken ct) =>
    {
        if (!string.IsNullOrWhiteSpace(request.ProviderId))
        {
            try
            {
                var externalUser = await identity.PasswordSignInAsync(request.Username, request.Password,
                    request.ProviderId, external, ct);
                await identity.SignInAsync(context, externalUser);
                return Results.NoContent();
            }
            catch (IdentityOperationException exception)
            {
                return Results.Json(new { code = exception.Code, message = exception.Message },
                    statusCode: exception.StatusCode);
            }
        }
        var admin = await local.ValidateAsync(request.Username, request.Password, ct);
        if (admin is null)
        {
            ManagementTelemetry.Authentication.Add(1, new KeyValuePair<string, object?>("method", "local"),
                new KeyValuePair<string, object?>("outcome", "failure"));
            return Results.Unauthorized();
        }

        await context.SignInAsync(ManagementAuth.CookieScheme, LocalAdministratorService.Principal(admin));
        ManagementTelemetry.Authentication.Add(1, new KeyValuePair<string, object?>("method", "local"),
            new KeyValuePair<string, object?>("outcome", "success"));
        return Results.NoContent();
    }).RequireRateLimiting("authentication").WithMetadata(new RequireAntiforgeryTokenAttribute());
auth.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(ManagementAuth.CookieScheme);
    return Results.NoContent();
}).WithMetadata(new RequireAntiforgeryTokenAttribute());
auth.MapPost("/change-password",
    async (ChangePasswordRequest request, HttpContext context, LocalAdministratorService local, CancellationToken ct) =>
    {
        var id = Guid.Parse(context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await local.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword,
            context.User.Identity!.Name!, ct);
        var user = (await local.ListAsync(ct)).Single(x => x.Id == id);
        await context.SignInAsync(ManagementAuth.CookieScheme, LocalAdministratorService.Principal(user));
        return Results.NoContent();
    }).RequireAuthorization().WithMetadata(new RequireAntiforgeryTokenAttribute());
app.MapAditifyIdentityExternalAuthentication();
app.MapAditifyIdentityManagement();
var certificateApi = app.MapGroup("/api/admin/certificates")
    .RequireAuthorization(ManagementAuth.AdministratorPolicy);
certificateApi.MapGet("/", async (InboundCertificateService certificates, CancellationToken ct) =>
    Results.Ok((await certificates.ListAsync(ct)).Select(InboundCertificateInfo.From)));
certificateApi.MapPost("/", async (HttpRequest request, HttpContext context,
    InboundCertificateService certificates, IAntiforgery antiforgery, CancellationToken ct) =>
{
    if (context.User.Identity?.AuthenticationType == ManagementAuth.CookieScheme)
        await antiforgery.ValidateRequestAsync(context);
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? throw new ArgumentException("A PKCS#12 file is required.");
    if (file.Length is 0 or > 5_242_880) throw new ArgumentException("Certificate files may contain at most 5 MiB.");
    await using var buffer = new MemoryStream((int)file.Length);
    await file.CopyToAsync(buffer, ct);
    var certificate = await certificates.UploadAsync(form["name"].ToString(), buffer.ToArray(),
        form["password"].FirstOrDefault(), context.User.Identity!.Name!, ct);
    return Results.Created($"/api/admin/certificates/{certificate.Id}", InboundCertificateInfo.From(certificate));
}).WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 5_505_024 });
certificateApi.MapPut("/{id:guid}", async (Guid id, HttpRequest request, HttpContext context,
    InboundCertificateService certificates, IAntiforgery antiforgery, CancellationToken ct) =>
{
    if (context.User.Identity?.AuthenticationType == ManagementAuth.CookieScheme)
        await antiforgery.ValidateRequestAsync(context);
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? throw new ArgumentException("A PKCS#12 file is required.");
    if (file.Length is 0 or > 5_242_880) throw new ArgumentException("Certificate files may contain at most 5 MiB.");
    if (!Guid.TryParse(form["expectedVersion"].FirstOrDefault(), out var expectedVersion))
        throw new ArgumentException("A valid expectedVersion is required.");
    await using var buffer = new MemoryStream((int)file.Length);
    await file.CopyToAsync(buffer, ct);
    return Results.Ok(InboundCertificateInfo.From(await certificates.ReplaceAsync(id, expectedVersion,
        buffer.ToArray(), form["password"].FirstOrDefault(), context.User.Identity!.Name!, ct)));
}).WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 5_505_024 });
certificateApi.MapDelete("/{id:guid}", async (Guid id, HttpContext context,
    InboundCertificateService certificates, IAntiforgery antiforgery, CancellationToken ct) =>
{
    if (context.User.Identity?.AuthenticationType == ManagementAuth.CookieScheme)
        await antiforgery.ValidateRequestAsync(context);
    await certificates.DeleteAsync(id, context.User.Identity!.Name!, ct);
    return Results.NoContent();
});
app.MapGraphQL().RequireAuthorization();
app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");
app.Run();

static string appCookieName(IHostEnvironment environment, bool authentication = false)
{
    return environment.IsDevelopment()
        ? $"ApiGateway.{(authentication ? "Auth" : "Antiforgery")}"
        : $"__Host-ApiGateway.{(authentication ? "Auth" : "Antiforgery")}";
}

public partial class Program;
