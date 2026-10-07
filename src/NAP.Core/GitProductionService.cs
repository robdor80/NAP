using System.Text;

namespace NAP.Core;

/// <summary>Explicit production Git engine. Neither execution coordinators nor UI trigger this service automatically.</summary>
public sealed class GitProductionService
{
    internal GitProductionProcess Git { get; set; } = new();
    internal Action<string>? Observer { get; set; }
    private GitProductionInspector Inspector => new() { Git = Git };

    public GitCommitPlan PrepareCommit(UniverseContext context, IEnumerable<ProductionAssetResult> results, string message) => GitProductionSafety.Run(() =>
    {
        using var lease = GitProductionSafety.Acquire(context); var subject = GitProductionSafety.Message(message);
        RequireNoPending(context); var status = Inspector.InspectLocked(context); RequireInitial(status);
        var owned = new Dictionary<string, GitOwnedFile>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (result is null || result.AssetKey.UniverseId != context.Id || result.Issues.ShouldStop || result.FilesVerified.Count == 0)
                throw GitProductionException.Stop(NapIssueCodes.GitUnownedChange, "Production evidence must belong to the active universe.");
            ProductionPaths.Resolve(context.Storage.ProductionRoot, result.RelativeDirectory);
            foreach (var file in result.FilesVerified)
            {
                var relative = result.RelativeDirectory + "/" + file.FileName; var expected = ProductionPaths.Resolve(context.Storage.ProductionRoot, relative);
                if (!string.Equals(Path.GetFullPath(file.DestinationPath), expected, StringComparison.Ordinal) || !owned.TryAdd(relative, new(relative, file.Digest, file.SizeBytes)))
                    throw GitProductionException.Stop(NapIssueCodes.GitUnownedChange, "Production output destination or ownership is ambiguous.");
            }
        }
        if (owned.Count == 0 || status.IsClean) throw GitProductionException.Stop(NapIssueCodes.GitUnownedChange, "An operation requires verified outputs and actual changes.");
        GitProductionSafety.Owned(context, owned.Values);
        if (status.Changes.Any(c => !owned.ContainsKey(c.RelativePath)))
            throw GitProductionException.Stop(NapIssueCodes.GitUnownedChange, "Every dirty path must be explained by the supplied production results.");
        if (Git.Ignored(context, owned.Keys.ToArray()).Length != 0)
            throw GitProductionException.Stop(NapIssueCodes.GitIgnoredOutput, "Git ignores a production output; review ignore rules externally.");
        RequireAttributes(context, owned.Keys.ToArray());
        return new GitCommitPlan(status, new(owned.Values, status.Changes.Select(c => c.RelativePath)), subject);
    });

    public GitCommitResult Commit(UniverseContext context, GitCommitPlan plan) => GitProductionSafety.Run(() =>
    {
        using var lease = GitProductionSafety.Acquire(context); RequireNoPending(context); Revalidate(context, plan);
        return CommitLocked(context, new(GitOperationId.New(), plan, GitReceiptState.Prepared), create: true);
    });

    public IReadOnlyList<GitOperationId> ListOperations(UniverseContext context) => GitProductionSafety.Run(() =>
    { using var lease = GitProductionSafety.Acquire(context); return GitProductionReceiptStore.List(context); });

    public GitOperationReceipt ReadReceipt(UniverseContext context, GitOperationId operationId) => GitProductionSafety.Run(() =>
    { using var lease = GitProductionSafety.Acquire(context); return GitProductionReceiptStore.Read(context, operationId); });

    /// <summary>Explicit recovery never moves HEAD. Base HEAD leaves Prepared for an explicit RetryCommit.</summary>
    public GitOperationReceipt Recover(UniverseContext context, GitOperationId operationId) => GitProductionSafety.Run(() =>
    {
        using var lease = GitProductionSafety.Acquire(context); var receipt = GitProductionReceiptStore.Read(context, operationId);
        var status = Inspector.InspectLocked(context); RequireBinding(receipt.Plan.Status, status, NapIssueCodes.GitReceiptInvalid);
        if (receipt.State == GitReceiptState.Prepared && status.Head == receipt.BaseHead)
        { ValidatePreparedIndex(context, receipt); return receipt; }
        if (status.Head != (receipt.State == GitReceiptState.Prepared ? receipt.ExpectedCommitSha : receipt.CommitSha))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "HEAD cannot be proven to be this operation's exact commit.");
        VerifyCommit(context, receipt, status.Head);
        if (receipt.State != GitReceiptState.Prepared) return receipt;
        var recovered = new GitOperationReceipt(receipt.OperationId, receipt.Plan, GitReceiptState.Committed, receipt.ExpectedTree, receipt.ExpectedCommitSha, status.Head, receipt.AuthorIdentity, receipt.CommitterIdentity);
        GitProductionReceiptStore.Write(context, recovered); return recovered;
    });

    /// <summary>Retries a Prepared operation at base HEAD; only proven owned index entries may be unstaged.</summary>
    public GitCommitResult RetryCommit(UniverseContext context, GitOperationId operationId) => GitProductionSafety.Run(() =>
    {
        using var lease = GitProductionSafety.Acquire(context); var receipt = GitProductionReceiptStore.Read(context, operationId);
        if (receipt.State != GitReceiptState.Prepared) throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Only Prepared operations can retry commit.");
        var current = Inspector.InspectLocked(context); RequireBinding(receipt.Plan.Status, current, NapIssueCodes.GitPlanStale);
        if (current.Head != receipt.BaseHead || current.RemoteTip != receipt.BaseHead) Stale();
        var staged = ValidatePreparedIndex(context, receipt);
        if (staged.Count != 0) Git.Unstage(context, staged, receipt.BaseHead);
        Revalidate(context, receipt.Plan);
        return CommitLocked(context, receipt, create: false);
    });

    public GitPushResult Push(UniverseContext context, GitCommitResult result) => GitProductionSafety.Run(() =>
    {
        using var lease = GitProductionSafety.Acquire(context);
        if (result.UniverseId != context.Id || result.RepositoryBinding != GitProductionSafety.Binding(context))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Commit result belongs to another universe or repository.");
        var receipt = GitProductionReceiptStore.Read(context, result.OperationId);
        if (receipt.CommitSha != result.CommitSha || receipt.BaseHead != result.OldHead || receipt.Plan.Message != result.Message ||
            !receipt.Plan.Selection.Paths.SequenceEqual(result.CommittedPaths))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Commit result differs from its durable receipt.");
        return PushLocked(context, receipt);
    });

    public GitPushResult Push(UniverseContext context, GitOperationId operationId) => GitProductionSafety.Run(() =>
    { using var lease = GitProductionSafety.Acquire(context); return PushLocked(context, GitProductionReceiptStore.Read(context, operationId)); });

    private GitCommitResult CommitLocked(UniverseContext c, GitOperationReceipt receipt, bool create)
    {
        var plan = receipt.Plan;
        var config = GitProductionSafety.Configuration(c, Git);
        if (!config.TryGetValue("user.name", out var names) || string.IsNullOrWhiteSpace(names.Last()) ||
            !config.TryGetValue("user.email", out var emails) || string.IsNullOrWhiteSpace(emails.Last()))
            throw GitProductionException.Stop(NapIssueCodes.GitIdentityMissing, "Explicit Git user.name and user.email are required before staging.");
        var author = Git.Identity(c, true); var committer = Git.Identity(c, false); ValidateIdentity(author); ValidateIdentity(committer);
        RequireAttributes(c, plan.Selection.OwnedFiles.Select(f => f.RelativePath).ToArray()); Revalidate(c, plan);
        GitProductionReceiptStore.Write(c, receipt, create); Observer?.Invoke("prepared");
        var attempted = new List<string>(); var moved = false;
        try
        {
            Revalidate(c, plan);
            RequireAttributes(c, plan.Selection.OwnedFiles.Select(f => f.RelativePath).ToArray());
            foreach (var path in plan.Selection.Paths) { attempted.Add(path); Git.Add(c, path); }
            Observer?.Invoke("staged"); VerifyIndex(c, plan); GitProductionSafety.Owned(c, plan.Selection.OwnedFiles);
            var status = Inspector.InspectLocked(c); RequireBinding(plan.Status, status, NapIssueCodes.GitPlanStale);
            if (status.Head != plan.Status.Head || status.RemoteTip != plan.Status.Head) Stale();
            var tree = Git.Tree(c);
            var payload = Encoding.UTF8.GetBytes("tree " + tree + "\nparent " + plan.Status.Head + "\nauthor " + author + "\ncommitter " + committer + "\n\n" + plan.Message + "\n");
            var expected = Git.CommitObjectHash(c, payload);
            if (receipt.ExpectedCommitSha is not null)
            {
                // A Prepared receipt with a fully frozen commit must retain its exact identity on retry.
                author = receipt.AuthorIdentity!; committer = receipt.CommitterIdentity!;
                payload = Encoding.UTF8.GetBytes("tree " + tree + "\nparent " + plan.Status.Head + "\nauthor " + author + "\ncommitter " + committer + "\n\n" + plan.Message + "\n");
                expected = Git.CommitObjectHash(c, payload);
                if (receipt.ExpectedCommitSha != expected) Stale();
            }
            receipt = new(receipt.OperationId, plan, GitReceiptState.Prepared, tree, expected, author: author, committer: committer);
            GitProductionReceiptStore.Write(c, receipt); Observer?.Invoke("before_commit");
            VerifyIndex(c, plan); GitProductionSafety.Owned(c, plan.Selection.OwnedFiles);
            var beforeCommit = Inspector.InspectLocked(c); RequireBinding(plan.Status, beforeCommit, NapIssueCodes.GitPlanStale);
            if (beforeCommit.Head != plan.Status.Head || beforeCommit.RemoteTip != plan.Status.Head || Git.Tree(c) != tree) Stale();
            Git.Commit(c, plan.Message, author, committer); moved = true; Observer?.Invoke("after_commit");
            var committedStatus = GitProductionInspector.Parse(Git.Status(c));
            var committedConfig = GitProductionSafety.Configuration(c, Git);
            if (committedStatus.Branch != plan.Status.Branch || GitProductionSafety.ConfigurationHash(committedConfig) != plan.Status.ConfigurationFingerprint)
                throw GitProductionException.Stop(NapIssueCodes.GitCommitVerificationFailed, "Branch or configuration changed during commit; receipt retained for explicit recovery.");
            var head = committedStatus.Head; VerifyCommit(c, receipt, head);
            var committed = new GitOperationReceipt(receipt.OperationId, plan, GitReceiptState.Committed, tree, expected, head, author, committer);
            GitProductionReceiptStore.Write(c, committed); Observer?.Invoke("committed"); return new(committed);
        }
        catch
        {
            // Commit may have succeeded even if its process or durable update failed. Never undo a moved HEAD.
            if (!moved && attempted.Count != 0)
            {
                string currentHead;
                try { currentHead = Git.Head(c); } catch (GitProductionException)
                { throw GitProductionException.Stop(NapIssueCodes.GitRollbackFailed, "HEAD could not be verified; index retained for explicit recovery."); }
                if (currentHead == plan.Status.Head)
                {
                    try
                    {
                        var indexed = Git.Tracked(c).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                            .Select(e => e[(e.IndexOf('\t') + 1)..]).ToHashSet(StringComparer.Ordinal);
                        // A staged deletion is absent from ls-files but still needs its original HEAD entry restored.
                        foreach (var entry in Git.TreeEntries(c, plan.Status.Head).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                            indexed.Add(entry[(entry.IndexOf('\t') + 1)..]);
                        var restore = attempted.Where(indexed.Contains).ToArray();
                        if (restore.Length != 0) Git.Unstage(c, restore, plan.Status.Head);
                    }
                    catch (Exception e) when (e is IOException or InvalidOperationException)
                    { throw GitProductionException.Stop(NapIssueCodes.GitRollbackFailed, "Owned staging rollback failed; index requires explicit review."); }
                    if (GitProductionInspector.Parse(Git.Status(c)).Changes.Any(e => !e.IsUntracked && e.IndexStatus != '.'))
                        throw GitProductionException.Stop(NapIssueCodes.GitRollbackFailed, "Index still contains staging after bounded rollback; review it externally.");
                }
            }
            throw;
        }
    }
    private GitPushResult PushLocked(UniverseContext c, GitOperationReceipt receipt)
    {
        if (receipt.State == GitReceiptState.Prepared || receipt.CommitSha is null)
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Push requires a verified Committed or Pushed receipt.");
        var status = Inspector.InspectLocked(c); RequireBinding(receipt.Plan.Status, status, NapIssueCodes.GitReceiptInvalid);
        VerifyCommit(c, receipt, status.Head);
        if (status.RemoteTip == receipt.CommitSha)
        {
            if (receipt.State != GitReceiptState.Pushed) GitProductionReceiptStore.Write(c,
                new(receipt.OperationId, receipt.Plan, GitReceiptState.Pushed, receipt.ExpectedTree, receipt.ExpectedCommitSha, receipt.CommitSha, receipt.AuthorIdentity, receipt.CommitterIdentity));
            return new(receipt.OperationId, receipt.CommitSha, GitPushOutcome.AlreadyPushed);
        }
        if (status.RemoteTip != receipt.BaseHead || receipt.State == GitReceiptState.Pushed)
            throw GitProductionException.Stop(NapIssueCodes.GitRemoteChanged, "Remote branch changed; local commit is retained for review.");
        Observer?.Invoke("before_push");
        status = Inspector.InspectLocked(c); RequireBinding(receipt.Plan.Status, status, NapIssueCodes.GitReceiptInvalid); VerifyCommit(c, receipt, status.Head);
        if (status.RemoteTip != receipt.BaseHead) throw GitProductionException.Stop(NapIssueCodes.GitRemoteChanged, "Remote changed before publication; local commit is retained.");
        Git.Push(c, receipt.RemoteName, receipt.Plan.Status.RemoteBranch);
        var tip = Inspector.ReadTip(c, receipt.RemoteName, receipt.Plan.Status.RemoteBranch);
        if (tip != receipt.CommitSha) throw GitProductionException.Stop(NapIssueCodes.GitPushVerificationFailed, "Remote SHA could not confirm publication; receipt remains Committed.");
        var final = Inspector.InspectLocked(c); RequireBinding(receipt.Plan.Status, final, NapIssueCodes.GitReceiptInvalid); VerifyCommit(c, receipt, final.Head);
        if (final.RemoteTip != receipt.CommitSha) throw GitProductionException.Stop(NapIssueCodes.GitPushVerificationFailed, "Remote changed during post-push verification.");
        GitProductionReceiptStore.Write(c, new(receipt.OperationId, receipt.Plan, GitReceiptState.Pushed, receipt.ExpectedTree, receipt.ExpectedCommitSha, receipt.CommitSha, receipt.AuthorIdentity, receipt.CommitterIdentity));
        return new(receipt.OperationId, receipt.CommitSha, GitPushOutcome.Pushed);
    }
    private void Revalidate(UniverseContext c, GitCommitPlan plan)
    {
        try
        {
            if (plan.UniverseId != c.Id || plan.Status.RepositoryBinding != GitProductionSafety.Binding(c)) Stale();
            var current = Inspector.InspectLocked(c); RequireBinding(plan.Status, current, NapIssueCodes.GitPlanStale);
            if (current.Head != plan.Status.Head || current.RemoteTip != plan.Status.RemoteTip || current.Porcelain != plan.Status.Porcelain) Stale();
            RequireInitial(current); GitProductionSafety.Owned(c, plan.Selection.OwnedFiles, NapIssueCodes.GitPlanStale);
            if (Git.Ignored(c, plan.Selection.OwnedFiles.Select(f => f.RelativePath).ToArray()).Length != 0) Stale();
        }
        catch (GitProductionException) { Stale(); }
    }
    private static void RequireBinding(GitRepositoryStatus frozen, GitRepositoryStatus current, string code)
    {
        if (current.UniverseId != frozen.UniverseId || current.RepositoryBinding != frozen.RepositoryBinding || current.Branch != frozen.Branch ||
            current.Upstream != frozen.Upstream || current.RemoteName != frozen.RemoteName || current.RemoteBranch != frozen.RemoteBranch ||
            current.RemoteFingerprint != frozen.RemoteFingerprint || current.ConfigurationFingerprint != frozen.ConfigurationFingerprint)
            throw GitProductionException.Stop(code, "Repository, universe, branch, upstream or configuration no longer matches the frozen operation.");
    }
    private static void RequireInitial(GitRepositoryStatus status)
    {
        if (!status.IndexIsClean) throw GitProductionException.Stop(NapIssueCodes.GitIndexNotClean, "Pre-existing staged changes require external review.");
        if (status.Changes.Any(c => c.IsConflict || c.IsSubmodule || c.ChangeKind is GitChangeKind.Deleted or GitChangeKind.Renamed or GitChangeKind.Copied or GitChangeKind.TypeChanged))
            throw GitProductionException.Stop(NapIssueCodes.GitChangesConflict, "Conflicts, submodules, renames, deletions and type changes cannot publish automatically.");
        if (status.RemoteTip != status.Head) throw GitProductionException.Stop(NapIssueCodes.GitNotSynchronized, "Local HEAD must exactly equal the existing remote branch tip before commit.");
    }
    private void RequireAttributes(UniverseContext c, IReadOnlyList<string> paths)
        => GitProductionAttributes.Require(c, Git, paths);
    private void VerifyIndex(UniverseContext c, GitCommitPlan plan)
    {
        var paths = Names(Git.StagedPaths(c), NapIssueCodes.GitStagingMismatch);
        if (!paths.SequenceEqual(plan.Selection.Paths)) throw GitProductionException.Stop(NapIssueCodes.GitStagingMismatch, "Staged paths differ from the exact authorized selection.");
        var status = GitProductionInspector.Parse(Git.Status(c));
        if (status.Head != plan.Status.Head || status.Branch != plan.Status.Branch || !status.Changes.Select(f => f.RelativePath).SequenceEqual(plan.Selection.Paths) ||
            status.Changes.Any(f => f.WorktreeStatus != '.' || f.IndexStatus is not ('A' or 'M') || f.IsSubmodule || f.IsConflict))
            throw GitProductionException.Stop(NapIssueCodes.GitStagingMismatch, "Index or residual working changes differ from the exact plan.");
        VerifyBlobs(c, plan, Git.Tracked(c), index: true);
    }
    private IReadOnlyList<string> ValidatePreparedIndex(UniverseContext c, GitOperationReceipt receipt)
    {
        GitProductionSafety.Owned(c, receipt.Plan.Selection.OwnedFiles);
        var current = GitProductionInspector.Parse(Git.Status(c));
        if (current.Head != receipt.BaseHead || current.Branch != receipt.Branch || !current.Changes.Select(f => f.RelativePath).SequenceEqual(receipt.Plan.Selection.Paths) ||
            current.Changes.Any(f => f.IsConflict || f.IsSubmodule || f.ChangeKind is GitChangeKind.Deleted or GitChangeKind.Renamed or GitChangeKind.TypeChanged))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Prepared operation no longer explains every working and staged change.");
        var paths = Names(Git.StagedPaths(c), NapIssueCodes.GitReceiptInvalid);
        if (paths.Any(p => !receipt.Plan.Selection.Paths.Contains(p, StringComparer.Ordinal)))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "Prepared index contains changes not owned by this operation.");
        if (paths.Count != 0)
            VerifyBlobs(c, new(receipt.Plan.Status, new(receipt.Plan.Selection.OwnedFiles, paths), receipt.Plan.Message), Git.Tracked(c), index: true);
        return paths;
    }
    private void VerifyCommit(UniverseContext c, GitOperationReceipt receipt, string head)
    {
        if (head != receipt.ExpectedCommitSha || receipt.ExpectedTree is null)
            throw GitProductionException.Stop(NapIssueCodes.GitCommitVerificationFailed, "HEAD is not the cryptographically frozen operation commit.");
        var current = GitProductionInspector.Parse(Git.Status(c));
        var commit = Encoding.UTF8.GetString(Git.Object(c, "commit", head)); var separator = commit.IndexOf("\n\n", StringComparison.Ordinal);
        var headers = separator < 0 ? [] : commit[..separator].Split('\n');
        if (headers.Length != 4 || headers[0] != "tree " + receipt.ExpectedTree || headers[1] != "parent " + receipt.BaseHead ||
            !headers[2].StartsWith("author ", StringComparison.Ordinal) || !headers[3].StartsWith("committer ", StringComparison.Ordinal) ||
            commit[(separator + 2)..] != receipt.Plan.Message + "\n" || !Names(Git.ChangedPaths(c, receipt.BaseHead, head), NapIssueCodes.GitCommitVerificationFailed).SequenceEqual(receipt.Plan.Selection.Paths) ||
            current.Head != head || current.Branch != receipt.Branch || current.Changes.Count != 0)
            throw GitProductionException.Stop(NapIssueCodes.GitCommitVerificationFailed, "Commit parent, tree, subject or clean repository verification failed.");
        GitProductionSafety.Owned(c, receipt.Plan.Selection.OwnedFiles, NapIssueCodes.GitCommitVerificationFailed);
        VerifyBlobs(c, receipt.Plan, Git.TreeEntries(c, head), index: false);
    }
    private void VerifyBlobs(UniverseContext c, GitCommitPlan plan, string entries, bool index)
    {
        var map = new Dictionary<string, (string Mode, string Sha, string Stage)>(StringComparer.Ordinal);
        foreach (var entry in entries.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t'); if (tab < 0) throw GitProductionException.Stop(NapIssueCodes.GitStagingMismatch, "Git object listing is malformed.");
            var data = entry[..tab].Split(' '); if (data.Length != 3 || !map.TryAdd(entry[(tab + 1)..], (data[0], index ? data[1] : data[2], index ? data[2] : "0")))
                throw GitProductionException.Stop(NapIssueCodes.GitStagingMismatch, "Git object listing is ambiguous.");
        }
        foreach (var path in plan.Selection.Paths)
        {
            var owned = plan.Selection.OwnedFiles.Single(f => f.RelativePath == path);
            if (!map.TryGetValue(path, out var blob) || blob.Stage != "0" || blob.Mode is not ("100644" or "100755") ||
                Git.ObjectSize(c, blob.Sha) != owned.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw GitProductionException.Stop(NapIssueCodes.GitStagedContentMismatch, "Owned Git blob type or size differs from verified physical content.");
            var blobBytes = Git.Object(c, "blob", blob.Sha);
            using var bytes = new MemoryStream(blobBytes, writable: false);
            GitProductionSafety.Owned(c, [owned]);
            if (new Sha256Hasher().Compute(bytes) != owned.Digest ||
                !blobBytes.AsSpan().SequenceEqual(File.ReadAllBytes(ProductionPaths.Resolve(c.Storage.ProductionRoot, path))))
                throw GitProductionException.Stop(NapIssueCodes.GitStagedContentMismatch, "Staged bytes differ from verified NAP bytes; review attributes or filters externally.");
        }
    }
    private static IReadOnlyList<string> Names(string nul, string code)
    {
        var parts = nul.Split('\0', StringSplitOptions.RemoveEmptyEntries); var result = new List<string>();
        if (parts.Length % 2 != 0) throw GitProductionException.Stop(code, "Git changed-path listing is malformed.");
        for (var i = 0; i < parts.Length; i += 2)
        {
            if (parts[i] is not ("A" or "M")) throw GitProductionException.Stop(code, "Git changed-path listing contains an unsupported operation.");
            result.Add(parts[i + 1]);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    internal static void ValidateIdentity(string identity)
    {
        if (identity.Any(char.IsControl) || identity.LastIndexOf(" <", StringComparison.Ordinal) < 1 || identity.LastIndexOf("> ", StringComparison.Ordinal) < 4 ||
            !System.Text.RegularExpressions.Regex.IsMatch(identity, @"^.+ <[^<>\s]+@[^<>\s]+> [0-9]+ [+-][0-9]{4}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw GitProductionException.Stop(NapIssueCodes.GitIdentityMissing, "Git author or committer identity is invalid.");
    }
    private void RequireNoPending(UniverseContext c)
    {
        if (GitProductionReceiptStore.List(c).Any(id => GitProductionReceiptStore.Read(c, id).State != GitReceiptState.Pushed))
            throw GitProductionException.Stop(NapIssueCodes.GitReceiptInvalid, "An earlier operation requires explicit recovery, retry or publication before preparing another commit.");
    }
    private static void Stale() => throw GitProductionException.Stop(NapIssueCodes.GitPlanStale, "Commit plan is stale; no automatic refresh or HEAD repair is permitted.");
}
