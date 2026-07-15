namespace CouchControl.Core.Models;

public sealed record AgentOperationStatus(
    Guid? OperationId,
    AgentMode? CurrentMode,
    ProfileOperationType CurrentOperation,
    ProfileOperationStep CurrentStep,
    AgentOperationState State,
    OperationResult? LastOperationResult,
    string? LastError,
    DateTimeOffset? OperationStartedAtUtc,
    DateTimeOffset? OperationCompletedAtUtc)
{
    public static AgentOperationStatus Idle(AgentMode? currentMode = null) =>
        new(
            null,
            currentMode,
            ProfileOperationType.None,
            ProfileOperationStep.None,
            AgentOperationState.Idle,
            null,
            null,
            null,
            null);
}
