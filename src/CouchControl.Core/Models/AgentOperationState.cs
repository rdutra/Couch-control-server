namespace CouchControl.Core.Models;

public enum AgentOperationState
{
    Idle = 0,
    Validating = 1,
    Running = 2,
    Succeeded = 3,
    PartiallySucceeded = 4,
    Failed = 5,
    Canceled = 6
}
