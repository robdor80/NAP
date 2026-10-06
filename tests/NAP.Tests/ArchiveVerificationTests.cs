using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveVerificationTests
{
    [Theory]
    [InlineData("bytes")]
    [InlineData("size")]
    [InlineData("missing")]
    [InlineData("reparse")]
    public void SourceChangesAfterPlanStopBeforeAnyArchiveWrite(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        var file = plan.Files.Single(f => f.Role == "prompt");
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "source_link_target")).FullName;
        if (mode == "bytes")
        {
            var bytes = File.ReadAllBytes(file.SourcePath);
            bytes[0] ^= 1;
            File.WriteAllBytes(file.SourcePath, bytes);
        }
        if (mode == "size") File.AppendAllText(file.SourcePath, "extra");
        if (mode is "missing" or "reparse") File.Delete(file.SourcePath);
        if (mode == "reparse") ArchiveTestFixture.Junction(file.SourcePath, target);
        try
        {
            var before = ArchiveTestFixture.Snapshot(fixture.Context.Storage.ArchiveRoot);
            ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)),
                mode == "reparse" ? NapIssueCodes.ArchiveEntryReparse : NapIssueCodes.ArchiveSourceChanged);
            ArchiveTestFixture.AssertSnapshot(before, fixture.Context.Storage.ArchiveRoot);
            Assert.False(File.Exists(fixture.Store.IndexPath));
            Assert.False(File.Exists(fixture.LockPath));
        }
        finally { if (mode == "reparse") Directory.Delete(file.SourcePath); }
    }

    [Theory]
    [InlineData("different")]
    [InlineData("reparse")]
    [InlineData("index_collision")]
    [InlineData("index_corrupt")]
    [InlineData("root_missing")]
    public void ChangedArchiveStateIsRevalidatedUnderLock(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination_link_target")).FullName;
        var link = Path.Combine(plan.ArchiveRoot, "portraits");
        string code;
        if (mode == "different")
        {
            Directory.CreateDirectory(plan.DestinationDirectory);
            File.WriteAllText(plan.Files[0].DestinationPath, "different appeared final");
            code = NapIssueCodes.ArchiveFileCollision;
        }
        else if (mode == "reparse") { ArchiveTestFixture.Junction(link, target); code = NapIssueCodes.ArchiveEntryReparse; }
        else if (mode == "index_collision")
        {
            fixture.WriteIndex(ArchiveTestFixture.ValidIndex(entries: "[" + ArchiveTestFixture.ValidEntry() + "]"));
            code = NapIssueCodes.ArchiveAssetCollision;
        }
        else if (mode == "index_corrupt") { fixture.WriteIndex("{corrupt"); code = NapIssueCodes.ArchiveIndexInvalid; }
        else
        {
            File.Delete(Path.Combine(plan.ArchiveRoot, "sentinel.txt"));
            Directory.Delete(plan.ArchiveRoot);
            code = NapIssueCodes.ArchiveRootMissing;
        }
        try
        {
            var before = ArchiveTestFixture.Snapshot(fixture.Root);
            ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), code);
            ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
            Assert.False(File.Exists(fixture.LockPath));
        }
        finally { if (mode == "reparse") Directory.Delete(link); }
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("size")]
    public void CorruptTempIsNeverPublishedOrIndexed(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(plan.DestinationDirectory);
        var file = plan.Files[0];
        var temp = file.DestinationPath + $".{Guid.NewGuid():N}.tmp";
        var bytes = File.ReadAllBytes(file.SourcePath);
        if (mode == "bytes") bytes[0] ^= 1;
        else bytes = bytes.Concat(new byte[] { 0 }).ToArray();
        File.WriteAllBytes(temp, bytes);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() =>
            ArchiveTestFixture.Invoke(new ArchiveMasterExecutor(fixture.Context), typeof(ArchiveMasterExecutor), "PublishFile", temp, file)), NapIssueCodes.ArchiveVerificationFailed);
        Assert.False(File.Exists(file.DestinationPath));
        Assert.False(File.Exists(fixture.Store.IndexPath));
        Assert.Equal(bytes, File.ReadAllBytes(temp));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("size")]
    [InlineData("missing")]
    [InlineData("reparse")]
    public void FinalVerificationRejectsCorruptMissingAndLinkedFinals(string mode)
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        fixture.PrepareFinals(plan, 5);
        var file = plan.Files[0];
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "final_link_target")).FullName;
        if (mode == "bytes")
        {
            var bytes = File.ReadAllBytes(file.DestinationPath);
            bytes[0] ^= 1;
            File.WriteAllBytes(file.DestinationPath, bytes);
        }
        if (mode == "size") File.AppendAllText(file.DestinationPath, "extra");
        if (mode is "missing" or "reparse") File.Delete(file.DestinationPath);
        if (mode == "reparse") ArchiveTestFixture.Junction(file.DestinationPath, target);
        try
        {
            var before = ArchiveTestFixture.Snapshot(plan.ArchiveRoot);
            ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => ArchiveTestFixture.Invoke(null, typeof(ArchiveMasterPlanner), "VerifyFile",
                file.DestinationPath, file, NapIssueCodes.ArchiveVerificationFailed)),
                mode == "reparse" ? NapIssueCodes.ArchiveEntryReparse : NapIssueCodes.ArchiveVerificationFailed);
            if (mode != "missing") Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan));
            ArchiveTestFixture.AssertSnapshot(before, plan.ArchiveRoot);
            Assert.False(File.Exists(fixture.Store.IndexPath));
        }
        finally { if (mode == "reparse") Directory.Delete(file.DestinationPath); }
    }

    [Fact]
    public void FinalAppearingDuringCopyIsNotAutomaticallyAcceptedEvenWithIdenticalBytes()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(plan.DestinationDirectory);
        var file = plan.Files[0];
        var temp = file.DestinationPath + $".{Guid.NewGuid():N}.tmp";
        var bytes = File.ReadAllBytes(file.SourcePath);
        File.WriteAllBytes(temp, bytes);
        File.WriteAllBytes(file.DestinationPath, bytes);
        var before = ArchiveTestFixture.Snapshot(plan.ArchiveRoot);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => ArchiveTestFixture.Invoke(
            new ArchiveMasterExecutor(fixture.Context), typeof(ArchiveMasterExecutor), "PublishFile", temp, file)), NapIssueCodes.ArchiveFileCollision);
        ArchiveTestFixture.AssertSnapshot(before, plan.ArchiveRoot);
        Assert.False(File.Exists(fixture.Store.IndexPath));
    }

    [Fact]
    public void PublicationFailureLeavesVerifiedMastersForIndexExistingRecovery()
    {
        using var fixture = new ArchiveTestFixture();
        fixture.WriteIndex(ArchiveTestFixture.ValidIndex());
        var plan = fixture.Plan();
        var original = File.ReadAllBytes(fixture.Store.IndexPath);
        using (var reader = new FileStream(fixture.Store.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => fixture.Execute(plan));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.Equal(original, File.ReadAllBytes(fixture.Store.IndexPath));
            Assert.All(plan.Files, file => Assert.Equal(File.ReadAllBytes(file.SourcePath), File.ReadAllBytes(file.DestinationPath)));
        }
        var stamps = plan.Files.ToDictionary(f => f.DestinationPath, f => File.GetLastWriteTimeUtc(f.DestinationPath));
        var recovery = fixture.Plan();
        Assert.Equal(ArchiveMasterAction.IndexExisting, recovery.Action);
        Assert.Empty(recovery.FilesToCopy);
        Assert.Equal(ArchiveMasterOutcome.IndexedExisting, fixture.Execute(recovery).Outcome);
        Assert.All(stamps, stamp => Assert.Equal(stamp.Value, File.GetLastWriteTimeUtc(stamp.Key)));
        Assert.Single(fixture.Store.Load().Entries);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(fixture.Store.IndexPath)!, "master_index.*.tmp"));
    }

    [Fact]
    public void TechnicalFilesCannotBeDirectoriesOrJunctions()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        Directory.CreateDirectory(fixture.LockPath);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), NapIssueCodes.ArchiveFileCollision);
        Assert.False(File.Exists(fixture.Store.IndexPath));
        Directory.Delete(fixture.LockPath);
        Directory.CreateDirectory(fixture.Store.IndexPath);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Store.Load()), NapIssueCodes.ArchiveIndexInvalid);
        Directory.Delete(fixture.Store.IndexPath);
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "technical_target")).FullName;
        foreach (var path in new[] { fixture.LockPath, fixture.Store.IndexPath })
        {
            ArchiveTestFixture.Junction(path, target);
            try
            {
                ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Execute(plan)), NapIssueCodes.ArchiveEntryReparse);
            }
            finally { Directory.Delete(path); }
        }
    }
}
