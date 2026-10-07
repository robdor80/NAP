using System.Reflection;
using System.Security.AccessControl;
using NAP.Core;
using Xunit;
using static NAP.Tests.GitProductionFixture;

namespace NAP.Tests;

public sealed class GitProductionSecurityTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExecutableRepositoryAndConfiguredHooksCannotRunDuringNapCommit(bool configuredOutsideHooks)
    {
        using var f = new GitProductionFixture(); var hooks = Path.Combine(f.Root, ".git", "hooks");
        var outsideHooks = Path.Combine(f.Production.Root, "user-hooks"); Directory.CreateDirectory(outsideHooks);
        var names = new[] { "pre-commit", "prepare-commit-msg", "commit-msg", "post-commit", "reference-transaction", "post-index-change", "pre-auto-gc", "post-rewrite", "pre-merge-commit", "future-hook" };
        foreach (var name in names)
        {
            var marker = Path.Combine(f.Production.Root, name + "-ran");
            WriteScript(Path.Combine(hooks, name), "printf executed > '" + marker.Replace('\\', '/') + "'\nexit 0\n");
            File.Copy(Path.Combine(hooks, name), Path.Combine(outsideHooks, name));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(outsideHooks, name), File.GetUnixFileMode(Path.Combine(hooks, name)));
            // Positive control: real Git recognizes and executes each installed script.
            f.Git("-c", "core.hooksPath=" + hooks, "hook", "run", name);
            Assert.True(File.Exists(marker)); File.Delete(marker);
        }
        if (configuredOutsideHooks) f.Git("config", "core.hooksPath", outsideHooks);
        var configBefore = File.ReadAllBytes(Path.Combine(f.Root, ".git", "config"));
        var result = f.Service.Commit(f.Context, f.Plan());
        Assert.Equal(result.CommitSha, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("status", "--porcelain"));
        Assert.All(names, name => Assert.False(File.Exists(Path.Combine(f.Production.Root, name + "-ran")), name));
        Assert.Equal(configBefore, File.ReadAllBytes(Path.Combine(f.Root, ".git", "config")));
    }

    [Fact]
    public void HookDirectoryIsFreshOutsideRepositoryEmptyAndRejectsScriptCreation()
    {
        using var f = new GitProductionFixture();
        var lease = Invoke<object>("GitProductionHooks", "Create", f.Context);
        var path = (string)lease.GetType().GetProperty("Path", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        try
        {
            Assert.StartsWith("nap-git-hooks-", Path.GetFileName(path)); Assert.Empty(Directory.EnumerateFileSystemEntries(path));
            Assert.StartsWith("..", Path.GetRelativePath(f.Root, path)); Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.ReparsePoint);
            if (OperatingSystem.IsWindows())
            {
                Assert.True(new DirectoryInfo(path).GetAccessControl().AreAccessRulesProtected);
                Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(path, "pre-commit"), "user script"));
            }
            else Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
        }
        finally { ((IDisposable)lease).Dispose(); }
        Assert.False(Directory.Exists(path));
    }

    [Theory] [InlineData("prepare", "malicious")] [InlineData("commit", "malicious")]
    [InlineData("prepare", "lfs")] [InlineData("commit", "lfs")]
    public void CustomAndLfsCleanScriptsStopBeforeAddWithoutChangingRepository(string operation, string filter)
    {
        using var f = new GitProductionFixture(); var result = f.Produce();
        var plan = operation == "commit" ? f.Plan(result) : null;
        File.WriteAllText(Path.Combine(f.Root, ".gitattributes"), "*.webp filter=" + filter + "\n");
        // Commit attributes only before enabling the malicious configuration; no production output is staged.
        f.Git("add", "--", ".gitattributes"); f.Git("commit", "-m", "filter attributes"); f.Git("push", "origin", "main");
        var marker = Path.Combine(f.Production.Root, "filter-ran"); var script = Path.Combine(f.Production.Root, "malicious-clean.sh");
        WriteScript(script, "printf executed > '" + marker.Replace('\\', '/') + "'\ncat\n");
        var clean = "sh '" + script.Replace('\\', '/') + "'";
        // Positive control in a separate temporary repo proves this clean command really executes.
        var control = Path.Combine(f.Production.Root, "filter-control"); Directory.CreateDirectory(control);
        Run(control, "init", "--initial-branch=main"); File.WriteAllText(Path.Combine(control, ".gitattributes"), "*.webp filter=" + filter + "\n");
        File.WriteAllText(Path.Combine(control, "control.webp"), "control bytes"); Run(control, "config", "filter." + filter + ".clean", clean);
        Run(control, "add", "--", "control.webp"); Assert.True(File.Exists(marker)); File.Delete(marker);
        f.Git("config", "filter." + filter + ".clean", clean);
        var head = f.Git("rev-parse", "HEAD"); var before = ArchiveTestFixture.Snapshot(f.Root);
        var commands = new List<string>(); f.ObserveCommands(commands.Add);
        Stop(() => { if (plan is null) f.Plan(result); else f.Service.Commit(f.Context, plan); });
        Assert.False(File.Exists(marker)); Assert.DoesNotContain("add", commands); Assert.DoesNotContain("commit", commands);
        Assert.Equal(head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Theory] [InlineData("filter=malicious")] [InlineData("filter=lfs")] [InlineData("working-tree-encoding=UTF-16")]
    [InlineData("ident")] [InlineData("text")] [InlineData("text=auto")] [InlineData("eol=lf")] [InlineData("crlf")]
    public void InfoAttributesChangedAfterPrepareStopBeforeAnyAdd(string attribute)
    {
        using var f = new GitProductionFixture(); var plan = f.Plan();
        File.WriteAllText(Path.Combine(f.Root, ".git", "info", "attributes"), "* " + attribute + "\n");
        var before = ArchiveTestFixture.Snapshot(f.Root); var commands = new List<string>(); f.ObserveCommands(commands.Add);
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitStagedContentMismatch);
        Assert.Contains("check-attr", commands); Assert.DoesNotContain("add", commands);
        Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }

    [Fact]
    public void AttributesChangedAfterPreparedReceiptStopBeforeFirstAdd()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan(); var commands = new List<string>(); f.ObserveCommands(commands.Add);
        f.Observe(stage => { if (stage == "prepared") File.WriteAllText(Path.Combine(f.Root, ".git", "info", "attributes"), "* filter=lfs\n"); });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitStagedContentMismatch);
        Assert.DoesNotContain("add", commands); Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void AttributeChangeBetweenAddsStopsNextAddAndRollsBackOnlyOwnedIndex()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan(); var physical = ArchiveTestFixture.Snapshot(Path.Combine(f.Root, "icons"));
        var commands = new List<string>();
        f.ObserveCommands(command =>
        {
            commands.Add(command);
            if (command == "add") File.WriteAllText(Path.Combine(f.Root, ".git", "info", "attributes"), "* filter=lfs\n");
        });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitStagedContentMismatch);
        Assert.Equal(1, commands.Count(command => command == "add")); Assert.DoesNotContain("commit", commands);
        Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        ArchiveTestFixture.AssertSnapshot(physical, Path.Combine(f.Root, "icons"));
    }

    [Theory] [InlineData("true")] [InlineData("input")]
    public void AmbientAutoCrLfCannotNormalizeVerifiedPhysicalBytes(string autocrlf)
    {
        using var f = new GitProductionFixture(); File.WriteAllText(f.Production.Package.FilesByRole["prompt"], "verified\r\nphysical\r\nbytes\r\n"); f.Production.Refresh();
        f.Git("config", "core.autocrlf", autocrlf); var result = f.Produce(); var plan = f.Plan(result);
        var commit = f.Service.Commit(f.Context, plan); Assert.Equal(commit.CommitSha, f.Git("rev-parse", "HEAD"));
        Assert.Equal(autocrlf, f.Git("config", "--local", "--get", "core.autocrlf"));
        Assert.All(result.FilesVerified, file => Assert.Equal(file.Digest, new Sha256Hasher().Compute(file.DestinationPath)));
    }

    [Fact]
    public void SameSizeAlteredStagedBlobFailsByteVerificationAndRollsBackIndex()
    {
        using var f = new GitProductionFixture(); var plan = f.Plan(); var physical = ArchiveTestFixture.Snapshot(Path.Combine(f.Root, "icons"));
        f.Observe(stage =>
        {
            if (stage != "staged") return;
            var path = plan.Selection.Paths[0]; var altered = File.ReadAllBytes(Path.Combine(f.Root, path.Replace('/', Path.DirectorySeparatorChar))); altered[0] ^= 1;
            var source = Path.Combine(f.Production.Root, "altered-blob"); File.WriteAllBytes(source, altered);
            var sha = f.Git("hash-object", "-w", "--no-filters", source);
            f.Git("update-index", "--cacheinfo", "100644", sha, path);
            f.Git("update-index", "--assume-unchanged", "--", path);
        });
        Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitStagedContentMismatch);
        Assert.Equal(plan.Status.Head, f.Git("rev-parse", "HEAD")); Assert.Equal("", f.Git("diff", "--cached", "--name-only"));
        ArchiveTestFixture.AssertSnapshot(physical, Path.Combine(f.Root, "icons"));
    }

    private static void WriteScript(string path, string body)
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
