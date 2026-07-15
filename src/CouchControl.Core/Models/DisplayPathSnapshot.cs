namespace CouchControl.Core.Models;

public sealed record DisplayPathSnapshot(
    DisplayIdentifier Identifier,
    string AdapterLuid,
    uint SourceId,
    uint TargetId,
    bool IsActive,
    bool IsPrimary,
    DisplayPoint? SourceDesktopPosition,
    uint? Width,
    uint? Height,
    string? PixelFormat,
    DisplayRefreshRate RefreshRate,
    string Rotation,
    string Scaling,
    string OutputTechnology,
    DisplaySourceModeSnapshot? SourceMode,
    DisplayTargetModeSnapshot? TargetMode)
{
    public OperationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(AdapterLuid))
        {
            return OperationResult.Failure(
                $"Display path '{Identifier}' is missing an adapter LUID.",
                "snapshot_adapter_luid_required");
        }

        if (RefreshRate is null)
        {
            return OperationResult.Failure(
                $"Display path '{Identifier}' is missing refresh-rate information.",
                "snapshot_refresh_rate_required");
        }

        if (IsPrimary && SourceDesktopPosition is not null && !SourceDesktopPosition.IsOrigin)
        {
            return OperationResult.Failure(
                $"Primary display path '{Identifier}' must have a desktop position of (0, 0).",
                "snapshot_primary_position_invalid");
        }

        if (IsActive && SourceMode is null)
        {
            return OperationResult.Failure(
                $"Active display path '{Identifier}' is missing source mode information.",
                "snapshot_source_mode_required");
        }

        if (IsActive && TargetMode is null)
        {
            return OperationResult.Failure(
                $"Active display path '{Identifier}' is missing target mode information.",
                "snapshot_target_mode_required");
        }

        if (IsActive && (!Width.HasValue || !Height.HasValue || Width.Value == 0 || Height.Value == 0))
        {
            return OperationResult.Failure(
                $"Active display path '{Identifier}' is missing valid dimensions.",
                "snapshot_dimensions_required");
        }

        return OperationResult.Success();
    }
}
