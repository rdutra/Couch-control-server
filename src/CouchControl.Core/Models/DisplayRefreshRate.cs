namespace CouchControl.Core.Models;

public sealed record DisplayRefreshRate(uint Numerator, uint Denominator)
{
    public decimal Hertz => Denominator == 0
        ? 0
        : decimal.Round((decimal)Numerator / Denominator, 2);

    public override string ToString() => Denominator == 0
        ? "0 Hz"
        : $"{Hertz:0.##} Hz ({Numerator}/{Denominator})";
}
