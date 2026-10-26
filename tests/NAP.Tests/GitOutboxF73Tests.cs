using System.Reflection;
using System.Text.Json;
using NAP.Core;
using Xunit;
using Xunit.Abstractions;
using Fixture = NAP.Tests.GitOutboxF72Tests.Fixture;

namespace NAP.Tests;

/// <summary>Focused F7.3 acceptance. Native synthetic Completed workflows and local bare transport only.</summary>
public sealed class GitOutboxF73Tests(ITestOutputHelper output)
{
    private sealed class Interruption : Exception;
    private static void Cut(Fixture f, string point)
    {
        BackupTestSupport.Set(f.Service, "Checkpoint", (Action<string>)(p => { if (p == point) throw new Interruption(); }));
        BackupTestSupport.Set(f.Git, "Observer", (Action<string>)(p => { if (p == point) throw new Interruption(); }));
    }
    private static List<string> Commands(Fixture f)
    {
        var commands = new List<string>();
        BackupTestSupport.Set(BackupTestSupport.Get(f.Git, "Git"), "CommandStarting", (Action<string>)commands.Add);
        return commands;
    }
    private static GitOutboxBatch CommitWithoutPush(Fixture f)
    {
        Cut(f, "before-push"); Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner)); f.Restart();
        var batch = Assert.Single(f.Outbox.Read().Batches);
        Assert.Equal(GitOutboxState.Committed, batch.State); Assert.Equal(f.BaseHead, f.RemoteHead);
        return batch;
    }
    private void Evidence(Fixture f, List<string> commands, int zipCount)
    {
        var state = f.Outbox.Read(); var batch = Assert.Single(state.Batches);
        var receipt = f.Git.ReadReceipt(f.F.Context, batch.OperationId);
        f.CheckCompleted();
        Assert.Equal("2", f.Run("rev-list", "--count", "HEAD"));
        Assert.Equal(batch.CommitSha, f.Run("rev-parse", "HEAD"));
        Assert.Equal(f.BaseHead, receipt.BaseHead);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ZipCount = zipCount, Identity = "f72@example.invalid", Transport = "temporary-local-bare-only",
            BaseHead = f.BaseHead, Head = f.Run("rev-parse", "HEAD"), batch.CommitSha,
            RemoteTip = f.RemoteHead, OperationId = batch.OperationId.Value, batch.PlanHash,
            Receipt = receipt.State.ToString(), Outbox = batch.State.ToString(), batch.Diagnostic,
            Intents = state.Intents.Select(i => new { i.Id, JobId = i.JobId.Value, QueueItemId = i.ItemId.Value,
                WorkflowAttemptId = i.AttemptId.Value, i.BatchId, i.PolicyRevision, i.AuthorizationId, State = i.State.ToString() }),
            Jobs = "Completed", Queue = "Completed", NativePushCommands = commands.Count(c => c == "push")
        }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AuthorizedBatchVerifiesRemoteAndPublishesExactlyOnce(int jobs)
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch);
        for (var n = 1; n <= jobs; n++) f.Complete(n);
        var commands = Commands(f);
        Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        var state = f.Outbox.Read(); var batch = Assert.Single(state.Batches);
        Assert.Equal(jobs, state.Intents.Count); Assert.All(state.Intents, i => Assert.Equal(GitOutboxState.Pushed, i.State));
        Assert.Equal(batch.CommitSha, f.RemoteHead); Assert.Equal(GitOutboxState.Pushed, batch.State);
        Assert.Equal(GitReceiptState.Pushed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Assert.Equal("", f.Run("status", "--porcelain"));
        Assert.DoesNotContain("foreign.txt", f.Run("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"));
        f.Restart(); Assert.Equal("git-idle", f.Service.RunOnce(f.Owner));
        Assert.Single(commands, c => c == "push"); Assert.Single(f.Git.ListOperations(f.F.Context));
        Evidence(f, commands, jobs);
    }

    [Theory]
    [InlineData(GitPublicationMode.AutoCommitAndPushBatch)]
    [InlineData(GitPublicationMode.AutoCommitBatch)]
    public void CommitPermissionNeverImpliesPush(GitPublicationMode mode)
    {
        using var f = new Fixture(mode: mode, allowPush: false); f.Complete(); var commands = Commands(f);
        Assert.Equal("git-committed", f.Service.RunOnce(f.Owner)); f.Restart();
        Assert.Equal("git-idle", f.Service.RunOnce(f.Owner));
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.DoesNotContain("push", commands);
        var batch = Assert.Single(f.Outbox.Read().Batches); Assert.Equal(GitOutboxState.Committed, batch.State);
        Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Evidence(f, commands, 1);
    }

    [Theory]
    [InlineData("before-push", 0)]
    [InlineData("before_push", 0)]
    [InlineData("before_push_command", 0)]
    [InlineData("after_push", 1)]
    [InlineData("pushed", 1)]
    [InlineData("push-returned", 1)]
    public void InterruptedPublicationRecoversSameOperationWithoutAnotherCommitOrPush(string point, int pushesBefore)
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete(); var commands = Commands(f);
        Cut(f, point); Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner));
        var batch = Assert.Single(f.Outbox.Read().Batches); Assert.Equal(GitOutboxState.Committed, batch.State);
        Assert.Equal(pushesBefore, commands.Count(c => c == "push"));
        var receipt = f.Git.ReadReceipt(f.F.Context, batch.OperationId);
        Assert.Equal(point is "pushed" or "push-returned" ? GitReceiptState.Pushed : GitReceiptState.Committed, receipt.State);
        f.Restart(); Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        Assert.Equal(batch.OperationId, f.Outbox.Read().Batches.Single().OperationId);
        Assert.Single(commands, c => c == "push"); Assert.Equal(batch.CommitSha, f.RemoteHead);
        Evidence(f, commands, 1);
    }

    [Fact]
    public void F73A1ExhaustedPushBudgetRecognizesExactExternalPublicationAfterRestart()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        ExhaustPushBudget(f);
        Assert.Equal("git_push_retry_3", f.Outbox.Read().Batches.Single().Diagnostic);
        Assert.DoesNotContain("push", commands);
        f.Run("push", "origin", "HEAD:refs/heads/main"); // External fixture actor only.
        f.Restart(); var before = commands.Count;
        Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        Assert.Contains("ls-remote", commands.Skip(before)); Assert.DoesNotContain("push", commands);
        var after = f.Outbox.Read().Batches.Single();
        Assert.Equal(batch.OperationId, after.OperationId); Assert.Equal(batch.PlanHash, after.PlanHash);
        Assert.Equal(batch.CommitSha, after.CommitSha); Assert.Equal(batch.CommitSha, f.RemoteHead);
        Assert.Equal(GitReceiptState.Pushed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Assert.Equal(GitOutboxState.Pushed, after.State);
        Assert.All(f.Outbox.Read().Intents, i => Assert.Equal(GitOutboxState.Pushed, i.State));
        output.WriteLine("F73-A1: exhausted writes=3; subsequent native push commands=0; exact external SHA recognized after restart.");
        Evidence(f, commands, 1);
    }

    [Fact]
    public void F73A1ExhaustedPushBudgetKeepsCommittedAndDurablyBoundsReadOnlyChecks()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        ExhaustPushBudget(f);
        for (var n = 1; n <= 3; n++)
        {
            f.Restart(); var before = commands.Count;
            // The second read-only query is genuinely unavailable; its reserved budget must survive too.
            var unavailable = f.Remote + ".offline";
            if (n == 2) Directory.Move(f.Remote, unavailable);
            try { Assert.Equal("git-push-retry-exhausted", f.Service.RunOnce(f.Owner)); }
            finally { if (n == 2) Directory.Move(unavailable, f.Remote); }
            Assert.Single(commands.Skip(before), c => c == "ls-remote");
            var current = f.Outbox.Read().Batches.Single();
            Assert.Equal("git_push_exhausted_verify_" + n, current.Diagnostic);
            Assert.Equal(GitOutboxState.Committed, current.State); Assert.Equal(batch.CommitSha, current.CommitSha);
            Assert.Equal(batch.OperationId, current.OperationId); Assert.Equal(batch.PlanHash, current.PlanHash);
        }
        f.Restart(); var exhausted = commands.Count;
        Assert.Equal("git-push-retry-exhausted", f.Service.RunOnce(f.Owner));
        Assert.DoesNotContain("ls-remote", commands.Skip(exhausted)); Assert.DoesNotContain("push", commands);
        Assert.Equal(f.BaseHead, f.RemoteHead);
        Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Assert.All(f.Outbox.Read().Intents, i => Assert.Equal(GitOutboxState.Committed, i.State));
        // Revocation must stop even local recovery before any new remote query.
        var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = p.PolicyRevision + 1, AuthorizationId = "synthetic_a1_revoked", Revoked = true }, p.PolicyRevision);
        f.Restart(); var revoked = commands.Count;
        Assert.Equal("git-disabled", f.Service.RunOnce(f.Owner)); Assert.Equal(revoked, commands.Count);
        output.WriteLine("F73-A1: exhausted writes=3; checks=3 (BaseHead/offline/BaseHead); restart at cap queries=0; revoked queries=0; native push commands=0.");
        Evidence(f, commands, 1);
    }

    private static void ExhaustPushBudget(Fixture f)
    {
        var unavailable = f.Remote + ".offline"; Directory.Move(f.Remote, unavailable);
        try
        {
            for (var n = 1; n <= 3; n++)
            {
                Assert.Equal("git-push-retry", f.Service.RunOnce(f.Owner));
                Assert.Equal("git_push_retry_" + n, f.Outbox.Read().Batches.Single().Diagnostic);
                f.Restart();
            }
        }
        finally { Directory.Move(unavailable, f.Remote); }
    }

    [Fact]
    public void OfflineRetryIsDurablyBoundedAndExplicitRetryRechecksRemote()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f); var unavailable = f.Remote + ".offline";
        Directory.Move(f.Remote, unavailable);
        try
        {
            for (var n = 1; n <= 3; n++)
            {
                Assert.Equal("git-push-retry", f.Service.RunOnce(f.Owner));
                Assert.Equal("git_push_retry_" + n, f.Outbox.Read().Batches.Single().Diagnostic);
                f.Restart();
            }
            Assert.Equal("git-push-retry-exhausted", f.Service.RunOnce(f.Owner));
            Assert.Equal(GitOutboxState.Committed, f.Outbox.Read().Batches.Single().State);
            Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
            Assert.Equal(f.BaseHead, GitProductionFixture.Run(unavailable, "rev-parse", "main"));
            Assert.DoesNotContain("push", commands); f.CheckCompleted();
        }
        finally { Directory.Move(unavailable, f.Remote); }
        Assert.Equal("git-pushed", f.Service.RetryPublication(f.Owner, batch.Id));
        Assert.Single(commands, c => c == "push"); Evidence(f, commands, 1);
    }

    [Fact]
    public void RejectedPushRetainsCommittedAndNextAuthorizedTurnRetriesNativeEngine()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        // Real rejection by the fictitious bare receiver, no authentication/credential changes.
        GitProductionFixture.Run(f.Remote, "config", "receive.hideRefs", "refs/heads/main");
        Assert.Equal("git-push-retry", f.Service.RunOnce(f.Owner));
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.Single(commands, c => c == "push");
        Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Assert.Equal(GitOutboxState.Committed, f.Outbox.Read().Batches.Single().State); f.CheckCompleted();
        GitProductionFixture.Run(f.Remote, "config", "--unset", "receive.hideRefs");
        f.Restart(); Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        Assert.Equal(2, commands.Count(c => c == "push")); Evidence(f, commands, 1);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("upstream")]
    [InlineData("remote")]
    [InlineData("advanced")]
    [InlineData("corrupt-receipt")]
    public void ChangedBindingOrReceiptBlocksWithoutRemoteMutation(string fault)
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        var receiptPath = Path.Combine(f.F.Roots.StateRoot, "git-production", batch.OperationId.Value + ".json");
        if (fault == "identity") f.Run("config", "user.email", "altered@example.invalid");
        if (fault == "upstream") f.Run("config", "branch.main.merge", "refs/heads/other");
        if (fault == "remote") f.Run("config", "remote.origin.url", Path.Combine(f.F.Root, "other-bare.git"));
        if (fault == "corrupt-receipt") File.AppendAllText(receiptPath, "corruption");
        if (fault == "advanced")
        {
            var writer = Path.Combine(f.F.Root, "synthetic-other-writer");
            f.Run("clone", f.Remote, writer);
            GitProductionFixture.Run(writer, "config", "user.name", "Synthetic external writer");
            GitProductionFixture.Run(writer, "config", "user.email", "other@example.invalid");
            File.WriteAllText(Path.Combine(writer, "other.txt"), "synthetic external change");
            GitProductionFixture.Run(writer, "add", "--", "other.txt");
            GitProductionFixture.Run(writer, "commit", "-m", "synthetic advanced remote");
            GitProductionFixture.Run(writer, "push", "origin", "main");
        }
        var remote = f.RemoteHead; var local = f.Run("rev-parse", "HEAD"); var index = f.Run("status", "--porcelain=v2");
        Assert.Equal("git-blocked", f.Service.RunOnce(f.Owner));
        Assert.Equal(GitOutboxState.Blocked, f.Outbox.Read().Batches.Single().State);
        Assert.Equal(remote, f.RemoteHead); Assert.Equal(local, f.Run("rev-parse", "HEAD"));
        Assert.Equal(index, f.Run("status", "--porcelain=v2")); Assert.DoesNotContain("push", commands);
        if (fault != "corrupt-receipt") Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        else File.WriteAllText(receiptPath, File.ReadAllText(receiptPath)[..^"corruption".Length]); // Restore only the synthetic test fault to emit receipt evidence.
        Evidence(f, commands, 1);
    }

    [Fact]
    public void RevocationAndNewGrantNeverReactivateOldCommittedBatch()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f); var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "synthetic_revocation", Revoked = true }, 1);
        f.Restart(); Assert.Equal("git-disabled", f.Service.RunOnce(f.Owner));
        f.Policy.Save(p with { PolicyRevision = 3, AuthorizationId = "synthetic_new_grant" }, 2);
        f.Restart(); Assert.Equal("git-authorization-required", f.Service.RunOnce(f.Owner));
        Assert.Throws<InvalidDataException>(() => f.Service.RetryPublication(f.Owner, batch.Id));
        Assert.Equal(GitOutboxState.Committed, f.Outbox.Read().Batches.Single().State);
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.DoesNotContain("push", commands); Evidence(f, commands, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualAndExternalPushReconcileOnlyTheExactBoundOperation(bool external)
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        if (external)
        {
            f.Run("push", "origin", "HEAD:refs/heads/main");
            Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
            Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        }
        else
        {
            var result = new GitPublicationManualService(f.Git, f.Inspector, f.Policy).Push(f.F.Context, batch.OperationId);
            Assert.Equal(GitPushOutcome.Pushed, result.Outcome);
        }
        Assert.Equal(batch.CommitSha, f.RemoteHead); Assert.Equal(GitOutboxState.Pushed, f.Outbox.Read().Batches.Single().State);
        Assert.Equal(external ? 0 : 1, commands.Count(c => c == "push")); Evidence(f, commands, 1);
    }

    [Fact]
    public void ManualModeRequiresExplicitPushAndDoesNotAdoptAnUnrelatedReceipt()
    {
        using var f = new Fixture(mode: GitPublicationMode.Manual, allowPush: true); var job = f.Complete(); var commands = Commands(f);
        Assert.Equal("git-manual", f.Service.RunOnce(f.Owner));
        var result = new DurableExecutionService(f.F.Context, f.F.Queue).VerifyCompletedReadOnly(job.ItemId!).Production;
        var manual = new GitPublicationManualService(f.Git, f.Inspector, f.Policy);
        var commit = manual.Commit(f.F.Context, manual.PrepareCommit(f.F.Context, [result], "synthetic unrelated manual operation"));
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.DoesNotContain("push", commands);
        Assert.Equal(GitPushOutcome.Pushed, manual.Push(f.F.Context, commit.OperationId).Outcome);
        Assert.Empty(f.Outbox.Read().Batches); Assert.Empty(f.Outbox.Read().Intents);
        Assert.Equal(commit.CommitSha, f.RemoteHead); Assert.Single(commands, c => c == "push"); f.CheckCompleted();
        output.WriteLine(JsonSerializer.Serialize(new { ZipCount = 1, Identity = "f72@example.invalid", Transport = "temporary-local-bare-only",
            f.BaseHead, Head = f.Run("rev-parse", "HEAD"), commit.CommitSha, RemoteTip = f.RemoteHead, OperationId = commit.OperationId.Value,
            Receipt = f.Git.ReadReceipt(f.F.Context, commit.OperationId).State.ToString(), Outbox = "empty; unrelated operation not adopted",
            Jobs = "Completed", Queue = "Completed", NativePushCommands = commands.Count(c => c == "push") }));
    }

    [Fact]
    public void ExternalPushInCommitOnlyModeIsRecognizedWithoutPushPermission()
    {
        using var f = new Fixture(); f.Complete(); var commands = Commands(f);
        Assert.Equal("git-committed", f.Service.RunOnce(f.Owner));
        var batch = f.Outbox.Read().Batches.Single();
        Assert.False(f.Policy.Load()!.AllowPush);
        f.Run("push", "origin", "HEAD:refs/heads/main");
        Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        Assert.Equal(GitOutboxState.Pushed, f.Outbox.Read().Batches.Single().State);
        Assert.Equal(batch.CommitSha, f.RemoteHead); Assert.DoesNotContain("push", commands); Evidence(f, commands, 1);
    }

    [Fact]
    public void NewManualGrantRequiresExplicitPushDecisionForOldBatch()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f); var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "synthetic_explicit_manual_grant", Mode = GitPublicationMode.Manual }, 1);
        f.Restart(); Assert.Equal("git-manual", f.Service.RunOnce(f.Owner));
        Assert.Equal(f.BaseHead, f.RemoteHead); Assert.DoesNotContain("push", commands);
        var result = new GitPublicationManualService(f.Git, f.Inspector, f.Policy).Push(f.F.Context, batch.OperationId);
        Assert.Equal(GitPushOutcome.Pushed, result.Outcome);
        Assert.Equal(GitOutboxState.Pushed, f.Outbox.Read().Batches.Single().State);
        Assert.Equal(p.AuthorizationId, f.Outbox.Read().Intents.Single().AuthorizationId);
        Assert.Equal(1, f.Outbox.Read().Intents.Single().PolicyRevision);
        Assert.Single(commands, c => c == "push"); Evidence(f, commands, 1);
    }

    [Fact]
    public void DurablePushedEvidenceCanReconcileUnderNewCommitOnlyGrantWithoutPublishing()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete(); var commands = Commands(f);
        Cut(f, "push-returned"); Assert.Throws<Interruption>(() => f.Service.RunOnce(f.Owner));
        var p = f.Policy.Load()!;
        f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "synthetic_read_reconciliation", Mode = GitPublicationMode.AutoCommitBatch, AllowPush = false }, 1);
        f.Restart(); Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner));
        Assert.Single(commands, c => c == "push");
        Assert.Equal(1, f.Outbox.Read().Intents.Single().PolicyRevision); Evidence(f, commands, 1);
    }

    [Fact]
    public void ManualAndPolicyWriterCannotRaceOwnerAtTheIrreversibleBoundary()
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete(); var commands = Commands(f);
        var checkpointReached = false;
        // Use the already durable batch ID so the observer never reacquires the policy mutex.
        var reserved = CommitWithoutPush(f); var p = f.Policy.Load()!;
        using (GitProductionFixture.Invoke<IDisposable>("ExecutionMutex", "Acquire", "Production", f.F.Roots.ProductionRoot, ""))
            Assert.Equal("git-busy", f.Service.RunOnce(f.Owner));
        Assert.Null(f.Outbox.Read().Batches.Single().Diagnostic); Assert.DoesNotContain("push", commands);
        BackupTestSupport.Set(f.Git, "Observer", (Action<string>)(point =>
        {
            if (point != "before_push_command") return;
            checkpointReached = true; Exception? manualError = null; Exception? policyError = null;
            var thread = new Thread(() =>
            {
                manualError = Record.Exception(() => new GitPublicationManualService(f.Git, f.Inspector, f.Policy).Push(f.F.Context, reserved.OperationId));
                policyError = Record.Exception(() => f.Policy.Save(p with { PolicyRevision = 2, AuthorizationId = "racing_revocation", Revoked = true }, 1));
            });
            thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            Assert.IsType<AutomationException>(manualError); Assert.IsType<AutomationException>(policyError);
            Assert.Throws<IOException>(() => f.Policy.Load()); // Same-thread policy reentry is also Busy.
        }));
        Assert.Equal("git-pushed", f.Service.RunOnce(f.Owner)); Assert.True(checkpointReached);
        Assert.Single(commands, c => c == "push"); Evidence(f, commands, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePushRechecksAuthorizationBeforeCommandAndAlreadyPushedReceipt(bool alreadyPushed)
    {
        using var f = new Fixture(mode: GitPublicationMode.AutoCommitAndPushBatch); f.Complete();
        var batch = CommitWithoutPush(f); var commands = Commands(f);
        if (alreadyPushed) f.Run("push", "origin", "HEAD:refs/heads/main");
        var invoked = false;
        Action<GitRepositoryStatus> reject = _ => { invoked = true; throw new InvalidDataException("synthetic last-boundary revocation"); };
        var method = typeof(GitProductionService).GetMethod("PushBound", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(f.Git, [f.F.Context, batch.OperationId, reject, false, false]));
        Assert.IsType<InvalidDataException>(error.InnerException); Assert.True(invoked);
        Assert.DoesNotContain("push", commands);
        Assert.Equal(alreadyPushed ? batch.CommitSha : f.BaseHead, f.RemoteHead);
        Assert.Equal(GitReceiptState.Committed, f.Git.ReadReceipt(f.F.Context, batch.OperationId).State);
        Assert.Equal(GitOutboxState.Committed, f.Outbox.Read().Batches.Single().State); Evidence(f, commands, 1);
    }
}
