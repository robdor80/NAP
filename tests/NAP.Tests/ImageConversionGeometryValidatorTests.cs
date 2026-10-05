using System.Globalization;
using System.Reflection;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class ImageConversionGeometryValidatorTests
{
    private readonly ImageConversionGeometryValidator _validator = new();

    [Fact]
    public void PublicContractIsSealedStatelessWithOnlyExactValidateApi()
    {
        var type = typeof(ImageConversionGeometryValidator);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Equal("Validate", method.Name);
        Assert.Equal(typeof(NapIssueReport), method.ReturnType);
        Assert.Equal(new[] { typeof(PngImageInfo), typeof(ResolvedImageConversion) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "source", "conversion" }, method.GetParameters().Select(p => p.Name));
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
    }

    [Fact]
    public void NullArgumentsAreCheckedInOrderWithExactParameterNames()
    {
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, null!)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, Resolve(768, 960))).ParamName);
        Assert.Equal("conversion", Assert.Throws<ArgumentNullException>(() => _validator.Validate(Source(4, 5), null!)).ParamName);
    }

    [Theory]
    [InlineData(1024, 1280, 768, 960)]
    [InlineData(1536, 1920, 768, 960)]
    [InlineData(384, 480, 768, 960)]
    [InlineData(768, 960, 768, 960)]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1500, 1000, 900, 600)]
    [InlineData(640, 360, 1280, 720)]
    [InlineData(int.MaxValue, int.MaxValue, 16383, 16383)]
    public void ExactCompatibleRatiosReturnAnEmptyContinuableReport(int width, int height, int outputWidth, int outputHeight)
    {
        var report = _validator.Validate(Source(width, height), Resolve(outputWidth, outputHeight));
        AssertClean(report);
    }

    [Theory]
    [InlineData(1920, 1080, 768, 960)]
    [InlineData(1000, 1000, 1200, 800)]
    [InlineData(1000, 799, 1200, 800)]
    [InlineData(1024, 1280, 769, 960)]
    [InlineData(0, 1280, 768, 960)]
    [InlineData(1024, 0, 768, 960)]
    [InlineData(-1, 1280, 768, 960)]
    [InlineData(1024, -1, 768, 960)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 16383, 16383)]
    [InlineData(16382, 16383, 16381, 16382)]
    [InlineData(1048576, 1048577, 1, 1)]
    public void IncompatibleAndNonpositiveSourceGeometryStopsExactlyWithoutToleranceOrOverflow(int width, int height, int outputWidth, int outputHeight)
    {
        var conversion = Resolve(outputWidth, outputHeight);
        AssertMismatch(_validator.Validate(Source(width, height), conversion), conversion,
            FormattableString.Invariant($"source={width}x{height}; output={outputWidth}x{outputHeight}"));
    }

    [Theory]
    [InlineData(0, 960)]
    [InlineData(768, 0)]
    [InlineData(-1, 960)]
    [InlineData(768, -1)]
    public void ImpossibleInternalTargetGeometryFailsClosedWithoutAddingExceptions(int width, int height)
    {
        // Exercise an impossible internal snapshot without weakening its production constructor.
        var conversion = Assert.IsType<ResolvedImageConversion>(Assert.Single(typeof(ResolvedImageConversion)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic))
            .Invoke([new UniverseAssetKey(new UniverseId("test_universe"), "image_example_001"),
                ImageConversionKind.PngToWebp, "source_image", MissingPath, width, height, 87, 1L]));
        AssertMismatch(_validator.Validate(Source(1024, 1280), conversion), conversion,
            FormattableString.Invariant($"source=1024x1280; output={width}x{height}"));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    public void FailureDetailsAndRepeatedValidationAreCultureInvariant(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var conversion = Resolve(768, 960);
            var report = _validator.Validate(Source(1920, 1080), conversion);
            AssertMismatch(report, conversion, "source=1920x1080; output=768x960");
            Assert.Equal(report.Issues, _validator.Validate(Source(1920, 1080), conversion).Issues);
            AssertClean(_validator.Validate(Source(4, 5), conversion));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void GeometryOnlyIgnoresPixelBudgetQualityAndPngStructureAndDoesNotInspectPaths(int quality)
    {
        var conversion = Resolve(768, 960, quality);
        Assert.Equal(1, conversion.MaxInputPixels);
        Assert.Equal(MissingPath, conversion.SourcePath);
        AssertClean(_validator.Validate(new PngImageInfo(1024, 1280, 0, 255, 255), conversion));
        AssertMismatch(_validator.Validate(Source(1920, 1080), conversion), conversion,
            "source=1920x1080; output=768x960");
        Assert.Equal(MissingPath, conversion.SourcePath);
    }

    [Theory]
    [InlineData(true, 4, 5, true)]
    [InlineData(true, 16, 9, false)]
    [InlineData(false, 3, 2, true)]
    [InlineData(false, 4, 3, false)]
    public void V4ProfileToPackageToResolutionToPngGeometryStopsBeforeAnyGenericExecution(
        bool nimroel, int width, int height, bool compatible)
    {
        var profile = UniverseProfileLoader.Load(nimroel ? UniverseProfileLoaderTests.ConfigPath : UniverseProfileV4LoaderTests.FixturePath);
        var type = nimroel ? "portrait" : "image_asset";
        var productionProfile = nimroel ? "portrait_npc" : "image_profile";
        using var fixture = new PackageSemanticTestFixture(profile, type + "_example_001", type, productionProfile);
        var rule = Assert.Single(profile.AssetRules, r => r.AssetType == type);
        var configured = Assert.IsType<ImageConversionRule>(rule.Conversion);
        var file = Assert.Single(rule.PackageFiles, f => f.Role == configured.SourceRole);
        using (var image = new Image<Rgba32>(width, height)) image.SaveAsPng(fixture.PathFor(file));
        var package = Assert.IsType<ValidatedAssetPackage>(fixture.ValidateReadOnly().Package);
        Assert.Same(configured, package.AssetRule.Conversion);
        fixture.AssertReadOnly(() =>
        {
            var conversion = Assert.IsType<ResolvedImageConversion>(new ImageConversionResolver().Resolve(package, 4000000));
            Assert.Equal(nimroel ? 768 : 1200, conversion.OutputWidth);
            Assert.Equal(nimroel ? 960 : 800, conversion.OutputHeight);
            Assert.Equal(nimroel ? 90 : 87, conversion.WebpQuality);
            Assert.Equal(package.FilesByRole[configured.SourceRole], conversion.SourcePath);
            var png = new PngMasterValidator().Validate(conversion.SourcePath);
            Assert.True(png.IsValid);
            var source = Assert.IsType<PngImageInfo>(png.ImageInfo);
            var report = _validator.Validate(source, conversion);
            if (compatible) AssertClean(report);
            else AssertMismatch(report, conversion,
                FormattableString.Invariant($"source={width}x{height}; output={conversion.OutputWidth}x{conversion.OutputHeight}"));
        });
    }

    private const string MissingPath = @"missing/../uncreated folder\source.png";
    private static PngImageInfo Source(int width, int height) => new(width, height, 8, 6, 0);

    private static ResolvedImageConversion Resolve(int width, int height, int quality = 87)
    {
        // Reuse the controlled package construction pattern of ImageConversionResolverTests.
        var rule = new UniverseAssetRule("image_asset", "image_profile", [], [],
            [new AssetPackageFileRule("source_image", "", ".png", true, "png_master")], null,
            new ImageConversionRule(ImageConversionKind.PngToWebp, "source_image", width, height, quality));
        var key = new UniverseAssetKey(new UniverseId("test_universe"), "image_example_001");
        var manifest = new AssetManifestV2 { SchemaVersion = 2, UniverseId = "test_universe", AssetId = key.AssetId,
            AssetType = "image_asset", ProductionProfile = "image_profile", Classification = [] };
        var package = Assert.IsType<ValidatedAssetPackage>(Assert.Single(typeof(ValidatedAssetPackage)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic))
            .Invoke([key, "missing_package", "missing_manifest", manifest, rule,
                new Dictionary<string, string> { ["source_image"] = MissingPath }]));
        return Assert.IsType<ResolvedImageConversion>(new ImageConversionResolver().Resolve(package, 1));
    }

    private static void AssertClean(NapIssueReport report)
    {
        Assert.True(report.IsClean);
        Assert.False(report.ShouldStop);
        Assert.True(report.CanContinue);
        Assert.Empty(report.Issues);
    }

    private static void AssertMismatch(NapIssueReport report, ResolvedImageConversion conversion, string detail)
    {
        Assert.False(report.IsClean);
        Assert.True(report.ShouldStop);
        Assert.False(report.CanContinue);
        var issue = Assert.Single(report.Issues);
        Assert.Equal("image_conversion_aspect_ratio_mismatch", NapIssueCodes.ImageConversionAspectRatioMismatch);
        Assert.Equal(NapIssueCodes.ImageConversionAspectRatioMismatch, issue.Code);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.Equal("The source image aspect ratio does not match the configured output ratio; full-frame conversion is required.", issue.Message);
        Assert.Equal(conversion.SourcePath, issue.SubjectPath);
        Assert.Equal(detail, issue.Detail);
    }
}
