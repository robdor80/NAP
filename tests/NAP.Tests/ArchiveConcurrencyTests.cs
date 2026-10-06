using System.Diagnostics;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveConcurrencyTests
{
    [Fact]
    public void ExclusiveLockStopsAnotherExecutorWithoutRetriesOrWrites()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.LockPath)!);
        using var owner = new FileStream(fixture.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var before = ArchiveTestFixture.Snapshot(fixture.Package.PackageRoot);
        Assert.Throws<IOException>(() => fixture.Execute(plan));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Package.PackageRoot);
        Assert.False(File.Exists(fixture.Store.IndexPath));
        Assert.False(Directory.Exists(plan.DestinationDirectory));
    }

    [Fact]
    public async Task CrossProcessFileLockIsExclusive()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.LockPath)!);
        // The child signals readiness only after acquiring FileShare.None, then waits on stdin; no sleep or timing race.
        var command = "$s=[IO.FileStream]::new('" + fixture.LockPath.Replace("'", "''") +
            "',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); [Console]::WriteLine('LOCKED'); [Console]::Out.Flush(); [Console]::ReadLine() | Out-Null; $s.Dispose()";
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);
        using var process = Process.Start(info)!;
        try
        {
            Assert.Equal("LOCKED", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Throws<IOException>(() => fixture.Execute(plan));
            Assert.False(File.Exists(fixture.Store.IndexPath));
            Assert.False(Directory.Exists(plan.DestinationDirectory));
        }
        finally
        {
            process.StandardInput.WriteLine("RELEASE");
            process.StandardInput.Flush();
            if (!process.WaitForExit(10000)) process.Kill();
        }
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(ArchiveMasterOutcome.Copied, fixture.Execute(plan).Outcome);
    }

    [Fact]
    public void TwoStalePlansReloadCurrentIndexAndPreserveBothEntries()
    {
        using var fixture = new ArchiveTestFixture();
        var first = fixture.Plan();
        var secondPackage = ArchiveExecutionTests.AddPackage(fixture, "portrait_treskal_farmer_male_041");
        var second = new ArchiveMasterPlanner(fixture.Context).Plan(secondPackage.Package, secondPackage.Plan);
        Assert.True(first.Issues.IsClean);
        Assert.True(second.Issues.IsClean);
        fixture.Execute(first);
        var firstBytes = first.Files.ToDictionary(file => file.DestinationPath, file => File.ReadAllBytes(file.DestinationPath));
        var secondResult = fixture.Execute(second);
        Assert.Equal(NapIssueCodes.ArchivePossibleDuplicate, Assert.Single(secondResult.Issues.Issues).Code);
        var index = fixture.Store.Load();
        Assert.Equal(new[] { first.AssetKey.AssetId, second.AssetKey.AssetId }, index.Entries.Select(e => e.AssetId));
        Assert.All(firstBytes, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
        var before = ArchiveTestFixture.Snapshot(first.ArchiveRoot);
        Assert.Equal(ArchiveMasterOutcome.AlreadyArchived, fixture.Execute(first).Outcome);
        ArchiveTestFixture.AssertSnapshot(before, first.ArchiveRoot);
    }

    [Fact]
    public void NewCandidateCollisionInIndexAfterPlanningCannotBeWritten()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        fixture.WriteIndex(ArchiveTestFixture.ValidIndex(entries: "[" + ArchiveTestFixture.ValidEntry() + "]"));
        var before = ArchiveTestFixture.Snapshot(plan.ArchiveRoot);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), NapIssueCodes.ArchiveAssetCollision);
        ArchiveTestFixture.AssertSnapshot(before, plan.ArchiveRoot);
    }

    [Fact]
    public void DisposedOrForeignLeaseCannotPublishAnIndex()
    {
        using var fixture = new ArchiveTestFixture();
        var type = ArchiveTestFixture.CoreType("ArchiveLock");
        var lease = (IDisposable)ArchiveTestFixture.Invoke(null, type, "Acquire", fixture.Context)!;
        Assert.Throws<InvalidOperationException>(() => ArchiveTestFixture.Invoke(fixture.Store, typeof(ArchiveMasterIndexStore), "Publish", new ArchiveMasterIndex(fixture.Context.Id, []), lease));
        ArchiveTestFixture.Invoke(lease, type, "EnableWrites");
        using (var other = new ArchiveTestFixture(ArchiveTestFixture.Generic()))
            Assert.Throws<ArgumentException>(() => ArchiveTestFixture.Invoke(other.Store, typeof(ArchiveMasterIndexStore), "Publish", new ArchiveMasterIndex(other.Context.Id, []), lease));
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ArchiveTestFixture.Invoke(fixture.Store, typeof(ArchiveMasterIndexStore), "Publish", new ArchiveMasterIndex(fixture.Context.Id, []), lease));
        Assert.False(File.Exists(fixture.Store.IndexPath));
    }
}
