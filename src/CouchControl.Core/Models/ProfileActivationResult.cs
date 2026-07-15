namespace CouchControl.Core.Models;

public sealed record ProfileActivationResult(
    AgentMode Mode,
    ProfileActivationStatus Status,
    OperationResult DisplayResult,
    OperationResult? SteamResult = null,
    DisplaySnapshot? Snapshot = null,
    Guid? OperationId = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null)
{
    public bool Succeeded => Status is not ProfileActivationStatus.Failure;
}
