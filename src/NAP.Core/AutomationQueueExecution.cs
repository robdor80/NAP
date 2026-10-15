using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    // Only the evidence-verifying adapter may transfer a stale claim. A TTL cannot authorize takeover.
    internal QueueClaim RecoverExecutionClaim(AutomationQueueOwner owner, QueueItemId id, long revision, ExecutionBinding binding)
        => Write(owner, (db, tx) =>
        {
            var item = Item(db, tx, id); Revision(item, revision);
            var policy = CurrentPolicy(db, tx);
            if (item.State != QueueState.Running || item.ClaimEpoch >= owner.Epoch || item.ActiveAttempt != binding.AttemptId ||
                item.Id != binding.ItemId || item.Policy != binding.Policy || !policy.IsExecutionAuthorized || policy.Reference != item.Policy)
                throw new AutomationException(AutomationError.InvalidClaim, "Recovery requires a stale exclusive claim and exact authorized evidence.");
            var attempt = Attempt(db, tx, binding.AttemptId);
            if (attempt.JobId != binding.JobId) throw new AutomationException(AutomationError.InvalidClaim, "Recovery Job differs.");
            var value = item with { ClaimToken = Guid.NewGuid().ToString("N"), ClaimEpoch = owner.Epoch,
                Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
            SaveItem(db, tx, value, revision); Event(db, tx, value, QueueStage.Execution, QueueEventCode.ClaimRecovered, QueueEventResult.Recorded);
            return new QueueClaim(id, value.ClaimToken, owner.Epoch, value.Revision);
        });

    // COMPLETED is never reopened for work. This narrow transition records a detected integrity incident.
    internal QueueItem ReportCompletedInconsistent(AutomationQueueOwner owner, QueueItemId id, long revision)
        => Write(owner, (db, tx) =>
        {
            var item = Item(db, tx, id); Revision(item, revision);
            if (item.State != QueueState.Completed) throw new AutomationException(AutomationError.IllegalTransition, "Only a completed verification can report this incident.");
            var next = item with { State = QueueState.NeedsReview, Incident = QueueIncident.InvalidEvidence,
                Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
            SaveItem(db, tx, next, revision); Event(db, tx, next, QueueStage.Verification, QueueEventCode.CompletedInconsistent, QueueEventResult.ReviewRequired);
            return next;
        });
}
