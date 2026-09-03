using ApiGateway.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiGateway;

public sealed class ApiKeyUsageWriter(
    ApiKeyUsageQueue queue,
    IServiceScopeFactory scopes,
    ILogger<ApiKeyUsageWriter> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DateTimeOffset> written = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var usage in queue.ReadAllAsync(stoppingToken))
        {
            if (usage.IsManagementKey || (written.TryGetValue(usage.KeyId, out var previous) &&
                                          usage.UsedAtUtc - previous < TimeSpan.FromMinutes(1))) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
                await db.ConsumerApiKeys.Where(x => x.Id == usage.KeyId)
                    .ExecuteUpdateAsync(x => x.SetProperty(k => k.LastUsedAtUtc, usage.UsedAtUtc), stoppingToken);
                written[usage.KeyId] = usage.UsedAtUtc;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Unable to update consumer API key usage time.");
            }
        }
    }
}