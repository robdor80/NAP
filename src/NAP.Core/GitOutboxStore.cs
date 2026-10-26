namespace NAP.Core;

/// <summary>Independent authenticated v1 ledger. Bounded inventory; never modifies native Git receipts.</summary>
public sealed class GitOutboxStore
{
    private readonly UniverseContext _context;
    private readonly GitPublicationPolicyStore _policy;
    private string PathFor => Path.Combine(_policy.OutboxDirectory, "outbox.json");
    public GitOutboxStore(UniverseContext context, GitPublicationPolicyStore? policy = null)
    { _context = context; _policy = policy ?? new(context); }
    internal GitOutboxSnapshot Empty() => new(1, _context.Id, GitProductionSafety.Binding(_context), 0, null, [], 0, [], []);
    public GitOutboxSnapshot Read()
    {
        using var owner = AutomationManualAccess.Enter(_context); using var lease = _policy.Lock();
        if (_policy.LoadLocked() is null) return Empty();
        return LoadLocked();
    }
    internal GitOutboxSnapshot LoadLocked()
    {
        GitPublicationFiles.Inventory(_policy.OutboxDirectory, "outbox.json");
        var s = GitPublicationFiles.Read<GitOutboxSnapshot>(PathFor, _policy.Key()); Validate(s); return s;
    }
    internal void SaveLocked(GitOutboxSnapshot state)
    {
        Validate(state); GitPublicationFiles.Inventory(_policy.OutboxDirectory, "outbox.json");
        GitPublicationFiles.Write(PathFor, state, _policy.Key(), File.Exists(PathFor));
    }
    private void Validate(GitOutboxSnapshot s)
    {
        if (s.SchemaVersion != 1 || s.UniverseId != _context.Id || s.RepositoryBinding != GitProductionSafety.Binding(_context) ||
            s.PolicyRevision < 1 || s.PolicyHash is null || s.PolicyHistory is null || s.PolicyHistory.Count != s.PolicyRevision || s.CompletionCursor < 0 || s.Intents is null || s.Batches is null ||
            s.Intents.Count > 4096 || s.Batches.Count > 4096 || s.Intents.Select(i => i.Id).Distinct().Count() != s.Intents.Count ||
            s.Batches.Select(b => b.Id).Distinct().Count() != s.Batches.Count || s.Batches.Select(b => b.OperationId).Distinct().Count() != s.Batches.Count)
            throw new InvalidDataException("Invalid Outbox v1 scope or inventory.");
        _ = new Sha256Digest(s.PolicyHash);
        for (var n = 0; n < s.PolicyHistory.Count; n++)
        {
            var revision = s.PolicyHistory[n];
            if (revision.Revision != n + 1 || !Enum.IsDefined(revision.Mode)) throw new InvalidDataException("Invalid policy history.");
            _ = new Sha256Digest(revision.PolicyHash); AutomationValidation.Text(revision.AuthorizationId);
            AutomationValidation.Text(revision.Actor); AutomationValidation.Utc(revision.AuthorizedUtc);
        }
        if (s.PolicyHistory[^1].PolicyHash != s.PolicyHash) throw new InvalidDataException("Policy history anchor differs.");
        foreach (var i in s.Intents)
        {
            _ = new Sha256Digest(i.Id); _ = new Sha256Digest(i.SnapshotHash);
            if (i.Id != IntentId(_context, i.JobId, i.SnapshotHash, i.PolicyRevision, i.AuthorizationId) || !Enum.IsDefined(i.State) ||
                i.PolicyRevision < 1 || i.PolicyRevision > s.PolicyRevision || i.State == GitOutboxState.Blocked && i.Diagnostic is null ||
                i.State is GitOutboxState.Committed or GitOutboxState.Pushed && i.BatchId is null ||
                i.BatchId is not null && !s.Batches.Any(b => b.Id == i.BatchId && b.IntentIds.Contains(i.Id)))
                throw new InvalidDataException("Invalid Outbox intention.");
            ArgumentNullException.ThrowIfNull(i.ItemId); ArgumentNullException.ThrowIfNull(i.AttemptId); ArgumentNullException.ThrowIfNull(i.JobId);
            AutomationValidation.Text(i.AuthorizationId);
        }
        foreach (var b in s.Batches)
        {
            _ = new Sha256Digest(b.PlanHash);
            if (b.IntentIds.Count is < 1 or > 20 || b.Id != BatchId(b.IntentIds) || !Enum.IsDefined(b.State) ||
                !b.IntentIds.SequenceEqual(b.IntentIds.Distinct().Order(StringComparer.Ordinal)) ||
                b.IntentIds.Any(id => !s.Intents.Any(i => i.Id == id && i.BatchId == b.Id && i.State == b.State)) ||
                b.State is GitOutboxState.Committed or GitOutboxState.Pushed && b.CommitSha is null ||
                b.State == GitOutboxState.Blocked && b.Diagnostic is null)
                throw new InvalidDataException("Invalid Outbox batch binding.");
            if (b.CommitSha is not null) GitProductionSafety.Sha(b.CommitSha);
            var members = s.Intents.Where(i => b.IntentIds.Contains(i.Id)).ToArray();
            if (members.Select(i => (i.PolicyRevision, i.AuthorizationId)).Distinct().Count() != 1)
                throw new InvalidDataException("Mixed batch authorization.");
        }
    }
    internal static string IntentId(UniverseContext c, JobId job, string snapshot, long revision, string authorization) =>
        AutomationJson.Hash(new { Universe = c.Id.Value, Binding = GitProductionSafety.Binding(c), Job = job.Value, Snapshot = snapshot, Revision = revision, Authorization = authorization }).Hex;
    internal static string BatchId(IEnumerable<string> ids) => AutomationJson.Hash(ids.Order(StringComparer.Ordinal).ToArray()).Hex;
    internal static string PlanHash(GitCommitPlan plan) => AutomationJson.Hash(new
    {
        plan.Status.UniverseId, plan.Status.RepositoryBinding, plan.Status.Head, plan.Status.Branch, plan.Status.Upstream,
        plan.Status.RemoteName, plan.Status.RemoteBranch, plan.Status.RemoteFingerprint, plan.Status.RemoteTip,
        plan.Status.ConfigurationFingerprint, plan.Status.Porcelain, plan.Message,
        plan.Selection.Paths, Files = plan.Selection.OwnedFiles.Select(f => new { f.RelativePath, f.Digest, f.SizeBytes }).ToArray()
    }).Hex;
}
