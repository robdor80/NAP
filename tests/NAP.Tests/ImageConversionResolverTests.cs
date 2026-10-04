using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ImageConversionResolverTests
{
    private readonly ImageConversionResolver _resolver = new();

    [Fact]
    public void PublicApiIsSealedStatelessAndResolvedSnapshotIsInternalGetOnly()
    {
        var type = typeof(ImageConversionResolver);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Equal("Resolve", method.Name);
        Assert.Equal(typeof(ResolvedImageConversion), method.ReturnType);
        Assert.Equal(new[] { typeof(ValidatedAssetPackage), typeof(long) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "package", "maxInputPixels" }, method.GetParameters().Select(p => p.Name));
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
        var result = typeof(ResolvedImageConversion);
        Assert.True(result.IsSealed);
        Assert.Empty(result.GetConstructors());
        Assert.True(Assert.Single(result.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).IsAssembly);
        Assert.All(result.GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal(new[] { "AssetKey", "Kind", "MaxInputPixels", "OutputHeight", "OutputWidth", "SourcePath", "SourceRole", "WebpQuality" },
            result.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void ArgumentsAreValidatedBeforeCheckingConversion()
    {
        Assert.Equal("package", Assert.Throws<ArgumentNullException>(() => _resolver.Resolve(null!, 0)).ParamName);
        Assert.Equal("maxInputPixels", Assert.Throws<ArgumentOutOfRangeException>(() => _resolver.Resolve(Package(conversion: false), 0)).ParamName);
        Assert.Equal("maxInputPixels", Assert.Throws<ArgumentOutOfRangeException>(() => _resolver.Resolve(Package(conversion: false), -1)).ParamName);
    }

    [Theory]
    [InlineData("portrait", "portrait_npc")]
    [InlineData("scene", "scene_example")]
    [InlineData("heraldry", "custom_profile")]
    public void NoConversionDoesNotInferFromTypeOrProfile(string type, string profile) =>
        Assert.Null(_resolver.Resolve(Package(false, type, profile), 1));

    [Theory]
    [InlineData("image_asset", "image_profile")]
    [InlineData("portrait", "portrait_npc")]
    [InlineData("scene", "scene_example")]
    [InlineData("environment", "future_profile")]
    public void ConversionResolvesGenericallyWithExactUnnormalizedSourcePath(string type, string profile)
    {
        const string path = @"nonexistent/../source folder\image.png";
        var package = Package(true, type, profile, path);
        var result = Assert.IsType<ResolvedImageConversion>(_resolver.Resolve(package, long.MaxValue));
        Assert.Same(package.AssetKey, result.AssetKey);
        Assert.Equal(ImageConversionKind.PngToWebp, result.Kind);
        Assert.Equal("source_image", result.SourceRole);
        Assert.Equal(path, result.SourcePath);
        Assert.Equal(1200, result.OutputWidth);
        Assert.Equal(800, result.OutputHeight);
        Assert.Equal(87, result.WebpQuality);
        Assert.Equal(long.MaxValue, result.MaxInputPixels);
        Assert.Equal(path, _resolver.Resolve(package, 4000000)!.SourcePath);
        Assert.Equal(4000000, _resolver.Resolve(package, 4000000)!.MaxInputPixels);
    }

    [Fact]
    public void IncoherentMissingRoleThrowsWithoutMasterOrExtensionFallback()
    {
        var package = Package(files: new Dictionary<string, string> { ["master"] = "other.png", ["source_images"] = "almost.png" });
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(package, 4000000));
    }

    [Fact]
    public void NimroelConfigToSemanticPackageToResolutionRetainsConversionWithoutExecuting()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        using var fixture = new PackageSemanticTestFixture(profile);
        var result = fixture.ValidateReadOnly();
        var package = Assert.IsType<ValidatedAssetPackage>(result.Package);
        Assert.Same(profile.AssetRules[0], package.AssetRule);
        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 768, 960, 90), package.AssetRule.Conversion);
        fixture.AssertReadOnly(() =>
        {
            var resolved = Assert.IsType<ResolvedImageConversion>(_resolver.Resolve(package, 4000000));
            Assert.Same(package.AssetKey, resolved.AssetKey);
            Assert.Equal("master", resolved.SourceRole);
            Assert.Equal(package.FilesByRole["master"], resolved.SourcePath);
            Assert.Equal(768, resolved.OutputWidth);
            Assert.Equal(960, resolved.OutputHeight);
            Assert.Equal(90, resolved.WebpQuality);
            Assert.Equal(4000000, resolved.MaxInputPixels);
        });
    }

    [Theory]
    [InlineData("image_asset", "image_profile", true)]
    [InlineData("metadata_asset", "metadata_profile", false)]
    public void GenericV4FixtureResolvesConfiguredImageAndExplicitNull(string type, string productionProfile, bool converted)
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileV4LoaderTests.FixturePath);
        using var fixture = new PackageSemanticTestFixture(profile, type + "_example_001", type, productionProfile);
        var package = Assert.IsType<ValidatedAssetPackage>(fixture.ValidateReadOnly().Package);
        fixture.AssertReadOnly(() =>
        {
            var resolved = _resolver.Resolve(package, 7654321);
            if (!converted) Assert.Null(resolved);
            else
            {
                Assert.NotNull(resolved);
                Assert.Equal("source_image", resolved.SourceRole);
                Assert.Equal(package.FilesByRole["source_image"], resolved.SourcePath);
                Assert.Equal(1200, resolved.OutputWidth);
                Assert.Equal(800, resolved.OutputHeight);
                Assert.Equal(87, resolved.WebpQuality);
                Assert.Equal(7654321, resolved.MaxInputPixels);
            }
        });
    }

    private static ValidatedAssetPackage Package(bool conversion = true, string type = "image_asset", string profile = "image_profile",
        string path = "missing.png", IReadOnlyDictionary<string, string>? files = null)
    {
        var rule = new UniverseAssetRule(type, profile, [], [],
            [new AssetPackageFileRule("source_image", "", ".png", true, "png_master")], null,
            conversion ? new ImageConversionRule(ImageConversionKind.PngToWebp, "source_image", 1200, 800, 87) : null);
        var key = new UniverseAssetKey(new UniverseId("test_universe"), type + "_example_001");
        var manifest = new AssetManifestV2 { SchemaVersion = 2, UniverseId = "test_universe", AssetId = key.AssetId,
            AssetType = type, ProductionProfile = profile, Classification = [] };
        return Assert.IsType<ValidatedAssetPackage>(Assert.Single(typeof(ValidatedAssetPackage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic))
            .Invoke([key, "missing_package", "missing_manifest", manifest, rule,
                files ?? new Dictionary<string, string> { ["source_image"] = path }]));
    }
}
