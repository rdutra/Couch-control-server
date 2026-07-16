using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IDisplayManager
{
    Task<IReadOnlyList<DisplayDevice>> GetDisplaysAsync(
        CancellationToken cancellationToken = default);

    Task<DisplaySnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> ActivateOnlyAsync(
        DisplayIdentifier display,
        DisplayMode? preferredMode,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RestoreSnapshotAsync(
        DisplaySnapshot snapshot,
        RestoreSnapshotOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult> PrepareForCouchModeAsync(
        AgentConfiguration configuration,
        CancellationToken cancellationToken = default);
}
