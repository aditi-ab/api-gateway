using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace ApiGateway;

public sealed class RouteRequestTracker
{
    private readonly ConcurrentDictionary<string, long> active = new(StringComparer.OrdinalIgnoreCase);

    public RouteRequestTracker()
    {
        GatewayTelemetry.RegisterActiveRequests(Observe);
    }

    public IDisposable Enter(string routeId)
    {
        active.AddOrUpdate(routeId, 1, static (_, value) => value + 1);
        return new Lease(this, routeId);
    }

    public IReadOnlyDictionary<string, long> Snapshot()
    {
        return active.Where(x => x.Value > 0).ToDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<Measurement<long>> Observe()
    {
        return active.Where(x => x.Value > 0)
            .Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>("route", x.Key)));
    }

    private void Exit(string routeId)
    {
        active.AddOrUpdate(routeId, 0, static (_, value) => Math.Max(0, value - 1));
    }

    private sealed class Lease(RouteRequestTracker owner, string routeId) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Exit(routeId);
        }
    }
}