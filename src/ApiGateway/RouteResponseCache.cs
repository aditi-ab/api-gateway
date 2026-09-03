using ApiGateway.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace ApiGateway;

public sealed class RouteResponseCache(IMemoryCache cache)
{
    public async Task<bool> TryServeAsync(HttpContext context, string routeId, GatewayRoute route,
        string configurationVersion)
    {
        if (!Eligible(context, route, out var policy)) return false;
        var key = Key(context, routeId, configurationVersion, policy!);
        if (!cache.TryGetValue<CachedResponse>(key, out var cached) || cached is null)
        {
            GatewayTelemetry.CacheMisses.Add(1);
            return false;
        }

        context.Response.StatusCode = cached.StatusCode;
        foreach (var (name, values) in cached.Headers) context.Response.Headers[name] = values;
        context.Response.ContentLength = cached.Body.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
        GatewayTelemetry.CacheHits.Add(1);
        return true;
    }

    public Capture BeginCapture(HttpContext context, string routeId, GatewayRoute route, string configurationVersion)
    {
        if (!Eligible(context, route, out var policy)) return Capture.Disabled;
        var original = context.Response.Body;
        var stream = new CaptureStream(original, policy!.MaximumBodyBytes);
        context.Response.Body = stream;
        return new Capture(context, cache, original, stream, Key(context, routeId, configurationVersion, policy),
            policy.TimeToLive);
    }

    private static bool Eligible(HttpContext context, GatewayRoute route, out ResponseCachePolicy? policy)
    {
        policy = route.ResponseCache;
        return policy is not null && route.AuthorizationPolicy == "Anonymous" &&
               (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
               !context.Request.Headers.ContainsKey("Authorization") && !context.Request.Headers.ContainsKey("Cookie");
    }

    private static string Key(HttpContext context, string routeId, string version, ResponseCachePolicy policy)
    {
        var varying = string.Join('\n', (policy.VaryByHeaders ?? []).Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => $"{name.ToLowerInvariant()}:{context.Request.Headers[name]}"));
        return
            $"{version}\n{routeId}\n{context.Request.Method}\n{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}\n{varying}";
    }

    private sealed record CachedResponse(int StatusCode, Dictionary<string, string[]> Headers, byte[] Body);

    public sealed class Capture : IAsyncDisposable
    {
        private readonly IMemoryCache? cache;
        private readonly HttpContext? context;
        private readonly string? key;
        private readonly TimeSpan lifetime;
        private readonly Stream? original;
        private readonly CaptureStream? stream;
        private bool completed;

        private Capture()
        {
        }

        internal Capture(HttpContext context, IMemoryCache cache, Stream original, CaptureStream stream, string key,
            TimeSpan lifetime)
        {
            this.context = context;
            this.cache = cache;
            this.original = original;
            this.stream = stream;
            this.key = key;
            this.lifetime = lifetime;
        }

        public static Capture Disabled { get; } = new();

        public async ValueTask DisposeAsync()
        {
            await CompleteAsync(CancellationToken.None);
        }

        public Task CompleteAsync(CancellationToken ct)
        {
            if (completed || context is null || stream is null) return Task.CompletedTask;
            completed = true;
            context.Response.Body = original!;
            var cacheControl = context.Response.Headers.CacheControl.ToString();
            if (!stream.Overflowed && context.Response.StatusCode == StatusCodes.Status200OK &&
                !context.Response.Headers.ContainsKey("Set-Cookie") &&
                !cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase) &&
                !cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase))
            {
                var headers = context.Response.Headers.Where(x => !x.Key.Equals("Transfer-Encoding",
                    StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key,
                    x => x.Value.Select(value => value ?? string.Empty).ToArray(), StringComparer.OrdinalIgnoreCase);
                cache!.Set(key!, new CachedResponse(context.Response.StatusCode, headers, stream.Captured), lifetime);
            }

            return Task.CompletedTask;
        }
    }

    internal sealed class CaptureStream(Stream inner, long maximum) : Stream
    {
        private readonly MemoryStream captured = new();
        public bool Overflowed { get; private set; }
        public byte[] Captured => captured.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            CaptureBytes(buffer.AsSpan(offset, count));
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            CaptureBytes(buffer.Span);
        }

        private void CaptureBytes(ReadOnlySpan<byte> bytes)
        {
            if (Overflowed) return;
            if (captured.Length + bytes.Length > maximum)
            {
                Overflowed = true;
                captured.SetLength(0);
                return;
            }

            captured.Write(bytes);
        }
    }
}