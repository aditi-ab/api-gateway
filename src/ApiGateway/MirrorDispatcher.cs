using System.Net;
using System.Threading.Channels;
using ApiGateway.Domain;

namespace ApiGateway;

public sealed record MirrorWork(
    Uri Target,
    string Method,
    IReadOnlyDictionary<string, string[]> Headers,
    byte[]? Body,
    TimeSpan Timeout);

public sealed class MirrorDispatcher(ILogger<MirrorDispatcher> logger) : BackgroundService
{
    private readonly HttpClient client = new(new SocketsHttpHandler
        { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None });

    private readonly Channel<MirrorWork> queue = Channel.CreateBounded<MirrorWork>(new BoundedChannelOptions(256)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryEnqueue(MirrorWork work)
    {
        var accepted = queue.Writer.TryWrite(work);
        (accepted ? GatewayTelemetry.MirrorEnqueued : GatewayTelemetry.MirrorDropped).Add(1);
        return accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in queue.Reader.ReadAllAsync(stoppingToken))
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(work.Method), work.Target);
                if (work.Body is not null) request.Content = new ByteArrayContent(work.Body);
                foreach (var header in work.Headers)
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(work.Timeout);
                using var response =
                    await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                GatewayTelemetry.MirrorFailures.Add(1);
                logger.LogDebug(ex, "A mirrored request failed.");
            }
    }
}

public static class MirrorRequestFactory
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
        { "Authorization", "Cookie", "X-Api-Key", "X-Management-Api-Key" };

    public static async Task<MirrorWork?> CreateAsync(HttpContext context, GatewayConfigDocument document,
        MirrorPolicy policy)
    {
        if (Random.Shared.NextDouble() * 100 >= policy.Percentage) return null;
        var destination = document.Clusters
            .FirstOrDefault(x => x.Id.Equals(policy.ClusterId, StringComparison.OrdinalIgnoreCase))?.Destinations.Values
            .FirstOrDefault();
        if (destination is null) return null;
        var allowed = policy.AllowedMethods ?? ["GET", "HEAD"];
        if (!allowed.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)) return null;
        byte[]? body = null;
        if (context.Request.ContentLength > 0)
        {
            if (policy.MaximumBufferedBodyBytes <= 0 ||
                context.Request.ContentLength > policy.MaximumBufferedBodyBytes) return null;
            context.Request.EnableBuffering();
            using var memory = new MemoryStream();
            await context.Request.Body.CopyToAsync(memory, context.RequestAborted);
            context.Request.Body.Position = 0;
            body = memory.ToArray();
        }

        var target = new Uri(new Uri(destination.Address), context.Request.Path + context.Request.QueryString);
        var removed = Sensitive.Concat(policy.RemoveHeaders ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headers = context.Request.Headers
            .Where(x => !removed.Contains(x.Key) && !x.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value.Where(v => v is not null).Select(v => v!).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        return new MirrorWork(target, context.Request.Method, headers, body, policy.Timeout ?? TimeSpan.FromSeconds(5));
    }
}