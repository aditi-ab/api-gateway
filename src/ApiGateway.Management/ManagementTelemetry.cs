using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ApiGateway.Management;

public static class ManagementTelemetry
{
    public const string MeterName = "ApiGateway.Management";
    private static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource Activities = new(MeterName);

    public static readonly Counter<long> Authentication =
        Meter.CreateCounter<long>("apigateway.management.authentication");

    public static readonly Histogram<double> GraphQlDuration =
        Meter.CreateHistogram<double>("apigateway.management.graphql.duration", "ms");
}