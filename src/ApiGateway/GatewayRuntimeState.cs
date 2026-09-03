namespace ApiGateway;

public sealed class GatewayRuntimeState
{
    private readonly object gate = new();
    public Guid? RevisionId { get; private set; }
    public string? ContentHash { get; private set; }
    public DateTimeOffset? ActivatedAtUtc { get; private set; }
    public string State { get; private set; } = "unavailable";
    public string? LastErrorCode { get; private set; }

    public void Activated(Guid revisionId, string hash)
    {
        lock (gate)
        {
            RevisionId = revisionId;
            ContentHash = hash;
            ActivatedAtUtc = DateTimeOffset.UtcNow;
            State = "active";
            LastErrorCode = null;
        }
    }

    public void Failed(string code)
    {
        lock (gate)
        {
            LastErrorCode = code;
            if (RevisionId is null) State = "unavailable";
        }
    }
}