using System.IO.Compression;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

/// <summary>Contract and offline production regressions for Nimroel's environmental scenes.</summary>
public sealed class NimroelEnvironmentProfileTests
{
    private const string TestAsset = "scene_treskal_market_evening_901";
    private const long PixelBudget = 4_000_000;

    [Fact]
    public void RealProfileAddsEnvironmentWithoutMutatingPortraitOrCartography()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        Assert.Equal("nimroel", profile.Id.Value);
        Assert.Equal(3, profile.AssetRules.Count);
        Assert.True(profile.TryGetAssetRule("scene", "scene_environment", out var environment));
        Assert.True(profile.TryGetAssetRule("scene", "scene_cartography", out var cartography));
        Assert.True(profile.TryGetAssetRule("portrait", "portrait_npc", out var portrait));
        Assert.False(profile.TryGetAssetRule("scene", "scene_narrative", out _));
        Assert.Equal(new[] { "culture", "realm", "region", "location" }, environment!.AllowedClassification);
        Assert.Equal(new[] { "culture", "location" }, environment.RequiredClassification);
        Assert.Equal(new[] { "master", "prompt", "info", "visual_identity" }, environment.PackageFiles.Select(f => f.Role));
        Assert.Equal(new[] { "", "_prompt", "_info", "_visual_identity" }, environment.PackageFiles.Select(f => f.Suffix));
        Assert.Equal(new[] { ".png", ".md", ".md", ".json" }, environment.PackageFiles.Select(f => f.Extension));
        Assert.All(environment.PackageFiles, f => Assert.True(f.Required));
        Assert.Equal(new string?[] { "png_master", null, null, null }, environment.PackageFiles.Select(f => f.ContentValidator));
        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 1920, 1080, 90), environment.Conversion);
        Assert.Equal(new string?[] { "scenes", "environment", "culture", "location", null },
            environment.Routing!.Segments.Select(s => s.Value));

        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 1600, 1000, 90), cartography!.Conversion);
        Assert.Equal(new string?[] { "scenes", "cartography", "culture", "location", null }, cartography.Routing!.Segments.Select(s => s.Value));
        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 768, 960, 90), portrait!.Conversion);
        Assert.Equal(new string?[] { "portraits", "culture", "location", "role", "sex", null },
            portrait.Routing!.Segments.Select(s => s.Value));
    }

    [Fact]
    public void FlatManifestV2AndFiveFilesAreAcceptedReadOnly()
    {
        using var f = EnvironmentScene();
        var result = f.ValidateReadOnly();
        var package = Assert.IsType<ValidatedAssetPackage>(result.Package);
        Assert.Equal("nimroel", package.AssetKey.UniverseId.Value);
        Assert.Equal("scene_environment", package.Manifest.ProductionProfile);
        Assert.Equal(2, package.Manifest.SchemaVersion);
        Assert.Equal(4, package.FilesByRole.Count);
        var resolved = Assert.IsType<ResolvedImageConversion>(new ImageConversionResolver().Resolve(package, PixelBudget));
        Assert.Equal((1920, 1080, 90), (resolved.OutputWidth, resolved.OutputHeight, resolved.WebpQuality));
        Assert.Equal(Path.Combine(f.PackageRoot, TestAsset + ".png"), resolved.SourcePath);
    }

    [Theory]
    [InlineData("scene_narrative")]
    [InlineData("nonexistent_profile")]
    public void UnknownSceneProfileIsNeverInferred(string profile)
    {
        using var f = EnvironmentScene();
        f.Manifest = f.Manifest with { ProductionProfile = profile };
        f.WriteManifest();
        Assert.Contains(f.ValidateReadOnly().Issues.Issues, i => i.Code == "package_rule_not_found");
    }

    [Fact]
    public void InvalidManifestVersionAndWrongUniverseStop()
    {
        using var f = EnvironmentScene();
        f.Manifest = f.Manifest with { SchemaVersion = 1 };
        f.WriteManifest();
        Assert.Contains(f.ValidateReadOnly().Issues.Issues, i => i.Code == "package_manifest_invalid");
        f.Manifest = f.Manifest with { SchemaVersion = 2, UniverseId = "other_universe" };
        f.WriteManifest();
        Assert.Contains(f.ValidateReadOnly().Issues.Issues, i => i.Code == "package_universe_mismatch");
    }

    [Theory]
    [InlineData("culture")]
    [InlineData("location")]
    public void MissingRequiredClassificationStops(string dimension)
    {
        using var f = EnvironmentScene();
        f.Manifest.Classification.Remove(dimension);
        f.WriteManifest();
        Assert.Contains(f.ValidateReadOnly().Issues.Issues, i => i.Code == "package_classification_invalid");
    }

    [Fact]
    public void OptionalRealmAndRegionAreAcceptedButOtherDimensionsAreRejected()
    {
        using var f = EnvironmentScene();
        f.Manifest.Classification["realm"] = "norgard";
        f.Manifest.Classification["region"] = "northern_reaches";
        f.WriteManifest();
        Assert.True(f.ValidateReadOnly().IsValid);
        f.Manifest.Classification["role"] = "farmer";
        f.WriteManifest();
        Assert.Contains(f.ValidateReadOnly().Issues.Issues, i => i.Code == "package_classification_invalid");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong_case")]
    [InlineData("nested")]
    public void MissingExtraMisnamedOrNestedFilesAreRejected(string mode)
    {
        using var f = EnvironmentScene();
        var master = Path.Combine(f.PackageRoot, TestAsset + ".png");
        if (mode == "missing") File.Delete(master);
        else if (mode == "extra") File.WriteAllText(Path.Combine(f.PackageRoot, "preview.txt"), "forbidden");
        else if (mode == "wrong_case") File.Move(master, Path.Combine(f.PackageRoot, TestAsset + ".PNG"));
        else Directory.CreateDirectory(Path.Combine(f.PackageRoot, "nested"));
        Assert.True(f.ValidateReadOnly().Issues.ShouldStop);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("apng")]
    public void InvalidOrUnsupportedPngIsRejected(string mode)
    {
        using var f = EnvironmentScene();
        File.WriteAllBytes(Path.Combine(f.PackageRoot, TestAsset + ".png"),
            mode == "invalid" ? [1, 2, 3] : PackageSemanticTestFixture.Png(unsupported: true));
        Assert.True(f.ValidateReadOnly().Issues.ShouldStop);
    }

    [Theory]
    [InlineData(16, 10)]
    [InlineData(4, 3)]
    public void WrongRatioStopsGeometryAndConversionWithoutMutatingFiles(int width, int height)
    {
        using var f = EnvironmentScene(width, height);
        var package = Assert.IsType<ValidatedAssetPackage>(f.ValidateReadOnly().Package);
        var resolved = new ImageConversionResolver().Resolve(package, PixelBudget)!;
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var png = new PngMasterValidator().Validate(resolved.SourcePath);
        Assert.Contains(new ImageConversionGeometryValidator().Validate(png.ImageInfo!, resolved).Issues,
            i => i.Code == NapIssueCodes.ImageConversionAspectRatioMismatch && i.StopsProcessing);
        var conversion = new ScenePngToWebpConverter().Convert(resolved.SourcePath, Settings(resolved));
        Assert.False(conversion.IsConverted);
        Assert.Contains(conversion.Issues.Issues, i => i.Code == NapIssueCodes.SceneAspectRatioMismatch && i.StopsProcessing);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void RoutingIsDistinctAndOptionalClassificationDoesNotChangeIt()
    {
        using var f = EnvironmentScene();
        f.Manifest.Classification["realm"] = "norgard";
        f.Manifest.Classification["region"] = "northern_reaches";
        f.WriteManifest();
        Directory.CreateDirectory(f.Context.Storage.ProductionRoot);
        var package = Assert.IsType<ValidatedAssetPackage>(f.ValidateReadOnly().Package);
        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var route = new ProductionDestinationResolver().Resolve(package, repository);
        Assert.Equal($"scenes/environment/norgard/treskal/{TestAsset}", route.RelativeDirectory);
    }

    [Fact]
    public async Task RealEnvironmentZipCompletesArchiveProductionAndSqliteWithoutAffectingOtherProfiles()
    {
        using var f = EnvironmentScene(160, 90);
        foreach (var root in new[] { f.Context.Storage.WorkspaceRoot, f.Context.Storage.ProductionRoot,
                     f.Context.Storage.ArchiveRoot, f.Context.Storage.StateRoot })
            Directory.CreateDirectory(root);

        var originalPortraits = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ProductionRoot, "portraits")).FullName;
        var originalMaps = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ProductionRoot, "scenes", "cartography")).FullName;
        File.WriteAllText(Path.Combine(originalPortraits, "sentinel.txt"), "portrait untouched");
        File.WriteAllText(Path.Combine(originalMaps, "sentinel.txt"), "cartography untouched");
        var portraitsBefore = ArchiveTestFixture.Snapshot(originalPortraits);
        var mapsBefore = ArchiveTestFixture.Snapshot(originalMaps);
        var originalMaster = File.ReadAllBytes(Path.Combine(f.PackageRoot, TestAsset + ".png"));
        var originalPackage = ArchiveTestFixture.Snapshot(f.PackageRoot);
        var zip = Path.Combine(new InboxPreparer().Prepare(f.Context.Storage.InboxRoot), TestAsset + ".zip");
        ZipFile.CreateFromDirectory(f.PackageRoot, zip);
        var zipHash = new Sha256Hasher().Compute(zip);

        var candidate = Assert.Single(new InboxPackageDetector().Detect(f.Context.Storage.InboxRoot));
        var checker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions
            { RequiredSamples = 2, SampleInterval = TimeSpan.Zero });
        var staged = await new InboxPackageStager(checker).StageAsync(candidate, f.Context.Storage.StagingRoot);
        Assert.Equal(InboxPackageStagingStatus.Staged, staged.Status);
        var extracted = await new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath,
            Path.Combine(f.Context.Storage.WorkspaceRoot, "extracted"));
        Assert.Equal(StagedPackageExtractionStatus.Extracted, extracted.Status);
        var validated = new PackageSemanticValidator().Validate(extracted.FinalPath!, f.Context);
        Assert.True(validated.IsValid);
        var package = validated.Package!;

        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var processing = new ProcessingPlanBuilder().Build(package, repository,
            new ProductionDestinationResolver().Resolve(package, repository));
        Assert.True(new ProcessingPlanValidator().Validate(processing,
            new ProductionRepositoryScanner().Scan(repository).Snapshot!).IsClean);
        var resolved = new ImageConversionResolver().Resolve(package, PixelBudget)!;
        Assert.Equal((1920, 1080, 90), (resolved.OutputWidth, resolved.OutputHeight, resolved.WebpQuality));
        Assert.True(new ImageConversionGeometryValidator().Validate(
            new PngMasterValidator().Validate(resolved.SourcePath).ImageInfo!, resolved).IsClean);
        var converted = new ScenePngToWebpConverter().Convert(resolved.SourcePath, Settings(resolved));
        Assert.True(converted.IsConverted);
        Assert.True(new SceneWebpOutputValidator().Validate(converted.Image!, Settings(resolved)).IsClean);

        var job = JobId.Create();
        var states = new JobStateStore(f.Context);
        states.Create(job);
        foreach (var state in new[] { JobState.Staged, JobState.Validated, JobState.Planned })
            states.Transition(job, state);
        var archivePlan = new ArchiveMasterPlanner(f.Context).Plan(package, processing);
        // Offline test-only PASS; no network call, authorization of real assets or implicit publication.
        var coordinator = new AssetExecutionCoordinator(f.Context);
        var result = coordinator.Execute(job, package, processing, archivePlan, ArchiveTestFixture.Pass, PixelBudget);
        Assert.Equal(JobState.Completed, result.FinalState);
        Assert.Equal($"scenes/environment/norgard/treskal/{TestAsset}", result.Production.RelativeDirectory);
        Assert.Equal(5, result.Archive.FilesVerified.Count);
        Assert.Equal(5, result.Production.FilesVerified.Count);
        var archivedMaster = Assert.Single(result.Archive.FilesVerified, file => file.Role == "master");
        Assert.Equal(originalMaster, File.ReadAllBytes(archivedMaster.DestinationPath));
        var webp = Assert.Single(result.Production.FilesVerified, file => file.Kind == ProductionAssetFileKind.GeneratedWebp);
        Assert.Equal(converted.Image!.ToArray(), File.ReadAllBytes(webp.DestinationPath));
        using (var decoded = Image.Load<Rgba32>(webp.DestinationPath))
            Assert.Equal((1920, 1080), (decoded.Width, decoded.Height));
        Assert.DoesNotContain(result.Production.FilesVerified, file => file.FileName.EndsWith(".png", StringComparison.Ordinal));
        Assert.Equal(package.AssetKey, Assert.Single(new AssetCatalog(f.Context).Query()).AssetKey);
        ArchiveTestFixture.AssertSnapshot(originalPackage, f.PackageRoot);
        ArchiveTestFixture.AssertSnapshot(portraitsBefore, originalPortraits);
        ArchiveTestFixture.AssertSnapshot(mapsBefore, originalMaps);
        Assert.Equal(zipHash, new Sha256Hasher().Compute(zip));

        var published = ArchiveTestFixture.Snapshot(f.Root);
        var again = coordinator.Execute(job, package, processing, archivePlan, ArchiveTestFixture.Pass, PixelBudget);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, again.Archive.Outcome);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, again.Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(published, f.Root);
    }

    [Fact]
    public void FullFrameCornerAndEdgeColorsSurviveSixteenNineResize()
    {
        using var f = EnvironmentScene(160, 90);
        var master = Path.Combine(f.PackageRoot, TestAsset + ".png");
        using (var image = new Image<Rgba32>(160, 90))
        {
            for (var y = 0; y < 90; y++)
            for (var x = 0; x < 160; x++)
            {
                var left = x < 24; var right = x >= 136;
                var top = y < 14; var bottom = y >= 76;
                image[x, y] = (left, right, top, bottom) switch
                {
                    (true, _, true, _) => new Rgba32(255, 0, 255),
                    (_, true, true, _) => new Rgba32(0, 255, 255),
                    (true, _, _, true) => new Rgba32(255, 255, 255),
                    (_, true, _, true) => new Rgba32(128, 0, 128),
                    (_, _, true, _) => new Rgba32(255, 0, 0),
                    (_, _, _, true) => new Rgba32(0, 255, 0),
                    (true, _, _, _) => new Rgba32(0, 0, 255),
                    (_, true, _, _) => new Rgba32(255, 255, 0),
                    _ => new Rgba32(32, 32, 32)
                };
            }
            image.SaveAsPng(master);
        }
        var original = File.ReadAllBytes(master);
        var package = Assert.IsType<ValidatedAssetPackage>(f.ValidateReadOnly().Package);
        var resolution = new ImageConversionResolver().Resolve(package, PixelBudget)!;
        var output = new ScenePngToWebpConverter().Convert(resolution.SourcePath, Settings(resolution));
        Assert.True(output.IsConverted);
        Assert.True(new SceneWebpOutputValidator().Validate(output.Image!, Settings(resolution)).IsClean);
        using var decoded = Image.Load<Rgba32>(output.Image!.ToArray());
        Assert.Equal((1920, 1080), (decoded.Width, decoded.Height));
        Color(new Rgba32(255, 0, 255), decoded[80, 80]);
        Color(new Rgba32(0, 255, 255), decoded[1840, 80]);
        Color(new Rgba32(255, 255, 255), decoded[80, 1000]);
        Color(new Rgba32(128, 0, 128), decoded[1840, 1000]);
        Color(new Rgba32(255, 0, 0), decoded[960, 80]);
        Color(new Rgba32(0, 255, 0), decoded[960, 1000]);
        Color(new Rgba32(0, 0, 255), decoded[80, 540]);
        Color(new Rgba32(255, 255, 0), decoded[1840, 540]);
        Color(new Rgba32(32, 32, 32), decoded[960, 540]);
        Assert.Equal(original, File.ReadAllBytes(master));
    }

    private static void Color(Rgba32 expected, Rgba32 actual)
    {
        Assert.InRange(Math.Abs(expected.R - actual.R), 0, 60);
        Assert.InRange(Math.Abs(expected.G - actual.G), 0, 60);
        Assert.InRange(Math.Abs(expected.B - actual.B), 0, 60);
    }

    private static PackageSemanticTestFixture EnvironmentScene(int width = 16, int height = 9)
    {
        var f = new PackageSemanticTestFixture(assetId: TestAsset, assetType: "scene", productionProfile: "scene_environment");
        try
        {
            f.Manifest.Classification["culture"] = "norgard";
            f.Manifest.Classification["location"] = "treskal";
            f.WriteManifest();
            ProductionTestFixture.WritePng(Path.Combine(f.PackageRoot, TestAsset + ".png"), width, height);
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_prompt.md"), "# Synthetic environmental scene prompt\n");
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_info.md"), "# Offline fixture, not Nimroel canon\n");
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_visual_identity.json"), "{\"test_fixture\":true}");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static SceneConversionSettings Settings(ResolvedImageConversion conversion) =>
        new(conversion.OutputWidth, conversion.OutputHeight, conversion.WebpQuality, conversion.MaxInputPixels);
}
