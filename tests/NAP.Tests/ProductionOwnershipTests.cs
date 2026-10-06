using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionOwnershipTests
{
    [Fact]
    public void FreshPlannedJobWithNoFinalsCanCompleteAndOwnEveryPublication()
    {
        using var f = new ProductionTestFixture(); var job = f.Job();
        var result = new AssetExecutionCoordinator(f.Context).Execute(job, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, result.FinalState);
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(Receipt(f, job)));
        Assert.Equal(job.Value, receipt.RootElement.GetProperty("job_id").GetString());
        Assert.Equal(f.Package.AssetKey.AssetId, receipt.RootElement.GetProperty("asset_id").GetString());
        Assert.Equal(5, receipt.RootElement.GetProperty("published").GetArrayLength());
        Assert.True(new JobRecoveryScanner(f.Context).Scan().Issues.IsClean);
    }

    [Theory]
    [InlineData("identical")] [InlineData("different")] [InlineData("empty_directory")]
    public void FreshJobNeverAdoptsAnyPreexistingDestination(string kind)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(); var archive = f.Archive(); var expected = f.Plan(archive);
        Directory.CreateDirectory(expected.DestinationDirectory);
        if (kind != "empty_directory") File.WriteAllBytes(expected.Files[0].DestinationPath, kind == "identical" ? expected.Files[0].ToArray() : [1, 2, 3]);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => new AssetExecutionCoordinator(f.Context).Execute(
            job, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(job).State);
        Assert.False(File.Exists(Receipt(f, job)));
        var plain = new ProductionAssetPlanner(f.Context).Plan(f.Package, f.Processing, archive, ProductionTestFixture.Budget);
        Assert.True(plain.Issues.ShouldStop); Assert.NotEqual(ProductionAssetAction.AlreadyProduced, plain.Action);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plain)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(5)]
    public void SameAuditedJobOnlyReusesFilesWithDurablePublicationProof(int count)
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, count);
        var original = plan.Files.Take(count).ToDictionary(p => p.DestinationPath, p => File.GetLastWriteTimeUtc(p.DestinationPath));
        var recovery = f.Plan(archive); Assert.True(recovery.Issues.IsClean); Assert.Equal(5 - count, recovery.FilesToWrite.Count);
        var result = new AssetExecutionCoordinator(f.Context).Execute(f.ProductionJob, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(JobState.Completed, result.FinalState);
        foreach (var (path, timestamp) in original) Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var second = new AssetExecutionCoordinator(f.Context).Execute(f.ProductionJob, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        Assert.Equal(ProductionAssetOutcome.AlreadyProduced, second.Production.Outcome); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("changed_owned")] [InlineData("extra_entry")] [InlineData("identical_unowned")]
    public void RecoveryDoesNotRelaxCollisionsOrAdoptCoincidentExternalFinals(string mutation)
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, 1);
        if (mutation == "changed_owned") File.AppendAllText(plan.Files[0].DestinationPath, "modified");
        if (mutation == "extra_entry") File.WriteAllText(Path.Combine(plan.DestinationDirectory, "external.txt"), "external");
        if (mutation == "identical_unowned") File.WriteAllBytes(plan.Files[1].DestinationPath, ProductionTestFixture.Bytes(plan.Files[1]));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => new AssetExecutionCoordinator(f.Context).Execute(
            f.ProductionJob, f.Package, f.Processing, f.ArchivePlan(), ProductionTestFixture.Pass, ProductionTestFixture.Budget)),
            mutation == "extra_entry" ? NapIssueCodes.ProductionUnexpectedEntry : NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(JobState.Audited, new JobStateStore(f.Context).Load(f.ProductionJob).State);
    }

    [Fact]
    public void AuditedStateAloneDoesNotEstablishOwnership()
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive);
        Directory.CreateDirectory(plan.DestinationDirectory); File.WriteAllBytes(plan.Files[0].DestinationPath, plan.Files[0].ToArray());
        var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.True(f.Plan(archive).Issues.ShouldStop);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void CrashAfterMoveBeforeReceiptIsConservativeStopEvenWithExactBytes()
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, 0);
        // Real writer publishes/validates the final, but the process exits before Record can persist ownership.
        ProductionTestFixture.Invoke(new ProductionAssetExecutor(f.Context), "ProductionAssetExecutor", "WriteFile", plan, plan.Files[0]);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void AnotherJobCannotReuseFirstJobsOutputsOrReceipt()
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var owner = f.Plan(archive); f.Prepare(owner, 3);
        var other = f.Job(JobState.Audited);
        File.Copy(Receipt(f, f.ProductionJob), Receipt(f, other));
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var plan = f.Plan(archive, jobId: other); Assert.True(plan.Issues.ShouldStop);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionRecoveryInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void MissingRecordedFinalIsStopBeforeAnyRepair()
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, 1);
        File.Delete(plan.Files[0].DestinationPath); var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionVerificationFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData(JobState.Executed)] [InlineData(JobState.Verified)]
    public void ExecutionCheckpointWithoutProvenanceCannotAuthorizeRecovery(JobState checkpoint)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(checkpoint); var archive = f.Archive(); var before = ArchiveTestFixture.Snapshot(f.Root);
        var plan = f.Plan(archive, jobId: job); Assert.True(plan.Issues.ShouldStop);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(plan)), NapIssueCodes.ProductionRecoveryInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("corrupt")] [InlineData("snapshot")] [InlineData("unknown_property")] [InlineData("duplicate_property")]
    public void InvalidProvenanceNeverAuthorizesRecovery(string mutation)
    {
        using var f = new ProductionTestFixture(); var archive = f.Archive(); var plan = f.Plan(archive); f.Prepare(plan, 1);
        var path = Receipt(f, f.ProductionJob); var json = File.ReadAllText(path);
        json = mutation switch { "corrupt" => "{", "snapshot" => json.Replace("\"snapshot_sha256\":\"", "\"snapshot_sha256\":\"f"),
            "unknown_property" => json.Insert(1, "\"unknown\":0,"), _ => json.Insert(1, "\"schema_version\":1,") };
        File.WriteAllText(path, json); var before = ArchiveTestFixture.Snapshot(f.Root);
        var recovery = f.Plan(archive); Assert.True(recovery.Issues.ShouldStop);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => f.Execute(recovery)), NapIssueCodes.ProductionRecoveryInvalid);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory]
    [InlineData("missing")] [InlineData("invalid")]
    public void CompletedWithoutExactProvenanceIsReadOnlyStop(string mutation)
    {
        using var f = new ProductionTestFixture(); var job = f.Job(); var archive = f.ArchivePlan(); var coordinator = new AssetExecutionCoordinator(f.Context);
        coordinator.Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget);
        var path = Receipt(f, job); if (mutation == "missing") File.Delete(path); else File.WriteAllText(path, "{}");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        ProductionTestFixture.Stop(Assert.Throws<ProductionStorageException>(() => coordinator.Execute(job, f.Package, f.Processing, archive, ProductionTestFixture.Pass, ProductionTestFixture.Budget)), NapIssueCodes.ProductionCompletedInconsistent);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    private static string Receipt(ProductionTestFixture fixture, JobId job) => Path.Combine(fixture.Context.Storage.StateRoot, "production-executions", job.Value + ".json");
}
