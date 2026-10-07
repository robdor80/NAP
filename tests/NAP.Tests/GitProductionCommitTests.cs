using System.Reflection;
using NAP.Core;
using Xunit;
using static NAP.Tests.GitProductionFixture;

namespace NAP.Tests;

public sealed class GitProductionCommitTests
{
    [Fact]
    public void UnexpectedStagedDeletionRestoresOnlyOwnedIndexEntryWithoutRecreatingFile()
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var prompt = result.FilesVerified.Single(p => p.Role == "prompt");
        var verified = File.ReadAllBytes(prompt.DestinationPath); File.WriteAllText(prompt.DestinationPath, "older tracked output\n");
        foreach (var file in result.FilesVerified) f.Git("add", "--", Path.GetRelativePath(f.Root, file.DestinationPath).Replace(Path.DirectorySeparatorChar, '/'));
        f.Git("commit", "-m", "temporary previous output history"); f.Git("push", "origin", "main"); File.WriteAllBytes(prompt.DestinationPath, verified);
        var plan = f.Plan(result); var path = Assert.Single(plan.Selection.Paths);
        f.Observe(stage => { if (stage == "staged") { File.Delete(prompt.DestinationPath); f.Git("add", "--", path); } });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitStagingMismatch);
        Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(prompt.DestinationPath)); Assert.Equal(" D " + path, f.Git("status", "--porcelain"));
    }
    [Fact]
    public void VerifiedModifiedOutputSelectsOnlyChangedPathAndRevalidatesUnchangedOutputs()
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var prompt = result.FilesVerified.Single(p => p.Role == "prompt");
        var verified = File.ReadAllBytes(prompt.DestinationPath); File.WriteAllText(prompt.DestinationPath, "older tracked output\n");
        foreach (var file in result.FilesVerified) f.Git("add", "--", Path.GetRelativePath(f.Root, file.DestinationPath).Replace(Path.DirectorySeparatorChar, '/'));
        f.Git("commit", "-m", "temporary previous output history"); f.Git("push", "origin", "main");
        File.WriteAllBytes(prompt.DestinationPath, verified);
        var plan = f.Plan(result); Assert.Equal(5, plan.Selection.OwnedFiles.Count);
        var path = Assert.Single(plan.Selection.Paths); Assert.EndsWith("_prompt.md", path);
        Assert.Equal(GitChangeKind.Modified, Assert.Single(plan.Status.Changes).ChangeKind);
        var commit = f.Service.Commit(f.Context, plan); Assert.Equal(new[] { path }, commit.CommittedPaths); Assert.Equal(verified, File.ReadAllBytes(prompt.DestinationPath));
        Assert.Equal("", f.Git("status", "--porcelain")); Assert.Equal(commit.OldHead, f.Git("rev-parse", "HEAD^"));
    }
    [Fact]
    public void PostCommitBranchChangeCannotPublishAVerifiedResult()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        f.Observe(stage => { if (stage == "after_commit") f.Git("checkout", "-b", "changed-after-commit"); });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitCommitVerificationFailed);
        Assert.NotEqual(plan.Status.Head, f.Git("rev-parse", "HEAD"));
        Assert.Equal(GitReceiptState.Prepared, f.Service.ReadReceipt(f.Context, Assert.Single(f.Service.ListOperations(f.Context))).State);
    }
    [Fact]
    public void FailedOwnedRollbackReportsActualIndexAndNeverResetsHead()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        f.Observe(stage => { if (stage == "staged") File.WriteAllText(Path.Combine(f.Root, ".git", "index.lock"), "external lock"); });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitRollbackFailed);
        Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.NotEqual("", f.Git("diff", "--cached", "--name-only"));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void SeveralResultsMustExplainEveryAssetChange(bool includeBoth)
    {
        using var f = new GitProductionFixture(); var first = f.Produce(); var second = f.ProduceAdditional();
        if (!includeBoth) { Stop(() => f.Plan(first), NapIssueCodes.GitUnownedChange); return; }
        var plan = f.Service.PrepareCommit(f.Context, [first, second], "Two verified assets"); Assert.Equal(10, plan.Selection.Paths.Count);
        var result = f.Service.Commit(f.Context, plan); Assert.Equal(10, result.CommittedPaths.Count); Assert.Equal("", f.Git("status", "--porcelain"));
    }
    [Theory] [InlineData("outside")] [InlineData("directory")] [InlineData("digest")] [InlineData("size")]
    public void ForgedStructuralEvidenceCannotAuthorizeArbitraryBytes(string mode)
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var file = result.FilesVerified[0];
        var type = file.GetType();
        switch (mode)
        {
            case "outside": type.GetField("<DestinationPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, Path.Combine(f.Production.Root, file.FileName)); break;
            case "directory": result.GetType().GetField("<RelativeDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(result, "other/asset"); break;
            case "digest": type.GetField("<Digest>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, new Sha256Digest(new string('1', 64))); break;
            case "size": type.GetField("<SizeBytes>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(file, file.SizeBytes + 1); break;
        }
        Stop(() => f.Plan(result), mode is "outside" or "directory" ? NapIssueCodes.GitUnownedChange : NapIssueCodes.GitOwnedFileChanged);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExactVerifiedProductionCommitsWithoutPushingAndPreservesBytes(bool sha256)
    {
        using var f = new GitProductionFixture(sha256: sha256); var result = f.Produce(); var before = ArchiveTestFixture.Snapshot(f.Root);
        var plan = f.Plan(result, "  Exact subject --not-an-option  "); ArchiveTestFixture.AssertSnapshot(before, f.Root);
        Assert.Equal("Exact subject --not-an-option", plan.Message); Assert.Equal(5, plan.Selection.Paths.Count); Assert.Equal(5, plan.Selection.OwnedFiles.Count);
        var commands = new List<string>(); f.ObserveCommands(commands.Add);
        var commit = f.Service.Commit(f.Context, plan);
        Assert.Equal(plan.Status.Head, commit.OldHead); Assert.Equal(sha256 ? 64 : 40, commit.CommitSha.Length);
        Assert.Equal(commit.CommitSha, f.Git("rev-parse", "HEAD")); Assert.Equal(commit.OldHead, f.Git("rev-parse", "HEAD^"));
        Assert.Equal(plan.Message, f.Git("log", "-1", "--format=%s")); Assert.Equal(plan.Selection.Paths, commit.CommittedPaths);
        Assert.Equal(plan.Status.Head, f.RemoteGit("rev-parse", "main")); Assert.DoesNotContain("push", commands);
        Assert.Equal("", f.Git("status", "--porcelain"));
        Assert.All(result.FilesVerified, file => { Assert.Equal(file.Digest, new Sha256Hasher().Compute(file.DestinationPath)); Assert.Equal(file.SizeBytes, new FileInfo(file.DestinationPath).Length); });
        Assert.Equal(GitReceiptState.Committed, f.Service.ReadReceipt(f.Context, commit.OperationId).State);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)plan.Selection.Paths).Clear());
        var receiptText = File.ReadAllText(f.ReceiptPath(commit.OperationId)); Assert.DoesNotContain(f.Root.Replace("\\", "\\\\"), receiptText); Assert.DoesNotContain(f.Remote.Replace("\\", "\\\\"), receiptText);
    }
    [Theory] [InlineData("")] [InlineData("  ")] [InlineData("a\nb")] [InlineData("a\rb")] [InlineData("nul\0b")] [InlineData("a\tb")]
    public void InvalidMessagesDoNotWriteAnything(string message)
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var before = ArchiveTestFixture.Snapshot(f.Production.Root);
        Stop(() => f.Plan(result, message), NapIssueCodes.GitMessageInvalid); ArchiveTestFixture.AssertSnapshot(before, f.Production.Root);
    }
    [Fact]
    public void OverlongMessageStops() { using var f = new GitProductionFixture(); Stop(() => f.Plan(message: new string('x', 201)), NapIssueCodes.GitMessageInvalid); }
    [Theory] [InlineData("README")] [InlineData("unknown")] [InlineData("staged")] [InlineData("deleted")] [InlineData("renamed")]
    public void UnownedManualChangesAlwaysStop(string mode)
    {
        using var f = new GitProductionFixture(); var result = f.Produce();
        switch (mode)
        {
            case "README": File.WriteAllText(Path.Combine(f.Root, "README.md"), "manual"); break;
            case "unknown": File.WriteAllText(Path.Combine(f.Root, "unknown.txt"), "manual"); break;
            case "staged": File.WriteAllText(Path.Combine(f.Root, "unknown.txt"), "manual"); f.Git("add", "--", "unknown.txt"); break;
            case "deleted": File.Delete(Path.Combine(f.Root, "README.md")); break;
            case "renamed": f.Git("mv", "README.md", "renamed.md"); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Production.Root);
        Stop(() => f.Plan(result), mode is "renamed" or "staged" ? NapIssueCodes.GitIndexNotClean : mode == "deleted" ? NapIssueCodes.GitChangesConflict : NapIssueCodes.GitUnownedChange);
        ArchiveTestFixture.AssertSnapshot(before, f.Production.Root);
    }
    [Theory] [InlineData("changed")] [InlineData("missing")] [InlineData("directory")] [InlineData("wrongsize")]
    public void PhysicallyUnverifiedOutputsCannotBecomeOwned(string mode)
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var file = result.FilesVerified[0];
        if (mode == "missing") File.Delete(file.DestinationPath);
        else if (mode == "directory") { File.Delete(file.DestinationPath); Directory.CreateDirectory(file.DestinationPath); }
        else if (mode == "wrongsize") File.AppendAllText(file.DestinationPath, "more bytes");
        else { var bytes = File.ReadAllBytes(file.DestinationPath); bytes[0] ^= 1; File.WriteAllBytes(file.DestinationPath, bytes); }
        Stop(() => f.Plan(result), NapIssueCodes.GitOwnedFileChanged);
    }
    [Fact]
    public void LinkedOutputAncestorStopsWithoutReadingOutsideRoot()
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var directory = Path.GetDirectoryName(result.FilesVerified[0].DestinationPath)!;
        var relocated = Path.Combine(f.Production.Root, "outside-outputs"); Directory.Move(directory, relocated); ArchiveTestFixture.Junction(directory, relocated);
        try { Stop(() => f.Plan(result), NapIssueCodes.GitOwnedFileChanged); }
        finally { Directory.Delete(directory); }
    }
    [Fact]
    public void IgnoredOutputIsExplicitlyRejected()
    {
        using var f = new GitProductionFixture(); f.Git("config", "core.excludesFile", Path.Combine(f.Production.Root, "ignores"));
        File.WriteAllText(Path.Combine(f.Production.Root, "ignores"), "*.webp\n"); var result = f.Produce();
        Stop(() => f.Plan(result), NapIssueCodes.GitIgnoredOutput);
    }
    [Theory] [InlineData("head")] [InlineData("file")] [InlineData("status")] [InlineData("remote")] [InlineData("branch")]
    [InlineData("upstream")] [InlineData("remoteurl")] [InlineData("remotename")] [InlineData("index")]
    public void EveryFrozenFactIsRevalidatedBeforeWriting(string mode)
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        switch (mode)
        {
            case "head": f.Git("commit", "--allow-empty", "-m", "other"); break;
            case "file": File.AppendAllText(Path.Combine(f.Root, plan.Selection.Paths[0].Replace('/', Path.DirectorySeparatorChar)), "changed"); break;
            case "status": File.WriteAllText(Path.Combine(f.Root, "unknown.txt"), "unknown"); break;
            case "remote": f.RemoteAdvance(); break;
            case "branch": f.Git("checkout", "-b", "another"); break;
            case "upstream": f.Git("config", "branch.main.merge", "refs/heads/other"); break;
            case "remoteurl": f.Git("remote", "set-url", "origin", f.Remote + "missing"); break;
            case "remotename": f.Git("remote", "rename", "origin", "elsewhere"); break;
            case "index": f.Git("add", "--", plan.Selection.Paths[0]); break;
        }
        var before = ArchiveTestFixture.Snapshot(f.Production.Root); Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitPlanStale);
        ArchiveTestFixture.AssertSnapshot(before, f.Production.Root); Assert.False(Directory.Exists(Path.Combine(f.Context.Storage.StateRoot, "git-production")));
    }
    [Theory] [InlineData("user.name")] [InlineData("user.email")]
    public void MissingIdentityStopsBeforeReceiptOrStaging(string key)
    {
        using var f = new GitProductionFixture(); f.Git("config", "--unset", key); var plan = f.Plan(); var before = ArchiveTestFixture.Snapshot(f.Production.Root);
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitIdentityMissing); ArchiveTestFixture.AssertSnapshot(before, f.Production.Root);
    }
    [Fact]
    public void AllHooksAndSigningAreDisabledForAssistedCommit()
    {
        using var f = new GitProductionFixture(); var hookDirectory = Path.Combine(f.Root, ".git", "hooks"); var marker = Path.Combine(f.Production.Root, "hook-ran");
        foreach (var hook in new[] { "pre-commit", "prepare-commit-msg", "commit-msg", "post-commit", "pre-push" })
        {
            var path = Path.Combine(hookDirectory, hook); File.WriteAllText(path, "#!/bin/sh\necho unexpected > '" + marker.Replace('\\', '/') + "'\nexit 1\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        f.Git("config", "commit.gpgSign", "true"); f.Git("config", "push.gpgSign", "true"); f.Git("config", "gpg.program", "does-not-exist");
        f.Git("config", "core.askPass", "does-not-exist");
        var result = f.Service.Commit(f.Context, f.Plan()); f.Service.Push(f.Context, result); Assert.False(File.Exists(marker));
        Assert.DoesNotContain("gpgsig", f.Git("cat-file", "commit", result.CommitSha));
        Assert.Equal("true", f.Git("config", "--local", "--get", "commit.gpgSign"));
        Assert.Equal("does-not-exist", f.Git("config", "--local", "--get", "core.askPass"));
    }
    [Theory] [InlineData("filter")] [InlineData("lfs")] [InlineData("encoding")] [InlineData("ident")]
    public void TransformingAttributesStopBeforeExecutingAnyFilter(string attribute)
    {
        using var f = new GitProductionFixture(); var marker = Path.Combine(f.Production.Root, "filter-ran");
        File.WriteAllText(Path.Combine(f.Root, ".gitattributes"), "icons/** " + (attribute switch { "encoding" => "working-tree-encoding=UTF-16", "ident" => "ident", _ => "filter=" + attribute }) + "\n");
        f.Git("add", "--", ".gitattributes"); f.Git("commit", "-m", "attributes"); f.Git("push", "origin", "main");
        if (attribute is "filter" or "lfs") f.Git("config", "filter." + attribute + ".clean", "echo unexpected > " + marker);
        var result = f.Produce(); Stop(() => f.Plan(result), NapIssueCodes.GitStagedContentMismatch); Assert.False(File.Exists(marker));
    }
    [Fact]
    public void ActualCrLfNormalizationStopsBeforeStaging()
    {
        using var f = new GitProductionFixture(); var source = f.Production.Package.FilesByRole["prompt"];
        File.WriteAllText(source, "verified\r\nphysical\r\nbytes\r\n"); f.Production.Refresh();
        File.WriteAllText(Path.Combine(f.Root, ".gitattributes"), "*.md text eol=lf\n"); f.Git("add", "--", ".gitattributes"); f.Git("commit", "-m", "attributes"); f.Git("push", "origin", "main");
        var result = f.Produce(); var before = ArchiveTestFixture.Snapshot(f.Root); var old = f.Git("rev-parse", "HEAD");
        var commands = new List<string>(); f.ObserveCommands(commands.Add);
        Stop(() => f.Plan(result), NapIssueCodes.GitStagedContentMismatch); Assert.DoesNotContain("add", commands);
        Assert.Equal(old, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Theory] [InlineData("extra")] [InlineData("missing")] [InlineData("residual")]
    public void TamperedIndexOrResidualChangesPreventCommitAndRollbackOnlyOwnedPaths(string mode)
    {
        using var f = new GitProductionFixture(); var plan = f.Plan(); var head = f.Git("rev-parse", "HEAD");
        f.Observe(stage =>
        {
            if (stage != "staged") return;
            if (mode == "missing") f.Git("restore", "--staged", "--", plan.Selection.Paths[0]);
            else { File.WriteAllText(Path.Combine(f.Root, "manual.txt"), "manual"); if (mode == "extra") f.Git("add", "--", "manual.txt"); }
        });
        Stop(() => f.Service.Commit(f.Context, plan), mode == "extra" ? NapIssueCodes.GitRollbackFailed : NapIssueCodes.GitStagingMismatch);
        Assert.Equal(head, f.Git("rev-parse", "HEAD"));
        Assert.Equal(mode == "extra" ? "manual.txt" : "", f.Git("diff", "--cached", "--name-only"));
        if (mode != "missing") Assert.Equal("manual", File.ReadAllText(Path.Combine(f.Root, "manual.txt")));
        Assert.All(plan.Selection.OwnedFiles, file => Assert.Equal(file.Digest, new Sha256Hasher().Compute(Path.Combine(f.Root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)))));
    }
    [Fact]
    public void CommitProcessFailureLeavesWorkingBytesAndReturnsOwnedIndexToClean()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan(); var head = f.Git("rev-parse", "HEAD"); var physical = ArchiveTestFixture.Snapshot(Path.Combine(f.Root, "icons"));
        f.Observe(stage => { if (stage == "before_commit") File.WriteAllText(Path.Combine(f.Root, ".git", "refs", "heads", "main.lock"), "external lock"); });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitCommitFailed); Assert.Equal(head, f.Git("rev-parse", "HEAD"));
        Assert.Equal("", f.Git("diff", "--cached", "--name-only")); ArchiveTestFixture.AssertSnapshot(physical, Path.Combine(f.Root, "icons"));
        Assert.Equal(GitReceiptState.Prepared, f.Service.ReadReceipt(f.Context, Assert.Single(f.Service.ListOperations(f.Context))).State);
    }
    [Fact]
    public void CrossUniversePlansResultsAndReceiptsCannotTouchAnotherRepository()
    {
        using var a = new GitProductionFixture("alpha"); using var b = new GitProductionFixture("beta"); var ra = a.Produce(); var rb = b.Produce(); var plan = b.Plan(rb);
        var protectedB = ArchiveTestFixture.Snapshot(b.Production.Root); Stop(() => a.Service.Commit(a.Context, plan), NapIssueCodes.GitPlanStale);
        Stop(() => a.Plan(rb), NapIssueCodes.GitUnownedChange); ArchiveTestFixture.AssertSnapshot(protectedB, b.Production.Root);
        var commit = b.Service.Commit(b.Context, plan); var beforeA = ArchiveTestFixture.Snapshot(a.Production.Root);
        Stop(() => a.Service.Push(a.Context, commit), NapIssueCodes.GitReceiptInvalid); ArchiveTestFixture.AssertSnapshot(beforeA, a.Production.Root);
        Assert.Equal(rb.AssetKey.AssetId, ra.AssetKey.AssetId);
    }
    [Fact]
    public void SameUniverseDifferentRootStillRejectsProductionResult()
    {
        using var a = new GitProductionFixture(); using var b = new GitProductionFixture(); var ra = a.Produce(); var rb = b.Produce();
        Stop(() => a.Plan(rb), NapIssueCodes.GitUnownedChange); Assert.Equal(ra.AssetKey, rb.AssetKey);
    }
}
