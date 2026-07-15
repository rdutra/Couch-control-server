namespace CouchControl.Core.Models;

public sealed record DisplaySourceModeSnapshot(
    uint Width,
    uint Height,
    string PixelFormat,
    DisplayPoint Position)
{
    public bool IsPrimary => Position.IsOrigin;
}
