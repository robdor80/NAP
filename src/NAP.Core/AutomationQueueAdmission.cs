using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    /// <summary>Links an immutable admission receipt to the existing attempt, atomically with its event.
    /// Does not claim, authorize execution, or change the schema/journal format.</summary>
    public WorkflowAttempt RecordAdmissionEvidence(AutomationQueueOwner owner, QueueItemId id, long revision,
        WorkflowAttemptId attemptId, Sha256Digest receiptHash) => Write(owner, (db, tx) =>
    {
        ArgumentNullException.ThrowIfNull(receiptHash);
        var item = Item(db, tx, id); Revision(item, revision);
        var attempt = Attempt(db, tx, attemptId);
        if (item.ActiveAttempt != attemptId || attempt.ItemId != id || attempt.Status != AttemptStatus.Active ||
            item.State is not (QueueState.Queued or QueueState.NeedsReview or QueueState.MissingSource) ||
            Scalar(db, tx, "SELECT asset_id FROM reservations WHERE item_id=$i", "$i", id.Value) is not null)
            throw new AutomationException(AutomationError.IllegalTransition, "Admission evidence requires an unclaimed, unreserved active attempt.");
        var value = attempt with { ExpectedEvidence = receiptHash };
        Execute(db, tx, "UPDATE attempts SET payload=$p WHERE id=$i", "$p", AutomationJson.Encode(value), "$i", attemptId.Value);
        RecordEvidenceEvent(db, tx, item, QueueStage.Admission); return value;
    });
}
