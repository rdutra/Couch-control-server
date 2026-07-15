namespace CouchControl.Core.Models;

public sealed record DisplayMode(int Width, int Height, decimal RefreshRateHz)
{
    public bool IsValid => Width > 0 && Height > 0 && RefreshRateHz > 0;

    public override string ToString() => $"{Width}x{Height} @ {RefreshRateHz:0.##} Hz";
}
