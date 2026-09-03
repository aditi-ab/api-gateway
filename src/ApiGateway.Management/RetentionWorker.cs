using ApiGateway.Persistence;
using Microsoft.Extensions.Options;

namespace ApiGateway.Management;

public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int ActivationDays { get; set; } = 30;
    public int AuditDays { get; set; } = 365;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}

public sealed class RetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<RetentionMaintenanceService>();
                var now = DateTimeOffset.UtcNow;
                await service.RunAsync(owner, now.AddDays(-Math.Max(1, options.Value.ActivationDays)),
                    now.AddDays(-Math.Max(1, options.Value.AuditDays)), "system:retention",
                    Guid.NewGuid().ToString("N"), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Retention maintenance failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}