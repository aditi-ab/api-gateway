namespace ApiGateway;

public sealed class GatewayOptions
{
    public string InstanceId { get; set; } = System.Environment.MachineName;
    public string DisplayName { get; set; } = System.Environment.MachineName;
    public string Environment { get; set; } = "development";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    public string LastKnownGoodPath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "state", "last-known-good.json");

    public string DataProtectionKeysPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "state", "keys");
}