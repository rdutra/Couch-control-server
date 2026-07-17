using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IAudioDeviceService
{
    Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetDefaultPlaybackDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken = default);
}
