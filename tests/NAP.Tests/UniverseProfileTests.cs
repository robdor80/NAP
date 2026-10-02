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

    [Fact]
    public void MinimalConstructor_RemainsCompatibleWithEmptyConfiguration()
    {
        var profile = new UniverseProfile(new UniverseId("example"), "Example");
        Assert.Empty(profile.ClassificationDimensions);
        Assert.Empty(profile.AssetRules);
        Assert.False(profile.TryGetAssetRule("type", "profile", out var rule));
        Assert.Null(rule);
    }

    [Fact]
    public void Configuration_IsSnapshotReadOnlyAndLookupIsOrdinal()
    {
        string[] dimensions = ["dimension"];
        var rule = new UniverseAssetRule("type", "profile", dimensions, dimensions);
        UniverseAssetRule[] rules = [rule];
        var profile = new UniverseProfile(new UniverseId("example"), "Example", dimensions, rules);
        dimensions[0] = "changed";
        rules[0] = new UniverseAssetRule("other", "profile", [], []);
        Assert.Equal(new[] { "dimension" }, profile.ClassificationDimensions);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)profile.ClassificationDimensions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<UniverseAssetRule>)profile.AssetRules).Clear());
        Assert.True(profile.TryGetAssetRule("type", "profile", out var found));
        Assert.Same(rule, found);
        Assert.False(profile.TryGetAssetRule("Type", "profile", out _));
        Assert.False(profile.TryGetAssetRule("type", "Profile", out _));
        Assert.False(profile.TryGetAssetRule("type", "other", out _));
        Assert.Throws<ArgumentNullException>(() => profile.TryGetAssetRule(null!, "profile", out _));
    }

    [Fact]
    public void InvalidProfileConfiguration_IsRejected()
    {
        var id = new UniverseId("example");
        var rule = new UniverseAssetRule("type", "profile", ["dimension"], []);
        Assert.Throws<ArgumentNullException>(() => new UniverseProfile(id, "Example", null!, []));
        Assert.Throws<ArgumentNullException>(() => new UniverseProfile(id, "Example", [], null!));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", ["dimension", "dimension"], []));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", ["Invalid"], []));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", [null!], []));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", [], [null!]));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", ["dimension"], [rule, rule]));
        Assert.Throws<ArgumentException>(() => new UniverseProfile(id, "Example", [], [rule]));
    }

    [Fact]
    public void DifferentCombinations_CanUseSameTypeOrProductionProfile()
    {
        UniverseAssetRule[] rules = [new("type", "profile", [], []), new("type", "other", [], []), new("other", "profile", [], [])];
        var profile = new UniverseProfile(new UniverseId("example"), "Example", [], rules);
        Assert.Equal(3, profile.AssetRules.Count);
        Assert.True(profile.TryGetAssetRule("type", "other", out var found));
        Assert.Same(rules[1], found);
    }
}
