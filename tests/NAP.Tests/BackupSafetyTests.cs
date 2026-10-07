using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class BackupSafetyTests
{
    [Theory] [InlineData("production")] [InlineData("git_metadata")] [InlineData("archive")]
    [InlineData("namespace")] [InlineData("artifact")]
    public void RealReparsePointsStopWithoutFollowingOtherRoot(string mode)
    {
        using var f = new BackupRepositoryFixture(); using var other = new BackupRepositoryFixture("beta");
        var target = other.Context.Storage.ProductionRoot; var link = mode switch
        {
            "production" => Path.Combine(f.Context.Storage.ProductionRoot, "linked"),
            "git_metadata" => Path.Combine(f.Context.Storage.ProductionRoot, ".git"),
            "archive" => f.Context.Storage.ArchiveRoot,
            "namespace" => Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_REPOSITORY_SNAPSHOTS"),
            _ => ""
        };
        if (mode == "artifact")
        {
            var e = new RepositorySnapshotService().Create(f.Context); link = Artifact(f.Context, e);
            Directory.Move(link, link + "_original");
        }
        if (mode == "archive") Directory.Delete(link);
        var before = ArchiveTestFixture.Snapshot(other.Root); ArchiveTestFixture.Junction(link, target);
        try
        {
            if (mode == "artifact") Stop(() => new BackupHistoryReader().Read(f.Context));
            else Stop(() => new RepositorySnapshotService().Create(f.Context));
            ArchiveTestFixture.AssertSnapshot(before, other.Root);
        }
        finally { Directory.Delete(link); }
    }
    [Theory] [InlineData("StateRoot")] [InlineData("database_namespace")]
    public void DatabaseBackupRejectsLinkedStateOrDestination(string mode)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize();
        var link = mode == "StateRoot" ? f.Context.Storage.StateRoot : Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_DATABASE_BACKUPS");
        var moved = Path.Combine(f.Root, "external-state");
        if (mode == "StateRoot") Directory.Move(link, moved); else Directory.CreateDirectory(moved);
        var before = ArchiveTestFixture.Snapshot(moved); ArchiveTestFixture.Junction(link, moved);
        try { Stop(() => new DatabaseBackupService().Create(f.Context)); ArchiveTestFixture.AssertSnapshot(before, moved); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public void RetentionStopsForReparseInsertedAfterPlanAndPreservesAllArtifacts()
    {
        using var f = new BackupRepositoryFixture(); using var other = new BackupRepositoryFixture("beta");
        var creator = new RepositorySnapshotService(); creator.Create(f.Context); creator.Create(f.Context);
        var s = new BackupRetentionService(); var p = s.Plan(f.Context, new(1));
        var link = Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_REPOSITORY_SNAPSHOTS", "foreign_link");
        var before = ArchiveTestFixture.Snapshot(other.Root); ArchiveTestFixture.Junction(link, other.Context.Storage.ArchiveRoot);
        try { Stop(() => s.Execute(f.Context, p)); ArchiveTestFixture.AssertSnapshot(before, other.Root); Assert.Equal(2, RecognizedDirectories(f.Context, BackupKind.RepositorySnapshot).Length); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public async Task SimultaneousDatabaseBackupsStopBusyWithoutDeadlockAndReleaseBothLeases()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var s = new DatabaseBackupService();
        using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Set(s, "Observer", (Action<string>)(_ => { ready.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test barrier timed out."); }));
        var first = Task.Run(() => s.Create(f.Context));
        try { Assert.True(ready.Wait(TimeSpan.FromSeconds(10))); Stop(() => new DatabaseBackupService().Create(f.Context), NapIssueCodes.BackupBusy); }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        new DatabaseBackupService().Create(f.Context); Assert.Equal(2, new BackupHistoryReader().Read(f.Context).Entries.Count);
    }
    [Fact]
    public async Task DatabaseBackupCoordinatesWithExistingArchiveMasterWriter()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var s = new DatabaseBackupService();
        using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Set(s, "Observer", (Action<string>)(_ => { ready.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test barrier timed out."); }));
        var task = Task.Run(() => s.Create(f.Context));
        try { Assert.True(ready.Wait(TimeSpan.FromSeconds(10))); Assert.Throws<IOException>(() => f.Production.Archive()); }
        finally { release.Set(); }
        await task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
    }
    [Fact]
    public async Task SnapshotAcquiresProductionBeforeArchiveAndStopsCompetingProductionWriter()
    {
        using var f = new BackupRepositoryFixture(); var s = new RepositorySnapshotService();
        using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Set(s, "Observer", (Action<string>)(stage => { if (stage == "source_captured") { ready.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test barrier timed out."); } }));
        var task = Task.Run(() => s.Create(f.Context));
        try { Assert.True(ready.Wait(TimeSpan.FromSeconds(10))); Stop(() => new RepositorySnapshotService().Create(f.Context), NapIssueCodes.BackupBusy); }
        finally { release.Set(); }
        await task.WaitAsync(TimeSpan.FromSeconds(10)); using var production = Lock(f.Context, "Production");
    }
    [Fact]
    public void ExternalCatalogChangeDuringBackupStopsPublication()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var s = new DatabaseBackupService();
        Set(s, "Observer", (Action<string>)(_ => { using var c = f.Raw(); CatalogTestFixture.Execute(c, "INSERT INTO objectives VALUES($u,'external','external',1,NULL,NULL,NULL)", ("$u", f.Context.Id.Value)); }));
        Stop(() => s.Create(f.Context)); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.Database));
    }
    [Fact]
    public void LinuxFifoIsRejectedWithoutOpeningAndBlocking()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new BackupRepositoryFixture(); var path = Path.Combine(f.Context.Storage.ProductionRoot, "fifo");
        var start = new System.Diagnostics.ProcessStartInfo("mkfifo") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(path); using var p = System.Diagnostics.Process.Start(start)!;
        Assert.True(p.WaitForExit(5000)); Assert.Equal(0, p.ExitCode);
        Stop(() => new RepositorySnapshotService().Create(f.Context), NapIssueCodes.BackupSourceInvalid);
        Assert.Empty(RecognizedDirectories(f.Context, BackupKind.RepositorySnapshot));
    }
}
