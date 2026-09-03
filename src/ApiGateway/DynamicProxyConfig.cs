using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace ApiGateway;

public sealed class DynamicProxyConfigProvider : IProxyConfigProvider
{
    private volatile Snapshot current = new([], []);

    public IProxyConfig GetConfig()
    {
        return current;
    }

    public void Set(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        var next = new Snapshot(routes, clusters);
        var previous = Interlocked.Exchange(ref current, next);
        previous.SignalChange();
    }

    private sealed class Snapshot : IProxyConfig
    {
        private readonly CancellationTokenSource source = new();

        public Snapshot(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(source.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChange()
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}