using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseIdTests
{
    [Theory]
    [InlineData("nimroel")]
    [InlineData("star_trek")]
    [InlineData("star_wars")]
    [InlineData("future_universe_2")]
    public void ValidMachineIdentifier_IsPreserved(string value)
    {
        var id = new UniverseId(value);
        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nimroel")]
    [InlineData("Star Trek")]
    [InlineData("star-trek")]
    [InlineData("_star_trek")]
    [InlineData("star_trek_")]
    [InlineData("star__trek")]
    [InlineData("stár_trek")]
    [InlineData(" nimroel ")]
    [InlineData("nimroel\n")]
    public void InvalidIdentifier_IsRejectedWithoutCorrection(string? value) =>
        Assert.Throws<ArgumentException>(() => new UniverseId(value!));

    [Fact]
    public void NamingV1LengthLimit_IsReused()
    {
        var atLimit = new string('a', AssetNamingRules.MaxMachineIdentifierLength);
        Assert.Equal(atLimit, new UniverseId(atLimit).Value);
        Assert.Throws<ArgumentException>(() => new UniverseId(atLimit + "a"));
    }

    [Fact]
    public void EqualityAndHashing_AreByCanonicalValue()
    {
        var first = new UniverseId("nimroel");
        var same = new UniverseId("nimroel");
        Assert.NotSame(first, same);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, new UniverseId("star_trek"));
        Assert.Single(new HashSet<UniverseId> { first, same });
    }
}
