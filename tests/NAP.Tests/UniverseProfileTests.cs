using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileTests
{
    [Fact]
    public void DisplayName_IsHumanTextAndNotTheIdentity()
    {
        var id = new UniverseId("star_trek");
        const string displayName = "Star Trek / universo futuro";
        var profile = new UniverseProfile(id, displayName);
        Assert.Same(id, profile.Id);
        Assert.Equal(displayName, profile.DisplayName);
        Assert.False(AssetNamingRules.IsValidMachineIdentifier(profile.DisplayName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingDisplayName_IsRejected(string? displayName) =>
        Assert.ThrowsAny<ArgumentException>(() => new UniverseProfile(new UniverseId("nimroel"), displayName!));

    [Fact]
    public void NullId_IsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new UniverseProfile(null!, "Name"));
}
