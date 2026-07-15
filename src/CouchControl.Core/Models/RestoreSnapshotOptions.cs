namespace CouchControl.Core.Models;

public sealed record RestoreSnapshotOptions(
    bool DryRun = false,
    bool ForceFallback = false);
