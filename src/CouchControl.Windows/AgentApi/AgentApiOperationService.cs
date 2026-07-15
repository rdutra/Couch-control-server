using System.Collections.ObjectModel;
using System.Linq;
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

    private readonly IProfileOrchestrator orchestrator;
    private readonly ILogger<AgentApiOperationService> logger;
    private readonly object gate = new();
    private readonly Dictionary<Guid, AgentOperationRecord> operations = [];
    private readonly LinkedList<Guid> operationOrder = [];
    private Guid? activeOperationId;

    public AgentApiOperationService(
        IProfileOrchestrator orchestrator,
        ILogger<AgentApiOperationService> logger)
    {
        this.orchestrator = orchestrator;
        this.logger = logger;
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
            return new ReadOnlyCollection<AgentOperationRecord>(
                operationOrder
                    .Select(id => operations[id])
                    .Reverse()
                    .ToList());
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
            if (activeOperationId.HasValue)
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
                DateTimeOffset.UtcNow,
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
                    CompletedAtUtc = result.CompletedAtUtc ?? DateTimeOffset.UtcNow,
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
                    CompletedAtUtc = DateTimeOffset.UtcNow,
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
