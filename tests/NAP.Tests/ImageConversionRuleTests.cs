using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ImageConversionRuleTests
{
    [Fact]
    public void KindAndRulePublicContractsAreExactAndImmutable()
    {
        Assert.Equal(new[] { ImageConversionKind.PngToWebp }, Enum.GetValues<ImageConversionKind>());
        var type = typeof(ImageConversionRule);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        var constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(new[] { typeof(ImageConversionKind), typeof(string), typeof(int), typeof(int), typeof(int) },
            constructor.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "kind", "sourceRole", "outputWidth", "outputHeight", "webpQuality" },
            constructor.GetParameters().Select(p => p.Name));
        Assert.Equal(new[] { "Kind", "OutputHeight", "OutputWidth", "SourceRole", "WebpQuality" },
            type.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        var rule = Rule();
        Assert.Equal(ImageConversionKind.PngToWebp, rule.Kind);
        Assert.Equal("source_image", rule.SourceRole);
        Assert.Equal(1200, rule.OutputWidth);
        Assert.Equal(800, rule.OutputHeight);
        Assert.Equal(87, rule.WebpQuality);
        Assert.Equal(rule, Rule());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void InvalidEnumUsesKind(int kind) =>
        Assert.Equal("kind", Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConversionRule((ImageConversionKind)kind, "master", 1, 1, 0)).ParamName);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Master")]
    [InlineData(" master")]
    [InlineData("bad-role")]
    [InlineData("bad__role")]
    [InlineData("master\n")]
    public void InvalidSourceRolesAreRejectedWithoutNormalization(string? role) =>
        Assert.Equal("sourceRole", Assert.ThrowsAny<ArgumentException>(() => new ImageConversionRule(ImageConversionKind.PngToWebp, role!, 1, 1, 0)).ParamName);

    [Theory]
    [InlineData(0, 1, 0, "outputWidth")]
    [InlineData(16384, 1, 0, "outputWidth")]
    [InlineData(1, 0, 0, "outputHeight")]
    [InlineData(1, 16384, 0, "outputHeight")]
    [InlineData(1, 1, -1, "webpQuality")]
    [InlineData(1, 1, 101, "webpQuality")]
    public void NumericLimitsUseExactParameterNames(int width, int height, int quality, string parameter) =>
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConversionRule(ImageConversionKind.PngToWebp, "master", width, height, quality)).ParamName);

    [Theory]
    [InlineData(1, 16383, 0)]
    [InlineData(16383, 1, 100)]
    public void TechnicalBoundaryValuesAreAllowed(int width, int height, int quality)
    {
        var rule = new ImageConversionRule(ImageConversionKind.PngToWebp, "master", width, height, quality);
        Assert.Equal(width, rule.OutputWidth);
        Assert.Equal(height, rule.OutputHeight);
        Assert.Equal(quality, rule.WebpQuality);
    }

    [Fact]
    public void HistoricConstructorsPreserveConversionNullAndNewConstructorRetainsRule()
    {
        var file = FileRule();
        var routing = new AssetRoutingRule([AssetRouteSegment.Literal("images"), AssetRouteSegment.AssetId()]);
        var constructors = typeof(UniverseAssetRule).GetConstructors().OrderBy(c => c.GetParameters().Length).ToArray();
        Assert.Equal(new[] { 4, 5, 6, 7 }, constructors.Select(c => c.GetParameters().Length));
        Assert.Null(new UniverseAssetRule("image_asset", "image_profile", [], []).Conversion);
        Assert.Null(new UniverseAssetRule("image_asset", "image_profile", [], [], [file]).Conversion);
        Assert.Null(new UniverseAssetRule("image_asset", "image_profile", [], [], [file], routing).Conversion);
        var conversion = Rule();
        var assetRule = new UniverseAssetRule("image_asset", "image_profile", [], [], [file], routing, conversion);
        Assert.Same(conversion, assetRule.Conversion);
        Assert.Same(routing, assetRule.Routing);
        Assert.Same(file, Assert.Single(assetRule.PackageFiles));
        Assert.Null(typeof(UniverseAssetRule).GetProperty("Conversion")!.SetMethod);
    }

    [Theory]
    [InlineData("absent", true, ".png", "png_master")]
    [InlineData("source_image", false, ".png", "png_master")]
    [InlineData("source_image", true, ".jpg", "png_master")]
    [InlineData("source_image", true, ".png", null)]
    [InlineData("source_image", true, ".png", "other_validator")]
    public void ConversionSourceMustBeExistingRequiredPngWithExactValidator(string role, bool required, string extension, string? validator)
    {
        Assert.Equal("conversion", Assert.Throws<ArgumentException>(() =>
            new UniverseAssetRule("image_asset", "image_profile", [], [],
                [new AssetPackageFileRule(role, "", extension, required, validator)], null, Rule())).ParamName);
    }

    [Fact]
    public void SourceRoleLookupIsOrdinalAndDoesNotInspectFilesystem()
    {
        var file = FileRule();
        typeof(AssetPackageFileRule).GetField("<Role>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, "SOURCE_IMAGE");
        Assert.Equal("conversion", Assert.Throws<ArgumentException>(() =>
            new UniverseAssetRule("image_asset", "image_profile", [], [], [file], null, Rule())).ParamName);
        var conversion = Rule();
        Assert.Same(conversion, new UniverseAssetRule("image_asset", "image_profile", [], [], [FileRule()], null, conversion).Conversion);
    }

    private static ImageConversionRule Rule() => new(ImageConversionKind.PngToWebp, "source_image", 1200, 800, 87);
    private static AssetPackageFileRule FileRule() => new("source_image", "", ".png", true, "png_master");
}
