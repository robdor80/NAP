using System.Reflection;
using NAP.Core;
using Xunit;
using static NAP.Tests.GitProductionFixture;

namespace NAP.Tests;

public sealed class GitProductionInspectionTests
{
    [Fact]
    public void ActualUnmergedIndexIsReportedFactuallyAndCannotPrepareCommit()
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var blob = f.Git("rev-parse", "HEAD:README.md");
        var start = new System.Diagnostics.ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-C", f.Root, "update-index", "--index-info" }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardInput.Write("0 " + new string('0', 40) + "\tREADME.md\n100644 " + blob + " 1\tREADME.md\n100644 " + blob + " 2\tREADME.md\n100644 " + blob + " 3\tREADME.md\n"); process.StandardInput.Close();
        Assert.True(process.WaitForExit(30000)); Assert.Equal(0, process.ExitCode);
        var change = f.Inspector.Inspect(f.Context).Changes.Single(c => c.RelativePath == "README.md"); Assert.True(change.IsConflict); Assert.Equal(GitChangeKind.Conflicted, change.ChangeKind);
        Stop(() => f.Plan(result), NapIssueCodes.GitIndexNotClean);
    }
    [Fact]
    public void SubmoduleChangesAreVisibleAndAlwaysBlockAutomaticCommit()
    {
        using var f = new GitProductionFixture(); f.Git("-c", "protocol.file.allow=always", "submodule", "add", f.Remote, "modules/child");
        f.Git("commit", "-m", "submodule fixture"); f.Git("push", "origin", "main");
        File.WriteAllText(Path.Combine(f.Root, "modules", "child", "README.md"), "manual submodule changes");
        var status = f.Inspector.Inspect(f.Context); Assert.Contains(status.Changes, c => c.RelativePath == "modules/child" && c.IsSubmodule);
        Stop(() => f.Plan(), NapIssueCodes.GitChangesConflict);
    }
    [Fact]
    public void CleanInspectionIsExactlyReadOnlyIncludingGitBytesAndTimestamps()
    {
        using var f = new GitProductionFixture(); var before = ArchiveTestFixture.Snapshot(f.Production.Root);
        var stamps = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc);
        var s = f.Inspector.Inspect(f.Context);
        Assert.True(s.IsClean); Assert.True(s.IndexIsClean); Assert.Equal(GitSynchronization.Synchronized, s.Synchronization);
        Assert.Equal(f.Git("rev-parse", "HEAD"), s.Head); Assert.Equal(s.Head, s.RemoteTip);
        Assert.Equal("main", s.Branch); Assert.Equal("origin", s.RemoteName); Assert.Equal("main", s.RemoteBranch); Assert.Equal("refs/remotes/origin/main", s.Upstream);
        Assert.Equal(64, s.RemoteFingerprint.Length); ArchiveTestFixture.AssertSnapshot(before, f.Production.Root);
        Assert.All(stamps, e => Assert.Equal(e.Value, File.GetLastWriteTimeUtc(e.Key)));
    }
    [Theory]
    [InlineData("untracked", '?', '?', GitChangeKind.Untracked)]
    [InlineData("modified", '.', 'M', GitChangeKind.Modified)]
    [InlineData("added", 'A', '.', GitChangeKind.Added)]
    [InlineData("deleted", '.', 'D', GitChangeKind.Deleted)]
    [InlineData("staged", 'M', '.', GitChangeKind.Modified)]
    [InlineData("both", 'M', 'M', GitChangeKind.Modified)]
    [InlineData("rename", 'R', '.', GitChangeKind.Renamed)]
    public void PorcelainDetectsExactIndexAndWorkingStates(string mode, char index, char worktree, GitChangeKind kind)
    {
        using var f = new GitProductionFixture(); var path = Path.Combine(f.Root, "README.md");
        switch (mode)
        {
            case "untracked": File.WriteAllText(Path.Combine(f.Root, "new.md"), "new"); break;
            case "added": File.WriteAllText(Path.Combine(f.Root, "new.md"), "new"); f.Git("add", "--", "new.md"); break;
            case "deleted": File.Delete(path); break;
            case "rename": f.Git("mv", "README.md", "renamed.md"); break;
            default: File.WriteAllText(path, "changed"); if (mode is "staged" or "both") f.Git("add", "--", "README.md");
                if (mode == "both") File.WriteAllText(path, "after staging"); break;
        }
        var e = Assert.Single(f.Inspector.Inspect(f.Context).Changes);
        Assert.Equal(index, e.IndexStatus); Assert.Equal(worktree, e.WorktreeStatus); Assert.Equal(kind, e.ChangeKind);
        Assert.Equal(mode == "rename" ? "README.md" : null, e.OriginalRelativePath);
    }
    [Theory] [InlineData("Unicode ñ 漢字.md")] [InlineData("space file.md")] [InlineData("-leading.md")] [InlineData("bracket[1].md")]
    public void FilenamesAreExactAndOrdinal(string name)
    {
        using var f = new GitProductionFixture(); File.WriteAllText(Path.Combine(f.Root, name), "bytes"); File.WriteAllText(Path.Combine(f.Root, "Z.md"), "Z");
        var s = f.Inspector.Inspect(f.Context); Assert.Contains(s.Changes, c => c.RelativePath == name);
        Assert.Equal(s.Changes.Select(c => c.RelativePath).Order(StringComparer.Ordinal), s.Changes.Select(c => c.RelativePath));
        Assert.Throws<NotSupportedException>(() => ((IList<GitChangeEntry>)s.Changes).Clear());
    }
    [Theory] [InlineData("a\"quote.md")] [InlineData("line\nbreak.md")] [InlineData("literal\\name.md")] [InlineData("tail space ")]
    public void RealUnixNamesRemainExact(string name)
    {
        if (OperatingSystem.IsWindows()) return;
        using var f = new GitProductionFixture(); File.WriteAllText(Path.Combine(f.Root, name), "bytes");
        Assert.Equal(name, Assert.Single(f.Inspector.Inspect(f.Context).Changes).RelativePath);
    }
    [Fact]
    public void NulParserPreservesQuotesNewlinesBackslashesAndRenamePairs()
    {
        var head = new string('1', 40); var nul = "# branch.oid " + head + "\0# branch.head main\0" +
            "? a\"quote.md\0? line\nbreak.md\0? literal\\name.md\0" +
            "2 R. N... 100644 100644 100644 " + head + " " + head + " R100 new name\0old name\0";
        var parsed = Invoke<object>("GitProductionInspector", "Parse", nul);
        var entries = (IReadOnlyList<GitChangeEntry>)parsed.GetType().GetProperty("Changes")!.GetValue(parsed)!;
        Assert.Contains(entries, e => e.RelativePath == "line\nbreak.md"); Assert.Contains(entries, e => e.RelativePath == "literal\\name.md");
        Assert.Equal("old name", entries.Single(e => e.ChangeKind == GitChangeKind.Renamed).OriginalRelativePath);
    }
    [Theory] [InlineData("garbage\0")] [InlineData("# branch.oid abc\0")] [InlineData("# branch.oid abc\0# branch.head main\0? ../outside\0")]
    public void MalformedPorcelainCannotBecomeAStatus(string input) => Stop(() => Invoke<object>("GitProductionInspector", "Parse", input), NapIssueCodes.GitRepositoryInvalid);
    [Theory]
    [InlineData("MERGE_HEAD")] [InlineData("REBASE_HEAD")] [InlineData("CHERRY_PICK_HEAD")] [InlineData("REVERT_HEAD")]
    [InlineData("BISECT_LOG")] [InlineData("rebase-merge")] [InlineData("rebase-apply")] [InlineData("sequencer")] [InlineData("shallow")]
    public void IncompatibleGitOperationsStop(string marker)
    {
        using var f = new GitProductionFixture(); var p = Path.Combine(f.Root, ".git", marker);
        if (marker is "rebase-merge" or "rebase-apply" or "sequencer") Directory.CreateDirectory(p); else File.WriteAllText(p, "operation");
        Stop(() => f.Inspector.Inspect(f.Context), NapIssueCodes.GitOperationInProgress);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DetachedAndUnbornBranchesStop(bool unborn)
    {
        using var f = new GitProductionFixture();
        if (unborn) f.Git("checkout", "--orphan", "unborn"); else f.Git("checkout", "--detach", "HEAD");
        Stop(() => f.Inspector.Inspect(f.Context), unborn ? NapIssueCodes.GitUnbornBranch : NapIssueCodes.GitDetachedHead);
    }
    [Theory] [InlineData("alternates")] [InlineData("promisor")] [InlineData("partialclone")] [InlineData("include")] [InlineData("includeIf")]
    [InlineData("uploadpack")] [InlineData("receivepack")] [InlineData("worktree")] [InlineData("gitfile")]
    public void ExternalOrHostileRepositoryMetadataStopsBeforeNetwork(string mode)
    {
        using var f = new GitProductionFixture(); var metadata = Path.Combine(f.Root, ".git");
        switch (mode)
        {
            case "alternates": File.WriteAllText(Path.Combine(metadata, "objects", "info", "alternates"), f.Remote); break;
            case "promisor": File.WriteAllText(Path.Combine(metadata, "objects", "pack", "remote.promisor"), ""); break;
            case "partialclone": f.Git("config", "extensions.partialclone", "origin"); break;
            case "include": case "includeIf": File.AppendAllText(Path.Combine(metadata, "config"), "\n[" + mode + (mode == "includeIf" ? " \"gitdir:*\"" : "") + "]\npath=missing\n"); break;
            case "uploadpack": case "receivepack": f.Git("config", "remote.origin." + mode, "unexpected-command"); break;
            case "worktree": case "gitfile":
                var linked = Path.Combine(f.Production.Root, "linked"); f.Git("worktree", "add", "--detach", linked);
                var ctx = ProductionTestFixture.WithProduction(f.Context, linked); Stop(() => f.Inspector.Inspect(ctx), NapIssueCodes.GitRepositoryInvalid); return;
        }
        Stop(() => f.Inspector.Inspect(f.Context), NapIssueCodes.GitRepositoryInvalid);
    }
    [Fact]
    public void ProductionSubdirectoryNeverReadsParentRepository()
    {
        using var f = new GitProductionFixture(); var sub = Path.Combine(f.Root, "sub"); Directory.CreateDirectory(sub);
        Stop(() => f.Inspector.Inspect(ProductionTestFixture.WithProduction(f.Context, sub)), NapIssueCodes.GitRepositoryInvalid);
    }
    [Theory] [InlineData("")] [InlineData("https://user:secret@example.invalid/repo")] [InlineData("https://token@example.invalid/repo")]
    [InlineData("ssh://user:secret@example.invalid/repo")] [InlineData("ext::unexpected")]
    [InlineData("git://example.invalid/repo")] [InlineData("arbitrary://host/repo")] [InlineData("https://example.invalid/repo\nsecret")]
    public void UnsafeUrlsStopWithoutLeakingSecrets(string url)
    {
        using var f = new GitProductionFixture(); f.Git("remote", "set-url", "origin", url);
        var e = Stop(() => f.Inspector.Inspect(f.Context), NapIssueCodes.GitRemoteInvalid); Assert.DoesNotContain("secret", e.Message); Assert.DoesNotContain(f.Root, e.Message);
    }
    [Theory] [InlineData("https://example.invalid/repo")] [InlineData("ssh://git@example.invalid/repo")] [InlineData("git@example.invalid:owner/repo.git")]
    public void RuntimePolicyAcceptsOnlySupportedPublicTransports(string url)
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitProductionSafety")!;
        var config = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { ["remote.origin.url"] = [url], ["remote.origin.fetch"] = ["+refs/heads/*:refs/remotes/origin/*"] };
        var fingerprint = (string)type.GetMethod("RemoteFingerprint", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [config, "origin", false])!;
        Assert.Equal(64, fingerprint.Length); Assert.DoesNotContain(url, fingerprint);
    }
    [Fact]
    public void FileTransportCannotBeEnabledThroughPublicRuntimeApi()
    {
        using var f = new GitProductionFixture(); Stop(() => new GitProductionInspector().Inspect(f.Context), NapIssueCodes.GitRemoteInvalid);
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitProductionProcess")!;
        Assert.Empty(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)); Assert.False(type.IsPublic);
    }
    [Theory] [InlineData("upstream")] [InlineData("differentbranch")] [InlineData("multiplepushurl")] [InlineData("rewrite")] [InlineData("mirror")] [InlineData("credential")]
    [InlineData("fetchredirect")] [InlineData("proxy")]
    public void AmbiguousUpstreamAndTransportConfigurationStops(string mode)
    {
        using var f = new GitProductionFixture();
        switch (mode)
        {
            case "upstream": f.Git("config", "--unset", "branch.main.remote"); break;
            case "differentbranch": f.Git("config", "branch.main.merge", "refs/heads/other"); break;
            case "multiplepushurl": f.Git("config", "--add", "remote.origin.pushurl", f.Remote); f.Git("config", "--add", "remote.origin.pushurl", f.Remote); break;
            case "rewrite": f.Git("config", "url.ext::bad.insteadOf", f.Remote); break;
            case "mirror": f.Git("config", "remote.origin.mirror", "true"); break;
            case "credential": f.Git("config", "credential.helper", "!unexpected-command"); break;
            case "fetchredirect": f.Git("config", "remote.origin.fetch", "+refs/heads/*:refs/heads/*"); break;
            case "proxy": f.Git("config", "remote.origin.proxy", "http://unreviewed.invalid"); break;
        }
        Stop(() => f.Inspector.Inspect(f.Context), mode == "upstream" ? NapIssueCodes.GitUpstreamMissing : mode == "rewrite" ? NapIssueCodes.GitRepositoryInvalid : NapIssueCodes.GitRemoteInvalid);
    }
    [Fact]
    public void MissingRemoteBranchIsFactualAndBlocksPreparation()
    {
        using var f = new GitProductionFixture(synchronized: false); var s = f.Inspector.Inspect(f.Context);
        Assert.Null(s.RemoteTip); Assert.Equal(GitSynchronization.RemoteBranchMissing, s.Synchronization);
        Stop(() => f.Plan(), NapIssueCodes.GitNotSynchronized);
    }
    [Theory] [InlineData("ahead", GitSynchronization.Ahead)] [InlineData("behind", GitSynchronization.Behind)] [InlineData("diverged", GitSynchronization.Diverged)]
    public void SyncStatesUseObservedTipAndNeverFetch(string mode, GitSynchronization expected)
    {
        using var f = new GitProductionFixture();
        if (mode is "behind" or "diverged") { f.RemoteAdvance(); f.Git("fetch", "origin"); }
        if (mode is "ahead" or "diverged") { File.WriteAllText(Path.Combine(f.Root, "README.md"), "local\n"); f.Git("add", "--", "README.md"); f.Git("commit", "-m", "local"); }
        Assert.Equal(expected, f.Inspector.Inspect(f.Context).Synchronization); Stop(() => f.Plan(), NapIssueCodes.GitNotSynchronized);
    }
    [Fact]
    public void UnavailableRemoteIsControlledAndUnknownTipIsNotGuessed()
    {
        using var f = new GitProductionFixture(); f.RemoteAdvance(); Assert.Equal(GitSynchronization.Unknown, f.Inspector.Inspect(f.Context).Synchronization);
        Directory.Move(f.Remote, f.Remote + ".away"); Stop(() => f.Inspector.Inspect(f.Context), NapIssueCodes.GitRemoteUnavailable);
    }
}
