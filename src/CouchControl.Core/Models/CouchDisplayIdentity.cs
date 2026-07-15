namespace CouchControl.Core.Models;

public sealed record CouchDisplayIdentity(
    string DevicePath,
    string FriendlyName,
    string Manufacturer,
    string ProductCode,
    string SerialOrInstance,
    string AdapterLuid,
    uint TargetId)
{
    public string StableId { get; init; } = DisplayStableId.FromDevicePath(DevicePath);
}
