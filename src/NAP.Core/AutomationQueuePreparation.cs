using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    /// <summary>Preparatory progress does not claim production and does not replace admission ExpectedEvidence.</summary>
    public QueueItem RecordPreparationProgress(AutomationQueueOwner owner, QueueItemId id, long revision,
        WorkflowAttemptId attemptId, QueueStage stage, QueueEventResult result) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, id); Revision(item, revision);
        if (item.ActiveAttempt != attemptId || item.State is not (QueueState.Queued or QueueState.NeedsReview) ||
            stage is not (QueueStage.Preparation or QueueStage.Normalization or QueueStage.Audit or QueueStage.Review))
            throw new AutomationException(AutomationError.IllegalTransition, "Preparatory progress requires its exact, unclaimed attempt.");
        AutomationValidation.Defined(result);
        var value = item with { Revision = item.Revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
        SaveItem(db, tx, value, revision); Event(db, tx, value, stage, QueueEventCode.StageRecorded, result, attemptId); return value;
    });
}
