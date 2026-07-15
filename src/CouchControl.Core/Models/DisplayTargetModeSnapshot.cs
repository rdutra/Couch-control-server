namespace CouchControl.Core.Models;

public sealed record DisplayTargetModeSnapshot(
    DisplayRefreshRate RefreshRate,
    uint ActiveWidth,
    uint ActiveHeight,
    string ScanLineOrdering);
