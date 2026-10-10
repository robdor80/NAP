namespace NAP.Core;

public enum QueueState { Observed, WaitingStable, Queued, Running, RetryScheduled, NeedsReview, Rejected, Completed, Duplicate, MissingSource }
public enum QueueIncident { None, UniverseMismatch, ProfileUnknown, RatioUnsupported, NormalizationLimitExceeded, ExceptionDecisionRequired, AssetCollision, ReservationOccupied, MissingSource, InterruptedClaim, InvalidEvidence, RetryExhausted, InvalidZip, UnsafeZip, InvalidManifest, AdmissionInterrupted, SourceChanged, ResourceLimit, StorageUnavailable }
public enum QueueStage { Observation, Admission, Preparation, Normalization, Audit, Execution, Verification, Catalog, Review }
public enum QueueEventCode { Observed, Transitioned, Claimed, ClaimInterrupted, AttemptCreated, AttemptClosed, AssetReserved, ReservationReleased, EvidenceRecorded, StageRecorded }
public enum QueueEventResult { Recorded, Waiting, Succeeded, Rejected, ReviewRequired }
public enum AttemptStatus { Active, Closed }
public enum AutomationPolicyState { Disabled, Enabled, Paused, Revoked }
[Flags]
public enum AutomationOperations { None = 0, Admit = 1, Normalize = 2, Audit = 4, Archive = 8, Produce = 16, Register = 32 }
public enum NormalizationAuthorityKind { HumanDecision, AuthorizedPolicy }
public enum NormalizationOperationState { Proposed, DerivativeDeclared, Rejected, Cancelled }
public enum ExceptionalScope { SingleAsset, ReusableProposal }
public enum ExceptionalDecisionState { Proposed, Approved, Revoked }
public enum AutomationError { InvalidContract, Missing, UniverseMismatch, UnsafePath, Busy, RevisionConflict, IllegalTransition, InvalidClaim, AssetCollision, ReservationOccupied, CorruptStore, UnsupportedSchema, PolicyMismatch, Disabled }

public sealed class AutomationException(AutomationError code, string message, Exception? inner = null) : Exception(message, inner)
{
    public AutomationError Code { get; } = code;
}

/// <summary>Opaque workflow identities; JobId and UniverseId retain their existing contracts.</summary>
public sealed record QueueItemId { public string Value { get; } public QueueItemId(string value) { AutomationValidation.Id(value, "queue_"); Value = value; } public static QueueItemId Create() => new("queue_" + Guid.NewGuid().ToString("N")); }
public sealed record WorkflowAttemptId { public string Value { get; } public WorkflowAttemptId(string value) { AutomationValidation.Id(value, "attempt_"); Value = value; } public static WorkflowAttemptId Create() => new("attempt_" + Guid.NewGuid().ToString("N")); }
public sealed record QueueBatchId { public string Value { get; } public QueueBatchId(string value) { AutomationValidation.Id(value, "batch_"); Value = value; } public static QueueBatchId Create() => new("batch_" + Guid.NewGuid().ToString("N")); }
public sealed record PolicyReference(string PolicyId, int Version, Sha256Digest Hash);
public sealed record QueueZipIdentity(Sha256Digest Hash, long SizeBytes);
public sealed record QueueItem(QueueItemId Id, UniverseId UniverseId, string SourcePath, QueueZipIdentity Zip,
    QueueState State, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, int AttemptCount, int RetryCount,
    DateTimeOffset? RetryAtUtc, QueueIncident Incident, QueueBatchId? BatchId, PolicyReference Policy, long Revision,
    WorkflowAttemptId? ActiveAttempt, string? ClaimToken, long? ClaimEpoch);
public sealed record QueueObservation(QueueItemId ItemId, string SourcePath, DateTimeOffset ObservedUtc, QueueBatchId? BatchId);
public sealed record QueueBatch(QueueBatchId Id, UniverseId UniverseId, DateTimeOffset CreatedUtc, DateTimeOffset? ClosedUtc);
public sealed record QueueBatchSummary(int Observations, int UniqueItems, int Completed, int Duplicates, int Incidents, int Pending);
public sealed record QueueEvent(QueueItemId ItemId, long Sequence, DateTimeOffset Utc, QueueStage Stage,
    QueueEventCode Code, QueueEventResult Result, QueueState State, QueueIncident Incident, WorkflowAttemptId? AttemptId);
public sealed record WorkflowAttempt(WorkflowAttemptId Id, QueueItemId ItemId, UniverseId UniverseId, JobId JobId,
    int Number, WorkflowAttemptId? ParentId, AttemptStatus Status, DateTimeOffset CreatedUtc,
    EffectiveRuleSnapshot? Profile, string? StagingRelativePath, string? ExtractionRelativePath, Sha256Digest? ExpectedEvidence);
public sealed record AssetReservation(UniverseAssetKey AssetKey, QueueItemId ItemId, WorkflowAttemptId AttemptId,
    JobId JobId, Sha256Digest ContentHash, DateTimeOffset CreatedUtc);
public sealed record QueueClaim(QueueItemId ItemId, string Token, long OwnerEpoch, long Revision);

public static class QueueTransitions
{
    public static bool CanTransition(QueueState from, QueueState to)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to) || from == to) return false;
        return from switch
        {
            QueueState.Observed => to is QueueState.WaitingStable or QueueState.Queued or QueueState.NeedsReview or QueueState.Rejected or QueueState.Duplicate or QueueState.MissingSource,
            QueueState.WaitingStable => to is QueueState.Queued or QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource or QueueState.Duplicate,
            QueueState.Queued => to is QueueState.Running or QueueState.NeedsReview or QueueState.Rejected or QueueState.Duplicate or QueueState.MissingSource,
            QueueState.Running => to is QueueState.RetryScheduled or QueueState.NeedsReview or QueueState.Rejected or QueueState.Completed or QueueState.MissingSource,
            QueueState.RetryScheduled => to is QueueState.Queued or QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource,
            QueueState.NeedsReview => to is QueueState.Queued or QueueState.Rejected or QueueState.Duplicate or QueueState.MissingSource,
            QueueState.MissingSource => to is QueueState.WaitingStable or QueueState.NeedsReview or QueueState.Rejected,
            _ => false
        };
    }
}

/// <summary>Infrastructure operations only. No admission service, scheduler or production executor is composed.</summary>
public interface IAutomationQueueStore
{
    UniverseId UniverseId { get; }
    string DatabasePath { get; }
    AutomationQueueOwner AcquireOwner();
    QueueItem Observe(AutomationQueueOwner owner, UniverseId universe, string sourcePath, QueueZipIdentity zip, QueueBatchId? batch = null);
    QueueItem Get(QueueItemId id);
    IReadOnlyList<QueueItem> List(int offset = 0, int limit = 100);
    QueueItem Transition(AutomationQueueOwner owner, QueueItemId id, long revision, QueueState next,
        QueueIncident incident = QueueIncident.None, DateTimeOffset? retryAtUtc = null, QueueClaim? claim = null);
    QueueClaim Claim(AutomationQueueOwner owner, QueueItemId id, long revision);
    IReadOnlyList<QueueEvent> Events(QueueItemId id);
    WorkflowAttempt CreateAttempt(AutomationQueueOwner owner, QueueItemId itemId, long revision, EffectiveRuleSnapshot? profile = null,
        string? stagingRelativePath = null, string? extractionRelativePath = null, Sha256Digest? expectedEvidence = null);
    WorkflowAttempt GetAttempt(WorkflowAttemptId id);
    void CloseAttempt(AutomationQueueOwner owner, QueueItemId itemId, long revision);
    void ReleaseReservation(AutomationQueueOwner owner, QueueItemId itemId, long revision);
    AssetReservation? GetReservation(UniverseAssetKey asset);
    IReadOnlyList<QueueObservation> Observations(QueueItemId id);
    QueueBatch CreateBatch(AutomationQueueOwner owner);
    QueueBatch CloseBatch(AutomationQueueOwner owner, QueueBatchId id);
    QueueBatch GetBatch(QueueBatchId id);
    QueueBatchSummary BatchSummary(QueueBatchId id);
    WorkflowAttempt BindProfile(AutomationQueueOwner owner, QueueItemId itemId, long revision, EffectiveRuleSnapshot profile);
    AssetReservation ReserveAsset(AutomationQueueOwner owner, QueueItemId itemId, long revision, UniverseAssetKey asset, Sha256Digest contentHash);
    QueueItem ParkInterruptedClaim(AutomationQueueOwner owner, QueueItemId id, long revision);
    QueueItem RecordProgress(AutomationQueueOwner owner, QueueItemId id, long revision, QueueClaim claim, QueueStage stage, QueueEventResult result);
}
public interface IAutomationPolicyStore
{
    StoredAutomationPolicy CurrentPolicy();
    StoredAutomationPolicy GetPolicy(PolicyReference reference);
    IReadOnlyList<StoredAutomationPolicy> PolicyHistory(PolicyReference reference);
    StoredAutomationPolicy SavePolicy(AutomationQueueOwner owner, AutomationPolicyDefinition definition);
    StoredAutomationPolicy RecordAuthorization(AutomationQueueOwner owner, PolicyReference reference, long revision, AutomationAuthorization authorization);
    StoredAutomationPolicy SetPolicyState(AutomationQueueOwner owner, PolicyReference reference, long revision, AutomationPolicyState state);
}
public interface IAutomationEvidenceStore
{
    NormalizationLineage RecordLineage(AutomationQueueOwner owner, long itemRevision, NormalizationLineage lineage, QueueClaim? claim = null);
    IReadOnlyList<NormalizationLineage> Lineage(QueueItemId id);
    ExceptionalConfigurationDecision RecordDecision(AutomationQueueOwner owner, long itemRevision, ExceptionalConfigurationDecision decision);
    IReadOnlyList<ExceptionalConfigurationDecision> Decisions(QueueItemId id);
}
