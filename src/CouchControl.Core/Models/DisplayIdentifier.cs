namespace CouchControl.Core.Models;

public sealed record DisplayIdentifier
{
    public DisplayIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Display identifier cannot be null, empty, or whitespace.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public bool Matches(DisplayIdentifier? other) =>
        other is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Value);

    public bool Matches(string? other) =>
        !string.IsNullOrWhiteSpace(other) &&
        StringComparer.OrdinalIgnoreCase.Equals(Value, other.Trim());

    public override string ToString() => Value;

    public bool Equals(DisplayIdentifier? other) => Matches(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}
