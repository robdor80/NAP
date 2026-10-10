using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;
using Xunit.Abstractions;

namespace NAP.Tests;

public sealed class InboxAdmissionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(1000)]
    [Trait("Category", "AdmissionVolume")]
    public void MonitorAdmitsVolumeWithBoundedPendingAndDurableDeduplication(int count)
    {
        using var f = new Fixture();
        for (var i = 0; i < count; i++) f.Zip($"input-{i:D4}.zip", asset: $"portrait_sample{i}_001");
        var originals = f.InboxHashes(); var timer = Stopwatch.StartNew(); long peak = 0;
        using (var monitor = new InboxAdmissionMonitor([f.Context], Fixture.Fast with { MaxPendingCandidates = 7 }))
        {
            monitor.Start();
            Until(() => monitor.AdmittedCount == count || monitor.Status().Any(s => s.State == InboxMonitorState.Blocked), TimeSpan.FromMinutes(20), () => peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64));
            Assert.All(monitor.Status(), s => Assert.Equal(InboxMonitorState.Monitoring, s.State));
            Assert.Equal(count, monitor.AdmittedCount); Assert.InRange(monitor.MaximumPendingObserved, 1, 7);
        }
        output.WriteLine($"{count} ZIP: {timer.Elapsed.TotalSeconds:F3}s, sampled process working set peak={peak} bytes (includes test runner).");
        var items = f.Items(); Assert.Equal(count, items.Count); Assert.All(items, i => Assert.Equal(QueueState.Queued, i.State));
        var jobs = items.Select(i => f.Queue.GetAttempt(i.ActiveAttempt!).JobId).ToArray(); Assert.Equal(count, jobs.Distinct().Count());
        using (var owner = f.Queue.AcquireOwner())
            foreach (var source in Directory.EnumerateFiles(f.Roots.InboxRoot)) Assert.Equal(InboxAdmissionCode.AlreadyKnown, f.Service.Admit(owner, source).Code);
        Assert.Equal(jobs, f.Items().Select(i => f.Queue.GetAttempt(i.ActiveAttempt!).JobId));
        Assert.Equal(JsonSerializer.Serialize(originals), JsonSerializer.Serialize(f.InboxHashes())); f.AssertUntouched();
    }
    [Fact]
    public void AliasNamesShareOneItemAndJobWhileChangedBytesAtSameNameAreIndependent()
    {
        using var f = new Fixture(); var path = f.Zip("unrelated-name.zip"); var bytes = File.ReadAllBytes(path);
        using var owner = f.Queue.AcquireOwner(); var first = f.Service.Admit(owner, path);
        Assert.Equal(InboxAdmissionCode.Admitted, first.Code);
        var alias = Path.Combine(f.Roots.InboxRoot, "alias.ZIP"); File.WriteAllBytes(alias, bytes);
        var second = f.Service.Admit(owner, alias); Assert.Equal(first.ItemId, second.ItemId); Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(2, f.Queue.Observations(first.ItemId!).Count);
        f.Zip("unrelated-name.zip", content: "different bytes"); var changed = f.Service.Admit(owner, path);
        Assert.Equal(InboxAdmissionCode.Admitted, changed.Code); Assert.NotEqual(first.ItemId, changed.ItemId); Assert.NotEqual(first.JobId, changed.JobId);
        Assert.Equal(2, f.Queue.List().Count); f.AssertUntouched();
    }
    [Fact]
    public void CrossedUniverseIsRejectedWithDurableExpectedAndDeclaredEvidenceWithoutJournal()
    {
        using var f = new Fixture(); var source = f.Zip("crossed.zip", declared: "foreign_universe"); var before = File.ReadAllBytes(source);
        using var owner = f.Queue.AcquireOwner(); var result = f.Service.Admit(owner, source);
        Assert.Equal(InboxAdmissionCode.UniverseMismatch, result.Code); Assert.Equal("foreign_universe", result.DeclaredUniverse);
        var item = f.Queue.Get(result.ItemId!); Assert.Equal(QueueState.Rejected, item.State); Assert.Equal(QueueIncident.UniverseMismatch, item.Incident);
        var receipt = f.Service.GetReceipt(item.Id); Assert.Equal(f.Context.Id, receipt.UniverseId); Assert.Equal("foreign_universe", receipt.DeclaredUniverse);
        Assert.False(File.Exists(f.Journal(result.JobId!))); Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, f.Zip("good.zip")).Code); f.AssertUntouched();
    }
    [Fact]
    public void TwoUniversesMonitorIndependentlyIncludingTheSameAssetId()
    {
        using var a = new Fixture("universe_a"); using var b = new Fixture("universe_b");
        a.Zip("a.zip"); b.Zip("b.zip");
        using (var monitor = new InboxAdmissionMonitor([a.Context, b.Context], Fixture.Fast))
        { monitor.Start(); Until(() => monitor.AdmittedCount == 2); Assert.All(monitor.Status(), s => Assert.Equal(InboxMonitorState.Monitoring, s.State)); }
        var ai = Assert.Single(a.Queue.List()); var bi = Assert.Single(b.Queue.List());
        Assert.NotEqual(ai.Id, bi.Id); Assert.Equal(a.Context.Id, ai.UniverseId); Assert.Equal(b.Context.Id, bi.UniverseId);
        Assert.NotEqual(a.Queue.GetAttempt(ai.ActiveAttempt!).JobId, b.Queue.GetAttempt(bi.ActiveAttempt!).JobId); a.AssertUntouched(); b.AssertUntouched();
    }
    [Fact]
    public void PartialIsIgnoredAndItsFinalRenameTriggersAdmissionWithoutExplicitRescan()
    {
        using var f = new Fixture(); var zip = f.Zip("candidate.partial");
        using var monitor = new InboxAdmissionMonitor([f.Context], Fixture.Fast); monitor.Start();
        Thread.Sleep(100); Assert.Equal(0, monitor.AdmittedCount);
        var final = Path.Combine(f.Roots.InboxRoot, "candidate.zip"); File.Move(zip, final);
        Until(() => monitor.AdmittedCount == 1); Assert.True(File.Exists(final)); f.AssertUntouched();
    }
    [Fact]
    public void IncompletePausedCopyNeverAllocatesAnAttemptAndCompletesUnderItsActualNewHash()
    {
        using var f = new Fixture(); var path = f.Zip("slow.zip"); var complete = File.ReadAllBytes(path);
        File.WriteAllBytes(path, complete[..(complete.Length / 2)]);
        using var owner = f.Queue.AcquireOwner();
        for (var i = 0; i < 4; i++) { Assert.Equal(InboxAdmissionCode.IncompleteZip, f.Service.Admit(owner, path).Code); Thread.Sleep(50); }
        var waiting = Assert.Single(f.Queue.List()); Assert.Equal(QueueState.WaitingStable, waiting.State); Assert.Null(waiting.ActiveAttempt);
        Assert.Empty(Directory.EnumerateFiles(f.Roots.StateRoot, "job_*.json"));
        File.WriteAllBytes(path, complete); var admitted = f.Service.Admit(owner, path); Assert.Equal(InboxAdmissionCode.Admitted, admitted.Code);
        Assert.NotEqual(waiting.Id, admitted.ItemId); f.AssertUntouched();
    }
    [Fact]
    public void ActiveWriterAndChangingFileAreNotAdmitted()
    {
        using var f = new Fixture(); var path = f.Zip("locked.zip"); using var owner = f.Queue.AcquireOwner();
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(InboxAdmissionCode.InUse, f.Service.Admit(owner, path).Code);
        var slow = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast with { SampleInterval = TimeSpan.FromMilliseconds(100) });
        var producer = new Thread(() => { Thread.Sleep(30); File.AppendAllText(path, "still copying"); }); producer.Start();
        Assert.Equal(InboxAdmissionCode.WaitingStable, slow.Admit(owner, path).Code); producer.Join();
        Assert.Empty(f.Queue.List()); f.AssertUntouched();
    }
    [Theory]
    [InlineData("../escape.txt")] [InlineData("/absolute.txt")] [InlineData("CON.txt")] [InlineData("asset:stream")]
    public void HostilePathsAreRejectedWithoutEscapeAndDoNotBlockNextPackage(string entry)
    {
        using var f = new Fixture(); var path = f.Zip("hostile.zip", extra: entry); using var owner = f.Queue.AcquireOwner();
        Assert.Equal(InboxAdmissionCode.UnsafeZip, f.Service.Admit(owner, path).Code);
        Assert.False(File.Exists(Path.Combine(f.Root, "escape.txt")));
        Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, f.Zip("valid.zip")).Code); f.AssertUntouched();
    }
    [Fact]
    public void InvalidCrcSymlinkEntryAndInvalidManifestAreRejectedIndividually()
    {
        using var f = new Fixture(); using var owner = f.Queue.AcquireOwner();
        var crc = AdversarialZipFactory.Create(AdversarialZipFactory.Text("content.txt"));
        crc.Bytes[crc.CentralHeaders[0] + 16] ^= 1;
        var corrupt = Path.Combine(f.Roots.InboxRoot, "crc.zip"); File.WriteAllBytes(corrupt, crc.Bytes);
        Assert.Equal(InboxAdmissionCode.InvalidZip, f.Service.Admit(owner, corrupt).Code);
        var link = AdversarialZipFactory.Create(AdversarialZipFactory.Text("link.txt", attributes: 0xA1FF << 16));
        var linkPath = Path.Combine(f.Roots.InboxRoot, "symlink.zip"); File.WriteAllBytes(linkPath, link.Bytes);
        Assert.Equal(InboxAdmissionCode.UnsafeZip, f.Service.Admit(owner, linkPath).Code);
        var bad = AdversarialZipFactory.Create(AdversarialZipFactory.Text("portrait_example_001_manifest.json", "{}"));
        var manifest = Path.Combine(f.Roots.InboxRoot, "manifest.zip"); File.WriteAllBytes(manifest, bad.Bytes);
        Assert.Equal(InboxAdmissionCode.InvalidManifest, f.Service.Admit(owner, manifest).Code);
        Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, f.Zip("good-after-errors.zip")).Code); f.AssertUntouched();
    }
    [Fact]
    public void SourceChangedAfterSnapshotPublicationCannotAuthorizeTheCandidate()
    {
        using var f = new Fixture(); var source = f.Zip("changed.zip");
        SetHook(f.Service, stage =>
        {
            if (stage == "snapshot-published")
            {
                // On Windows sharing prevents this mutation; Unix permits a hostile external writer.
                try { File.WriteAllText(source, "changed while copying"); } catch (IOException) { throw new IOException("Writer denied by source sharing"); }
            }
        });
        using var owner = f.Queue.AcquireOwner(); var result = f.Service.Admit(owner, source);
        Assert.Contains(result.Code, new[] { InboxAdmissionCode.SourceChanged, InboxAdmissionCode.StorageUnavailable });
        Assert.False(File.Exists(f.Journal(result.JobId!))); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(result.ItemId!).State); f.AssertUntouched();
    }
    [Fact]
    public void SymlinkZipAndSymlinkStagingFailClosed()
    {
        using var f = new Fixture(); var target = f.Zip("normal.partial"); var link = Path.Combine(f.Roots.InboxRoot, "link.zip"); File.CreateSymbolicLink(link, target);
        using var owner = f.Queue.AcquireOwner(); Assert.Equal(InboxAdmissionCode.UnsafeZip, f.Service.Admit(owner, link).Code);
        Directory.Delete(f.Roots.StagingRoot); Directory.CreateSymbolicLink(f.Roots.StagingRoot, f.Roots.ProductionRoot);
        Assert.Throws<ProductionStorageException>(() => f.Service.Admit(owner, f.Zip("good.zip"))); f.AssertUntouched();
    }
    [Theory]
    [InlineData("attempt-recorded", false)] [InlineData("intent-recorded", true)]
    [InlineData("snapshot-starting", false)] [InlineData("snapshot-published", false)]
    [InlineData("snapshot-recorded", true)] [InlineData("extraction-published", false)]
    [InlineData("extraction-recorded", true)] [InlineData("journal-created", true)] [InlineData("admission-recorded", true)]
    public void RestartKeepsJobIdAndOnlyResumesFinalizedEvidence(string checkpoint, bool recoverable)
    {
        using var f = new Fixture(); var source = f.Zip("restart.zip");
        SetHook(f.Service, value => { if (value == checkpoint) throw new SimulatedCrash(); });
        using (var owner = f.Queue.AcquireOwner()) Assert.Throws<SimulatedCrash>(() => f.Service.Admit(owner, source));
        var item = Assert.Single(f.Queue.List()); var job = f.Queue.GetAttempt(item.ActiveAttempt!).JobId;
        var reopened = new InboxAdmissionService(f.Context, new AutomationQueueStore(f.Roots), Fixture.Fast);
        using (var owner = f.Queue.AcquireOwner())
        {
            var result = reopened.Reconcile(owner, item.Id);
            Assert.Equal(recoverable ? checkpoint == "admission-recorded" ? InboxAdmissionCode.AlreadyKnown : InboxAdmissionCode.Admitted : InboxAdmissionCode.AmbiguousEvidence, result.Code);
        }
        Assert.Equal(job, f.Queue.GetAttempt(item.ActiveAttempt!).JobId); Assert.Equal(1, f.Queue.Get(item.Id).AttemptCount);
        if (recoverable) Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(job).State);
        else Assert.Equal(QueueState.NeedsReview, f.Queue.Get(item.Id).State);
        f.AssertUntouched();
    }
    [Fact]
    public void MissingOrChangedSourceAtDurableIntentIsNeverSubstituted()
    {
        using var f = new Fixture(); var source = f.Zip("gone.zip"); SetHook(f.Service, s => { if (s == "intent-recorded") throw new SimulatedCrash(); });
        using (var owner = f.Queue.AcquireOwner()) Assert.Throws<SimulatedCrash>(() => f.Service.Admit(owner, source));
        var item = Assert.Single(f.Queue.List()); File.Delete(source);
        using var owner2 = f.Queue.AcquireOwner(); var fresh = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast);
        Assert.Equal(InboxAdmissionCode.MissingSource, fresh.Reconcile(owner2, item.Id).Code);
        f.Zip("gone.zip", content: "replacement"); Assert.Equal(InboxAdmissionCode.SourceChanged, fresh.Reconcile(owner2, item.Id).Code);
        Assert.False(File.Exists(f.Journal(f.Queue.GetAttempt(item.ActiveAttempt!).JobId))); f.AssertUntouched();
    }
    [Fact]
    public void InterruptedReceiptBindingIsAmbiguousAndNeverAdoptedByFilename()
    {
        using var f = new Fixture(); var source = f.Zip("rollback.zip");
        SetHook(f.Service, stage =>
        {
            if (stage == "snapshot-published") typeof(AutomationQueueStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(f.Queue, (Action<string>)(_ => throw new IOException("simulated lost commit")));
        });
        using (var owner = f.Queue.AcquireOwner()) Assert.Throws<AutomationException>(() => f.Service.Admit(owner, source));
        typeof(AutomationQueueStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Queue, null);
        var item = Assert.Single(f.Queue.List()); var files = Directory.EnumerateFiles(f.Roots.StagingRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToArray();
        using var owner2 = f.Queue.AcquireOwner(); Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast).Reconcile(owner2, item.Id).Code);
        Assert.Equal(files.Length, Directory.EnumerateFiles(f.Roots.StagingRoot, "*", SearchOption.AllDirectories).Count()); f.AssertUntouched();
    }
    [Fact]
    public void DiskBudgetAndSimulatedFullDiskStopOnlyTheirCandidates()
    {
        using var f = new Fixture(); using var owner = f.Queue.AcquireOwner(); var source = f.Zip("space.zip");
        var constrained = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast with { MaxRetainedBytes = 1 });
        Assert.Equal(InboxAdmissionCode.ResourceLimit, constrained.Admit(owner, source).Code);
        var full = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast); SetHook(full, s => { if (s == "snapshot-starting") throw new IOException("synthetic ENOSPC"); });
        Assert.Equal(InboxAdmissionCode.StorageUnavailable, full.Admit(owner, f.Zip("full.zip", content: "full")).Code);
        Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, f.Zip("other.zip", content: "safe")).Code); f.AssertUntouched();
    }
    [Fact]
    public void PreexistingJournalWithoutCreationIntentIsNeverAdopted()
    {
        using var f = new Fixture(); JobId? id = null; byte[]? bytes = null;
        SetHook(f.Service, stage =>
        {
            if (stage != "extraction-recorded") return;
            id = f.Queue.GetAttempt(Assert.Single(f.Queue.List()).ActiveAttempt!).JobId;
            new JobStateStore(f.Context).Create(id); bytes = File.ReadAllBytes(f.Journal(id));
        });
        using var owner = f.Queue.AcquireOwner(); var result = f.Service.Admit(owner, f.Zip("foreign-journal.zip"));
        Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, result.Code); Assert.Equal(bytes, File.ReadAllBytes(f.Journal(id!))); f.AssertUntouched();
    }
    [Fact]
    public void ObservedWithoutAttemptIsReconciledWhenSourceDisappearsOrChanges()
    {
        using var f = new Fixture(); var path = f.Zip("observed.zip"); SetHook(f.Service, stage => { if (stage == "observation-recorded") throw new SimulatedCrash(); });
        using (var owner = f.Queue.AcquireOwner()) Assert.Throws<SimulatedCrash>(() => f.Service.Admit(owner, path));
        var item = Assert.Single(f.Queue.List()); Assert.Null(item.ActiveAttempt); File.Delete(path);
        using var owner2 = f.Queue.AcquireOwner(); var fresh = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast);
        Assert.Equal(InboxAdmissionCode.MissingSource, fresh.Reconcile(owner2, item.Id).Code);
        f.Zip("observed.zip", content: "new content"); Assert.Equal(InboxAdmissionCode.SourceChanged, fresh.Reconcile(owner2, item.Id).Code);
        Assert.Equal(InboxAdmissionCode.Admitted, fresh.Admit(owner2, path).Code); f.AssertUntouched();
    }
    [Fact]
    public void MissingAdmittedJournalIsParkedAndNeverRecreated()
    {
        using var f = new Fixture(); using var owner = f.Queue.AcquireOwner();
        var admitted = f.Service.Admit(owner, f.Zip("admitted.zip")); File.Delete(f.Journal(admitted.JobId!));
        Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, f.Service.Reconcile(owner, admitted.ItemId!).Code);
        Assert.False(File.Exists(f.Journal(admitted.JobId!))); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(admitted.ItemId!).State);
        Assert.Equal(1, f.Queue.Get(admitted.ItemId!).AttemptCount); f.AssertUntouched();
    }
    [Fact]
    public void UnknownProfileIsParkedWithoutInventingRuleOrJournal()
    {
        using var f = new Fixture(); using var owner = f.Queue.AcquireOwner(); var result = f.Service.Admit(owner, f.Zip("unknown.zip", profile: "unknown_profile"));
        Assert.Equal(InboxAdmissionCode.ProfileUnknown, result.Code); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(result.ItemId!).State);
        Assert.Null(f.Queue.GetAttempt(f.Queue.Get(result.ItemId!).ActiveAttempt!).Profile); Assert.False(File.Exists(f.Journal(result.JobId!))); f.AssertUntouched();
    }
    [Fact]
    public void OverflowReconciliationAndCompetingInstanceDoNotDuplicateJobs()
    {
        using var f = new Fixture();
        using var first = new InboxAdmissionMonitor([f.Context], Fixture.Fast); first.Start();
        using (var second = new InboxAdmissionMonitor([f.Context], Fixture.Fast))
        { second.Start(); Assert.Equal(AutomationError.Busy, Assert.Single(second.Status()).Error); }
        f.Zip("overflow.zip"); first.RequestRescan(f.Context.Id, InboxRescanReason.WatcherOverflow); Until(() => first.AdmittedCount == 1);
        var scans = Assert.Single(first.Status()).Scans; first.RequestRescan(f.Context.Id); Until(() => Assert.Single(first.Status()).Scans > scans);
        Assert.Equal(1, first.AdmittedCount); Assert.True(Assert.Single(first.Status()).WatcherErrors >= 1); f.AssertUntouched();
    }
    [Fact]
    public void PausedTruncatedCopyOutlivesTheDefaultFiveSecondStabilityWindow()
    {
        using var f = new Fixture(); var path = f.Zip("paused.zip"); var complete = File.ReadAllBytes(path);
        File.WriteAllBytes(path, complete[..(complete.Length / 2)]);
        using var owner = f.Queue.AcquireOwner(); var timer = Stopwatch.StartNew();
        var conservative = new InboxAdmissionService(f.Context, f.Queue, new() { MinFreeBytes = 0 });
        Assert.Equal(InboxAdmissionCode.IncompleteZip, conservative.Admit(owner, path).Code);
        Assert.True(timer.Elapsed >= TimeSpan.FromSeconds(5)); Assert.Null(Assert.Single(f.Queue.List()).ActiveAttempt);
        File.WriteAllBytes(path, complete); Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, path).Code); f.AssertUntouched();
    }
    [Fact]
    public void DisappearedSourceCanResumeOnlyWhenItsIdenticalBytesReturn()
    {
        using var f = new Fixture(); var path = f.Zip("returned.zip"); var bytes = File.ReadAllBytes(path);
        SetHook(f.Service, s => { if (s == "intent-recorded") throw new SimulatedCrash(); });
        using (var owner = f.Queue.AcquireOwner()) Assert.Throws<SimulatedCrash>(() => f.Service.Admit(owner, path));
        var item = Assert.Single(f.Queue.List()); var job = f.Queue.GetAttempt(item.ActiveAttempt!).JobId; File.Delete(path);
        var fresh = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast);
        using var owner2 = f.Queue.AcquireOwner(); Assert.Equal(InboxAdmissionCode.MissingSource, fresh.Reconcile(owner2, item.Id).Code);
        File.WriteAllBytes(path, bytes); Assert.Equal(InboxAdmissionCode.Admitted, fresh.Reconcile(owner2, item.Id).Code);
        Assert.Equal(QueueState.Queued, f.Queue.Get(item.Id).State); Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(job).State); f.AssertUntouched();
    }
    [Fact]
    public void UnknownResidualAndMalformedReceiptRemainIsolatedAndPreserved()
    {
        using var f = new Fixture(); var source = f.Zip("residual.zip"); string? foreign = null;
        SetHook(f.Service, s =>
        {
            if (s != "attempt-recorded") return;
            var attempt = f.Queue.GetAttempt(Assert.Single(f.Queue.List()).ActiveAttempt!);
            var root = Path.Combine(f.Roots.StagingRoot, attempt.StagingRelativePath!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(root); foreign = Path.Combine(root, "unknown.txt"); File.WriteAllText(foreign, "unowned sentinel");
        });
        using var owner = f.Queue.AcquireOwner(); Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, f.Service.Admit(owner, source).Code);
        Assert.Equal("unowned sentinel", File.ReadAllText(foreign!));
        var fresh = new InboxAdmissionService(f.Context, f.Queue, Fixture.Fast); var good = fresh.Admit(owner, f.Zip("good.zip", content: "good"));
        var attemptGood = f.Queue.GetAttempt(f.Queue.Get(good.ItemId!).ActiveAttempt!);
        var dir = Path.Combine(f.Roots.StateRoot, "automation", "admission", good.ItemId!.Value, attemptGood.Id.Value);
        var receipt = Directory.EnumerateFiles(dir).OrderBy(p => p).Last(); File.WriteAllText(receipt, "{}");
        Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, fresh.Reconcile(owner, good.ItemId).Code);
        Assert.Equal(InboxAdmissionCode.Admitted, fresh.Admit(owner, f.Zip("next.zip", content: "next")).Code); f.AssertUntouched();
    }
    [Fact]
    public void IdenticalTransportBytesInTwoInboxesNeverDeduplicateAcrossUniverses()
    {
        using var a = new Fixture("universe_a"); using var b = new Fixture("universe_b"); var source = a.Zip("same.zip");
        var other = Path.Combine(b.Roots.InboxRoot, "same.zip"); File.Copy(source, other);
        using var ao = a.Queue.AcquireOwner(); using var bo = b.Queue.AcquireOwner();
        var accepted = a.Service.Admit(ao, source); var crossed = b.Service.Admit(bo, other);
        Assert.Equal(InboxAdmissionCode.Admitted, accepted.Code); Assert.Equal(InboxAdmissionCode.UniverseMismatch, crossed.Code);
        Assert.Equal(a.Queue.Get(accepted.ItemId!).Zip.Hash, b.Queue.Get(crossed.ItemId!).Zip.Hash);
        Assert.NotEqual(accepted.ItemId, crossed.ItemId); a.AssertUntouched(); b.AssertUntouched();
    }
    [Theory]
    [InlineData("Files")] [InlineData("SnapshotRelativePath")] [InlineData("ExtractionRelativePath")] [InlineData("FileEntry")]
    public void NullReceiptFieldsAreAnIndividualIncidentRatherThanAScopeFailure(string field)
    {
        using var f = new Fixture(); using var owner = f.Queue.AcquireOwner();
        var first = f.Service.Admit(owner, f.Zip("corrupt-receipt.zip"));
        var attempt = f.Queue.GetAttempt(f.Queue.Get(first.ItemId!).ActiveAttempt!);
        var dir = Path.Combine(f.Roots.StateRoot, "automation", "admission", first.ItemId!.Value, attempt.Id.Value);
        var path = Directory.EnumerateFiles(dir).OrderBy(p => p).Last();
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false) } };
        var receipt = JsonSerializer.Deserialize<AdmissionReceipt>(File.ReadAllText(path), options)!;
        receipt = field switch
        {
            "Files" => receipt with { Files = null! },
            "SnapshotRelativePath" => receipt with { SnapshotRelativePath = null! },
            "ExtractionRelativePath" => receipt with { ExtractionRelativePath = null! },
            _ => receipt with { Files = new AdmissionFile[] { null! } }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, options));
        Assert.Equal(InboxAdmissionCode.AmbiguousEvidence, f.Service.Reconcile(owner, first.ItemId).Code);
        Assert.Equal(QueueState.NeedsReview, f.Queue.Get(first.ItemId).State);
        Assert.Equal(InboxAdmissionCode.Admitted, f.Service.Admit(owner, f.Zip("next.zip", content: "unaffected")).Code);
        Assert.True(File.Exists(path)); f.AssertUntouched();
    }
    [Fact]
    public void CorruptQueueBlocksOnlyItsUniverse()
    {
        using var a = new Fixture("broken_scope"); using var b = new Fixture("good_scope");
        using (a.Queue.AcquireOwner()) { }
        File.WriteAllText(a.Queue.DatabasePath, "corrupt SQLite"); b.Zip("good.zip");
        using var monitor = new InboxAdmissionMonitor([a.Context, b.Context], Fixture.Fast); monitor.Start(); Until(() => monitor.AdmittedCount == 1);
        Assert.Equal(InboxMonitorState.Blocked, monitor.Status().Single(s => s.UniverseId == a.Context.Id).State);
        Assert.Equal(InboxMonitorState.Monitoring, monitor.Status().Single(s => s.UniverseId == b.Context.Id).State); a.AssertUntouched(); b.AssertUntouched();
    }
    private static void SetHook(InboxAdmissionService service, Action<string> hook) => typeof(InboxAdmissionService).GetProperty("Checkpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, hook);
    private sealed class SimulatedCrash : Exception;
    private static void Until(Func<bool> condition, TimeSpan? limit = null, Action? sample = null)
    {
        var timer = Stopwatch.StartNew(); while (!condition()) { sample?.Invoke(); if (timer.Elapsed > (limit ?? TimeSpan.FromSeconds(20))) throw new TimeoutException("Admission condition did not occur."); Thread.Sleep(20); }
    }
    private sealed class Fixture : IDisposable
    {
        public static InboxAdmissionOptions Fast => new() { SampleInterval = TimeSpan.Zero, InvalidZipGrace = TimeSpan.FromHours(1), MinFreeBytes = 0, RescanInterval = TimeSpan.FromHours(1), Debounce = TimeSpan.Zero };
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "nap-admission-" + Guid.NewGuid().ToString("N"));
        public UniverseStorageConfig Roots { get; }
        public UniverseContext Context { get; }
        public AutomationQueueStore Queue { get; }
        public InboxAdmissionService Service { get; }
        public Fixture(string universe = "test_universe")
        {
            Roots = new(new(universe), Path.Combine(Root, "workspace"), Path.Combine(Root, "production"), Path.Combine(Root, "archive"));
            foreach (var dir in new[] { Roots.WorkspaceRoot, Roots.InboxRoot, Roots.StagingRoot, Roots.ProductionRoot, Roots.ArchiveRoot }) Directory.CreateDirectory(dir);
            var profile = """
            {"schema_version":4,"universe_id":"UNIVERSE","display_name":"Synthetic","classification_dimensions":[],"asset_rules":[{"asset_type":"portrait","production_profile":"portrait_npc","allowed_classification":[],"required_classification":[],"package_files":[{"role":"master","suffix":"","extension":".png","required":true,"content_validator":"png_master"}],"routing":{"segments":[{"literal":"portraits"},{"asset_id":true}]},"conversion":{"kind":"png_to_webp","source_role":"master","output_width":768,"output_height":960,"webp_quality":90}}]}
            """.Replace("UNIVERSE", universe);
            Context = new(UniverseProfileLoader.Load(new MemoryStream(Encoding.UTF8.GetBytes(profile))), Roots);
            Queue = new(Roots); Service = new(Context, Queue, Fast);
            foreach (var dir in new[] { Roots.ArchiveRoot, Roots.ProductionRoot }) File.WriteAllText(Path.Combine(dir, "foreign.txt"), "unrelated sentinel");
        }
        public string Zip(string name, string? declared = null, string asset = "portrait_example_001", string content = "synthetic transport bytes", string profile = "portrait_npc", string? extra = null)
        {
            var path = Path.Combine(Roots.InboxRoot, name); var exists = File.Exists(path);
            using var zip = ZipFile.Open(path, exists ? ZipArchiveMode.Update : ZipArchiveMode.Create);
            if (exists) foreach (var entry in zip.Entries.ToArray()) entry.Delete();
            using (var writer = new StreamWriter(zip.CreateEntry(asset + "_manifest.json").Open()))
                writer.Write(JsonSerializer.Serialize(new { schema_version = 2, universe_id = declared ?? Context.Id.Value, asset_id = asset, asset_type = "portrait", production_profile = profile, classification = new { } }));
            using (var writer = new StreamWriter(zip.CreateEntry(asset + ".png").Open())) writer.Write(content);
            if (extra is not null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("hostile"); }
            return path;
        }
        public IReadOnlyList<QueueItem> Items()
        { var items = new List<QueueItem>(); for (int offset = 0; ; offset += 1000) { var page = Queue.List(offset, 1000); items.AddRange(page); if (page.Count < 1000) return items; } }
        public Dictionary<string, string> InboxHashes() => Directory.EnumerateFiles(Roots.InboxRoot).OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetFileName(p), p => new Sha256Hasher().Compute(p).Hex);
        public string Journal(JobId id) => Path.Combine(Roots.StateRoot, id.Value + ".json");
        public void AssertUntouched()
        {
            foreach (var dir in new[] { Roots.ArchiveRoot, Roots.ProductionRoot }) { Assert.Equal("unrelated sentinel", File.ReadAllText(Path.Combine(dir, "foreign.txt"))); Assert.Single(Directory.EnumerateFileSystemEntries(dir)); }
            Assert.False(File.Exists(Roots.CatalogPath)); Assert.False(File.Exists(Path.Combine(Roots.ProductionRoot, ".git")));
            if (File.Exists(Queue.DatabasePath) && new FileInfo(Queue.DatabasePath).Length > 50)
            {
                StoredAutomationPolicy? policy = null;
                Until(() => { try { policy = Queue.CurrentPolicy(); return true; } catch (AutomationException e) when (e.Code == AutomationError.Busy) { return false; } });
                Assert.Equal(AutomationPolicyState.Disabled, policy!.State);
            }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
