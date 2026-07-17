namespace CouchControl.Core.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefault);
