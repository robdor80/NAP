using System.IO.Compression;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogPipelineTests
{
    [Fact]
    public async Task RealNimroelZipIsVerifiedCatalogedCompletedAndIdempotent()
    {
        using var f = new ProductionTestFixture(nimroel: true); var zip = Path.Combine(f.Context.Storage.InboxRoot, f.Package.AssetKey.AssetId + ".zip");
        ZipFile.CreateFromDirectory(f.Package.PackageRoot, zip);
        var candidate = Assert.Single(new InboxPackageDetector().Detect(f.Context.Storage.InboxRoot));
        var checker = new InboxPackageReadinessChecker(new InboxPackageReadinessOptions { RequiredSamples = 2, SampleInterval = TimeSpan.Zero });
        var staged = await new InboxPackageStager(checker).StageAsync(candidate, f.Context.Storage.StagingRoot);
        var extracted = await new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath, Path.Combine(f.Context.Storage.WorkspaceRoot, "extracted"));
        var validation = new PackageSemanticValidator().Validate(extracted.FinalPath!, f.Context); Assert.True(validation.IsValid); var package = validation.Package!;
        var repository = new ProductionRepositoryValidator().Validate(f.Context).Repository!;
        var processing = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
        Assert.True(new ProcessingPlanValidator().Validate(processing, new ProductionRepositoryScanner().Scan(repository).Snapshot!).IsClean);
        var archive = new ArchiveMasterPlanner(f.Context).Plan(package, processing); var job = f.Job();
        var coordinator = new AssetExecutionCoordinator(f.Context);
        var result = coordinator.Execute(job, package, processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State);
        var catalog = new AssetCatalog(f.Context); var record = Assert.Single(catalog.Query()); catalog.CheckIntegrity();
        Assert.Equal("nimroel", record.AssetKey.UniverseId.Value); Assert.Equal(package.AssetKey, record.AssetKey);
        Assert.Equal(package.Manifest.Classification.OrderBy(p => p.Key), record.Classification.OrderBy(p => p.Key));
        Assert.Equal("portrait", record.AssetType); Assert.Equal("portrait_npc", record.ProductionProfile);
        Assert.Equal("PASS", record.AuditState);
        Assert.Equal(result.Archive.MasterDigest, record.MasterDigest); Assert.Equal(new FileInfo(package.FilesByRole["master"]).Length, record.MasterSizeBytes);
        var webp = result.Production.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp);
        Assert.Equal(webp.Digest, record.ProductionDigest); Assert.Equal(webp.SizeBytes, record.ProductionSizeBytes);
        Assert.Equal(result.Production.RelativeDirectory, record.ProductionRelativeDirectory); Assert.Equal(result.Archive.RelativeDirectory, record.ArchiveRelativeDirectory);
        foreach (var role in new[] { "prompt", "info", "manifest", "visual_identity" })
            Assert.Equal(File.ReadAllBytes(role == "manifest" ? package.ManifestPath : package.FilesByRole[role]), record.Documents.Single(d => d.Role == role).ToArray());
        Assert.Contains(record.Traits, t => t.Key == "/eye_color" && t.Value == "green-grey");
        Assert.All(record.Files, file => { Assert.False(Path.IsPathRooted(file.RelativePath)); Assert.DoesNotContain('\\', file.RelativePath); });
        Assert.DoesNotContain(record.Files, file => file.Location == CatalogFileLocation.Production && file.RelativePath.EndsWith(".png"));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, coordinator.Execute(job, package, processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget).Production.Outcome);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("busy")] [InlineData("invalid")]
    public void CatalogFailureLeavesDurableJobAtVerifiedAndRecoveryDoesNotRewriteOutputs(string failure)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var source = f.Production; var job = source.Job(); var archive = source.ArchivePlan();
        using (var raw = f.Raw())
        {
            if (failure == "busy") CatalogTestFixture.Execute(raw, "BEGIN IMMEDIATE");
            else CatalogTestFixture.Execute(raw, "UPDATE catalog_metadata SET schema_version=2");
            CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new AssetExecutionCoordinator(f.Context).Execute(job, source.Package, source.Processing, archive,
                ProductionTestFixture.Pass, ProductionTestFixture.Budget)), failure == "busy" ? NapIssueCodes.CatalogBusy : NapIssueCodes.CatalogSchemaUnsupported);
            Assert.Equal(JobState.Verified, new JobStateStore(f.Context).Load(job).State);
            Assert.Equal(0L, CatalogTestFixture.Scalar(raw, "SELECT COUNT(*) FROM assets"));
            if (failure == "busy") CatalogTestFixture.Execute(raw, "ROLLBACK"); else CatalogTestFixture.Execute(raw, "UPDATE catalog_metadata SET schema_version=1");
        }
        var outputs = f.Sources();
        var result = new AssetExecutionCoordinator(f.Context).Execute(job, source.Package, source.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, result.FinalState); Assert.Single(f.Catalog.Query()); f.AssertSources(outputs);
    }
    [Fact]
    public void CommitThenCrashBeforeCompletedResumesExactRegistrationWithoutDbOrAssetWrites()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var states = new JobStateStore(f.Context);
        states.Transition(result.Job, JobState.Executed); states.Transition(result.Job, JobState.Verified);
        Assert.True(f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production, ProductionTestFixture.Pass));
        // This durable state is the actual crash boundary: DB committed, journal still VERIFIED.
        var sources = f.Sources(); var db = File.ReadAllBytes(f.Catalog.CatalogPath); var timestamp = File.GetLastWriteTimeUtc(f.Catalog.CatalogPath);
        var completed = new AssetExecutionCoordinator(f.Context).Execute(result.Job, result.Package, result.Plan,
            new ArchiveMasterPlanner(f.Context).Plan(result.Package, result.Plan), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, completed.FinalState); Assert.Single(f.Catalog.Query()); f.AssertSources(sources);
        Assert.Equal(db, File.ReadAllBytes(f.Catalog.CatalogPath)); Assert.Equal(timestamp, File.GetLastWriteTimeUtc(f.Catalog.CatalogPath));
    }
    [Fact]
    public void ConflictingCatalogStopsRecoveryAtVerifiedWithoutOverwritingAnything()
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(); var states = new JobStateStore(f.Context);
        states.Transition(result.Job, JobState.Executed); states.Transition(result.Job, JobState.Verified);
        f.Catalog.RegisterVerified(result.Package, result.Plan, result.Archive, result.Production);
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE assets SET master_sha256=$h; UPDATE files SET sha256=$h WHERE kind='master'", ("$h", new string('f', 64)));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new AssetExecutionCoordinator(f.Context).Execute(result.Job, result.Package, result.Plan,
            new ArchiveMasterPlanner(f.Context).Plan(result.Package, result.Plan), ProductionTestFixture.Pass, ProductionTestFixture.Budget)), NapIssueCodes.CatalogAssetConflict);
        Assert.Equal(JobState.Verified, states.Load(result.Job).State); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory]
    [InlineData("missing")] [InlineData("corrupt")]
    public void CompletedRerunNeverRepairsMissingOrCorruptCatalog(string kind)
    {
        using var f = new CatalogTestFixture(); var result = f.Publish(automatic: true);
        if (kind == "missing") File.Delete(f.Catalog.CatalogPath); else File.WriteAllBytes(f.Catalog.CatalogPath, [1, 2, 3]);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var rerun = new AssetExecutionCoordinator(f.Context).Execute(result.Job, result.Package, result.Plan, new ArchiveMasterPlanner(f.Context).Plan(result.Package, result.Plan), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, rerun.FinalState); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Throws<CatalogException>(() => f.Catalog.Query());
    }
}
