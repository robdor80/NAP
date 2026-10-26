using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>F7.2 selected acceptance only. At most five synthetic ZIP per scenario, local bare transport only.</summary>
public sealed class GitOutboxF72Tests
{
    private sealed class Interruption : Exception;
    private sealed class Fixture : IDisposable
    {
        internal PreparationTestFixture F { get; } = new();
        internal AutomationQueueOwner Owner { get; }
        internal GitProductionService Git { get; } = new();
        internal GitProductionInspector Inspector { get; } = new();
        internal GitPublicationPolicyStore Policy { get; }
        internal GitBatchPublicationService Service { get; private set; }
        internal GitOutboxStore Outbox { get; }
        internal string Remote => Path.Combine(F.Root, "bare.git");
        internal string BaseHead { get; }
        internal readonly List<InboxAdmissionResult> Jobs = [];
        internal Fixture(bool authorize = true, int maxJobs = 5, GitPublicationMode mode = GitPublicationMode.AutoCommitBatch)
        {
            Owner = F.Queue.AcquireOwner(); DurableExecutionTests.Enable(F, Owner);
            GitProductionFixture.Enable(Git); GitProductionFixture.Enable(Inspector);
            Run("init", "--initial-branch=main"); Run("config", "user.name", "Synthetic F72"); Run("config", "user.email", "f72@example.invalid");
            Run("add", "--", "foreign.txt"); Run("commit", "-m", "synthetic base");
            Run("init", "--bare", "--initial-branch=main", Remote); Run("remote", "add", "origin", Remote); Run("push", "-u", "origin", "main");
            BaseHead = Run("rev-parse", "HEAD");
            Policy = new(F.Context, Path.Combine(F.Root, "private-git-policy")); Outbox = new(F.Context, Policy);
            if (authorize)
            {
                var proposed = Policy.Propose(mode, "synthetic responsible", ["assets"], maxJobs, Inspector);
                Policy.Save(proposed with { AllowCommit = true, AllowPush = mode == GitPublicationMode.AutoCommitAndPushBatch }, 0);
            }
            Service = new(F.Context, F.Queue, Policy, Git);
        }
        internal InboxAdmissionResult Complete(int n = 1)
        {
            var a = F.Admit(Owner, F.Zip("synthetic" + n + ".zip", "portrait_synthetic_" + n.ToString("000"), seed: n));
            Assert.Equal(PreparationCode.AuditPass, F.Service.Audit(Owner, a.ItemId!, new DurableExecutionTests.AuditClient()).Code);
            Assert.Equal(DurableExecutionCode.Completed, new DurableExecutionService(F.Context, F.Queue).Execute(Owner, a.ItemId!).Code);
            Jobs.Add(a); return a;
        }
        internal string Run(params string[] args) => GitProductionFixture.Run(F.Roots.ProductionRoot, args);
        internal string RemoteHead => GitProductionFixture.Run(Remote, "rev-parse", "main");
        internal void CheckCompleted()
        {
            foreach (var a in Jobs)
            {
                Assert.Equal(QueueState.Completed, F.Queue.Get(a.ItemId!).State);
                Assert.Equal(JobState.Completed, new JobStateStore(F.Context).Load(a.JobId!).State);
            }
        }
        internal void Observe(string point) => BackupTestSupport.Set(Git, "Observer", (Action<string>)(p => { if (p == point) throw new Interruption(); }));
        internal void Cut(string point) => BackupTestSupport.Set(Service, "Checkpoint", (Action<string>)(p => { if (p == point) throw new Interruption(); }));
        internal void Restart() { BackupTestSupport.Set(Git, "Observer", (Action<string>)(_ => { })); Service = new(F.Context, new(F.Roots), new(F.Context, Policy.DirectoryPath), Git); }
        public void Dispose() { Owner.Dispose(); F.Dispose(); }
    }
    [Fact]
    public void DisabledCompletedHasNoGitEffectsAndNoOutbox()
    {
        using var f = new Fixture(authorize: false); f.Complete();
        f.Observe("prepared");
        Assert.Null(f.Policy.Load()); Assert.Equal("git-disabled", f.Service.RunOnce(f.Owner));
        Assert.Empty(f.Outbox.Read().Intents); Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD"));
        Assert.False(Directory.Exists(Path.Combine(f.F.Roots.StateRoot, "git-production"))); f.CheckCompleted();
    }
    [Fact]
    public void FiveCompletedJobsBecomeOneExactBatchAndReconcileIdempotently()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch);
        for (var i = 1; i <= 5; i++) f.Complete(i);
        string Assets() => string.Join("\n", DurableExecutionTests.Inventory(f.F.Roots.ProductionRoot, f.F.Roots.ArchiveRoot, f.F.Roots.CatalogPath).Split('\n').Where(p => !p.Contains("/.git/")));
        var before = Assets();
        Assert.Equal("git-committed", f.Service.RunOnce(f.Owner));
        var s = f.Outbox.Read(); Assert.Equal(5, s.Intents.Count); Assert.Single(s.Batches);
        Assert.All(s.Intents, i => Assert.Equal(GitOutboxState.Committed, i.State));
        var receipt = f.Git.ReadReceipt(f.F.Context, s.Batches[0].OperationId);
        Assert.Equal(GitReceiptState.Committed, receipt.State); Assert.Equal(f.BaseHead, receipt.BaseHead);
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.Equal("2", f.Run("rev-list", "--count", "HEAD"));
        Assert.Equal("", f.Run("status", "--porcelain"));
        Assert.DoesNotContain("foreign.txt", f.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"));
        f.Restart(); Assert.Equal("git-idle", f.Service.RunOnce(f.Owner)); Assert.Equal(5, f.Outbox.Read().Intents.Count);
        // Assets/archive/catalog remain byte-identical; Git metadata is the only production mutation.
        Assert.Equal(before, Assets());
        f.CheckCompleted();
    }
    [Theory]
    [InlineData("batch-reserved")]
    [InlineData("reconciled")]
    [InlineData("prepared")]
    [InlineData("after_commit")]
    [InlineData("commit-returned")]
    public void InterruptedPublicationResumesExactReservedOperation(string point)
    {
        using var f = new Fixture(); f.Complete();
        if (point is "prepared" or "after_commit") f.Observe(point); else f.Cut(point);
        Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner));
        var before = f.Outbox.Read(); var reserved = before.Batches.SingleOrDefault()?.OperationId;
        f.Restart(); Assert.Equal("git-committed", f.Service.RunOnce(f.Owner));
        var after = f.Outbox.Read(); Assert.Single(after.Intents); Assert.Single(after.Batches);
        if (reserved is not null) Assert.Equal(reserved, after.Batches[0].OperationId);
        Assert.Equal("2", f.Run("rev-list", "--count", "HEAD"));
        Assert.Single(f.Git.ListOperations(f.F.Context)); Assert.Equal("git-idle", f.Service.RunOnce(f.Owner));
        Assert.Equal(f.BaseHead, f.RemoteHead); f.CheckCompleted();
    }
    [Theory]
    [InlineData("foreign-worktree")]
    [InlineData("foreign-index")]
    [InlineData("tampered-output")]
    [InlineData("identity")]
    public void UnsafeEvidenceOrGitChangeBlocksAndPreservesCompleted(string fault)
    {
        using var f = new Fixture(); f.Complete();
        if (fault == "foreign-worktree") File.AppendAllText(Path.Combine(f.F.Roots.ProductionRoot, "foreign.txt"), "changed");
        if (fault == "foreign-index") { File.AppendAllText(Path.Combine(f.F.Roots.ProductionRoot, "foreign.txt"), "staged"); f.Run("add", "--", "foreign.txt"); }
        if (fault == "identity") f.Run("config", "user.email", "changed@example.invalid");
        if (fault == "tampered-output") File.AppendAllText(Directory.GetFiles(f.F.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Single(), "tampered");
        var status = f.Run("status", "--porcelain=v2");
        Assert.Contains(f.Service.RunOnce(f.Owner), new[] { "git-blocked", "git-idle" });
        Assert.Equal(GitOutboxState.Blocked, Assert.Single(f.Outbox.Read().Intents).State);
        Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD")); Assert.Equal(status, f.Run("status", "--porcelain=v2"));
        Assert.Empty(f.Git.ListOperations(f.F.Context)); f.CheckCompleted();
    }
    [Fact]
    public void SecondBatchIsBlockedByNativeRequireNoPending()
    {
        using var f = new Fixture(); f.Complete(); Assert.Equal("git-committed", f.Service.RunOnce(f.Owner));
        var head = f.Run("rev-parse", "HEAD"); f.Complete(2);
        Assert.Equal("git-blocked", f.Service.RunOnce(f.Owner));
        var s = f.Outbox.Read(); Assert.Equal(2, s.Intents.Count); Assert.Single(s.Batches);
        Assert.Contains(s.Intents, i => i.State == GitOutboxState.Blocked && i.Diagnostic == NapIssueCodes.GitReceiptInvalid);
        Assert.Equal(head, f.Run("rev-parse", "HEAD")); Assert.Single(f.Git.ListOperations(f.F.Context)); f.CheckCompleted();
    }
    [Fact]
    public void CorruptPolicyAndRevocationFailClosed()
    {
        using var f = new Fixture(); f.Complete(); f.Cut("reconciled"); Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner)); f.Restart();
        var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "revoked_synthetic", Revoked = true }, 1);
        Assert.Equal("git-disabled", f.Service.RunOnce(f.Owner)); Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD"));
        Assert.Equal(GitOutboxState.Blocked, f.Outbox.Read().Intents.Single().State);
        var path = Path.Combine(f.Policy.DirectoryPath, "policy.json"); var text = File.ReadAllText(path);
        File.WriteAllText(path, text.Replace("revoked_synthetic", "altered_synthetic"));
        Assert.ThrowsAny<Exception>(() => f.Policy.Load()); Assert.ThrowsAny<Exception>(() => f.Service.RunOnce(f.Owner));
        Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD")); f.CheckCompleted();
    }
    [Fact]
    public void OrphanReceiptTempBlocksWithoutAdoption()
    {
        using var f = new Fixture(); f.Complete(); f.Cut("batch-reserved"); Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner));
        var directory = Path.Combine(f.F.Roots.StateRoot, "git-production"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "unowned.tmp"), "synthetic"); f.Restart();
        Assert.Equal("git-blocked", f.Service.RunOnce(f.Owner)); Assert.Equal(GitOutboxState.Blocked, f.Outbox.Read().Batches.Single().State);
        Assert.True(File.Exists(Path.Combine(directory, "unowned.tmp"))); Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD")); f.CheckCompleted();
    }
    [Fact]
    public void FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit()
    {
        using var f = new Fixture(); f.Complete();
        BackupTestSupport.Set(f.Git, "Observer", (Action<string>)(p => { if (p == "before_commit") throw new IOException("synthetic Git failure"); }));
        Assert.Equal("git-blocked", f.Service.RunOnce(f.Owner));
        var blocked = f.Outbox.Read(); var batch = Assert.Single(blocked.Batches);
        var receipt = f.Git.ReadReceipt(f.F.Context, batch.OperationId);
        Assert.Equal(GitReceiptState.Prepared, receipt.State); Assert.NotNull(receipt.ExpectedCommitSha);
        Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD"));
        Assert.Equal("", f.Run("diff", "--cached", "--name-only")); f.CheckCompleted();
        f.Restart(); Assert.Equal("git-committed", f.Service.RetryBlocked(f.Owner, blocked.Intents.Single().Id));
        Assert.Equal(receipt.ExpectedCommitSha, f.Run("rev-parse", "HEAD"));
        Assert.Equal(batch.OperationId, f.Outbox.Read().Batches.Single().OperationId);
        Assert.Equal("2", f.Run("rev-list", "--count", "HEAD")); f.CheckCompleted();
    }
    [Fact]
    public void ManualCommitNeedsNewGrantAndPushNeedsIndependentPermission()
    {
        using var f = new Fixture(mode: GitPublicationMode.Manual); var job = f.Complete();
        var result = new DurableExecutionService(f.F.Context, f.F.Queue).VerifyCompletedReadOnly(job.ItemId!).Production;
        var manual = new GitPublicationManualService(f.Git, f.Inspector, f.Policy);
        Assert.Equal("git-manual", f.Service.RunOnce(f.Owner));
        var plan = manual.PrepareCommit(f.F.Context, [result], "synthetic manual commit");
        var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "new_synthetic_grant" }, 1);
        Assert.Throws<InvalidDataException>(() => manual.Commit(f.F.Context, plan));
        plan = manual.PrepareCommit(f.F.Context, [result], "synthetic manual commit");
        var committed = manual.Commit(f.F.Context, plan);
        Assert.Throws<InvalidDataException>(() => manual.Push(f.F.Context, committed.OperationId));
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.Equal(committed.CommitSha, f.Run("rev-parse", "HEAD"));
        Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, committed.OperationId).State); f.CheckCompleted();
    }
    [Fact]
    public void LateCompletionIsReconciledByBoundedPagesWithoutRepeatingProduction()
    {
        using var f = new Fixture(); f.Complete(); f.Complete(2);
        Assert.Equal("git-reconciling", f.Service.RunOnce(f.Owner, 1));
        Assert.Single(f.Outbox.Read().Intents); Assert.Empty(f.Git.ListOperations(f.F.Context));
        f.Restart(); Assert.Equal("git-reconciling", f.Service.RunOnce(f.Owner, 1));
        Assert.Equal(2, f.Outbox.Read().Intents.Count); Assert.Empty(f.Git.ListOperations(f.F.Context));
        f.Restart(); Assert.Equal("git-committed", f.Service.RunOnce(f.Owner, 1));
        Assert.Equal(2, f.Outbox.Read().Batches.Single().IntentIds.Count); f.CheckCompleted();
    }
    [Fact]
    public void ForeignOwnerThreadCannotEnterManualGit()
    {
        using var f = new Fixture();
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => new GitPublicationManualService(f.Git, f.Inspector, f.Policy).Inspect(f.F.Context)));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.NotNull(error); Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD"));
        Assert.Equal("git-idle", f.Service.RunOnce(f.Owner));
    }

    [Fact]
    public void CatalogAndFilesAloneNeverSubstituteForNativeOwnership()
    {
        using var f = new Fixture(); var job = f.Complete();
        File.Delete(Path.Combine(f.F.Roots.StateRoot, "production-executions", job.JobId!.Value + ".json"));
        Assert.ThrowsAny<Exception>(() => new DurableExecutionService(f.F.Context, f.F.Queue).VerifyCompletedReadOnly(job.ItemId!));
        f.Service.RunOnce(f.Owner);
        Assert.Equal(GitOutboxState.Blocked, f.Outbox.Read().Intents.Single().State);
        Assert.Empty(f.Git.ListOperations(f.F.Context)); Assert.Equal(f.BaseHead, f.Run("rev-parse", "HEAD")); f.CheckCompleted();
    }

}
