using System.Threading.Channels;

namespace ApiGateway.Persistence;

public sealed record ApiKeyUsage(Guid KeyId, bool IsManagementKey, DateTimeOffset UsedAtUtc);

public sealed class ApiKeyUsageQueue
{
    private readonly Channel<ApiKeyUsage> channel = Channel.CreateBounded<ApiKeyUsage>(new BoundedChannelOptions(2048)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });

    public bool TryRecord(Guid keyId, bool isManagementKey)
    {
        return channel.Writer.TryWrite(new ApiKeyUsage(keyId, isManagementKey, DateTimeOffset.UtcNow));
    }

    public IAsyncEnumerable<ApiKeyUsage> ReadAllAsync(CancellationToken ct)
    {
        return channel.Reader.ReadAllAsync(ct);
    }
}