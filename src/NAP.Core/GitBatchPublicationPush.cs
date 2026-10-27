namespace NAP.Core;

public sealed partial class GitBatchPublicationService
{
    private const int MaxPushAttempts = 3;
    private const int MaxExhaustedVerifications = 3;
    private static bool AllowsAutomaticPush(GitPublicationPolicyV1 p) =>
        p.Mode == GitPublicationMode.AutoCommitAndPushBatch && p.AllowCommit && p.AllowPush && !p.Revoked;
    private static bool OriginalAuthorization(GitPublicationPolicyV1 p, GitOutboxSnapshot state, GitOutboxBatch batch) =>
        batch.IntentIds.All(id => state.Intents.Any(i => i.Id == id && i.PolicyRevision == p.PolicyRevision && i.AuthorizationId == p.AuthorizationId));

    private string CommitThenPublish(GitPublicationPolicyV1 p, GitOutboxSnapshot state, GitOutboxBatch batch, GitCommitPlan? plan = null)
    {
        var result = ExecuteBatch(p, state, batch, plan);
        if (result != "git-committed" || !AllowsAutomaticPush(p)) return result;
        // Commit and publication have separate durable boundaries and independent authorization.
        var committed = _outbox.LoadLocked();
        return PublishCommitted(p, committed, committed.Batches.Single(b => b.Id == batch.Id));
    }

    private string? PublishFirstCommitted(GitPublicationPolicyV1 p, GitOutboxSnapshot state)
    {
        foreach (var batch in state.Batches.Where(b => b.State == GitOutboxState.Committed))
        {
            var result = PublishCommitted(p, state, batch);
            if (result != "git-awaiting-push") return result;
        }
        return null;
    }

    private string PublishCommitted(GitPublicationPolicyV1 p, GitOutboxSnapshot state, GitOutboxBatch batch, bool explicitRetry = false)
    {
        GitOperationReceipt receipt;
        var verificationOnly = false;
        var exhaustedPushBudget = false;
        var originalState = state;
        var attempts = 0;
        try
        {
            _ = _git.ListOperations(_context); // Reject orphan/corrupt inventory, including unrelated receipts.
            receipt = _git.ReadReceipt(_context, batch.OperationId);
            GitOutboxStore.RequireReceipt(batch, receipt);
            var originalAuthorization = OriginalAuthorization(p, state, batch);
            if (receipt.State != GitReceiptState.Pushed && !originalAuthorization)
                return "git-authorization-required";
            verificationOnly = receipt.State == GitReceiptState.Pushed;
            void Authorize(GitRepositoryStatus status) => _policy.Require(p, status, _git.Git,
                receipt.Plan.Selection.OwnedFiles.Select(f => f.RelativePath), push: !verificationOnly, automatic: true);

            if (verificationOnly)
            {
                // A Pushed receipt already durably proves publication. Complete Outbox using the
                // native local verifier before any remote budget gate, even after repeated crashes.
                receipt = _git.VerifyDurablePushedBound(_context, batch.OperationId, Authorize);
            }
            else
            {
                exhaustedPushBudget = !explicitRetry && (batch.Diagnostic is "git_push_attempt_3" or "git_push_retry_3" ||
                    ExhaustedVerifications(batch.Diagnostic) > 0);
                verificationOnly = exhaustedPushBudget || receipt.State == GitReceiptState.Pushed || !AllowsAutomaticPush(p);
                // Only Committed receipts need a budget for remote discovery.
                attempts = exhaustedPushBudget ? ExhaustedVerifications(batch.Diagnostic) :
                    explicitRetry || verificationOnly && batch.Diagnostic?.StartsWith("git_push_", StringComparison.Ordinal) == true ? 0 : PushAttempts(batch.Diagnostic);
                if (!explicitRetry && attempts >= (exhaustedPushBudget ? MaxExhaustedVerifications : MaxPushAttempts)) return "git-push-retry-exhausted";

                Checkpoint?.Invoke("before-push");
                if (exhaustedPushBudget)
                {
                    // Preserve the exhausted write budget while reserving every read-only turn, even
                    // BaseHead/offline/crash outcomes. Restarting cannot enable a fourth push or endless polling.
                    state = WithPublicationDiagnostic(state, batch, "git_push_exhausted_verify_" + (attempts + 1));
                    _outbox.SaveLocked(state); batch = state.Batches.Single(b => b.Id == batch.Id);
                }
                else if (!verificationOnly)
                {
                    // Persist before any remote write. A crash may consume an attempt but cannot reset it.
                    state = WithPublicationDiagnostic(state, batch, "git_push_attempt_" + (attempts + 1));
                    _outbox.SaveLocked(state); batch = state.Batches.Single(b => b.Id == batch.Id);
                }
                var result = _git.PushBound(_context, batch.OperationId, Authorize, verificationOnly, requirePushedReceipt: !originalAuthorization);
                if (result is null) return exhaustedPushBudget ? "git-push-retry-exhausted" : "git-awaiting-push";
                receipt = _git.ReadReceipt(_context, batch.OperationId);
            }
        }
        catch (Exception e) when (Controlled(e))
        {
            if (Busy(e))
            {
                if (!ReferenceEquals(state, originalState)) _outbox.SaveLocked(originalState);
                return "git-busy";
            }
            if (TransientPush(e))
            {
                if (exhaustedPushBudget) return "git-push-retry-exhausted"; // Its verification reservation is already durable.
                // The command may have reached the server. Every retry uses native ls-remote first.
                var failures = verificationOnly ? attempts + 1 : Math.Max(1, PushAttempts(batch.Diagnostic));
                _outbox.SaveLocked(WithPublicationDiagnostic(state, batch, (verificationOnly ? "git_verify_retry_" : "git_push_retry_") + failures));
                return "git-push-retry";
            }
            BlockBatch(state, batch, Diagnostic(e)); return "git-blocked";
        }
        // A crash/final-write failure here leaves the native Pushed evidence for a verification-only turn.
        Checkpoint?.Invoke("push-returned");
        _outbox.RecordPushedLocked(state, batch, receipt);
        return "git-pushed";
    }

    /// <summary>Explicitly renews the bounded retry budget, never the original authorization or operation.</summary>
    public string RetryPublication(AutomationQueueOwner owner, string batchId)
    {
        owner.Require(_context.Storage.StateRoot);
        using var access = AutomationManualAccess.Enter(_context); using var lease = _policy.Lock();
        var p = _policy.LoadLocked() ?? throw new InvalidDataException("Git disabled.");
        if (p.Revoked || !p.AllowCommit || p.Mode is not (GitPublicationMode.AutoCommitBatch or GitPublicationMode.AutoCommitAndPushBatch))
            throw new InvalidDataException("Automatic Git authorization inactive.");
        var state = _outbox.LoadLocked();
        var batch = state.Batches.Single(b => b.Id == batchId && b.State == GitOutboxState.Committed);
        var receipt = _git.ReadReceipt(_context, batch.OperationId); GitOutboxStore.RequireReceipt(batch, receipt);
        if (receipt.State != GitReceiptState.Pushed && !OriginalAuthorization(p, state, batch))
            throw new InvalidDataException("Original publication authorization is required.");
        return PublishCommitted(p, state, batch, explicitRetry: true);
    }

    private static int PushAttempts(string? diagnostic) => diagnostic switch
    {
        "git_push_attempt_1" or "git_push_retry_1" or "git_verify_attempt_1" or "git_verify_retry_1" => 1,
        "git_push_attempt_2" or "git_push_retry_2" or "git_verify_attempt_2" or "git_verify_retry_2" => 2,
        "git_push_attempt_3" or "git_push_retry_3" or "git_verify_attempt_3" or "git_verify_retry_3" or
            "git_push_exhausted_verify_1" or "git_push_exhausted_verify_2" or "git_push_exhausted_verify_3" => 3,
        _ => 0
    };
    private static int ExhaustedVerifications(string? diagnostic) => diagnostic switch
    {
        "git_push_exhausted_verify_1" => 1,
        "git_push_exhausted_verify_2" => 2,
        "git_push_exhausted_verify_3" => 3,
        _ => 0
    };
    private static GitOutboxSnapshot WithPublicationDiagnostic(GitOutboxSnapshot state, GitOutboxBatch batch, string code) => state with
    {
        Intents = state.Intents.Select(i => i.BatchId == batch.Id ? i with { State = GitOutboxState.Committed, Diagnostic = code } : i).ToArray(),
        Batches = state.Batches.Select(b => b.Id == batch.Id ? b with { State = GitOutboxState.Committed, Diagnostic = code } : b).ToArray()
    };
    private static bool Busy(Exception e) => e is GitProductionException g && g.Issues.Issues.Any(i => i.Code == NapIssueCodes.GitBusy) ||
        e is AutomationException { Code: AutomationError.Busy };
    private static bool TransientPush(Exception e) => e is GitProductionException g && g.Issues.Issues.Any(i =>
        i.Code is NapIssueCodes.GitRemoteUnavailable or NapIssueCodes.GitPushRejected or NapIssueCodes.GitTimeout);
}
