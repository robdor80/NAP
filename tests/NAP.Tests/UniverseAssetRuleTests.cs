using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseAssetRuleTests
{
    [Fact]
    public void HistoricalConstructorHasEmptyReadOnlyPackageFiles()
    {
        var rule = new UniverseAssetRule("type", "profile", [], []);
        Assert.Empty(rule.PackageFiles);
        Assert.Throws<NotSupportedException>(() => ((IList<AssetPackageFileRule>)rule.PackageFiles).Add(File("master", "", ".png")));
    }

    [Fact]
    public void PackageFilesAreOrderedDefensiveAndReadOnly()
    {
        var first = File("master", "", ".png");
        AssetPackageFileRule[] files = [first, File("prompt", "_prompt", ".md")];
        var rule = new UniverseAssetRule("type", "profile", ["dim"], ["dim"], files);
        files[0] = File("changed", "_changed", ".json");
        Assert.Same(first, rule.PackageFiles[0]);
        Assert.Equal(new[] { "master", "prompt" }, rule.PackageFiles.Select(file => file.Role));
        Assert.Throws<NotSupportedException>(() => ((IList<AssetPackageFileRule>)rule.PackageFiles)[0] = files[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<AssetPackageFileRule>)rule.PackageFiles).Clear());
    }

    [Fact]
    public void NullFilesDuplicateRolesAndFilenameCollisionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UniverseAssetRule("type", "profile", [], [], null!));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", [], [], [null!]));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", [], [],
            [File("master", "", ".png"), File("master", "_different", ".wav")]));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", [], [],
            [File("first", "_info", ".md"), File("second", "_info", ".md")]));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", [], [],
            [File("metadata", "_manifest", ".json")])); // Universal envelope filename is reserved.
    }

    [Fact]
    public void SameSuffixDifferentExtensionAndDifferentSuffixSameExtensionAreAllowed()
    {
        var rule = new UniverseAssetRule("type", "profile", [], [],
            [File("first", "_metadata", ".json"), File("second", "_metadata", ".md"), File("third", "_info", ".md")]);
        Assert.Equal(3, rule.PackageFiles.Count);
    }

    private static AssetPackageFileRule File(string role, string suffix, string extension) => new(role, suffix, extension, true);

    [Fact]
    public void Collections_AreOrderedDefensiveAndReadOnly()
    {
        string[] allowed = ["required_dim", "optional_dim"];
        string[] required = ["required_dim"];
        var rule = new UniverseAssetRule("future_type", "future_profile", allowed, required);
        allowed[0] = "changed";
        required[0] = "changed";
        Assert.Equal(new[] { "required_dim", "optional_dim" }, rule.AllowedClassification);
        Assert.Equal(new[] { "required_dim" }, rule.RequiredClassification);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)rule.AllowedClassification)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)rule.RequiredClassification).Clear());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("two words")]
    [InlineData("bad__dimension")]
    public void AllIdentifiers_UseNamingWithoutCorrection(string? invalid)
    {
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule(invalid!, "profile", [], []));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", invalid!, [], []));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", [invalid!], []));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", ["valid"], [invalid!]));
    }

    [Fact]
    public void NullCollectionsDuplicatesAndRequiredOutsideAllowed_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new UniverseAssetRule("type", "profile", null!, []));
        Assert.Throws<ArgumentNullException>(() => new UniverseAssetRule("type", "profile", [], null!));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", ["dim", "dim"], []));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", ["dim"], ["dim", "dim"]));
        Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile", ["allowed"], ["other"]));
    }

    [Fact]
    public void Classification_ReportsBothFailuresAndDoesNotCheckValues()
    {
        var rule = new UniverseAssetRule("type", "profile", ["first", "second", "optional"], ["first", "second"]);
        var input = new Dictionary<string, string> { ["first"] = "unregistered_value", ["other"] = "anything" };
        var result = rule.ValidateClassification(input);
        input.Clear();
        Assert.False(result.IsValid);
        Assert.Equal(new[] { "second" }, result.MissingRequired);
        Assert.Equal(new[] { "other" }, result.NotAllowed);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.MissingRequired).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.NotAllowed).Clear());
        Assert.True(rule.ValidateClassification(new Dictionary<string, string>
        { ["first"] = "unregistered_value", ["second"] = "human value", ["optional"] = "anything" }).IsValid);
    }

    [Fact]
    public void Classification_IsOrdinalEvenForCaseInsensitiveDictionary()
    {
        var rule = new UniverseAssetRule("type", "profile", ["dimension"], ["dimension"]);
        var result = rule.ValidateClassification(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Dimension"] = "value" });
        Assert.Equal(new[] { "dimension" }, result.MissingRequired);
        Assert.Equal(new[] { "Dimension" }, result.NotAllowed);
        Assert.True(new UniverseAssetRule("type", "profile", [], []).ValidateClassification(new Dictionary<string, string>()).IsValid);
        Assert.Throws<ArgumentNullException>(() => rule.ValidateClassification(null!));
    }
}
