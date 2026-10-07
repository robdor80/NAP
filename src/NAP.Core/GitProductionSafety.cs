using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NAP.Core;

internal static class GitProductionSafety
{
    internal static T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (GitProductionException) { throw; }
        catch (ProductionStorageException) { throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Production boundary or file validation rejected the operation."); }
        catch (BackupException) { throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Git metadata must be ordinary local entries without links."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException)
        { throw GitProductionException.Stop(NapIssueCodes.GitIoFailed, "A controlled Git operation failed; review its durable receipt before retrying."); }
    }
    internal static ExecutionMutex Acquire(UniverseContext c)
    {
        ProductionStorageRootValidator.Require(c);
        try { return ExecutionMutex.Acquire("Production", c.Storage.ProductionRoot); }
        catch (IOException) { throw GitProductionException.Stop(NapIssueCodes.GitBusy, "Production is occupied; retry explicitly."); }
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static string Binding(UniverseContext c)
    {
        string Normalize(string p) { p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)); return OperatingSystem.IsWindows() ? p.ToUpperInvariant() : p; }
        return Hash(c.Id.Value + "\n" + Normalize(c.Storage.ProductionRoot) + "\n" + Normalize(c.Storage.StateRoot));
    }
    internal static void Sha(string value)
    {
        if (value.Length is not (40 or 64) || value.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Git returned an invalid object identity.");
    }
    internal static string Message(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Any(char.IsControl) || message.Length > 200 || message.Any(char.IsSurrogate))
            throw GitProductionException.Stop(NapIssueCodes.GitMessageInvalid, "Commit subject must be nonempty, at most 200 characters and contain no controls or surrogates.");
        return message.Trim();
    }
    internal static void RefName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value == "@" || value.Contains("..", StringComparison.Ordinal) ||
            value.Contains("@{", StringComparison.Ordinal) || value.Any(c => char.IsControl(c) || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\') ||
            value.Split('/').Any(s => s.Length == 0 || s.StartsWith('.') || s.EndsWith('.') || s.EndsWith(".lock", StringComparison.Ordinal)))
            throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Branch or remote identity is not a supported Git name.");
    }
    internal static Dictionary<string, List<string>> Configuration(UniverseContext c, GitProductionProcess git)
    {
        var metadata = Path.Combine(c.Storage.ProductionRoot, ".git");
        if (ProductionPaths.CheckPath(metadata, NapIssueCodes.GitRepositoryInvalid) is not { } a || (a & FileAttributes.Directory) == 0)
            throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "ProductionRoot requires a standalone local .git directory.");
        var configPath = Path.Combine(metadata, "config"); BackupStorage.Fingerprint(configPath);
        if (File.ReadLines(configPath).Any(l => Regex.IsMatch(l, @"^\s*\[\s*include", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Local Git configuration includes require external review.");
        var tree = BackupTree.Capture(metadata, false);
        if (tree.Files.Any(f => f.RelativePath is "objects/info/alternates" or "objects/info/http-alternates" or "commondir" or "gitdir" or "config.worktree" or "info/grafts" ||
            f.RelativePath.EndsWith(".promisor", StringComparison.Ordinal) || f.RelativePath.StartsWith("refs/replace/", StringComparison.Ordinal)))
            throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "External object stores, replacement objects and partial clones are unsupported.");
        if (!ProductionPaths.Same(git.Root(c), c.Storage.ProductionRoot) || !ProductionPaths.Same(git.Directory(c), metadata) || git.Bare(c) != "false")
            throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "ProductionRoot must be the exact standalone Git toplevel.");
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in git.Config(c).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = record.IndexOf('\n'); var key = separator < 0 ? record : record[..separator]; var value = separator < 0 ? "" : record[(separator + 1)..];
            if (!result.TryGetValue(key, out var values)) result.Add(key, values = []); values.Add(value);
            if (key.Equals("extensions.partialclone", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".promisor", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".uploadpack", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".receivepack", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".vcs", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("push.pushoption", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".pushoption", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("url.", StringComparison.OrdinalIgnoreCase) || key.Equals("core.sshcommand", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("core.gitproxy", StringComparison.OrdinalIgnoreCase) || key.Equals("core.worktree", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("extensions.worktreeconfig", StringComparison.OrdinalIgnoreCase))
                throw GitProductionException.Stop(NapIssueCodes.GitRepositoryInvalid, "Git configuration redirects repository or transport execution.");
        }
        git.DisableFilters(c, result.Keys.Where(k => k.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) && k.LastIndexOf('.') > 7)
            .Select(k => k[7..k.LastIndexOf('.')]));
        return result;
    }
    internal static string ConfigurationHash(Dictionary<string, List<string>> config) => Hash(string.Join('\0',
        config.OrderBy(p => p.Key, StringComparer.Ordinal).SelectMany(p => p.Value.Select(v => p.Key + "\n" + v))));
    internal static string RemoteFingerprint(Dictionary<string, List<string>> config, string remote, bool testTransport)
    {
        var prefix = "remote." + remote + ".";
        var urls = Values("url"); var push = Values("pushurl");
        var fetch = Values("fetch");
        if (urls.Count != 1 || push.Count > 1 || (push.Count == 1 && push[0] != urls[0]) ||
            Values("mirror").Any(v => !v.Equals("false", StringComparison.OrdinalIgnoreCase)) || Values("push").Count != 0 ||
            fetch.Count != 1 || fetch[0] != "+refs/heads/*:refs/remotes/" + remote + "/*" || Values("proxy").Count != 0)
            throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Remote has ambiguous or redirected publication configuration.");
        var url = urls[0];
        if (string.IsNullOrWhiteSpace(url) || url.Any(char.IsControl) || url.Any(char.IsWhiteSpace) || url.StartsWith('-') || url.Contains("::", StringComparison.Ordinal)) Invalid();
        if (testTransport && (Path.IsPathFullyQualified(url) || url.StartsWith("file://", StringComparison.Ordinal))) return Hash(url);
        if (Path.IsPathFullyQualified(url) || url.Contains('\\') || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) Invalid();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh")
        {
            if (uri.Host.Length == 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath is "" or "/" ||
                (uri.Scheme == "https" && uri.UserInfo.Length != 0) || (uri.Scheme == "ssh" && (uri.UserInfo.Contains(':') || uri.UserInfo.Contains('%')))) Invalid();
            return Hash(uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped));
        }
        // SCP-style SSH allows a non-secret username, a host and a nonempty repository path.
        if (!Regex.IsMatch(url, @"^(?:[A-Za-z0-9._-]+@)?[A-Za-z0-9][A-Za-z0-9.-]*:[^/:].+$", RegexOptions.CultureInvariant) ||
            url.Contains("//", StringComparison.Ordinal) || url.Contains('%')) Invalid();
        return Hash(url);
        List<string> Values(string suffix) => config.TryGetValue(prefix + suffix, out var v) ? v : [];
        static void Invalid() => throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Remote URL violates the supported transport or authentication policy.");
    }
    internal static void NoLocalScripts(UniverseContext c, bool staging)
    {
        var config = File.ReadAllText(Path.Combine(c.Storage.ProductionRoot, ".git", "config"));
        if (Regex.IsMatch(config, @"^\s*\[\s*(credential|http)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant))
            throw GitProductionException.Stop(NapIssueCodes.GitRemoteInvalid, "Repository-local authentication or HTTP overrides require external review.");
        if (staging && Regex.IsMatch(config, @"^\s*\[\s*filter\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant))
            throw GitProductionException.Stop(NapIssueCodes.GitStagedContentMismatch, "Repository clean filters and LFS are unsupported for byte-exact publication.");
    }
    internal static void Operation(UniverseContext c)
    {
        foreach (var marker in new[] { "MERGE_HEAD", "REBASE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG", "BISECT_START", "rebase-apply", "rebase-merge", "sequencer", "index.lock", "HEAD.lock", "shallow" })
            if (ProductionPaths.Attributes(Path.Combine(c.Storage.ProductionRoot, ".git", marker)) is not null)
                throw GitProductionException.Stop(NapIssueCodes.GitOperationInProgress, "Git operation, lock or shallow history requires external review.");
    }
    internal static void Owned(UniverseContext c, IEnumerable<GitOwnedFile> files, string code = NapIssueCodes.GitOwnedFileChanged)
    {
        foreach (var file in files)
        {
            try
            {
                var full = ProductionPaths.Resolve(c.Storage.ProductionRoot, file.RelativePath);
                BackupFileTypes.RequireOrdinary(full); ProductionPaths.Verify(full, file.Digest, file.SizeBytes, code);
            }
            catch (Exception e) when (e is ProductionStorageException or BackupException or IOException or UnauthorizedAccessException)
            { throw GitProductionException.Stop(code, "An owned output is absent, linked or differs from its verified SHA-256 and size."); }
        }
    }
}
