using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.AgentApi;

public interface IAgentApiOperationService
{
    event EventHandler<AgentOperationRecord>? OperationCompleted;

    bool IsOperationRunning { get; }

    bool TryStartActivateCouchMode(out Guid operationId);

    bool TryStartActivateDesktopMode(out Guid operationId);

    bool TryGetOperation(Guid operationId, out AgentOperationRecord? operation);

    IReadOnlyList<AgentOperationRecord> GetRecentOperations();
}

public sealed class AgentApiOperationService : IAgentApiOperationService
{
    private const int MaxOperations = 50;
    private static readonly TimeSpan DisplayOperationSettleWindow = TimeSpan.FromSeconds(15);

    private readonly IProfileOrchestrator orchestrator;
    private readonly ILogger<AgentApiOperationService> logger;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();
    private readonly Dictionary<Guid, AgentOperationRecord> operations = [];
    private readonly LinkedList<Guid> operationOrder = [];
    private Guid? activeOperationId;
    private DateTimeOffset rejectNewOperationsUntilUtc;

    public AgentApiOperationService(
        IProfileOrchestrator orchestrator,
        ILogger<AgentApiOperationService> logger,
        TimeProvider timeProvider)
    {
        this.orchestrator = orchestrator;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    public event EventHandler<AgentOperationRecord>? OperationCompleted;

    public bool IsOperationRunning
    {
        get
        {
            lock (gate)
            {
                return activeOperationId.HasValue;
            }
        }
    }

    public bool TryStartActivateCouchMode(out Guid operationId) =>
        TryStartOperation(AgentMode.Couch, ProfileOperationType.ActivateCouchMode, orchestrator.ActivateCouchModeAsync, out operationId);

    public bool TryStartActivateDesktopMode(out Guid operationId) =>
        TryStartOperation(AgentMode.Desktop, ProfileOperationType.ActivateDesktopMode, orchestrator.ActivateDesktopModeAsync, out operationId);

    public bool TryGetOperation(Guid operationId, out AgentOperationRecord? operation)
    {
        lock (gate)
        {
            return operations.TryGetValue(operationId, out operation);
        }
    }

    public IReadOnlyList<AgentOperationRecord> GetRecentOperations()
    {
        lock (gate)
        {
            var records = new AgentOperationRecord[operationOrder.Count];
            var node = operationOrder.Last;
            for (var index = 0; node is not null; index++, node = node.Previous)
            {
                records[index] = operations[node.Value];
            }

            return records;
        }
    }

    private bool TryStartOperation(
        AgentMode mode,
        ProfileOperationType operationType,
        Func<CancellationToken, Task<ProfileActivationResult>> executor,
        out Guid operationId)
    {
        AgentOperationRecord record;

        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            if (activeOperationId.HasValue || now < rejectNewOperationsUntilUtc)
            {
                operationId = Guid.Empty;
                return false;
            }

            operationId = Guid.NewGuid();
            activeOperationId = operationId;
            record = new AgentOperationRecord(
                operationId,
                mode,
                operationType,
                AgentApiOperationState.Running,
                now,
                null,
                null,
                null,
                null,
                null);

            AddOrUpdate(record);
        }

        var startedOperationId = operationId;

        _ = Task.Run(async () =>
        {
            AgentOperationRecord completed;

            try
            {
                var result = await executor(CancellationToken.None);
                completed = record with
                {
                    State = MapState(result.Status),
                    StartedAtUtc = result.StartedAtUtc,
                    CompletedAtUtc = result.CompletedAtUtc ?? timeProvider.GetUtcNow(),
                    Message = result.SteamResult?.Message ?? result.DisplayResult.Message,
                    ErrorCode = result.SteamResult?.ErrorCode ?? result.DisplayResult.ErrorCode,
                    Result = result
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Agent operation {OperationType} failed unexpectedly.", operationType);
                completed = record with
                {
                    State = AgentApiOperationState.Failed,
                    CompletedAtUtc = timeProvider.GetUtcNow(),
                    Message = ex.Message,
                    ErrorCode = "agent_operation_exception"
                };
            }

            lock (gate)
            {
                AddOrUpdate(completed);
                if (activeOperationId == startedOperationId)
                {
                    activeOperationId = null;
                    rejectNewOperationsUntilUtc = timeProvider.GetUtcNow().Add(DisplayOperationSettleWindow);
                }
            }

            OperationCompleted?.Invoke(this, completed);
        });

        return true;
    }

    private void AddOrUpdate(AgentOperationRecord record)
    {
        bool existed = operations.ContainsKey(record.OperationId);
        operations[record.OperationId] = record;
        if (!existed)
        {
            operationOrder.AddLast(record.OperationId);
        }

        while (operationOrder.Count > MaxOperations)
        {
            var oldest = operationOrder.First!.Value;
            operationOrder.RemoveFirst();
            operations.Remove(oldest);
        }
    }

    private static AgentApiOperationState MapState(ProfileActivationStatus status) =>
        status switch
        {
            ProfileActivationStatus.Success => AgentApiOperationState.Succeeded,
            ProfileActivationStatus.PartialSuccess => AgentApiOperationState.PartiallySucceeded,
            _ => AgentApiOperationState.Failed
        };
}

public sealed record AgentOperationRecord(
    Guid OperationId,
    AgentMode Mode,
    ProfileOperationType OperationType,
    AgentApiOperationState State,
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Message,
    string? ErrorCode,
    ProfileActivationResult? Result);

public enum AgentApiOperationState
{
    Running = 0,
    Succeeded = 1,
    PartiallySucceeded = 2,
    Failed = 3
}
