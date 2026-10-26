namespace NAP.Core;

/// <summary>Owner-thread Git orchestration. No production execution or SQLite transaction across Git.</summary>
public sealed partial class GitBatchPublicationService
{
    private readonly UniverseContext _context;
    private readonly AutomationQueueStore _queue;
    private readonly GitPublicationPolicyStore _policy;
    private readonly GitOutboxStore _outbox;
    private readonly GitProductionService _git;
    internal Action<string>? Checkpoint { get; set; }
    public GitBatchPublicationService(UniverseContext context, AutomationQueueStore queue,
        GitPublicationPolicyStore? policy = null, GitProductionService? git = null)
    {
        if (!AutomationValidation.SameRoots(context.Storage, queue.Storage)) throw new InvalidDataException("Git queue roots differ.");
        _context = context; _queue = queue; _policy = policy ?? new(context); _outbox = new(context, _policy); _git = git ?? new();
    }
    /// <summary>One bounded completion page and one batch at most. Repeated calls recover the Completed-to-Outbox window.</summary>
    public string RunOnce(AutomationQueueOwner owner, int pageSize = 20)
    {
        owner.Require(_context.Storage.StateRoot);
        using var access = AutomationManualAccess.Enter(_context); using var policyLease = _policy.Lock();
        var p = _policy.LoadLocked();
        if (p is null || p.Mode == GitPublicationMode.Disabled || p.Revoked || !p.AllowCommit) return "git-disabled";
        if (p.Mode == GitPublicationMode.Manual) return "git-manual";
        var state = _outbox.LoadLocked();
        var publication = PublishFirstCommitted(p, state);
        if (publication is not null) return publication;
        state = Reconcile(p, state, pageSize, out var caughtUp);
        // Do not form an incomplete batch while more completed evidence remains on the next page.
        if (!caughtUp) return "git-reconciling";
        var recovery = state.Batches.FirstOrDefault(b => b.State == GitOutboxState.Pending);
        if (recovery is not null) return CommitThenPublish(p, state, recovery);
        var candidates = state.Intents.Where(i => i.State == GitOutboxState.Pending && i.BatchId is null)
            .OrderBy(i => i.Id, StringComparer.Ordinal).Take(p.MaxJobsPerBatch).ToArray();
        if (candidates.Length == 0) return "git-idle";
        GitOutboxBatch reserved;
        GitCommitPlan preparedPlan;
        try
        {
            if (candidates.Any(i => i.PolicyRevision != p.PolicyRevision || i.AuthorizationId != p.AuthorizationId))
                throw new InvalidDataException("Pending policy revision changed.");
            var results = candidates.Select(Verify).ToArray();
            var batchId = GitOutboxStore.BatchId(candidates.Select(i => i.Id));
            var plan = _git.PrepareCommit(_context, results, "NAP batch " + batchId);
            using (var production = GitProductionSafety.Acquire(_context))
                _policy.Require(p, new GitProductionInspector { Git = _git.Git }.InspectLocked(_context), _git.Git,
                    plan.Selection.OwnedFiles.Select(f => f.RelativePath), automatic: true);
            var batch = new GitOutboxBatch(batchId, candidates.Select(i => i.Id).Order(StringComparer.Ordinal).ToArray(),
                GitOperationId.New(), GitOutboxStore.PlanHash(plan), GitOutboxState.Pending, null, null);
            state = state with { Intents = state.Intents.Select(i => batch.IntentIds.Contains(i.Id) ? i with { BatchId = batch.Id } : i).ToArray(),
                Batches = state.Batches.Append(batch).ToArray() };
            // One atomic final reserves membership + exact operation ID + full native plan fingerprint, before Prepared.
            _outbox.SaveLocked(state); Checkpoint?.Invoke("batch-reserved");
            reserved = batch; preparedPlan = plan;
        }
        catch (Exception e) when (Controlled(e))
        {
            // Reload: a failure after reservation must preserve that exact operation for explicit recovery.
            state = _outbox.LoadLocked();
            var batch = state.Batches.FirstOrDefault(b => b.State == GitOutboxState.Pending && candidates.Any(i => b.IntentIds.Contains(i.Id)));
            if (batch is not null) BlockBatch(state, batch, Diagnostic(e));
            else _outbox.SaveLocked(state with { Intents = state.Intents.Select(i => candidates.Any(c => c.Id == i.Id)
                ? i with { State = GitOutboxState.Blocked, Diagnostic = Diagnostic(e) } : i).ToArray() });
            return "git-blocked";
        }
        return CommitThenPublish(p, state, reserved, preparedPlan);
    }
    private GitOutboxSnapshot Reconcile(GitPublicationPolicyV1 p, GitOutboxSnapshot state, int limit, out bool caughtUp)
    {
        var page = _queue.GitCompletions(state.CompletionCursor, limit);
        caughtUp = page.Count < limit;
        foreach (var entry in page)
        {
            var item = _queue.Get(entry.Item);
            var attempt = _queue.GetAttempt(item.ActiveAttempt ?? throw new InvalidDataException("Completed item has no attempt."));
            if (item.State != QueueState.Completed || new JobStateStore(_context).Load(attempt.JobId).State != JobState.Completed)
                throw new InvalidDataException("Git completion cursor encountered inconsistent completion.");
            if (!state.Intents.Any(i => i.JobId == attempt.JobId))
            {
                string snapshot; string? diagnostic = null;
                try { snapshot = new DurableExecutionService(_context, _queue).VerifyCompletedReadOnly(item.Id).SnapshotHash.Hex; }
                catch (Exception e) when (Controlled(e))
                {
                    // This is a blocked correlation, never ownership. It cannot become Pending without a new verified intent.
                    snapshot = AutomationJson.Hash(new { Job = attempt.JobId.Value, Attempt = attempt.Id.Value, Evidence = "unverified" }).Hex;
                    diagnostic = "native_evidence_unavailable";
                }
                var intent = new GitOutboxIntent(GitOutboxStore.IntentId(_context, attempt.JobId, snapshot, p.PolicyRevision, p.AuthorizationId),
                    item.Id, attempt.Id, attempt.JobId, item.BatchId, snapshot, p.PolicyRevision, p.AuthorizationId,
                    diagnostic is null ? GitOutboxState.Pending : GitOutboxState.Blocked, diagnostic, null);
                state = state with { Intents = state.Intents.Append(intent).ToArray() };
            }
            state = state with { CompletionCursor = entry.Sequence };
        }
        if (page.Count != 0) { _outbox.SaveLocked(state); Checkpoint?.Invoke("reconciled"); }
        return state;
    }
    private ProductionAssetResult Verify(GitOutboxIntent intent)
    {
        var evidence = new DurableExecutionService(_context, _queue).VerifyCompletedReadOnly(intent.ItemId);
        if (evidence.JobId != intent.JobId || evidence.AttemptId != intent.AttemptId || evidence.SnapshotHash.Hex != intent.SnapshotHash)
            throw new InvalidDataException("Native Git evidence changed.");
        return evidence.Production;
    }
    private string ExecuteBatch(GitPublicationPolicyV1 p, GitOutboxSnapshot state, GitOutboxBatch batch, GitCommitPlan? plan = null)
    {
        try
        {
            var intents = batch.IntentIds.Select(id => state.Intents.Single(i => i.Id == id)).ToArray();
            if (intents.Any(i => i.PolicyRevision != p.PolicyRevision || i.AuthorizationId != p.AuthorizationId))
                throw new InvalidDataException("Reserved batch authorization changed.");
            var results = intents.Select(Verify).ToArray();
            var paths = results.SelectMany(r => r.FilesVerified.Select(f => r.RelativeDirectory + "/" + f.FileName)).Order(StringComparer.Ordinal).ToArray();
            void Authorize() => _policy.Require(p, new GitProductionInspector { Git = _git.Git }.InspectLocked(_context), _git.Git, paths, automatic: true);
            var operations = _git.ListOperations(_context); // Strict inventory rejects every orphan/corrupt receipt.
            if (operations.Contains(batch.OperationId))
            {
                var receipt = _git.ReadReceipt(_context, batch.OperationId);
                if (GitOutboxStore.PlanHash(receipt.Plan) != batch.PlanHash) throw new InvalidDataException("Reserved native plan differs from receipt.");
                using (var production = GitProductionSafety.Acquire(_context)) Authorize();
                receipt = _git.Recover(_context, batch.OperationId);
                if (receipt.State == GitReceiptState.Prepared)
                {
                    _git.RetryCommitBound(_context, batch.OperationId, Authorize);
                    receipt = _git.ReadReceipt(_context, batch.OperationId);
                }
                Complete(state, batch, receipt.CommitSha!); return "git-committed";
            }
            plan ??= _git.PrepareCommit(_context, results, "NAP batch " + batch.Id);
            if (GitOutboxStore.PlanHash(plan) != batch.PlanHash) throw new InvalidDataException("Reserved Git plan is stale; no replan permitted.");
            var result = _git.CommitBound(_context, plan, batch.OperationId, Authorize);
            Checkpoint?.Invoke("commit-returned"); Complete(state, batch, result.CommitSha); return "git-committed";
        }
        catch (Exception e) when (Controlled(e)) { BlockBatch(state, batch, Diagnostic(e)); return "git-blocked"; }
    }
    private void Complete(GitOutboxSnapshot s, GitOutboxBatch b, string sha) => _outbox.SaveLocked(s with
    {
        Intents = s.Intents.Select(i => i.BatchId == b.Id ? i with { State = GitOutboxState.Committed, Diagnostic = null } : i).ToArray(),
        Batches = s.Batches.Select(i => i.Id == b.Id ? i with { State = GitOutboxState.Committed, CommitSha = sha, Diagnostic = null } : i).ToArray()
    });
    private void BlockBatch(GitOutboxSnapshot s, GitOutboxBatch b, string code) => _outbox.SaveLocked(s with
    {
        Intents = s.Intents.Select(i => i.BatchId == b.Id ? i with { State = GitOutboxState.Blocked, Diagnostic = code } : i).ToArray(),
        Batches = s.Batches.Select(i => i.Id == b.Id ? i with { State = GitOutboxState.Blocked, Diagnostic = code } : i).ToArray()
    });
    /// <summary>Explicit retry of one blocked intent/batch, preserving its reserved ID and plan. Never deletes evidence.</summary>
    public string RetryBlocked(AutomationQueueOwner owner, string intentId)
    {
        owner.Require(_context.Storage.StateRoot); using var access = AutomationManualAccess.Enter(_context); using var lease = _policy.Lock();
        var p = _policy.LoadLocked() ?? throw new InvalidDataException("Git disabled.");
        if (p.Revoked || !p.AllowCommit || p.Mode is not (GitPublicationMode.AutoCommitBatch or GitPublicationMode.AutoCommitAndPushBatch)) throw new InvalidDataException("Automatic Git authorization inactive.");
        var s = _outbox.LoadLocked(); var i = s.Intents.Single(i => i.Id == intentId && i.State == GitOutboxState.Blocked);
        if (i.PolicyRevision != p.PolicyRevision || i.AuthorizationId != p.AuthorizationId) throw new InvalidDataException("Explicit new policy migration is required.");
        if (i.BatchId is { } id)
        {
            var batch = s.Batches.Single(b => b.Id == id);
            return batch.CommitSha is not null ? PublishCommitted(p, s, batch, explicitRetry: true) : CommitThenPublish(p, s, batch);
        }
        Verify(i);
        _outbox.SaveLocked(s with { Intents = s.Intents.Select(v => v.Id == i.Id ? v with { State = GitOutboxState.Pending, Diagnostic = null } : v).ToArray() });
        return "git-pending";
    }
    private static bool Controlled(Exception e) => e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
        AutomationException or ProductionStorageException or ArchiveStorageException or CatalogException or System.Text.Json.JsonException or FormatException;
    private static string Diagnostic(Exception e) => e is GitProductionException g ? g.Issues.Issues.First().Code : "git_publication_blocked";
}
