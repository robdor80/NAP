using System.Reflection;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ExecutionRecoveryTests
{
    [Theory]
    [InlineData("before-execution-intent")] [InlineData("execution-intent")] [InlineData("before-claim")] [InlineData("claimed")]
    [InlineData("before-coordinator")] [InlineData("before:journal-Audited")] [InlineData("effect:journal-Audited")]
    [InlineData("before:archive-start")] [InlineData("before:archive-file:portrait_example_001_brief.txt")]
    [InlineData("after:archive-file:portrait_example_001_brief.txt")] [InlineData("before:archive-index")]
    [InlineData("effect:archive-index")] [InlineData("after:archive-index")]
    [InlineData("before:production-plan")] [InlineData("after:production-plan")]
    [InlineData("before:production-intent")] [InlineData("after:production-intent")]
    [InlineData("before:production-file:portrait_example_001.webp")]
    [InlineData("effect:production-file:portrait_example_001.webp")]
    [InlineData("after:production-file:portrait_example_001.webp")]
    [InlineData("effect:journal-Executed")] [InlineData("effect:journal-Verified")]
    [InlineData("before:catalog-register")] [InlineData("effect:catalog-register")]
    [InlineData("after:catalog-register")] [InlineData("effect:journal-Completed")]
    [InlineData("after-coordinator")] [InlineData("effect:catalog-verified")]
    [InlineData("execution-completed")] [InlineData("queue-completed")]
    public void CrashReopensSameJobAndNeverRepeatsPublishedFiles(string point)
    {
        using var f = new PreparationTestFixture(); InboxAdmissionResult a;
        var client = new DurableExecutionTests.AuditClient(); var hits = new List<string>();
        using (var owner = f.Queue.AcquireOwner())
        {
            DurableExecutionTests.Enable(f, owner); a = f.Admit(owner);
            Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
            var execution = new DurableExecutionService(f.Context, f.Queue);
            DurableExecutionTests.Hook(execution, s => { hits.Add(s); if (s == point) throw new PreparationTestFixture.SimulatedCrash(); });
            Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => execution.Execute(owner, a.ItemId!));
        }
        var published = ExistingFiles(f);
        var queue = new AutomationQueueStore(f.Roots);
        using (var owner = queue.AcquireOwner())
        {
            var execution = new DurableExecutionService(f.Context, queue); var trace = new List<string>(); DurableExecutionTests.Hook(execution, trace.Add);
            var result = execution.Execute(owner, a.ItemId!);
            Assert.True(result.Code == DurableExecutionCode.Completed, point + "\n" + string.Join("\n", trace));
            Assert.Equal(a.JobId, result.JobId); Assert.Equal(a.JobId, queue.GetAttempt(queue.Get(a.ItemId!).ActiveAttempt!).JobId);
            Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(a.JobId!).State);
            Assert.Single(Directory.EnumerateFiles(f.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
            Assert.Single(new AssetCatalog(f.Context).Query()); Assert.Equal(1, client.Calls);
            foreach (var (path, stamp) in published) Assert.Equal(stamp, Stamp(path));
            Assert.Equal(QueueState.Completed, new AutomationQueueStore(f.Roots).Get(a.ItemId!).State);
        }
    }

    [Theory]
    [InlineData("before:archive-publish:portrait_example_001_brief.txt")]
    [InlineData("effect:archive-publish:portrait_example_001_brief.txt")]
    [InlineData("after:archive-publish:portrait_example_001_brief.txt")]
    [InlineData("effect:archive-file:portrait_example_001_brief.txt")]
    [InlineData("before:production-publish:portrait_example_001.webp")]
    [InlineData("effect:production-publish:portrait_example_001.webp")]
    [InlineData("after:production-publish:portrait_example_001.webp")]
    public void AmbiguousTemporaryOrFinalWithoutOwnershipIsPreservedForReview(string point)
    {
        using var f = new PreparationTestFixture(); InboxAdmissionResult a;
        using (var owner = f.Queue.AcquireOwner())
        {
            DurableExecutionTests.Enable(f, owner); a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
            var service = new DurableExecutionService(f.Context, f.Queue);
            DurableExecutionTests.Hook(service, s => { if (s == point) throw new PreparationTestFixture.SimulatedCrash(); });
            Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => service.Execute(owner, a.ItemId!));
        }
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot);
        var queue = new AutomationQueueStore(f.Roots); using var next = queue.AcquireOwner();
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(f.Context, queue).Execute(next, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot));
        Assert.Equal(QueueState.NeedsReview, new AutomationQueueStore(f.Roots).Get(a.ItemId!).State);
        Assert.NotEqual(JobState.Completed, new JobStateStore(f.Context).Load(a.JobId!).State);
    }

    [Theory] [InlineData("catalog")] [InlineData("production")] [InlineData("archive")]
    public void CompletedCorruptionIsAnIncidentAndNeverRebuilt(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var execution = new DurableExecutionService(f.Context, f.Queue);
        Assert.Equal(DurableExecutionCode.Completed, execution.Execute(owner, a.ItemId!).Code);
        if (kind == "catalog") File.Delete(f.Roots.CatalogPath);
        else File.WriteAllText(Directory.EnumerateFiles(kind == "archive" ? f.Roots.ArchiveRoot : f.Roots.ProductionRoot,
            kind == "archive" ? "*.png" : "*.webp", SearchOption.AllDirectories).Single(), "synthetic corruption");
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath);
        Assert.Equal(DurableExecutionCode.NeedsReview, execution.Execute(owner, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath));
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(a.JobId!).State);
        Assert.Equal(QueueState.NeedsReview, new AutomationQueueStore(f.Roots).Get(a.ItemId!).State);
        Assert.Contains(f.Queue.Events(a.ItemId!), e => e.Code == QueueEventCode.CompletedInconsistent);
    }

    [Fact]
    public void ExecutionTransactionRollbackLeavesNoClaimOrProductiveEffect()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var p = DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var service = new DurableExecutionService(f.Context, f.Queue);
        var hook = typeof(ExecutionStateStore).GetProperty("BeforeCommit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        hook.SetValue(service.State, (Action<string>)(_ => throw new PreparationTestFixture.SimulatedCrash()));
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => service.Execute(owner, a.ItemId!));
        Assert.Null(new ExecutionStateStore(f.Roots).Get(a.ItemId!)); Assert.Equal(QueueState.Queued, f.Queue.Get(a.ItemId!).State);
        f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Disabled); f.AssertUntouched();
    }
    private static Dictionary<string, string> ExistingFiles(PreparationTestFixture f) => new[] { f.Roots.ArchiveRoot, f.Roots.ProductionRoot }
        .SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories)).ToDictionary(p => p, Stamp);
    private static string Stamp(string path) => new Sha256Hasher().Compute(path).Hex + ":" + File.GetLastWriteTimeUtc(path).Ticks;
}
