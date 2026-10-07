using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NAP.Core;

/// <summary>Typed, private command construction. No arbitrary command or arguments entry point is exposed.</summary>
internal sealed class GitProductionProcess
{
    internal TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
    internal const int OutputLimit = 8 * 1024 * 1024;
    // Test-only seam. Runtime callers cannot enable the file transport.
    internal bool AllowLocalTestTransport { get; set; }
    internal Action<string>? Observer { get; set; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _filters = new(StringComparer.Ordinal);
    internal void DisableFilters(UniverseContext c, IEnumerable<string> names) => _filters[GitProductionSafety.Binding(c)] = names.Distinct(StringComparer.Ordinal).ToArray();
    internal string Root(UniverseContext c) => Text(c, "rev-parse", "--show-toplevel").TrimEnd('\r', '\n');
    internal string Directory(UniverseContext c) => Text(c, "rev-parse", "--absolute-git-dir").TrimEnd('\r', '\n');
    internal string Bare(UniverseContext c) => Text(c, "rev-parse", "--is-bare-repository").Trim();
    internal string Head(UniverseContext c) => Text(c, "rev-parse", "--verify", "HEAD").Trim();
    internal string Status(UniverseContext c) => Text(c, "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all", "--ignore-submodules=none");
    internal string Config(UniverseContext c) => Text(c, "config", "--null", "--list");
    internal string Upstream(UniverseContext c) => Text(c, "for-each-ref", "--format=%(refname)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads/");
    internal string RemoteTip(UniverseContext c, string remote, string branch) => TextCode(c, NapIssueCodes.GitRemoteUnavailable, "ls-remote", "--heads", "--", remote, "refs/heads/" + branch);
    internal string Tracked(UniverseContext c) => Text(c, "ls-files", "-z", "--stage");
    internal string Attributes(UniverseContext c, IReadOnlyList<string> paths) => Encoding.UTF8.GetString(Run(c,
        ["check-attr", "-z", "--stdin", .. GitProductionAttributes.Names], Encoding.UTF8.GetBytes(string.Join('\0', paths) + "\0"), [0], NapIssueCodes.GitStagedContentMismatch));
    internal string Ignored(UniverseContext c, IReadOnlyList<string> paths) => Encoding.UTF8.GetString(Run(c,
        ["check-ignore", "-z", "--stdin"], Encoding.UTF8.GetBytes(string.Join('\0', paths) + "\0"), [0, 1], NapIssueCodes.GitIgnoredOutput));
    internal string Identity(UniverseContext c, bool author) => TextCode(c, NapIssueCodes.GitIdentityMissing, "var", author ? "GIT_AUTHOR_IDENT" : "GIT_COMMITTER_IDENT").Trim();
    internal bool Ancestor(UniverseContext c, string first, string second) => RunExit(c, ["merge-base", "--is-ancestor", first, second], [0, 1, 128]) == 0;
    internal void Add(UniverseContext c, string path)
    {
        // This boundary applies to every add, including retries and attribute changes after Prepare.
        GitProductionAttributes.Require(c, this, [path]);
        TextCode(c, NapIssueCodes.GitStagingMismatch, "add", "--", path);
    }
    internal void Unstage(UniverseContext c, IReadOnlyList<string> paths, string head)
    {
        // Keep argv bounded even for many assets; each restore is limited to one owned entry.
        foreach (var path in paths) TextCode(c, NapIssueCodes.GitRollbackFailed, "restore", "--staged", "--source=" + head, "--", path);
    }
    internal string StagedPaths(UniverseContext c) => Text(c, "diff", "--cached", "--name-status", "--no-renames", "-z", "--no-ext-diff", "--no-textconv");
    internal string ChangedPaths(UniverseContext c, string parent, string head) => Text(c, "diff-tree", "--no-commit-id", "--name-status", "--no-renames", "-r", "-z", parent, head, "--");
    internal string Tree(UniverseContext c) => Text(c, "write-tree").Trim();
    internal string CommitObjectHash(UniverseContext c, byte[] payload) => Encoding.ASCII.GetString(Run(c,
        ["hash-object", "-t", "commit", "--stdin"], payload, [0], NapIssueCodes.GitCommitFailed)).Trim();
    internal byte[] Object(UniverseContext c, string type, string sha) => Run(c, ["cat-file", type, sha], null, [0], NapIssueCodes.GitCommitVerificationFailed);
    internal string ObjectSize(UniverseContext c, string sha) => Text(c, "cat-file", "-s", sha).Trim();
    internal string TreeEntries(UniverseContext c, string sha) => Text(c, "ls-tree", "-r", "-z", sha);
    internal void Commit(UniverseContext c, string message, string author, string committer)
    {
        using var hooks = GitProductionHooks.Create(c);
        Run(c, ["commit", "--no-gpg-sign", "--cleanup=verbatim", "--message", message], null, [0], NapIssueCodes.GitCommitFailed,
            IdentityEnvironment(author, committer), hooks: hooks);
    }
    internal void Push(UniverseContext c, string remote, string branch) => TextCode(c, NapIssueCodes.GitPushRejected,
        "push", "--porcelain", "--no-verify", "--recurse-submodules=no", "--", remote, "HEAD:refs/heads/" + branch);

    private string Text(UniverseContext c, params string[] args) => TextCode(c, NapIssueCodes.GitRepositoryInvalid, args);
    private string TextCode(UniverseContext c, string code, params string[] args) => new UTF8Encoding(false, true).GetString(Run(c, args, null, [0], code));
    private int RunExit(UniverseContext c, string[] args, int[] exits)
    { var exit = 0; Run(c, args, null, exits, NapIssueCodes.GitRepositoryInvalid, onExit: e => exit = e); return exit; }
    private byte[] Run(UniverseContext c, string[] args, byte[]? input, int[] exits, string code,
        Dictionary<string, string>? environment = null, Action<int>? onExit = null, GitProductionHooks? hooks = null)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = c.Storage.ProductionRoot,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        foreach (var arg in new[] { "--no-pager", "--literal-pathspecs", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false",
            "-c", "core.hooksPath=" + (hooks?.Path ?? "/dev/null"), "-c", "core.autocrlf=false", "-c", "core.askPass=", "-c", "commit.gpgSign=false", "-c", "push.gpgSign=false", "-c", "push.followTags=false",
            "-c", "push.recurseSubmodules=no", "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-c", "i18n.commitEncoding=UTF-8",
            "-C", c.Storage.ProductionRoot }.Where(a => a != "--literal-pathspecs" || args[0] is not ("check-ignore" or "check-attr")).Concat(args)) start.ArgumentList.Add(arg);
        // Status itself can invoke clean filters. Disable every discovered filter even on read-only invocations.
        if (args[0] != "config")
        {
            // Insert invocation configuration before the subcommand, never into its argument list.
            var insertion = start.ArgumentList.Count - args.Length;
            foreach (var filter in _filters.GetValueOrDefault(GitProductionSafety.Binding(c), []))
                foreach (var setting in new[] { "clean=", "smudge=", "process=", "required=false" })
                { start.ArgumentList.Insert(insertion++, "-c"); start.ArgumentList.Insert(insertion++, "filter." + filter + "." + setting); }
        }
        // Remove ambient routing/identity overrides; retain host authentication configuration.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1"; start.Environment["GIT_NO_LAZY_FETCH"] = "1";
        start.Environment["GIT_ALLOW_PROTOCOL"] = AllowLocalTestTransport ? "https:ssh:file" : "https:ssh";
        if (AllowLocalTestTransport)
        {
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        }
        start.Environment["GCM_INTERACTIVE"] = "never";
        start.Environment["GIT_ASKPASS"] = ""; start.Environment["SSH_ASKPASS"] = ""; start.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        start.Environment["GIT_SSH_COMMAND"] = "ssh -oBatchMode=yes -oStrictHostKeyChecking=yes";
        if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = new Process { StartInfo = start };
        hooks?.RequireEmpty();
        try { if (!process.Start()) throw GitProductionException.Stop(NapIssueCodes.GitUnavailable, "Git could not be started."); }
        catch (Win32Exception) { throw GitProductionException.Stop(NapIssueCodes.GitUnavailable, "Git is unavailable on this host."); }
        using var timeout = new CancellationTokenSource(Timeout);
        var output = Drain(process.StandardOutput.BaseStream); var errors = Drain(process.StandardError.BaseStream);
        var write = input is null ? Task.CompletedTask : Write();
        try
        {
            Task.WhenAll(output, errors, write, process.WaitForExitAsync(timeout.Token)).GetAwaiter().GetResult();
            if (!exits.Contains(process.ExitCode)) throw GitProductionException.Stop(code, "A permitted Git command rejected the operation; review Git externally.");
            onExit?.Invoke(process.ExitCode); Observer?.Invoke(args[0]); return output.Result;
        }
        catch (OperationCanceledException)
        { throw GitProductionException.Stop(NapIssueCodes.GitTimeout, "Git exceeded its bounded timeout or output limit."); }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.WaitForExit(5000);
            }
        }
        async Task Write()
        { await process.StandardInput.BaseStream.WriteAsync(input!, timeout.Token).ConfigureAwait(false); process.StandardInput.Close(); }
        async Task<byte[]> Drain(Stream source)
        {
            try { return await ReadBounded(source, timeout.Token).ConfigureAwait(false); }
            catch (GitProductionException) { timeout.Cancel(); throw; }
        }
    }
    private static async Task<byte[]> ReadBounded(Stream input, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (read == 0) break;
            if (output.Length + read > OutputLimit) throw GitProductionException.Stop(NapIssueCodes.GitOutputLimit, "Git output exceeds the bounded limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    private static Dictionary<string, string> IdentityEnvironment(string author, string committer)
    {
        var result = new Dictionary<string, string>(); Add("AUTHOR", author); Add("COMMITTER", committer); return result;
        void Add(string prefix, string identity)
        {
            var open = identity.LastIndexOf(" <", StringComparison.Ordinal); var close = identity.LastIndexOf("> ", StringComparison.Ordinal);
            result["GIT_" + prefix + "_NAME"] = identity[..open]; result["GIT_" + prefix + "_EMAIL"] = identity[(open + 2)..close];
            result["GIT_" + prefix + "_DATE"] = "@" + identity[(close + 2)..];
        }
    }
}
