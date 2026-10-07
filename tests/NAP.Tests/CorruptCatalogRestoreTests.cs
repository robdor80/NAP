using System.Reflection;
using System.Text.Json;
using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class CorruptCatalogRestoreTests
{
    private static string EvidenceRoot(UniverseContext c) => Path.Combine(c.Storage.ArchiveRoot, "NAP_DATABASE_BACKUPS", "_recovery");
    private static string EvidenceDirectory(UniverseContext c, CatalogRecoveryArtifact e) => Path.Combine(EvidenceRoot(c), e.RecoveryId.Value);
    private static string EvidenceFile(UniverseContext c, CatalogRecoveryArtifact e) => Path.Combine(EvidenceDirectory(c, e), e.Artifact);
    private static byte[] Corrupt(CatalogTestFixture f)
    { var bytes = new byte[137113]; new Random(71).NextBytes(bytes); File.WriteAllBytes(f.Catalog.CatalogPath, bytes); return bytes; }

    [Fact]
    public void CorruptCatalogIsPreservedExactlyAndRestoredWithOperationalDataAndExplorerRevalidation()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var other = new CatalogTestFixture(universe: "beta");
        f.Publish(automatic: true); other.Publish(automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Original objective", new(traits: [new("/eye_color", "green-grey", CatalogTraitType.String)]), 7));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Original campaign", ["objective"]));
        var expectedRows = CatalogRows(f.Catalog.CatalogPath); var backup = new DatabaseBackupService().Create(f.Context);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Changed", new(), 99));
        var explorer = new CatalogExplorerReader(f.Context); explorer.Page();
        var production = ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot); var masters = f.Sources();
        var otherBytes = ArchiveTestFixture.Snapshot(other.Root); var corrupt = Corrupt(f);
        var service = new DatabaseRestoreService(); var beforePlan = ArchiveTestFixture.Snapshot(f.Root);
        var plan = service.PlanRestore(f.Context, backup.BackupId); ArchiveTestFixture.AssertSnapshot(beforePlan, f.Root);
        Assert.Equal(ActiveCatalogState.Corrupt, plan.ActiveState); Assert.True(plan.RequiresRecoveryPreservation); Assert.False(plan.RequiresSafetyBackup);
        var result = service.Execute(f.Context, plan); Assert.True(result.Restored); Assert.Equal(ActiveCatalogState.Corrupt, result.PriorActiveState);
        Assert.Null(result.SafetyBackupId); var evidence = Assert.IsType<CatalogRecoveryArtifact>(result.RecoveryArtifact);
        Assert.Equal(corrupt, File.ReadAllBytes(EvidenceFile(f.Context, evidence)));
        Assert.Equal(corrupt.LongLength, evidence.Size); Assert.Equal(new Sha256Hasher().Compute(EvidenceFile(f.Context, evidence)), evidence.Sha256);
        using var original = new MemoryStream(corrupt); Assert.Equal(new Sha256Hasher().Compute(original), evidence.Sha256);
        var manifestText = File.ReadAllText(Path.Combine(EvidenceDirectory(f.Context, evidence), "recovery_manifest.json"));
        using var manifest = JsonDocument.Parse(manifestText); var m = manifest.RootElement;
        Assert.Equal(8, m.EnumerateObject().Count()); Assert.Equal(1, m.GetProperty("schema_version").GetInt32());
        Assert.Equal(evidence.RecoveryId.Value, m.GetProperty("recovery_id").GetString()); Assert.Equal("alpha", m.GetProperty("universe_id").GetString());
        Assert.Equal("active_catalog_corrupt", m.GetProperty("reason").GetString()); Assert.Equal(evidence.Artifact, m.GetProperty("artifact").GetString());
        Assert.Equal(evidence.Size, m.GetProperty("size").GetInt64()); Assert.Equal(evidence.Sha256.Hex, m.GetProperty("sha256").GetString());
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(m.GetProperty("created_utc").GetString()!).Offset); Assert.DoesNotContain(f.Root, manifestText);
        Assert.Equal(File.ReadAllBytes(Artifact(f.Context, backup)), File.ReadAllBytes(f.Catalog.CatalogPath));
        Assert.Equal(expectedRows, CatalogRows(f.Catalog.CatalogPath)); f.Catalog.CheckIntegrity();
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
        Assert.Throws<ArgumentException>(() => new BackupId(evidence.RecoveryId.Value));
        // Even moving forensic bytes/manifest into a syntactically normal backup directory cannot adopt them.
        var attempted = BackupId.New(); var spoof = Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_DATABASE_BACKUPS", attempted.Value);
        Directory.CreateDirectory(spoof); File.Copy(EvidenceFile(f.Context, evidence), Path.Combine(spoof, "catalog.db"));
        File.Copy(Path.Combine(EvidenceDirectory(f.Context, evidence), "recovery_manifest.json"), Path.Combine(spoof, "manifest.json"));
        Stop(() => service.PlanRestore(f.Context, attempted)); File.Delete(Path.Combine(spoof, "catalog.db")); File.Delete(Path.Combine(spoof, "manifest.json")); Directory.Delete(spoof);
        explorer.Page(); Assert.Equal(2, (int)typeof(CatalogExplorerReader).GetProperty("StrongValidationCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(explorer)!);
        ArchiveTestFixture.AssertSnapshot(production, f.Context.Storage.ProductionRoot); ArchiveTestFixture.AssertSnapshot(otherBytes, other.Root);
        foreach (var master in masters) Assert.Equal(master.Value, f.Sources()[master.Key]);
    }
    [Fact]
    public void OrdinaryRetentionNeverSelectsOrDeletesRecoveryEvidence()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var creator = new DatabaseBackupService(); var backup = creator.Create(f.Context);
        Corrupt(f); var restore = new DatabaseRestoreService(); var result = restore.Execute(f.Context, restore.PlanRestore(f.Context, backup.BackupId));
        creator.Create(f.Context); var evidence = ArchiveTestFixture.Snapshot(EvidenceRoot(f.Context));
        var retention = new BackupRetentionService(); var plan = retention.Plan(f.Context, new(0));
        Assert.Equal(2, plan.Decisions.Count); Assert.Single(plan.Decisions, d => d.Action == BackupRetentionAction.Delete);
        retention.Execute(f.Context, plan); ArchiveTestFixture.AssertSnapshot(evidence, EvidenceRoot(f.Context));
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries); Assert.NotNull(result.RecoveryArtifact);
    }
    [Theory] [InlineData("blocked_root")] [InlineData("copy_changed")] [InlineData("manifest_changed")] [InlineData("collision")]
    public void PreservationFailureStopsBeforeCandidateAndLeavesCorruptActiveBytes(string failure)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); var corrupt = Corrupt(f);
        var s = new DatabaseRestoreService(); var plan = s.PlanRestore(f.Context, backup.BackupId); var store = Get(s, "RecoveryArtifacts");
        if (failure == "blocked_root") File.WriteAllText(EvidenceRoot(f.Context), "foreign file");
        else if (failure == "collision")
        {
            var id = CatalogRecoveryId.New(); var root = Directory.CreateDirectory(Path.Combine(EvidenceRoot(f.Context), id.Value)).FullName;
            File.WriteAllText(Path.Combine(root, "foreign.txt"), "must remain"); Set(store, "NewId", (Func<CatalogRecoveryId>)(() => id));
        }
        else Set(store, "Observer", (Action<string>)(stage =>
        {
            if (failure == "copy_changed" && stage == "copied") File.AppendAllText(Directory.GetFiles(EvidenceRoot(f.Context), "AssetCatalog.corrupt.db", SearchOption.AllDirectories).Single(), "changed");
            if (failure == "manifest_changed" && stage == "verified") File.WriteAllText(Directory.GetFiles(EvidenceRoot(f.Context), "recovery_manifest.json", SearchOption.AllDirectories).Single(), "{}");
        }));
        Stop(() => s.Execute(f.Context, plan)); Assert.Equal(corrupt, File.ReadAllBytes(f.Catalog.CatalogPath));
        Assert.Empty(Directory.GetFiles(f.Context.Storage.StateRoot, ".restore-*.db"));
        if (failure != "blocked_root") Assert.DoesNotContain(Directory.GetDirectories(EvidenceRoot(f.Context), "recovery_*"), d => File.Exists(Path.Combine(d, "recovery_manifest.json")));
        if (failure == "collision") Assert.Equal("must remain", File.ReadAllText(Directory.GetFiles(EvidenceRoot(f.Context), "foreign.txt", SearchOption.AllDirectories).Single()));
    }
    [Fact]
    public void InvalidHistoricalBackupDoesNotCreateEvidenceOrReplaceAnything()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); Corrupt(f);
        File.AppendAllText(Artifact(f.Context, backup), "broken"); var before = ArchiveTestFixture.Snapshot(f.Root);
        Stop(() => new DatabaseRestoreService().PlanRestore(f.Context, backup.BackupId));
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.False(Directory.Exists(EvidenceRoot(f.Context)));
    }
    [Theory] [InlineData("candidate")] [InlineData("published")]
    public void CandidateOrFinalValidationFailureRetainsPriorCorruptBytesAndPublishedEvidence(string failure)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); var corrupt = Corrupt(f);
        var s = new DatabaseRestoreService(); var plan = s.PlanRestore(f.Context, backup.BackupId);
        Set(s, "Observer", (Action<string>)(stage =>
        {
            if (failure == "candidate" && stage == "candidate_verified") File.WriteAllText(Directory.GetFiles(f.Context.Storage.StateRoot, ".restore-*.db").Single(), "invalid candidate");
            if (failure == "published" && stage == "published") throw new IOException("injected failure after replacement");
        }));
        Stop(() => s.Execute(f.Context, plan)); Assert.Equal(corrupt, File.ReadAllBytes(f.Catalog.CatalogPath));
        Assert.Equal(corrupt, File.ReadAllBytes(Directory.GetFiles(EvidenceRoot(f.Context), "AssetCatalog.corrupt.db", SearchOption.AllDirectories).Single()));
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
    }
    [Theory] [InlineData("changed_bytes")] [InlineData("replaced_file")] [InlineData("became_valid")]
    public void CorruptActivePlansAreStaleAfterAnyChange(string change)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); Corrupt(f);
        var s = new DatabaseRestoreService(); var plan = s.PlanRestore(f.Context, backup.BackupId);
        if (change == "changed_bytes") File.AppendAllText(f.Catalog.CatalogPath, "changed");
        else if (change == "became_valid") File.Copy(Artifact(f.Context, backup), f.Catalog.CatalogPath, true);
        else { var copy = f.Catalog.CatalogPath + ".copy"; File.Copy(f.Catalog.CatalogPath, copy); File.Move(copy, f.Catalog.CatalogPath, true); }
        var before = File.ReadAllBytes(f.Catalog.CatalogPath); Stop(() => s.Execute(f.Context, plan), NapIssueCodes.RestoreStalePlan);
        Assert.Equal(before, File.ReadAllBytes(f.Catalog.CatalogPath)); Assert.False(Directory.Exists(EvidenceRoot(f.Context)));
    }
    [Theory] [InlineData("foreign_active")] [InlineData("foreign_damaged_schema")] [InlineData("foreign_backup")]
    [InlineData("foreign_ambiguous_metadata")]
    public void WrongUniverseIsStillStopBeforePreservation(string mode)
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Catalog.Initialize(); b.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(a.Context);
        if (mode == "foreign_backup") { backup = new DatabaseBackupService().Create(b.Context); Corrupt(a); }
        else
        {
            File.Copy(b.Catalog.CatalogPath, a.Catalog.CatalogPath, true);
            if (mode == "foreign_damaged_schema") using (var c = a.Raw()) CatalogTestFixture.Execute(c, "ALTER TABLE catalog_metadata RENAME COLUMN schema_version TO broken_column");
            if (mode == "foreign_ambiguous_metadata") using (var c = a.Raw(false)) CatalogTestFixture.Execute(c,
                "DROP TABLE catalog_metadata; CREATE TABLE catalog_metadata(singleton INTEGER,schema_version INTEGER,universe_id TEXT); INSERT INTO catalog_metadata VALUES(1,1,'alpha'),(1,1,'beta')");
        }
        var before = File.ReadAllBytes(a.Catalog.CatalogPath); var other = ArchiveTestFixture.Snapshot(b.Root);
        Stop(() => new DatabaseRestoreService().PlanRestore(a.Context, backup.BackupId), mode == "foreign_backup" ? NapIssueCodes.RestoreInvalid : NapIssueCodes.BackupWrongUniverse);
        Assert.Equal(before, File.ReadAllBytes(a.Catalog.CatalogPath)); Assert.False(Directory.Exists(EvidenceRoot(a.Context))); ArchiveTestFixture.AssertSnapshot(other, b.Root);
    }
    [Theory] [InlineData("empty")] [InlineData("fk")] [InlineData("logical")]
    public void EmptyOrKnownSchemaLogicalCorruptionCanBeRecovered(string mode)
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var backup = new DatabaseBackupService().Create(f.Context);
        if (mode == "empty") File.WriteAllBytes(f.Catalog.CatalogPath, []);
        else using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, mode == "fk"
            ? "INSERT INTO objective_classifications VALUES('future_world','missing','x','y')"
            : "UPDATE documents SET content=X'1234' WHERE role='prompt'");
        var bytes = File.ReadAllBytes(f.Catalog.CatalogPath); var s = new DatabaseRestoreService();
        var result = s.Execute(f.Context, s.PlanRestore(f.Context, backup.BackupId));
        Assert.Equal(bytes, File.ReadAllBytes(EvidenceFile(f.Context, result.RecoveryArtifact!))); f.Catalog.CheckIntegrity();
    }
    [Fact]
    public void EvidenceMustStillBeVerifiedAtThePublicationBoundary()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); var corrupt = Corrupt(f);
        var s = new DatabaseRestoreService(); var plan = s.PlanRestore(f.Context, backup.BackupId);
        Set(s, "Observer", (Action<string>)(stage =>
        {
            if (stage == "candidate_verified") File.AppendAllText(Directory.GetFiles(EvidenceRoot(f.Context), "AssetCatalog.corrupt.db", SearchOption.AllDirectories).Single(), "changed evidence");
        }));
        Stop(() => s.Execute(f.Context, plan), NapIssueCodes.BackupIntegrityFailed); Assert.Equal(corrupt, File.ReadAllBytes(f.Catalog.CatalogPath));
    }
    [Fact]
    public void HistoricalBackupInvalidatedAfterPlanningDoesNotPreserveActiveBytesUnnecessarily()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var backup = new DatabaseBackupService().Create(f.Context); var corrupt = Corrupt(f);
        var s = new DatabaseRestoreService(); var plan = s.PlanRestore(f.Context, backup.BackupId);
        File.AppendAllText(Artifact(f.Context, backup), "invalidated backup");
        Stop(() => s.Execute(f.Context, plan)); Assert.Equal(corrupt, File.ReadAllBytes(f.Catalog.CatalogPath)); Assert.False(Directory.Exists(EvidenceRoot(f.Context)));
    }
}
