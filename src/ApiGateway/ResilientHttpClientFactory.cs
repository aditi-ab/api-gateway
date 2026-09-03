using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using ApiGateway.Domain;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace ApiGateway;

public sealed class ResilientHttpClientFactory(
    ILogger<ForwarderHttpClientFactory> logger,
    IOptions<UpstreamTlsOptions> tls) : ForwarderHttpClientFactory(logger)
{
    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);
        if (context.NewMetadata?.TryGetValue("ApiGateway.HttpClient", out var httpRaw) == true &&
            JsonSerializer.Deserialize<UpstreamHttpPolicy>(httpRaw, GatewayJson.Options) is { } http)
        {
            handler.AllowAutoRedirect = http.AllowAutoRedirect;
            handler.AutomaticDecompression =
                http.AutomaticDecompression ? DecompressionMethods.All : DecompressionMethods.None;
            if (http.PooledConnectionLifetime is { } lifetime) handler.PooledConnectionLifetime = lifetime;
        }

        if (context.NewMetadata?.TryGetValue("ApiGateway.ClientCertificateRef", out var certificateRef) == true &&
            tls.Value.ClientCertificates.TryGetValue(certificateRef, out var secret))
        {
            handler.SslOptions.ClientCertificates ??= [];
            handler.SslOptions.ClientCertificates.Add(
                X509CertificateLoader.LoadPkcs12FromFile(secret.Path, secret.Password));
        }

        if (context.NewMetadata?.TryGetValue("ApiGateway.TrustBundleRef", out var trustRef) == true &&
            tls.Value.TrustBundles.TryGetValue(trustRef, out var path))
        {
            var authority = X509CertificateLoader.LoadCertificateFromFile(path);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(authority);
                return chain.Build(new X509Certificate2(certificate));
            };
        }
    }

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
    {
        if (context.NewMetadata?.TryGetValue("ApiGateway.Resilience", out var raw) == true &&
            !string.IsNullOrWhiteSpace(raw) &&
            JsonSerializer.Deserialize<ResiliencePolicy>(raw, GatewayJson.Options) is { } policy)
            return new ResilienceHandler(context.ClusterId, policy) { InnerHandler = handler };
        return handler;
    }

    private sealed class ResilienceHandler(string clusterId, ResiliencePolicy policy) : DelegatingHandler
    {
        private readonly ConcurrentQueue<(DateTimeOffset At, bool Failed)> outcomes = new();
        private DateTimeOffset circuitUntil;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (circuitUntil > DateTimeOffset.UtcNow)
            {
                GatewayTelemetry.CircuitOpen.Add(1, new KeyValuePair<string, object?>("cluster", clusterId));
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    { RequestMessage = request, ReasonPhrase = "Gateway circuit is open" };
            }

            var methods = policy.AllowedMethods ?? ["GET", "HEAD", "OPTIONS"];
            var retryEligible = methods.Contains(request.Method.Method, StringComparer.OrdinalIgnoreCase);
            byte[]? bufferedContent = null;
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>>? contentHeaders = null;
            if (retryEligible && request.Content is not null &&
                request.Method.Method is not ("GET" or "HEAD" or "OPTIONS"))
            {
                if (policy.MaximumBufferedRequestBytes <= 0 ||
                    request.Content.Headers.ContentLength > policy.MaximumBufferedRequestBytes)
                {
                    retryEligible = false;
                }
                else
                {
                    bufferedContent = await request.Content.ReadAsByteArrayAsync(ct);
                    if (bufferedContent.LongLength > policy.MaximumBufferedRequestBytes)
                    {
                        retryEligible = false;
                    }
                    else
                    {
                        contentHeaders = request.Content.Headers
                            .Select(x => new KeyValuePair<string, IEnumerable<string>>(x.Key, x.Value)).ToArray();
                        request.Content = Content(bufferedContent, contentHeaders);
                    }
                }
            }

            var attempts = retryEligible ? Math.Max(1, policy.RetryCount + 1) : 1;
            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var attemptStarted = Stopwatch.GetTimestamp();
                using var activity = GatewayTelemetry.Activities.StartActivity("upstream.attempt");
                activity?.SetTag("gateway.cluster", clusterId);
                activity?.SetTag("gateway.attempt", attempt + 1);
                GatewayTelemetry.UpstreamAttempts.Add(1, new KeyValuePair<string, object?>("cluster", clusterId));
                if (attempt > 0)
                    GatewayTelemetry.Retries.Add(1, new KeyValuePair<string, object?>("cluster", clusterId));
                var current = attempt == 0 ? request : Clone(request, bufferedContent, contentHeaders);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (policy.AttemptTimeout is { } attemptTimeout) timeout.CancelAfter(attemptTimeout);
                try
                {
                    response = await base.SendAsync(current, timeout.Token);
                    GatewayTelemetry.UpstreamDuration.Record(Stopwatch.GetElapsedTime(attemptStarted).TotalMilliseconds,
                        new KeyValuePair<string, object?>("cluster", clusterId));
                    if (!ShouldRetry(response) || attempt == attempts - 1)
                    {
                        Record(ShouldRetry(response));
                        return response;
                    }

                    response.Dispose();
                }
                catch (Exception ex) when (policy.RetryTransportFailures &&
                                           ex is HttpRequestException or OperationCanceledException &&
                                           !ct.IsCancellationRequested && attempt < attempts - 1)
                {
                    if (ex is OperationCanceledException)
                        GatewayTelemetry.Timeouts.Add(1, new KeyValuePair<string, object?>("cluster", clusterId));
                }
                catch
                {
                    Record(true);
                    throw;
                }

                var delay = TimeSpan.FromTicks((long)((policy.Backoff ?? TimeSpan.FromMilliseconds(100)).Ticks *
                                                      Math.Pow(2, attempt)));
                if (policy.Jitter) delay += TimeSpan.FromMilliseconds(Random.Shared.Next(25));
                await Task.Delay(delay, ct);
            }

            Record(true);
            return response ?? new HttpResponseMessage(HttpStatusCode.BadGateway) { RequestMessage = request };
        }

        private bool ShouldRetry(HttpResponseMessage response)
        {
            return policy.StatusCodes?.Contains((int)response.StatusCode) == true;
        }

        private void Record(bool failed)
        {
            var now = DateTimeOffset.UtcNow;
            var duration = policy.SamplingDuration ?? TimeSpan.FromSeconds(30);
            outcomes.Enqueue((now, failed));
            while (outcomes.TryPeek(out var item) && now - item.At > duration) outcomes.TryDequeue(out _);
            var snapshot = outcomes.ToArray();
            if (policy.FailureRatio is { } ratio && snapshot.Length >= (policy.MinimumThroughput ?? 10) &&
                snapshot.Count(x => x.Failed) / (double)snapshot.Length >= ratio)
            {
                circuitUntil = now + (policy.BreakDuration ?? TimeSpan.FromSeconds(30));
                while (outcomes.TryDequeue(out _))
                {
                }
            }
        }

        private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
        {
            var clone = new HttpRequestMessage(source.Method, source.RequestUri)
            {
                Version = source.Version, VersionPolicy = source.VersionPolicy,
                Content = body is null ? null : Content(body, contentHeaders)
            };
            foreach (var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in source.Options)
                clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            return clone;
        }

        private static HttpContent Content(byte[] body,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>>? headers)
        {
            var content = new ByteArrayContent(body);
            foreach (var header in headers ?? []) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return content;
        }
    }
}