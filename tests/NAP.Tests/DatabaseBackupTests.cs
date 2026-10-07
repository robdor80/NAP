using System.Text.Json;
using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class DatabaseBackupTests
{
    [Fact]
    public void RealBackupPreservesEveryCatalogTableAndAllSourceBytes()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Target", new(classification: new Dictionary<string, string> { ["future"] = "value" },
            traits: [new("/nested/future_trait", "true", CatalogTraitType.Boolean)]), 8));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Campaign", ["objective"]));
        var db = File.ReadAllBytes(f.Catalog.CatalogPath); var sources = f.Sources(); var backup = new DatabaseBackupService().Create(f.Context);
        Assert.Equal(db, File.ReadAllBytes(f.Catalog.CatalogPath));
        Assert.Equal(CatalogRows(f.Catalog.CatalogPath), CatalogRows(Artifact(f.Context, backup)));
        foreach (var original in sources)
            Assert.Equal(original.Value, f.Sources()[original.Key]);
        Assert.Equal(1, backup.Manifest.CatalogSchemaVersion); Assert.Equal(f.Context.Id, backup.UniverseId);
        Assert.Equal(DatabaseBackupPurpose.Manual, backup.Manifest.DatabasePurpose);
        Assert.Equal(TimeSpan.Zero, backup.CreatedUtc.Offset); Assert.True(backup.LocallyVerified);
        Assert.Equal(new FileInfo(Artifact(f.Context, backup)).Length, backup.Size);
        Assert.Equal(new Sha256Hasher().Compute(Artifact(f.Context, backup)), backup.Sha256);
        using var json = JsonDocument.Parse(File.ReadAllText(ManifestPath(f.Context, backup)));
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.DoesNotContain(f.Root, File.ReadAllText(ManifestPath(f.Context, backup)));
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
    }
    [Theory]
    [InlineData("missing")] [InlineData("corrupt")] [InlineData("schema")] [InlineData("universe")]
    [InlineData("fk")] [InlineData("wal")] [InlineData("shm")] [InlineData("journal")]
    [InlineData("unknown_table")] [InlineData("archive_missing")] [InlineData("state_missing")]
    public void InvalidSourceOrBoundaryCannotPublish(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        switch (mode)
        {
            case "missing": File.Delete(f.Catalog.CatalogPath); break;
            case "corrupt": File.WriteAllText(f.Catalog.CatalogPath, "not SQLite"); break;
            case "schema": using (var c = f.Raw()) CatalogTestFixture.Execute(c, "PRAGMA user_version=2"); break;
            case "universe": using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE catalog_metadata SET universe_id='beta'"); break;
            case "fk": using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, "INSERT INTO objective_classifications VALUES($u,'missing','d','v')", ("$u", f.Context.Id.Value)); break;
            case "unknown_table": using (var c = f.Raw()) CatalogTestFixture.Execute(c, "CREATE TABLE alien(value TEXT)"); break;
            case "archive_missing": Directory.Delete(f.Context.Storage.ArchiveRoot); break;
            case "state_missing": Directory.Delete(f.Context.Storage.StateRoot, true); break;
            default: File.WriteAllText(f.Catalog.CatalogPath + "-" + mode, "sidecar"); break;
        }
        Stop(() => new DatabaseBackupService().Create(f.Context)); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.Database));
        if (mode == "archive_missing") Assert.False(Directory.Exists(f.Context.Storage.ArchiveRoot));
    }
    [Fact]
    public void StrongIdCollisionNeverOverwrites()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var service = new DatabaseBackupService();
        Set(service, "NewId", (Func<BackupId>)(() => new("b_00000000000000000000000000000001")));
        var e = service.Create(f.Context); var bytes = File.ReadAllBytes(Artifact(f.Context, e));
        Stop(() => service.Create(f.Context), NapIssueCodes.BackupCollision); Assert.Equal(bytes, File.ReadAllBytes(Artifact(f.Context, e)));
    }
    [Theory] [InlineData("Catalog")] [InlineData("Archive")]
    public void LockContentionStopsWithoutFinalArtifacts(string scope)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        using var lease = scope == "Archive" ? (IDisposable)ArchiveLock(f.Context) : Lock(f.Context, scope);
        Stop(() => new DatabaseBackupService().Create(f.Context), NapIssueCodes.BackupBusy);
        Assert.Empty(RecognizedDirectories(f.Context, BackupKind.Database));
    }
    [Fact]
    public void UniversesWithIdenticalNamesAndAssetIdsHaveIndependentBackups()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Publish(automatic: true); b.Publish(automatic: true);
        var aa = new DatabaseBackupService().Create(a.Context); var bb = new DatabaseBackupService().Create(b.Context);
        Assert.NotEqual(aa.BackupId, bb.BackupId); Assert.NotEqual(aa.Sha256, bb.Sha256);
        Assert.Equal(aa.BackupId, Assert.Single(new BackupHistoryReader().Read(a.Context).Entries).BackupId);
        Assert.Equal(bb.BackupId, Assert.Single(new BackupHistoryReader().Read(b.Context).Entries).BackupId);
        Stop(() => new DatabaseRestoreService().PlanRestore(a.Context, bb.BackupId), NapIssueCodes.RestoreInvalid);
    }
    [Theory] [InlineData("")] [InlineData("b_ABCDEF00000000000000000000000000")] [InlineData("../x")] [InlineData("2026-10-07")]
    public void IdentityIsStrict(string value) => Assert.Throws<ArgumentException>(() => new BackupId(value));
}
