using System.IO.Compression;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class NimroelCartographyProfileTests
{
    private const string TestAsset = "scene_treskal_test_plan_001";
    private const long PixelBudget = 4_000_000;

    [Fact]
    public void RealNimroelProfileDeclaresCartographyWithoutChangingPortrait()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        Assert.Equal(2, profile.AssetRules.Count);
        Assert.True(profile.TryGetAssetRule("portrait", "portrait_npc", out var portrait));
        Assert.True(profile.TryGetAssetRule("scene", "scene_cartography", out var cartography));
        Assert.False(profile.TryGetAssetRule("scene", "scene_narrative", out _));
        Assert.Equal(new[] { "culture", "location", "role", "sex" }, portrait.RequiredClassification);
        Assert.Equal(new[] { "culture", "location" }, cartography.RequiredClassification);
        Assert.Equal(new[] { "culture", "realm", "region", "location" }, cartography.AllowedClassification);
        Assert.Equal(new[] { "master", "prompt", "info", "visual_identity" }, cartography.PackageFiles.Select(f => f.Role));
        Assert.All(cartography.PackageFiles, file => Assert.True(file.Required));
        Assert.Equal(new string?[] { "png_master", null, null, null }, cartography.PackageFiles.Select(f => f.ContentValidator));
        Assert.Equal(new ImageConversionRule(ImageConversionKind.PngToWebp, "master", 1600, 1000, 90), cartography.Conversion);
        Assert.Equal(new string?[] { "scenes", "cartography", "culture", "location", null },
            cartography.Routing!.Segments.Select(segment => segment.Value));
    }

    [Fact]
    public void FlatCartographyPackageWithMatchingManifestIsSemanticallyValid()
    {
        using var fixture = new PackageSemanticTestFixture(
            assetId: TestAsset,
            assetType: "scene", productionProfile: "scene_cartography");
        fixture.Manifest.Classification["culture"] = "norgard";
        fixture.Manifest.Classification["location"] = "treskal";
        fixture.WriteManifest();
        var result = fixture.ValidateReadOnly();
        Assert.True(result.IsValid);
        Assert.Equal("scene_cartography", result.Package!.Manifest.ProductionProfile);
        Assert.Equal(4, result.Package.FilesByRole.Count);
    }

    [Fact]
    public void InexactDiegeticMapDimensionsCannotPassExactSixteenTenGeometry()
    {
        Assert.False(new PngImageInfo(1586, 992, 8, 2, 0).HasAspectRatio(1600, 1000));
        Assert.True(new PngImageInfo(1600, 1000, 8, 2, 0).HasAspectRatio(1600, 1000));
        Assert.True(new PngImageInfo(1920, 1200, 8, 2, 0).HasAspectRatio(1600, 1000));
    }

    [Theory]
    [InlineData("culture")]
    [InlineData("location")]
    public void MissingRequiredClassificationStopsWithoutChangingPackage(string dimension)
    {
        using var f = Map();
        f.Manifest.Classification.Remove(dimension); f.WriteManifest();
        Assert.True(f.ValidateReadOnly().Issues.ShouldStop);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("sex")]
    [InlineData("faction")]
    public void PortraitAndUnknownDimensionsAreNotAdmittedForCartography(string dimension)
    {
        using var f = Map();
        f.Manifest.Classification[dimension] = "unregistered_value"; f.WriteManifest();
        Assert.True(f.ValidateReadOnly().Issues.ShouldStop);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OptionalRealmAndRegionAreAcceptedAndDoNotAlterTheRoute(bool realm, bool region)
    {
        using var f = Map();
        if (realm) f.Manifest.Classification["realm"] = "test_realm";
        if (region) f.Manifest.Classification["region"] = "test_region";
        f.WriteManifest(); Directory.CreateDirectory(f.Context.Storage.ProductionRoot);
        var package = Assert.IsType<ValidatedAssetPackage>(f.ValidateReadOnly().Package);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var route = new ProductionDestinationResolver().Resolve(package, repository);
        Assert.Equal($"scenes/cartography/norgard/treskal/{TestAsset}", route.RelativeDirectory);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void NarrativeManifestIsNotSilentlyReinterpretedAsCartography()
    {
        using var f = Map();
        f.Manifest = f.Manifest with { ProductionProfile = "scene_narrative" }; f.WriteManifest();
        Assert.True(f.ValidateReadOnly().Issues.ShouldStop);
    }

    [Theory]
    [InlineData(1586, 992)]
    [InlineData(1920, 1080)]
    public void InexactMasterStopsRealConversionAndProductionPlanningWithoutWriting(int width, int height)
    {
        using var f = Map(width, height); CreateStorage(f.Context);
        var package = Assert.IsType<ValidatedAssetPackage>(f.ValidateReadOnly().Package);
        var processing = Processing(f.Context, package);
        var resolved = new ImageConversionResolver().Resolve(package, PixelBudget)!;
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var png = new PngMasterValidator().Validate(resolved.SourcePath);
        Assert.Contains(new ImageConversionGeometryValidator().Validate(png.ImageInfo!, resolved).Issues,
            i => i.Code == NapIssueCodes.ImageConversionAspectRatioMismatch && i.StopsProcessing);
        var conversion = new ScenePngToWebpConverter().Convert(resolved.SourcePath, Settings(resolved));
        Assert.False(conversion.IsConverted); Assert.Null(conversion.Image);
        Assert.Contains(conversion.Issues.Issues, i => i.Code == NapIssueCodes.SceneAspectRatioMismatch && i.StopsProcessing);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);

        // The archive retains original bytes; a conversion STOP never publishes or repairs production.
        var archive = new ArchiveMasterExecutor(f.Context).Execute(new ArchiveMasterPlanner(f.Context).Plan(package, processing), ArchiveTestFixture.Pass);
        var archived = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() =>
            new ProductionAssetPlanner(f.Context).Plan(package, processing, archive, PixelBudget)), NapIssueCodes.ProductionConversionFailed);
        ArchiveTestFixture.AssertSnapshot(archived, f.Root);
        Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ProductionRoot));
        Assert.False(File.Exists(f.Context.Storage.CatalogPath));
    }

    [Theory]
    [InlineData(1600, 1000)]
    [InlineData(1920, 1200)]
    public async Task RealMapZipCompletesVerifiedPublicationAndPreservesOriginalsAndExistingAssets(int width, int height)
    {
        using var f = Map(width, height); CreateStorage(f.Context);
        var existingPortraits = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ProductionRoot, "portraits")).FullName;
        var existingArchive = Directory.CreateDirectory(Path.Combine(f.Context.Storage.ArchiveRoot, "portraits")).FullName;
        File.WriteAllText(Path.Combine(existingPortraits, "legacy-sentinel.txt"), "existing published asset: never overwrite");
        File.WriteAllText(Path.Combine(existingArchive, "legacy-sentinel.txt"), "existing master: never overwrite");
        var portraitsBefore = ArchiveTestFixture.Snapshot(existingPortraits);
        var archiveBefore = ArchiveTestFixture.Snapshot(existingArchive);
        var inputBefore = ArchiveTestFixture.Snapshot(f.PackageRoot);
        var master = File.ReadAllBytes(Path.Combine(f.PackageRoot, TestAsset + ".png"));
        var zip = Path.Combine(new InboxPreparer().Prepare(f.Context.Storage.InboxRoot), TestAsset + ".zip");
        ZipFile.CreateFromDirectory(f.PackageRoot, zip);
        var zipDigest = new Sha256Hasher().Compute(zip);
        var candidate = Assert.Single(new InboxPackageDetector().Detect(f.Context.Storage.InboxRoot));
        var checker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions { RequiredSamples = 2, SampleInterval = TimeSpan.Zero });
        var staged = await new InboxPackageStager(checker).StageAsync(candidate, f.Context.Storage.StagingRoot);
        Assert.Equal(InboxPackageStagingStatus.Staged, staged.Status);
        var extracted = await new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath, Path.Combine(f.Context.Storage.WorkspaceRoot, "extracted"));
        Assert.Equal(StagedPackageExtractionStatus.Extracted, extracted.Status);
        var validated = new PackageSemanticValidator().Validate(extracted.FinalPath!, f.Context);
        Assert.True(validated.IsValid); var package = validated.Package!;
        var processing = Processing(f.Context, package);
        var resolved = new ImageConversionResolver().Resolve(package, PixelBudget)!;
        Assert.Equal((1600, 1000, 90), (resolved.OutputWidth, resolved.OutputHeight, resolved.WebpQuality));
        var converted = new ScenePngToWebpConverter().Convert(resolved.SourcePath, Settings(resolved));
        Assert.True(converted.IsConverted); Assert.Equal(90, converted.Image!.WebpQuality);
        Assert.True(new SceneWebpOutputValidator().Validate(converted.Image, Settings(resolved)).IsClean);

        var job = JobId.Create(); var states = new JobStateStore(f.Context); states.Create(job);
        foreach (var state in new[] { JobState.Staged, JobState.Validated, JobState.Planned }) states.Transition(job, state);
        var archivePlan = new ArchiveMasterPlanner(f.Context).Plan(package, processing);
        var coordinator = new AssetExecutionCoordinator(f.Context);
        // Offline test audit only: this grants no approval to any real map or package.
        var result = coordinator.Execute(job, package, processing, archivePlan, ArchiveTestFixture.Pass, PixelBudget);
        Assert.Equal(JobState.Completed, result.FinalState); Assert.Equal(JobState.Completed, states.Load(job).State);
        Assert.Equal($"scenes/cartography/norgard/treskal/{TestAsset}", result.Production.RelativeDirectory);
        Assert.Equal(5, result.Archive.FilesVerified.Count); Assert.Equal(5, result.Production.FilesVerified.Count);
        var archivedMaster = Assert.Single(result.Archive.FilesVerified, file => file.Role == "master");
        Assert.Equal(master, File.ReadAllBytes(archivedMaster.DestinationPath));
        Assert.Equal(new Sha256Hasher().Compute(archivedMaster.SourcePath), new Sha256Hasher().Compute(archivedMaster.DestinationPath));
        foreach (var file in result.Archive.FilesVerified) Assert.Equal(File.ReadAllBytes(file.SourcePath), File.ReadAllBytes(file.DestinationPath));
        foreach (var file in result.Production.FilesVerified.Where(file => file.SourcePath is not null))
            Assert.Equal(File.ReadAllBytes(file.SourcePath!), File.ReadAllBytes(file.DestinationPath));
        var webp = Assert.Single(result.Production.FilesVerified, file => file.Kind == ProductionAssetFileKind.GeneratedWebp);
        Assert.Equal(converted.Image.ToArray(), File.ReadAllBytes(webp.DestinationPath));
        using (var decoded = Image.Load<Rgba32>(webp.DestinationPath)) Assert.Equal((1600, 1000), (decoded.Width, decoded.Height));
        Assert.DoesNotContain(result.Production.FilesVerified, file => file.FileName.EndsWith(".png", StringComparison.Ordinal));
        Assert.Equal(package.AssetKey, Assert.Single(new AssetCatalog(f.Context).Query()).AssetKey);
        ArchiveTestFixture.AssertSnapshot(inputBefore, f.PackageRoot);
        ArchiveTestFixture.AssertSnapshot(portraitsBefore, existingPortraits);
        ArchiveTestFixture.AssertSnapshot(archiveBefore, existingArchive);
        Assert.Equal(zipDigest, new Sha256Hasher().Compute(zip));

        var published = ArchiveTestFixture.Snapshot(f.Root);
        var second = coordinator.Execute(job, package, processing, archivePlan, ArchiveTestFixture.Pass, PixelBudget);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, second.Archive.Outcome);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, second.Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(published, f.Root);
    }

    private static PackageSemanticTestFixture Map(int width = 16, int height = 10)
    {
        var f = new PackageSemanticTestFixture(assetId: TestAsset, assetType: "scene", productionProfile: "scene_cartography");
        try
        {
            f.Manifest.Classification["culture"] = "norgard"; f.Manifest.Classification["location"] = "treskal"; f.WriteManifest();
            ProductionTestFixture.WritePng(Path.Combine(f.PackageRoot, TestAsset + ".png"), width, height);
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_prompt.md"), "# Synthetic cartography test prompt\n");
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_info.md"), "# Synthetic integration fixture; not canonical geography\n");
            File.WriteAllText(Path.Combine(f.PackageRoot, TestAsset + "_visual_identity.json"), "{\"test_fixture\":true}");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static void CreateStorage(UniverseContext context)
    {
        foreach (var path in new[] { context.Storage.WorkspaceRoot, context.Storage.ProductionRoot, context.Storage.ArchiveRoot, context.Storage.StateRoot })
            Directory.CreateDirectory(path);
    }

    private static ProcessingPlan Processing(UniverseContext context, ValidatedAssetPackage package)
    {
        var repository = new ProductionRepositoryValidator().Validate(context).Repository!;
        var plan = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
        Assert.True(new ProcessingPlanValidator().Validate(plan, new ProductionRepositoryScanner().Scan(repository).Snapshot!).IsClean);
        return plan;
    }

    private static SceneConversionSettings Settings(ResolvedImageConversion conversion) =>
        new(conversion.OutputWidth, conversion.OutputHeight, conversion.WebpQuality, conversion.MaxInputPixels);
}
