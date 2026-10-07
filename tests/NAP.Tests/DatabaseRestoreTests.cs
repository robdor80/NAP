using System.Reflection;
using System.Text.Json;
using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class DatabaseRestoreTests
{
    [Fact]
    public void RestoreReturnsExactOperationalDataDocumentsTraitsAssetsAndExplorerRevalidates()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        var filter = new CatalogFilter(assetType: "emblem", classification: new Dictionary<string, string> { ["future"] = "value" },
            traits: [new("/eye_color", "green-grey", CatalogTraitType.String)]);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Original objective", filter, 12));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Original campaign", ["objective"]));
        var planning = JsonSerializer.Serialize(f.Catalog.Planning()); var asset = CatalogTestFixture.Logical(f.Catalog.Get("emblem_example_001")!);
        var backup = new DatabaseBackupService().Create(f.Context);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Modified", new(assetType: "absent"), 33));
        f.Catalog.SaveObjective(new(f.Context.Id, "extra", "Extra", new(), 2));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Changed", ["extra"]));
        var beforeRestore = File.ReadAllBytes(f.Catalog.CatalogPath); var safetyRows = CatalogRows(f.Catalog.CatalogPath); var sources = f.Sources();
        var reader = new CatalogExplorerReader(f.Context); reader.Page();
        var service = new DatabaseRestoreService(); var plan = service.PlanRestore(f.Context, backup.BackupId);
        var plannedBytes = File.ReadAllBytes(f.Catalog.CatalogPath);
        Assert.True(plan.RequiresSafetyBackup); Assert.Equal(beforeRestore, plannedBytes);
        var result = service.Execute(f.Context, plan); Assert.True(result.Restored); Assert.Equal(backup.BackupId, result.BackupId);
        Assert.NotNull(result.SafetyBackupId); Assert.Equal(planning, JsonSerializer.Serialize(f.Catalog.Planning()));
        Assert.Equal(asset, CatalogTestFixture.Logical(f.Catalog.Get("emblem_example_001")!));
        var safety = new BackupHistoryReader().Read(f.Context).Entries.Single(e => e.BackupId == result.SafetyBackupId);
        Assert.Equal(DatabaseBackupPurpose.PreRestore, safety.Manifest.DatabasePurpose);
        Assert.Equal(safetyRows, CatalogRows(Artifact(f.Context, safety)));
        foreach (var original in sources) Assert.Equal(original.Value, f.Sources()[original.Key]);
        reader.Page();
        var count = (int)typeof(CatalogExplorerReader).GetProperty("StrongValidationCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader)!;
        Assert.Equal(2, count); Stop(() => service.Execute(f.Context, plan), NapIssueCodes.RestoreStalePlan);
    }
    [Fact]
    public void AbsentCatalogCanBeRestoredWithoutInventingASafetyBackup()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); File.Delete(f.Catalog.CatalogPath);
        var service = new DatabaseRestoreService(); var plan = service.PlanRestore(f.Context, backup.BackupId);
        Assert.Equal(ActiveCatalogState.Missing, plan.ActiveState); Assert.False(plan.RequiresSafetyBackup);
        var result = service.Execute(f.Context, plan); Assert.Null(result.SafetyBackupId);
        Assert.Equal(ActiveCatalogState.Missing, result.PriorActiveState); Assert.Null(result.RecoveryArtifact); f.Catalog.CheckIntegrity();
    }
    [Theory] [InlineData("schema")] [InlineData("universe")]
    [InlineData("journal")] [InlineData("wal")]
    public void InvalidActiveCatalogStopsAndRetainsAllPriorBytes(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context);
        switch (mode)
        {
            case "schema": using (var c = f.Raw()) CatalogTestFixture.Execute(c, "PRAGMA user_version=2"); break;
            case "universe": using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE catalog_metadata SET universe_id='beta'"); break;
            default: File.WriteAllText(f.Catalog.CatalogPath + "-" + mode, "evidence"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root); Stop(() => new DatabaseRestoreService().PlanRestore(f.Context, backup.BackupId));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory] [InlineData("active_changed")] [InlineData("active_replaced")] [InlineData("active_missing")]
    [InlineData("manifest_changed")] [InlineData("artifact_changed")] [InlineData("other_context")]
    public void StalePlansNeverReplaceTheCatalog(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context);
        var service = new DatabaseRestoreService(); var plan = service.PlanRestore(f.Context, backup.BackupId);
        var context = f.Context;
        switch (mode)
        {
            case "active_changed": f.Catalog.SaveObjective(new(f.Context.Id, "new", "New", new(), 1)); break;
            case "active_replaced": var temp = f.Catalog.CatalogPath + ".replacement"; File.Copy(f.Catalog.CatalogPath, temp); File.Move(temp, f.Catalog.CatalogPath, true); break;
            case "active_missing": File.Delete(f.Catalog.CatalogPath); break;
            case "manifest_changed": File.AppendAllText(ManifestPath(f.Context, backup), "\n"); break;
            case "artifact_changed": File.AppendAllText(Artifact(f.Context, backup), "bad"); break;
            case "other_context": using (var other = new CatalogTestFixture(universe: "beta"))
                { Stop(() => service.Execute(other.Context, plan), NapIssueCodes.RestoreStalePlan); } return;
        }
        var before = File.Exists(f.Catalog.CatalogPath) ? File.ReadAllBytes(f.Catalog.CatalogPath) : null;
        Stop(() => service.Execute(context, plan));
        if (before is null) Assert.False(File.Exists(f.Catalog.CatalogPath)); else Assert.Equal(before, File.ReadAllBytes(f.Catalog.CatalogPath));
    }
    [Theory] [InlineData("safety_failure")] [InlineData("candidate_corrupt")] [InlineData("publication_failure")]
    public void FailureCannotSacrificeActiveCatalog(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context);
        f.Catalog.SaveObjective(new(f.Context.Id, "later", "Later", new(), 2)); var before = File.ReadAllBytes(f.Catalog.CatalogPath);
        var service = new DatabaseRestoreService(); var plan = service.PlanRestore(f.Context, backup.BackupId);
        if (mode == "safety_failure") Set(Get(service, "SafetyBackups"), "NewId", (Func<BackupId>)(() => backup.BackupId));
        else Set(service, "Observer", (Action<string>)(stage =>
        {
            if (mode == "candidate_corrupt" && stage == "candidate_verified")
                File.WriteAllText(Directory.GetFiles(f.Context.Storage.StateRoot, ".restore-*.db").Single(), "broken candidate");
            if (mode == "publication_failure" && stage == "published") throw new IOException("injected post-publish failure");
        }));
        Stop(() => service.Execute(f.Context, plan)); Assert.Equal(before, File.ReadAllBytes(f.Catalog.CatalogPath)); f.Catalog.CheckIntegrity();
    }
    [Theory] [InlineData("Catalog")] [InlineData("Archive")]
    public void RestoreRespectsExistingWriters(string scope)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context);
        var service = new DatabaseRestoreService(); var plan = service.PlanRestore(f.Context, backup.BackupId);
        using var lease = scope == "Archive" ? (IDisposable)ArchiveLock(f.Context) : Lock(f.Context, scope);
        var before = File.ReadAllBytes(f.Catalog.CatalogPath); Stop(() => service.Execute(f.Context, plan), NapIssueCodes.BackupBusy);
        Assert.Equal(before, File.ReadAllBytes(f.Catalog.CatalogPath));
    }
    [Fact]
    public void SnapshotCannotBeUsedAsDatabaseBackup()
    {
        using var f = new BackupRepositoryFixture(); var snapshot = new RepositorySnapshotService().Create(f.Context);
        Stop(() => new DatabaseRestoreService().PlanRestore(f.Context, snapshot.BackupId), NapIssueCodes.RestoreInvalid);
    }
    [Fact]
    public void ForeignBackupCopiedIntoNamespaceIsRejected()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); b.Catalog.Initialize();
        var backup = new DatabaseBackupService().Create(b.Context); var target = Path.Combine(a.Context.Storage.ArchiveRoot, Namespace(backup.Kind), backup.BackupId.Value);
        Directory.CreateDirectory(target); foreach (var file in Directory.GetFiles(DirectoryPath(b.Context, backup))) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        Stop(() => new DatabaseRestoreService().PlanRestore(a.Context, backup.BackupId), NapIssueCodes.BackupWrongUniverse);
    }
}
