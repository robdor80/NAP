using System.Text.Json;

namespace NAP.Core;

/// <summary>Closed versioned facts; never adopts temp siblings or silently deletes ambiguous receipts.</summary>
internal static class GitProductionReceiptStore
{
    internal const int Limit = 4 * 1024 * 1024;
    private static string Root(UniverseContext c) => Path.Combine(c.Storage.StateRoot, "git-production");
    private static string PathFor(UniverseContext c, GitOperationId id) => Path.Combine(Root(c), id.Value + ".json");
    internal static GitOperationReceipt Read(UniverseContext c, GitOperationId id)
    {
        try
        {
            CheckState(c); var path = PathFor(c, id); CheckFile(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > Limit) Invalid();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 }); var j = document.RootElement;
            Closed(j, "schema_version", "operation_id", "universe_id", "repository_binding", "state", "base_head", "branch", "upstream", "remote_name",
                "remote_branch", "remote_fingerprint", "remote_tip", "configuration_fingerprint", "porcelain", "message", "paths", "owned_files", "expected_tree", "expected_commit_sha", "commit_sha", "author_ident", "committer_ident");
            if (j.GetProperty("schema_version").GetRawText() != "1" || S(j, "operation_id") != id.Value || S(j, "universe_id") != c.Id.Value ||
                S(j, "repository_binding") != GitProductionSafety.Binding(c)) Invalid();
            var head = S(j, "base_head"); GitProductionSafety.Sha(head);
            var branch = S(j, "branch"); var remote = S(j, "remote_name"); var remoteBranch = S(j, "remote_branch");
            GitProductionSafety.RefName(branch); GitProductionSafety.RefName(remote); GitProductionSafety.RefName(remoteBranch);
            if (branch != remoteBranch || S(j, "remote_tip") != head) Invalid();
            var fingerprint = S(j, "remote_fingerprint"); var configuration = S(j, "configuration_fingerprint");
            _ = new Sha256Digest(fingerprint); _ = new Sha256Digest(configuration);
            var porcelain = S(j, "porcelain"); var parsed = GitProductionInspector.Parse(porcelain);
            if (parsed.Head != head || parsed.Branch != branch) Invalid();
            var message = S(j, "message"); if (GitProductionSafety.Message(message) != message) Invalid();
            var owned = j.GetProperty("owned_files").EnumerateArray().Select(f =>
            {
                Closed(f, "relative_path", "sha256", "size_bytes"); var relative = S(f, "relative_path"); ProductionPaths.Resolve(c.Storage.ProductionRoot, relative);
                var size = f.GetProperty("size_bytes").GetInt64(); if (size < 0) Invalid();
                return new GitOwnedFile(relative, new Sha256Digest(S(f, "sha256")), size);
            }).ToArray();
            var paths = j.GetProperty("paths").EnumerateArray().Select(p => p.GetString() ?? throw new InvalidDataException()).ToArray();
            if (owned.Length == 0 || paths.Length == 0 || !owned.Select(f => f.RelativePath).SequenceEqual(owned.Select(f => f.RelativePath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
                !paths.SequenceEqual(paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) || paths.Any(p => !owned.Any(f => f.RelativePath == p)) ||
                !paths.SequenceEqual(parsed.Changes.Select(f => f.RelativePath)) || parsed.Changes.Any(f => !f.IsUntracked && (f.IndexStatus != '.' || f.WorktreeStatus != 'M') || f.IsSubmodule || f.IsConflict)) Invalid();
            var stateText = S(j, "state");
            if (!Enum.TryParse<GitReceiptState>(stateText, out var state) || !Enum.IsDefined(state) || state.ToString() != stateText) Invalid();
            var expectedTree = Nullable(j, "expected_tree"); var expected = Nullable(j, "expected_commit_sha"); var commit = Nullable(j, "commit_sha");
            var author = Nullable(j, "author_ident"); var committer = Nullable(j, "committer_ident");
            if ((expectedTree is null) != (expected is null) || (commit is not null && (commit != expected || state == GitReceiptState.Prepared)) ||
                (state != GitReceiptState.Prepared && commit is null) || (expected is null) != (author is null) || (expected is null) != (committer is null)) Invalid();
            foreach (var sha in new[] { expectedTree, expected, commit }.OfType<string>()) { GitProductionSafety.Sha(sha); if (sha.Length != head.Length) Invalid(); }
            if (expected is not null)
            {
                GitProductionService.ValidateIdentity(author!); GitProductionService.ValidateIdentity(committer!);
                var payload = System.Text.Encoding.UTF8.GetBytes("tree " + expectedTree + "\nparent " + head + "\nauthor " + author + "\ncommitter " + committer + "\n\n" + message + "\n");
                var prefix = System.Text.Encoding.ASCII.GetBytes("commit " + payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0");
                var bytes = prefix.Concat(payload).ToArray();
                var actual = Convert.ToHexString(head.Length == 40 ? System.Security.Cryptography.SHA1.HashData(bytes) : System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if (expected != actual) Invalid();
            }
            var status = new GitRepositoryStatus(c.Id, GitProductionSafety.Binding(c), head, branch, S(j, "upstream"), remote, remoteBranch,
                fingerprint, head, GitSynchronization.Synchronized, configuration, porcelain, parsed.Changes);
            return new(id, new(status, new(owned, paths), message), state, expectedTree, expected, commit, author, committer);
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or InvalidOperationException or ProductionStorageException or BackupException or KeyNotFoundException or FormatException)
        { throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Receipt is missing, corrupt, ambiguous or belongs to another universe or repository."); }
    }
    internal static void Write(UniverseContext c, GitOperationReceipt receipt, bool create = false)
    {
        CheckState(c); var directory = Root(c); Directory.CreateDirectory(directory); CheckDirectory(directory);
        var path = PathFor(c, receipt.OperationId); var previous = create ? null : Read(c, receipt.OperationId);
        if (create && ProductionPaths.Attributes(path) is not null) Invalid();
        if (previous is not null && (previous.Plan.Status.Porcelain != receipt.Plan.Status.Porcelain || previous.BaseHead != receipt.BaseHead ||
            previous.State > receipt.State || previous.Plan.Message != receipt.Plan.Message ||
            previous.ExpectedCommitSha is not null && previous.ExpectedCommitSha != receipt.ExpectedCommitSha)) Invalid();
        var bytes = Serialize(receipt); if (bytes.Length > Limit) Invalid();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        CheckState(c); CheckFile(temp);
        if (create) { if (ProductionPaths.Attributes(path) is not null) Invalid(); }
        else
        {
            var fresh = Read(c, receipt.OperationId);
            if (!Serialize(fresh).SequenceEqual(Serialize(previous!))) Invalid();
        }
        File.Move(temp, path, overwrite: !create);
        if (!Serialize(Read(c, receipt.OperationId)).SequenceEqual(bytes)) Invalid();
    }
    internal static IReadOnlyList<GitOperationId> List(UniverseContext c)
    {
        CheckState(c); var directory = Root(c); if (!Directory.Exists(directory)) return Array.AsReadOnly(Array.Empty<GitOperationId>());
        CheckDirectory(directory); var ids = new List<GitOperationId>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            CheckFile(entry); var name = Path.GetFileName(entry);
            if (!name.EndsWith(".json", StringComparison.Ordinal)) Invalid(); // Orphan temps demand review; they are never adopted.
            var id = new GitOperationId(name[..^5]); Read(c, id); ids.Add(id);
        }
        return ids.AsReadOnly();
    }
    private static byte[] Serialize(GitOperationReceipt r)
    {
        var s = r.Plan.Status;
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, operation_id = r.OperationId.Value, universe_id = r.UniverseId.Value, repository_binding = s.RepositoryBinding,
            state = r.State.ToString(), base_head = s.Head, branch = s.Branch, upstream = s.Upstream, remote_name = s.RemoteName,
            remote_branch = s.RemoteBranch, remote_fingerprint = s.RemoteFingerprint, remote_tip = s.RemoteTip,
            configuration_fingerprint = s.ConfigurationFingerprint, porcelain = s.Porcelain, message = r.Plan.Message,
            paths = r.Plan.Selection.Paths, owned_files = r.Plan.Selection.OwnedFiles.Select(f => new { relative_path = f.RelativePath, sha256 = f.Digest.Hex, size_bytes = f.SizeBytes }),
            expected_tree = r.ExpectedTree, expected_commit_sha = r.ExpectedCommitSha, commit_sha = r.CommitSha,
            author_ident = r.AuthorIdentity, committer_ident = r.CommitterIdentity
        });
    }
    private static void CheckState(UniverseContext c)
    {
        ProductionStorageRootValidator.Require(c);
        if (ProductionPaths.Overlaps(c.Storage.StateRoot, c.Storage.ProductionRoot) || ProductionPaths.Overlaps(c.Storage.StateRoot, c.Storage.ArchiveRoot)) Invalid();
        var a = ProductionPaths.CheckPath(c.Storage.StateRoot, NapIssueCodes.GitReceiptInvalid);
        if (a is not null && (a & FileAttributes.Directory) == 0) Invalid();
        var root = ProductionPaths.CheckPath(Root(c), NapIssueCodes.GitReceiptInvalid);
        if (root is not null && (root & FileAttributes.Directory) == 0) Invalid();
    }
    private static void CheckDirectory(string p)
    { if (ProductionPaths.CheckPath(p, NapIssueCodes.GitReceiptInvalid) is not { } a || (a & FileAttributes.Directory) == 0) Invalid(); }
    private static void CheckFile(string p)
    { if (!ProductionPaths.FileExists(p, NapIssueCodes.GitReceiptInvalid)) Invalid(); BackupFileTypes.RequireOrdinary(p); }
    private static void Closed(JsonElement j, params string[] names)
    {
        if (j.ValueKind != JsonValueKind.Object) Invalid(); var expected = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var p in j.EnumerateObject()) if (!expected.Remove(p.Name)) Invalid();
        if (expected.Count != 0) Invalid();
    }
    private static string S(JsonElement j, string name) => j.GetProperty(name).GetString() ?? throw new InvalidDataException();
    private static string? Nullable(JsonElement j, string name) => j.GetProperty(name).ValueKind == JsonValueKind.Null ? null : S(j, name);
    private static void Invalid() => throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Receipt schema, binding or durable publication is invalid; review it externally.");
}
