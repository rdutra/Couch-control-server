namespace CouchControl.Core.Models;

public sealed record DisplayDevice(
    DisplayIdentifier Identifier,
    string FriendlyName,
    bool IsActive,
    bool IsPrimary,
    DisplayMode? CurrentMode,
    string? DevicePath = null,
    string? AdapterLuid = null,
    uint? SourceId = null,
    uint? TargetId = null,
    string? OutputTechnology = null);
