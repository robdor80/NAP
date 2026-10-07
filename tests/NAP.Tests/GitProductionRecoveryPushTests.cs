using System.Text.Json.Nodes;
using NAP.Core;
using Xunit;
using static NAP.Tests.GitProductionFixture;

namespace NAP.Tests;

public sealed class GitProductionRecoveryPushTests
{
    [Fact]
    public void SimulatedNoninteractiveAuthenticationFailureKeepsCommittedReceipt()
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan());
        f.ObserveCommands(command => { if (command == "ls-remote") throw AiAuditTestData.Construct<GitProductionException>(NapIssueCodes.GitRemoteUnavailable, "Noninteractive authentication failed."); });
        Stop(() => f.Service.Push(f.Context, result), NapIssueCodes.GitRemoteUnavailable);
        Assert.Equal(result.CommitSha, f.Git("rev-parse", "HEAD")); Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, result.OperationId).State);
    }
    [Theory] [InlineData("prepared")] [InlineData("before_commit")] [InlineData("after_commit")]
    public void DurableRecoveryHandlesCrashesAtEachBoundary(string boundary)
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        f.Observe(stage => { if (stage == boundary) throw new SimulatedCrash(); });
        Assert.Throws<SimulatedCrash>(() => f.Service.Commit(f.Context, plan));
        var id = Assert.Single(f.Service.ListOperations(f.Context)); var receipt = f.Service.ReadReceipt(f.Context, id);
        Assert.Equal(GitReceiptState.Prepared, receipt.State); f.Observe(_ => { });
        if (boundary == "after_commit")
        {
            Assert.NotEqual(plan.Status.Head, f.Git("rev-parse", "HEAD"));
            var recovered = f.Service.Recover(f.Context, id); Assert.Equal(GitReceiptState.Committed, recovered.State);
            Assert.Equal(receipt.ExpectedCommitSha, recovered.CommitSha); Assert.Equal(GitPushOutcome.Pushed, f.Service.Push(f.Context, id).Outcome);
        }
        else
        {
            Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal(GitReceiptState.Prepared, f.Service.Recover(f.Context, id).State);
            var committed = f.Service.RetryCommit(f.Context, id); Assert.Equal(id, committed.OperationId);
            Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, id).State);
            Assert.Equal(GitPushOutcome.Pushed, f.Service.Push(f.Context, committed).Outcome);
        }
    }
    [Fact]
    public void SimulatedHardCrashRetainsOwnedIndexAndExplicitRetryRecoversOnlyThatIndex()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        f.Observe(stage => { if (stage == "prepared") throw new SimulatedCrash(); });
        Assert.Throws<SimulatedCrash>(() => f.Service.Commit(f.Context, plan)); f.Observe(_ => { });
        var id = Assert.Single(f.Service.ListOperations(f.Context)); f.Git("add", "--", plan.Selection.Paths[0]);
        Assert.Equal(GitReceiptState.Prepared, f.Service.Recover(f.Context, id).State);
        var result = f.Service.RetryCommit(f.Context, id); Assert.Equal(plan.Selection.Paths, result.CommittedPaths); Assert.Equal("", f.Git("status", "--porcelain"));
    }
    [Fact]
    public void ForeignCommitWithSamePathsParentAndSubjectIsNeverAdopted()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        f.Observe(stage => { if (stage == "before_commit") throw new SimulatedCrash(); });
        Assert.Throws<SimulatedCrash>(() => f.Service.Commit(f.Context, plan)); var id = Assert.Single(f.Service.ListOperations(f.Context));
        foreach (var path in plan.Selection.Paths) f.Git("add", "--", path);
        f.Git("config", "user.name", "Other author"); f.Git("commit", "-m", plan.Message); f.Git("config", "user.name", "NAP temporary test");
        var head = f.Git("rev-parse", "HEAD"); Stop(() => f.Service.Recover(f.Context, id), NapIssueCodes.GitReceiptInvalid);
        Assert.Equal(head, f.Git("rev-parse", "HEAD")); Assert.Equal(GitReceiptState.Prepared, f.Service.ReadReceipt(f.Context, id).State);
    }
    [Theory] [InlineData("corrupt")] [InlineData("version")] [InlineData("unknown")] [InlineData("duplicate")] [InlineData("universe")]
    [InlineData("binding")] [InlineData("path")] [InlineData("sha")] [InlineData("size")] [InlineData("state")]
    public void ClosedReceiptSchemaRejectsTamperingAndNeverRepairs(string mutation)
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan()); var path = f.ReceiptPath(result.OperationId);
        var j = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        switch (mutation)
        {
            case "version": j["schema_version"] = 2; break;
            case "unknown": j["unexpected"] = true; break;
            case "universe": j["universe_id"] = "another_world"; break;
            case "binding": j["repository_binding"] = new string('1', 64); break;
            case "path": j["owned_files"]![0]!["relative_path"] = "../outside"; break;
            case "sha": j["expected_commit_sha"] = new string('1', result.CommitSha.Length); break;
            case "size": j["owned_files"]![0]!["size_bytes"] = -1; break;
            case "state": j["state"] = "999"; break;
        }
        File.WriteAllText(path, mutation == "corrupt" ? "{invalid" : mutation == "duplicate" ? j.ToJsonString()[..^1] + ",\"schema_version\":1}" : j.ToJsonString());
        var before = File.ReadAllBytes(path); var head = f.Git("rev-parse", "HEAD");
        Stop(() => f.Service.ReadReceipt(f.Context, result.OperationId), NapIssueCodes.GitReceiptInvalid);
        Stop(() => f.Service.Push(f.Context, result), NapIssueCodes.GitReceiptInvalid);
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(head, f.Git("rev-parse", "HEAD"));
    }
    [Fact]
    public void TempReceiptIsNeverAdoptedOrDeleted()
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan()); var path = f.ReceiptPath(result.OperationId); var temp = path + ".pending.tmp";
        File.Move(path, temp); var bytes = File.ReadAllBytes(temp);
        Stop(() => f.Service.ReadReceipt(f.Context, result.OperationId), NapIssueCodes.GitReceiptInvalid);
        Stop(() => f.Service.ListOperations(f.Context), NapIssueCodes.GitReceiptInvalid); Assert.Equal(bytes, File.ReadAllBytes(temp)); Assert.False(File.Exists(path));
    }
    [Fact]
    public void CopiedReceiptCannotCrossRepositoryBindingEvenWithinSameUniverse()
    {
        using var a = new GitProductionFixture(); using var b = new GitProductionFixture(); var result = a.Service.Commit(a.Context, a.Plan());
        Directory.CreateDirectory(Path.GetDirectoryName(b.ReceiptPath(result.OperationId))!); File.Copy(a.ReceiptPath(result.OperationId), b.ReceiptPath(result.OperationId));
        Stop(() => b.Service.ReadReceipt(b.Context, result.OperationId), NapIssueCodes.GitReceiptInvalid);
    }
    [Fact]
    public void FastForwardPushVerifiesRemoteAndSecondPushIsIdempotentWithNoOtherRefs()
    {
        using var f = new GitProductionFixture(); f.Git("tag", "private-tag"); f.Git("branch", "private-branch"); f.Git("config", "push.followTags", "true");
        var result = f.Service.Commit(f.Context, f.Plan()); var commands = new List<string>(); f.ObserveCommands(commands.Add);
        Assert.Equal(GitPushOutcome.Pushed, f.Service.Push(f.Context, result).Outcome); Assert.Equal(result.CommitSha, f.RemoteGit("rev-parse", "main"));
        Assert.Equal(GitReceiptState.Pushed, f.Service.ReadReceipt(f.Context, result.OperationId).State); Assert.Single(commands, c => c == "push");
        Assert.Equal(GitPushOutcome.AlreadyPushed, f.Service.Push(f.Context, result.OperationId).Outcome); Assert.Single(commands, c => c == "push");
        Assert.Equal("refs/heads/main", f.RemoteGit("for-each-ref", "--format=%(refname)")); Assert.Equal(result.CommitSha, f.Git("rev-parse", "HEAD"));
        Assert.DoesNotContain("fetch", commands); Assert.DoesNotContain("pull", commands); Assert.DoesNotContain("merge", commands); Assert.DoesNotContain("rebase", commands);
    }
    [Fact]
    public void PushAlreadyPerformedBeforeReceiptUpdateIsRecoveredWithoutAnotherPush()
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan()); f.Git("push", "origin", "HEAD:refs/heads/main");
        var commands = new List<string>(); f.ObserveCommands(commands.Add);
        Assert.Equal(GitPushOutcome.AlreadyPushed, f.Service.Push(f.Context, result).Outcome); Assert.DoesNotContain("push", commands);
        Assert.Equal(GitReceiptState.Pushed, f.Service.ReadReceipt(f.Context, result.OperationId).State);
    }
    [Theory] [InlineData("advance")] [InlineData("missing")] [InlineData("offline")] [InlineData("rejected")]
    public void FailedPublicationPreservesCommitReceiptAndCleanRepository(string mode)
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan());
        if (mode == "advance") f.RemoteAdvance();
        if (mode == "missing") f.RemoteGit("update-ref", "-d", "refs/heads/main");
        if (mode == "offline") Directory.Move(f.Remote, f.Remote + ".offline");
        if (mode == "rejected") f.RemoteGit("config", "receive.hideRefs", "refs/heads/main");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        Stop(() => f.Service.Push(f.Context, result), mode is "advance" or "missing" ? NapIssueCodes.GitRemoteChanged : mode == "offline" ? NapIssueCodes.GitRemoteUnavailable : NapIssueCodes.GitPushRejected);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(result.CommitSha, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("status", "--porcelain"));
        Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, result.OperationId).State);
        if (mode == "offline") { Directory.Move(f.Remote + ".offline", f.Remote); Assert.Equal(GitPushOutcome.Pushed, f.Service.Push(f.Context, result).Outcome); }
    }
    [Fact]
    public void PushPostVerificationDoesNotTrustSuccessfulExit()
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan());
        f.ObserveCommands(command => { if (command == "push") f.RemoteGit("update-ref", "refs/heads/main", result.OldHead); });
        Stop(() => f.Service.Push(f.Context, result), NapIssueCodes.GitPushVerificationFailed);
        Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, result.OperationId).State); Assert.Equal(result.CommitSha, f.Git("rev-parse", "HEAD"));
    }
    [Theory] [InlineData("file")] [InlineData("head")] [InlineData("branch")] [InlineData("remote")]
    public void PushRequiresExactLocalCommitAndFrozenRepository(string mode)
    {
        using var f = new GitProductionFixture(); var result = f.Service.Commit(f.Context, f.Plan());
        switch (mode)
        {
            case "file": File.WriteAllText(Path.Combine(f.Root, "manual.txt"), "manual"); break;
            case "head": f.Git("commit", "--allow-empty", "-m", "other"); break;
            case "branch": f.Git("checkout", "-b", "different"); break;
            case "remote": f.Git("remote", "rename", "origin", "different"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Root); Stop(() => f.Service.Push(f.Context, result)); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal(result.OldHead, f.RemoteGit("rev-parse", "main")); Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, result.OperationId).State);
    }
    private sealed class SimulatedCrash : Exception;
}
