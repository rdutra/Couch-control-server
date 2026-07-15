namespace CouchControl.Core.Models;

public sealed record DisplayPoint(int X, int Y)
{
    public bool IsOrigin => X == 0 && Y == 0;

    public override string ToString() => $"({X}, {Y})";
}
