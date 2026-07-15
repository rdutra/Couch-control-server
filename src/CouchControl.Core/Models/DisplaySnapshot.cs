namespace CouchControl.Core.Models;

public sealed record DisplaySnapshot
{
    public DisplaySnapshot(
        string SnapshotId,
        DateTimeOffset CapturedAtUtc,
        IReadOnlyList<DisplayDevice>? Displays = null,
        IReadOnlyList<DisplayPathSnapshot>? Paths = null)
    {
        this.SnapshotId = string.IsNullOrWhiteSpace(SnapshotId)
            ? throw new ArgumentException("Snapshot ID cannot be null, empty, or whitespace.", nameof(SnapshotId))
            : SnapshotId.Trim();
        this.CapturedAtUtc = CapturedAtUtc;
        this.Displays = Displays ?? Array.Empty<DisplayDevice>();
        this.Paths = Paths ?? Array.Empty<DisplayPathSnapshot>();
    }

    public string SnapshotId { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public IReadOnlyList<DisplayDevice> Displays { get; }

    public IReadOnlyList<DisplayPathSnapshot> Paths { get; }

    public OperationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(SnapshotId))
        {
            return OperationResult.Failure("Snapshot ID is required.", "snapshot_id_required");
        }

        if (CapturedAtUtc == default)
        {
            return OperationResult.Failure("Capture timestamp is required.", "snapshot_timestamp_required");
        }

        if (Paths.Count == 0)
        {
            return OperationResult.Failure("Snapshot must contain at least one display path.", "snapshot_paths_required");
        }

        if (!Paths.Any(static path => path.IsActive))
        {
            return OperationResult.Failure("Snapshot must contain at least one active display path.", "snapshot_active_path_required");
        }

        foreach (var path in Paths)
        {
            var validation = path.Validate();
            if (!validation.Succeeded)
            {
                return validation;
            }
        }

        return OperationResult.Success("Display snapshot is valid.");
    }
}
