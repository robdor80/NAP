using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class BackupRetentionTests
{
    [Theory] [InlineData(2, 0, 0, 0, 2)] [InlineData(0, 2, 0, 0, 2)] [InlineData(0, 0, 2, 0, 2)]
    [InlineData(0, 0, 0, 2, 2)] [InlineData(2, 2, 2, 2, 4)] [InlineData(0, 0, 0, 0, 1)]
    public void UtcCalendarBucketsAndNewestProtectionAreDeterministicAndPlanningIsReadOnly(int newest, int daily, int weekly, int monthly, int count)
    {
        using var f = new BackupRepositoryFixture(); var creator = new RepositorySnapshotService(); var entries = new List<BackupHistoryEntry>();
        foreach (var date in new[] { "2026-01-01T00:00:00Z", "2026-01-02T00:00:00Z", "2026-02-02T00:00:00Z", "2026-02-03T00:00:00Z", "2026-02-03T01:00:00Z" })
        { Set(creator, "Clock", new Clock(DateTimeOffset.Parse(date))); entries.Add(creator.Create(f.Context)); }
        var before = ArchiveTestFixture.Snapshot(f.Root); var service = new BackupRetentionService();
        var p = service.Plan(f.Context, new(newest, daily, weekly, monthly)); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal(count, p.Decisions.Count(d => d.Action == BackupRetentionAction.Keep));
        Assert.Contains(p.Decisions, d => d.Backup.BackupId == entries.Last().BackupId && d.Action == BackupRetentionAction.Keep);
        var deleted = p.Decisions.Where(d => d.Action == BackupRetentionAction.Delete).Select(d => d.Backup.BackupId).ToArray();
        Assert.Equal(deleted, service.Execute(f.Context, p).DeletedBackupIds);
        Assert.Equal(count, new BackupHistoryReader().Read(f.Context).Entries.Count);
        Stop(() => service.Execute(f.Context, p), NapIssueCodes.RetentionStalePlan);
    }
    [Theory] [InlineData("new_backup")] [InlineData("artifact_changed")] [InlineData("manifest_changed")]
    [InlineData("foreign_added")] [InlineData("foreign_changed")] [InlineData("backup_removed")]
    public void ChangedHistoryStopsEntirePlanBeforeAnyDeletion(string mode)
    {
        using var f = new BackupRepositoryFixture(); var creator = new RepositorySnapshotService(); var a = creator.Create(f.Context); creator.Create(f.Context);
        var foreign = Path.Combine(f.Context.Storage.ArchiveRoot, Namespace(a.Kind), "foreign.txt"); File.WriteAllText(foreign, "foreign");
        var s = new BackupRetentionService(); var p = s.Plan(f.Context, new(1));
        switch (mode)
        {
            case "new_backup": creator.Create(f.Context); break;
            case "artifact_changed": File.AppendAllText(Path.Combine(Artifact(f.Context, a), "README.md"), "changed"); break;
            case "manifest_changed": File.AppendAllText(ManifestPath(f.Context, a), "\n"); break;
            case "foreign_added": File.WriteAllText(foreign + ".new", "new"); break;
            case "foreign_changed": File.WriteAllText(foreign, "changed"); break;
            case "backup_removed": Directory.Move(DirectoryPath(f.Context, a), DirectoryPath(f.Context, a) + "-moved"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root); Stop(() => s.Execute(f.Context, p), NapIssueCodes.RetentionStalePlan); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void RetentionProtectsMastersIndexUnmanagedEntriesAndOtherUniverseAndNewestOfEveryKind()
    {
        using var a = new BackupRepositoryFixture("alpha", git: true); using var b = new BackupRepositoryFixture("beta", git: true);
        new AssetCatalog(a.Context).Initialize(); new AssetCatalog(b.Context).Initialize();
        var master = Path.Combine(a.Context.Storage.ArchiveRoot, "masters"); Directory.CreateDirectory(master); File.WriteAllText(Path.Combine(master, "original.png"), "master");
        var index = Path.Combine(a.Context.Storage.ArchiveRoot, "_nap", "master_index.json"); Directory.CreateDirectory(Path.GetDirectoryName(index)!); File.WriteAllText(index, "index evidence");
        new DatabaseBackupService().Create(b.Context); new GitBundleService().Create(b.Context); new RepositorySnapshotService().Create(b.Context);
        var other = ArchiveTestFixture.Snapshot(b.Root);
        for (var n = 0; n < 2; n++) { new DatabaseBackupService().Create(a.Context); new RepositorySnapshotService().Create(a.Context); new GitBundleService().Create(a.Context); }
        var foreign = Path.Combine(a.Context.Storage.ArchiveRoot, "NAP_DATABASE_BACKUPS", "foreign.db"); File.WriteAllText(foreign, "unknown");
        var s = new BackupRetentionService(); var p = s.Plan(a.Context, new(0));
        Assert.Equal(3, p.Decisions.Count(d => d.Action == BackupRetentionAction.Keep)); Assert.Equal(3, s.Execute(a.Context, p).DeletedBackupIds.Count);
        Assert.Equal("master", File.ReadAllText(Path.Combine(master, "original.png"))); Assert.Equal("index evidence", File.ReadAllText(index)); Assert.Equal("unknown", File.ReadAllText(foreign));
        ArchiveTestFixture.AssertSnapshot(other, b.Root); Assert.Equal(3, new BackupHistoryReader().Read(a.Context).Entries.Count);
        Stop(() => s.Execute(b.Context, p), NapIssueCodes.RetentionStalePlan);
    }
    [Fact]
    public void InterruptedExecutionReportsExactProgressAndLeavesRecognizedUnitsIntact()
    {
        using var f = new BackupRepositoryFixture(); var creator = new RepositorySnapshotService();
        for (var n = 0; n < 3; n++) creator.Create(f.Context);
        var s = new BackupRetentionService(); var p = s.Plan(f.Context, new(1));
        Set(s, "Observer", (Action<string>)(stage => { if (stage == "deleted") throw new IOException("injected cleanup failure"); }));
        var e = Stop(() => s.Execute(f.Context, p), NapIssueCodes.RetentionExecutionFailed); Assert.Single(e.CompletedBackupIds);
        Assert.Equal(2, new BackupHistoryReader().Read(f.Context).Entries.Count);
    }
    [Fact]
    public void LastMomentChangeIsRejectedBeforeDeletion()
    {
        using var f = new BackupRepositoryFixture(); var creator = new RepositorySnapshotService(); creator.Create(f.Context); creator.Create(f.Context);
        var s = new BackupRetentionService(); var p = s.Plan(f.Context, new(1));
        Set(s, "Observer", (Action<string>)(stage => { if (stage == "revalidated") File.WriteAllText(Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_REPOSITORY_SNAPSHOTS", "foreign"), "new"); }));
        Stop(() => s.Execute(f.Context, p), NapIssueCodes.RetentionStalePlan); Assert.Equal(2, RecognizedDirectories(f.Context, BackupKind.RepositorySnapshot).Length);
    }
    [Fact]
    public void PoliciesRejectNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupRetentionPolicy(-1)); Assert.Throws<ArgumentOutOfRangeException>(() => new BackupRetentionPolicy(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupRetentionPolicy(1, weekly: -1)); Assert.Throws<ArgumentOutOfRangeException>(() => new BackupRetentionPolicy(1, monthly: -1));
    }
}
