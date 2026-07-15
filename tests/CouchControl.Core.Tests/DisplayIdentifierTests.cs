using CouchControl.Core.Models;

namespace CouchControl.Core.Tests;

public sealed class DisplayIdentifierTests
{
    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        var first = new DisplayIdentifier(@"\\.\DISPLAY1");
        var second = new DisplayIdentifier(@"\\.\display1");

        Assert.Equal(first, second);
        Assert.True(first.Matches(second));
    }

    [Fact]
    public void Matches_HandlesWhitespaceAndNullSafely()
    {
        var identifier = new DisplayIdentifier(" TV-Display ");

        Assert.True(identifier.Matches("tv-display"));
        Assert.False(identifier.Matches((string?)null));
        Assert.False(identifier.Matches(" "));
    }
}
