using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetPackageFileRuleTests
{
    [Theory]
    [InlineData("master")]
    [InlineData("prompt")]
    [InlineData("visual_identity")]
    [InlineData("audio_master")]
    [InlineData("future_role")]
    public void RolesAreOpenMachineIdentifiers(string role)
    {
        var rule = new AssetPackageFileRule(role, "", ".wav", false, "future_validator");
        Assert.Equal(role, rule.Role);
        Assert.False(rule.Required);
        Assert.Equal("future_validator", rule.ContentValidator);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Master")]
    [InlineData("two words")]
    [InlineData("bad__role")]
    [InlineData("role_")]
    [InlineData("role\n")]
    public void InvalidRoleAndNonNullValidatorAreRejectedWithoutNormalization(string? invalid)
    {
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule(invalid!, "", ".png", true));
        if (invalid is not null)
            Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("master", "", ".png", true, invalid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("_prompt")]
    [InlineData("_visual_identity")]
    [InlineData("_metadata2")]
    public void ValidSuffixResolvesExactly(string suffix)
    {
        var rule = new AssetPackageFileRule("role", suffix, ".md", true);
        Assert.Equal(suffix, rule.Suffix);
        Assert.Equal("portrait_example_001" + suffix + ".md", rule.ResolveFileName("portrait_example_001"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("prompt")]
    [InlineData("__prompt")]
    [InlineData("_Prompt")]
    [InlineData("_prompt_")]
    [InlineData("_two words")]
    [InlineData("../x")]
    [InlineData("_a/b")]
    [InlineData("_a\\b")]
    [InlineData("_a:b")]
    [InlineData("_a.b")]
    [InlineData("_prompt\n")]
    public void InvalidSuffixIsRejected(string? suffix) =>
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("role", suffix!, ".md", true));

    [Theory]
    [InlineData(".png")]
    [InlineData(".md")]
    [InlineData(".json")]
    [InlineData(".webp")]
    [InlineData(".wav")]
    [InlineData(".ogg")]
    [InlineData(".tar.gz")]
    [InlineData(".7z")]
    public void SafeSimpleAndCompoundExtensionsAreAccepted(string extension) =>
        Assert.Equal(extension, new AssetPackageFileRule("role", "", extension, true).Extension);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("png")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".PNG")]
    [InlineData(".bad extension")]
    [InlineData("../png")]
    [InlineData(".a/b")]
    [InlineData(".a\\b")]
    [InlineData(".a:b")]
    [InlineData(".a..b")]
    [InlineData(".png.")]
    [InlineData(".png\n")]
    [InlineData(".é")]
    [InlineData(".a*")]
    [InlineData(".a?")]
    [InlineData(".a|")]
    [InlineData(".a<")]
    [InlineData(".a>")]
    [InlineData(".a\"")]
    [InlineData(".a\0")]
    public void UnsafeExtensionsAreRejected(string? extension) =>
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("role", "", extension!, true));

    [Fact]
    public void IdentifierLengthBoundariesAndNullValidatorArePreserved()
    {
        var maximum = new string('a', 64);
        var rule = new AssetPackageFileRule(maximum, "_" + maximum, ".png", true, maximum);
        Assert.Equal(maximum, rule.Role);
        Assert.Equal("_" + maximum, rule.Suffix);
        Assert.Equal(maximum, rule.ContentValidator);
        Assert.Null(new AssetPackageFileRule("master", "", ".png", true, null).ContentValidator);
        Assert.Equal("png_master", new AssetPackageFileRule("master", "", ".png", true, "png_master").ContentValidator);
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule(maximum + "a", "", ".png", true));
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("role", "_" + maximum + "a", ".png", true));
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("role", "", ".png", true, maximum + "a"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" portrait_example_001")]
    [InlineData("Portrait_example_001")]
    [InlineData("portrait_example_000")]
    [InlineData("portrait_001")]
    [InlineData("../portrait_example_001")]
    [InlineData("portrait_example_001\n")]
    public void InvalidAssetIdCannotResolve(string? assetId) =>
        Assert.Throws<ArgumentException>(() => new AssetPackageFileRule("master", "", ".png", true).ResolveFileName(assetId!));
}
