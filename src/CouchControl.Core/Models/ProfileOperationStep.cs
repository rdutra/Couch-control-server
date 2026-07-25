namespace CouchControl.Core.Models;

public enum ProfileOperationStep
{
    None = 0,
    Validating = 1,
    LoadingConfiguration = 2,
    MatchingDisplay = 3,
    CapturingSnapshot = 4,
    PersistingSnapshot = 5,
    ActivatingDisplay = 6,
    LaunchingLauncher = 7,
    LoadingSnapshot = 8,
    RestoringDesktop = 9,
    Completed = 10
}
