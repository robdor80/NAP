using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class BackupHistoryTests
{
    [Theory]
    [InlineData("invalid_json")] [InlineData("unknown_field")] [InlineData("duplicate")] [InlineData("schema")]
    [InlineData("universe")] [InlineData("kind")] [InlineData("artifact")] [InlineData("hash")]
    [InlineData("size")] [InlineData("missing_artifact")] [InlineData("unknown_inside")] [InlineData("utc")]
    [InlineData("identity")] [InlineData("db_schema")] [InlineData("missing_manifest")]
    public void CorruptRecognizedBackupIsNeverPresentedAsRestorable(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var e = new DatabaseBackupService().Create(f.Context);
        switch (mode)
        {
            case "invalid_json": File.WriteAllText(ManifestPath(f.Context, e), "{"); break;
            case "duplicate": var text = File.ReadAllText(ManifestPath(f.Context, e)); File.WriteAllText(ManifestPath(f.Context, e), text.Insert(1, "\"schema_version\":1,")); break;
            case "missing_manifest": File.Delete(ManifestPath(f.Context, e)); break;
            case "missing_artifact": File.Delete(Artifact(f.Context, e)); break;
            case "unknown_inside": File.WriteAllText(Path.Combine(DirectoryPath(f.Context, e), "foreign.txt"), "foreign"); break;
            default:
                Mutate(f.Context, e, j =>
                {
                    switch (mode)
                    {
                        case "unknown_field": j["future"] = true; break;
                        case "schema": j["schema_version"] = 2; break;
                        case "universe": j["universe_id"] = "beta"; break;
                        case "kind": j["backup_kind"] = "Unknown"; break;
                        case "artifact": j["artifact"] = "../AssetCatalog.db"; break;
                        case "hash": j["sha256"] = new string('0', 64); break;
                        case "size": j["size"] = e.Size + 1; break;
                        case "utc": j["created_utc"] = "2026-10-07T12:00:00.0000000+02:00"; break;
                        case "identity": j["backup_id"] = BackupId.New().Value; break;
                        case "db_schema": j["catalog_schema_version"] = 2; break;
                    }
                }); break;
        }
        Stop(() => new BackupHistoryReader().Read(f.Context));
    }
    [Fact]
    public void UnmanagedEntriesAreReportedReadOnlyWithoutAdoptionOrCloudClaims()
    {
        using var f = new BackupRepositoryFixture(); var e = new RepositorySnapshotService().Create(f.Context);
        var foreign = Path.Combine(f.Context.Storage.ArchiveRoot, Namespace(e.Kind), "foreign.db"); File.WriteAllText(foreign, "foreign");
        var before = ArchiveTestFixture.Snapshot(f.Root); var history = new BackupHistoryReader().Read(f.Context);
        Assert.Single(history.Entries); Assert.Contains(history.Issues.Issues, i => i.Code == NapIssueCodes.BackupUnmanaged);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.DoesNotContain(typeof(BackupHistoryEntry).GetProperties(), p => p.Name.Contains("Cloud") || p.Name.Contains("Synced") || p.Name.Contains("Remote"));
    }
    [Fact]
    public void DeterministicUtcOrderingUsesMetadataAndIdInsteadOfFilesystemTimes()
    {
        using var f = new BackupRepositoryFixture(); var s = new RepositorySnapshotService();
        var date = DateTimeOffset.Parse("2026-10-07T00:00:00Z"); Set(s, "Clock", new Clock(date));
        Set(s, "NewId", (Func<BackupId>)(() => new("b_00000000000000000000000000000002"))); var second = s.Create(f.Context);
        Set(s, "NewId", (Func<BackupId>)(() => new("b_00000000000000000000000000000001"))); var first = s.Create(f.Context);
        File.SetLastWriteTimeUtc(ManifestPath(f.Context, second), date.AddYears(1).UtcDateTime);
        Assert.Equal(new[] { first.BackupId, second.BackupId }, new BackupHistoryReader().Read(f.Context).Entries.Select(e => e.BackupId));
    }
    [Fact]
    public void AllThreeKindsAreSelfContainedAndDiscoveredTogether()
    {
        using var f = new BackupRepositoryFixture(git: true); new AssetCatalog(f.Context).Initialize();
        new DatabaseBackupService().Create(f.Context); new RepositorySnapshotService().Create(f.Context); new GitBundleService().Create(f.Context);
        // Loss of active catalog does not lose historical metadata.
        File.Delete(f.Context.Storage.CatalogPath);
        Directory.Move(f.Context.Storage.ProductionRoot, Path.Combine(f.Root, "offline-production"));
        Assert.Equal(3, new BackupHistoryReader().Read(f.Context).Entries.Count);
    }
    [Theory] [InlineData("bytes")] [InlineData("schema")] [InlineData("universe")]
    [InlineData("fk")] [InlineData("logical")]
    public void ValidSizeAndHashCannotHideAnInvalidDatabase(string mode)
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var e = new DatabaseBackupService().Create(f.Context); var artifact = Artifact(f.Context, e);
        if (mode == "bytes") File.WriteAllText(artifact, "invalid SQLite");
        else
        {
            using var c = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = artifact, Pooling = false, ForeignKeys = false }.ToString()); c.Open();
            CatalogTestFixture.Execute(c, mode switch
            {
                "schema" => "PRAGMA user_version=2",
                "universe" => "UPDATE catalog_metadata SET universe_id='beta'",
                "fk" => "INSERT INTO objective_classifications VALUES('future_world','missing','d','v')",
                _ => "UPDATE documents SET content=X'1234' WHERE role='prompt'"
            });
        }
        Mutate(f.Context, e, j => { j["size"] = new FileInfo(artifact).Length; j["sha256"] = new Sha256Hasher().Compute(artifact).Hex; });
        Stop(() => new BackupHistoryReader().Read(f.Context)); Stop(() => new DatabaseRestoreService().PlanRestore(f.Context, e.BackupId));
    }
    [Theory] [InlineData("head")] [InlineData("dirty")] [InlineData("contents")]
    public void GitFactsAreClosedAndRequired(string field)
    {
        using var f = new BackupRepositoryFixture(git: true); var e = new GitBundleService().Create(f.Context);
        Mutate(f.Context, e, j => j[field] = null); Stop(() => new BackupHistoryReader().Read(f.Context), NapIssueCodes.BackupManifestInvalid);
    }
}
