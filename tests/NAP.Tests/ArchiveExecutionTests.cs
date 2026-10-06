using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveExecutionTests
{
    [Fact]
    public void NewArchiveVerifiesEveryByteAndSecondRunDoesNotWriteAnything()
    {
        using var fixture = new ArchiveTestFixture();
        var production = ArchiveTestFixture.Snapshot(fixture.Context.Storage.ProductionRoot);
        var workspace = ArchiveTestFixture.Snapshot(fixture.Context.Storage.WorkspaceRoot);
        var source = ArchiveTestFixture.Snapshot(fixture.Package.PackageRoot);
        var firstPlan = fixture.Plan();
        var first = fixture.Execute(firstPlan);
        Assert.Equal(ArchiveMasterOutcome.Copied, first.Outcome);
        Assert.Equal(5, first.FilesVerified.Count);
        Assert.All(first.FilesVerified, file =>
        {
            Assert.Equal(File.ReadAllBytes(file.SourcePath), File.ReadAllBytes(file.DestinationPath));
            Assert.Equal(new Sha256Hasher().Compute(file.SourcePath), new Sha256Hasher().Compute(file.DestinationPath));
            Assert.Equal(new FileInfo(file.SourcePath).Length, new FileInfo(file.DestinationPath).Length);
        });
        Assert.Single(fixture.Store.Load().Entries);
        Assert.Equal(0, new FileInfo(fixture.LockPath).Length);
        foreach (var path in firstPlan.Files.Select(f => f.DestinationPath).Append(fixture.Store.IndexPath).Append(fixture.LockPath))
            File.SetLastWriteTimeUtc(path, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var archived = ArchiveTestFixture.Snapshot(fixture.Context.Storage.ArchiveRoot);
        var secondPlan = fixture.Plan();
        Assert.Equal(ArchiveMasterAction.AlreadyArchived, secondPlan.Action);
        Assert.Empty(secondPlan.FilesToCopy);
        Assert.True(secondPlan.Issues.IsClean);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, fixture.Execute(secondPlan).Outcome);
        // File hashes, lengths, last-write timestamps and directory/file inventory all agree.
        ArchiveTestFixture.AssertSnapshot(archived, fixture.Context.Storage.ArchiveRoot);
        ArchiveTestFixture.AssertSnapshot(production, fixture.Context.Storage.ProductionRoot);
        ArchiveTestFixture.AssertSnapshot(workspace, fixture.Context.Storage.WorkspaceRoot);
        ArchiveTestFixture.AssertSnapshot(source, fixture.Package.PackageRoot);
        Assert.Equal(JobState.Audited, new JobStateStore(fixture.Context).Load(fixture.Job).State);
        Assert.False(File.Exists(fixture.Context.Storage.CatalogPath));
    }

    [Theory]
    [InlineData(2, ArchiveMasterAction.CopyAndIndex, ArchiveMasterOutcome.Copied)]
    [InlineData(5, ArchiveMasterAction.IndexExisting, ArchiveMasterOutcome.IndexedExisting)]
    public void PartialRecoveryCopiesOnlyMissingFinals(int existing, ArchiveMasterAction action, ArchiveMasterOutcome outcome)
    {
        using var fixture = new ArchiveTestFixture();
        var original = fixture.Plan();
        fixture.PrepareFinals(original, existing);
        var correct = original.Files.Take(existing).ToDictionary(file => file.DestinationPath,
            file => (File.ReadAllBytes(file.DestinationPath), File.GetLastWriteTimeUtc(file.DestinationPath)));
        var plan = fixture.Plan();
        Assert.Equal(action, plan.Action);
        Assert.Equal(5 - existing, plan.FilesToCopy.Count);
        var result = fixture.Execute(plan);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(5, result.FilesVerified.Count);
        foreach (var (path, expected) in correct)
        {
            Assert.Equal(expected.Item1, File.ReadAllBytes(path));
            Assert.Equal(expected.Item2, File.GetLastWriteTimeUtc(path));
        }
        Assert.Single(fixture.Store.Load().Entries);
    }

    [Fact]
    public void OrphanPackageTempIsIgnoredAndPreserved()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(plan.DestinationDirectory);
        var orphan = plan.Files[0].DestinationPath + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(orphan, "partial copy");
        var newPlan = fixture.Plan();
        Assert.Equal(5, newPlan.FilesToCopy.Count);
        fixture.Execute(newPlan);
        Assert.Equal("partial copy", File.ReadAllText(orphan));
        Assert.Single(fixture.Store.Load().Entries);
        Assert.Equal(6, Directory.GetFiles(plan.DestinationDirectory).Length);
    }

    [Fact]
    public void AlreadyArchivedNeedsNoNewLockFile()
    {
        using var fixture = new ArchiveTestFixture();
        fixture.Execute();
        File.Delete(fixture.LockPath); // Represents an externally prepared, coherent archive without a technical lock file.
        var before = ArchiveTestFixture.Snapshot(fixture.Context.Storage.ArchiveRoot);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, fixture.Execute().Outcome);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Context.Storage.ArchiveRoot);
        Assert.False(File.Exists(fixture.LockPath));
    }

    [Theory]
    [InlineData("master", NapIssueCodes.ArchiveAssetCollision)]
    [InlineData("prompt", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("info", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("manifest", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("visual_identity", NapIssueCodes.ArchiveFileCollision)]
    public void IndexedIdentityAndEveryCompanionMustMatch(string role, string code)
    {
        using var fixture = new ArchiveTestFixture();
        var oldPlan = fixture.Plan();
        fixture.Execute(oldPlan);
        File.AppendAllText(oldPlan.Files.Single(f => f.Role == role).SourcePath, "\nchanged");
        var before = ArchiveTestFixture.Snapshot(fixture.Context.Storage.ArchiveRoot);
        var collision = fixture.Plan();
        Assert.True(collision.Issues.ShouldStop);
        Assert.Contains(collision.Issues.Issues, issue => issue.Code == code);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(collision)), code);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Context.Storage.ArchiveRoot);
    }

    [Theory]
    [InlineData("different_file", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("file_blocker", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("directory_instead_of_file", NapIssueCodes.ArchiveFileCollision)]
    [InlineData("missing_physical", NapIssueCodes.ArchiveIndexInconsistent)]
    [InlineData("index_path", NapIssueCodes.ArchiveIndexInconsistent)]
    [InlineData("index_size", NapIssueCodes.ArchiveIndexInconsistent)]
    [InlineData("index_type", NapIssueCodes.ArchiveIndexInconsistent)]
    public void PhysicalAndIndexInconsistenciesAreStoppedWithoutRepair(string mode, string code)
    {
        using var fixture = new ArchiveTestFixture();
        var original = fixture.Plan();
        if (mode.StartsWith("index", StringComparison.Ordinal) || mode == "missing_physical") fixture.Execute(original);
        else if (mode == "file_blocker") File.WriteAllText(Path.Combine(original.ArchiveRoot, "portraits"), "blocker");
        else
        {
            Directory.CreateDirectory(original.DestinationDirectory);
            if (mode == "different_file") File.WriteAllText(original.Files[0].DestinationPath, "different");
            else Directory.CreateDirectory(original.Files[0].DestinationPath);
        }
        if (mode == "missing_physical") File.Delete(original.Files[0].DestinationPath);
        if (mode == "index_path") File.WriteAllText(fixture.Store.IndexPath, File.ReadAllText(fixture.Store.IndexPath).Replace(original.RelativeDirectory, "other/" + original.AssetKey.AssetId));
        if (mode == "index_size") File.WriteAllText(fixture.Store.IndexPath, File.ReadAllText(fixture.Store.IndexPath).Replace("\"master_size_bytes\":" + original.MasterSizeBytes, "\"master_size_bytes\":1"));
        if (mode == "index_type") File.WriteAllText(fixture.Store.IndexPath, File.ReadAllText(fixture.Store.IndexPath).Replace("\"asset_type\":\"portrait\"", "\"asset_type\":\"scene\""));
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        var plan = fixture.Plan();
        Assert.Contains(plan.Issues.Issues, i => i.Code == code && i.StopsProcessing);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), code);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void DifferentAssetWithSameMasterWarnsAndPreservesBothIndexEntries()
    {
        using var fixture = new ArchiveTestFixture();
        fixture.Execute();
        var original = fixture.Store.Load();
        var second = AddPackage(fixture, "portrait_treskal_farmer_male_041");
        var plan = new ArchiveMasterPlanner(fixture.Context).Plan(second.Package, second.Plan);
        Assert.False(plan.Issues.ShouldStop);
        var warning = Assert.Single(plan.Issues.Issues);
        Assert.Equal(NapIssueCodes.ArchivePossibleDuplicate, warning.Code);
        Assert.Equal(NapIssueSeverity.Warning, warning.Severity);
        Assert.Equal(NapIssueDisposition.Continue, warning.Disposition);
        var result = fixture.Execute(plan);
        Assert.Equal(warning.Code, Assert.Single(result.Issues.Issues).Code);
        Assert.Equal(2, fixture.Store.Load().Entries.Count);
        Assert.Equal(original.Entries[0].MasterSha256, fixture.Store.Load().Entries[1].MasterSha256);
        var archived = new ArchiveMasterPlanner(fixture.Context).Plan(second.Package, second.Plan);
        Assert.Equal(ArchiveMasterAction.AlreadyArchived, archived.Action);
        Assert.True(archived.Issues.IsClean);
        var before = ArchiveTestFixture.Snapshot(plan.ArchiveRoot);
        Assert.True(fixture.Execute(archived).Issues.IsClean);
        ArchiveTestFixture.AssertSnapshot(before, plan.ArchiveRoot);
    }

    [Theory]
    [InlineData(AiAuditDecision.Warning, AiAuditFindingSeverity.Warning)]
    [InlineData(AiAuditDecision.Fail, AiAuditFindingSeverity.Error)]
    public void NonPassCannotWriteAnyRootOrCreateInfrastructure(AiAuditDecision decision, AiAuditFindingSeverity severity)
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        var report = new AiAuditReport(decision, "Review required.", [new AiAuditFinding("archive_review", severity, "Review.")]);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        var error = Assert.Throws<InvalidOperationException>(() => new ArchiveMasterExecutor(fixture.Context).Execute(plan, report));
        Assert.Contains("AI audit PASS", error.Message);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
        Assert.False(Directory.Exists(Path.Combine(plan.ArchiveRoot, "_nap")));
        Assert.False(Directory.Exists(plan.DestinationDirectory));
        Assert.Equal(JobState.Audited, new JobStateStore(fixture.Context).Load(fixture.Job).State);
    }

    [Fact]
    public void AiRequestStaysPrivacySafeAndExecutionRequiresANonNullReport()
    {
        using var fixture = new ArchiveTestFixture();
        var request = new AiAuditRequestBuilder().Build(fixture.Processing, new NapIssueReport([]));
        var json = new AiAuditRequestJsonRenderer().Render(request);
        foreach (var path in new[] { fixture.Root, fixture.Context.Storage.ArchiveRoot, fixture.Store.IndexPath, fixture.LockPath })
            Assert.DoesNotContain(path, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArchiveRoot", json, StringComparison.OrdinalIgnoreCase);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterExecutor(fixture.Context).Execute(fixture.Plan(), null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterExecutor(fixture.Context).Execute(null!, ArchiveTestFixture.Pass));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void InvalidAuditEnumIsRejectedBeforeAnyFilesystemAccess()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        var report = (AiAuditReport)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AiAuditReport));
        typeof(AiAuditReport).GetField("<Decision>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(report, (AiAuditDecision)999);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        Assert.Throws<InvalidOperationException>(() => new ArchiveMasterExecutor(fixture.Context).Execute(plan, report));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    internal static (ValidatedAssetPackage Package, ProcessingPlan Plan) AddPackage(ArchiveTestFixture fixture, string id)
    {
        var original = fixture.Package;
        var directory = Directory.CreateDirectory(Path.Combine(fixture.Root, id)).FullName;
        var manifest = original.Manifest with { AssetId = id };
        var manifestPath = Path.Combine(directory, id + "_manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        foreach (var rule in original.AssetRule.PackageFiles)
            if (original.FilesByRole.TryGetValue(rule.Role, out var source)) File.WriteAllBytes(Path.Combine(directory, rule.ResolveFileName(id)), File.ReadAllBytes(source));
        var validation = new PackageSemanticValidator().Validate(directory, fixture.Context);
        Assert.True(validation.IsValid);
        var package = validation.Package!;
        var repository = new ProductionRepositoryValidator().Validate(fixture.Context).Repository!;
        return (package, new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository)));
    }
}
