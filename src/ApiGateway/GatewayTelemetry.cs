using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ApiGateway;

public static class GatewayTelemetry
{
    public const string MeterName = "ApiGateway";
    private static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource Activities = new(MeterName);

    public static readonly Counter<long> ActivationSuccesses =
        Meter.CreateCounter<long>("apigateway.activation.successes");

    public static readonly Counter<long> ActivationFailures =
        Meter.CreateCounter<long>("apigateway.activation.failures");

    public static readonly Counter<long> AuthorizationRejections =
        Meter.CreateCounter<long>("apigateway.authorization.rejections");

    public static readonly Counter<long> RateLimitRejections =
        Meter.CreateCounter<long>("apigateway.ratelimit.rejections");

    public static readonly Counter<long> MirrorEnqueued = Meter.CreateCounter<long>("apigateway.mirror.enqueued");
    public static readonly Counter<long> MirrorDropped = Meter.CreateCounter<long>("apigateway.mirror.dropped");
    public static readonly Counter<long> MirrorFailures = Meter.CreateCounter<long>("apigateway.mirror.failures");
    public static readonly Counter<long> ProxyRequests = Meter.CreateCounter<long>("apigateway.proxy.requests");

    public static readonly Histogram<double> ProxyDuration =
        Meter.CreateHistogram<double>("apigateway.proxy.duration", "ms");

    public static readonly Counter<long> UpstreamAttempts = Meter.CreateCounter<long>("apigateway.upstream.attempts");

    public static readonly Histogram<double> UpstreamDuration =
        Meter.CreateHistogram<double>("apigateway.upstream.duration", "ms");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("apigateway.upstream.retries");
    public static readonly Counter<long> CircuitOpen = Meter.CreateCounter<long>("apigateway.circuit.open");
    public static readonly Counter<long> Timeouts = Meter.CreateCounter<long>("apigateway.upstream.timeouts");
    public static readonly Counter<long> PollFailures = Meter.CreateCounter<long>("apigateway.poll.failures");
    public static readonly Counter<long> AccessRejections = Meter.CreateCounter<long>("apigateway.access.rejections");

    public static readonly Counter<long> RequestSizeRejections =
        Meter.CreateCounter<long>("apigateway.request_size.rejections");

    public static readonly Counter<long> RequestValidationRejections =
        Meter.CreateCounter<long>("apigateway.request_validation.rejections");

    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("apigateway.cache.hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("apigateway.cache.misses");

    public static readonly Histogram<double> ActivationLag =
        Meter.CreateHistogram<double>("apigateway.activation.lag", "ms");

    public static readonly Counter<long> OperationalStateResponses =
        Meter.CreateCounter<long>("apigateway.route.operational_state.responses");

    public static void RegisterActiveRequests(Func<IEnumerable<Measurement<long>>> observe)
    {
        Meter.CreateObservableGauge("apigateway.route.active_requests", observe);
    }
}