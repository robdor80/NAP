using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    public StoredAutomationPolicy CurrentPolicy() => Read(db => CurrentPolicy(db, null));
    public IReadOnlyList<StoredAutomationPolicy> PolicyHistory(PolicyReference reference) => Read(db => { Policy(db, null, reference); return Payloads<StoredAutomationPolicy>(db, null, "SELECT payload FROM policy_history WHERE policy_hash=$h ORDER BY revision", "$h", reference.Hash.Hex); });
    public StoredAutomationPolicy GetPolicy(PolicyReference reference) => Read(db => Policy(db, null, reference));
    public StoredAutomationPolicy SavePolicy(AutomationQueueOwner owner, AutomationPolicyDefinition definition) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Policy(definition, _roots); var reference = definition.Reference;
        var existing = (string?)Scalar(db, tx, "SELECT payload FROM policies WHERE policy_id=$i AND version=$v", "$i", reference.PolicyId, "$v", reference.Version);
        if (existing is not null)
        {
            var prior = AutomationJson.Decode<StoredAutomationPolicy>(existing);
            if (prior.Reference != reference) throw new AutomationException(AutomationError.PolicyMismatch, "An immutable policy version cannot be replaced.");
            return prior;
        }
        var latest = Convert.ToInt64(Scalar(db, tx, "SELECT ifnull(max(version),0) FROM policies WHERE policy_id=$i", "$i", reference.PolicyId));
        if (reference.Version <= latest) throw new AutomationException(AutomationError.PolicyMismatch, "Policy version must advance.");
        var value = new StoredAutomationPolicy(definition, reference, AutomationPolicyState.Disabled, null, 0);
        InsertPolicy(db, tx, value); Execute(db, tx, "UPDATE queue_metadata SET current_policy=$p WHERE id=1", "$p", reference.Hash.Hex); return value;
    });
    public StoredAutomationPolicy RecordAuthorization(AutomationQueueOwner owner, PolicyReference reference, long revision, AutomationAuthorization authorization) => Write(owner, (db, tx) =>
    {
        var prior = Policy(db, tx, reference); PolicyRevision(prior, revision);
        AutomationValidation.Scope(UniverseId, authorization.UniverseId); AutomationValidation.Text(authorization.AuthorizationId); AutomationValidation.Text(authorization.Actor); AutomationValidation.Utc(authorization.AuthorizedUtc);
        if (authorization.Policy != prior.Reference || prior.State == AutomationPolicyState.Revoked || prior.Authorization is not null)
            throw new AutomationException(AutomationError.PolicyMismatch, "Authorization must bind an exact, unrevoked policy once.");
        var value = prior with { Authorization = authorization, Revision = revision + 1 }; SavePolicyRow(db, tx, value); return value;
    });
    public StoredAutomationPolicy SetPolicyState(AutomationQueueOwner owner, PolicyReference reference, long revision, AutomationPolicyState state) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Defined(state);
        if (state == AutomationPolicyState.Enabled) throw new AutomationException(AutomationError.Disabled, "Phase 1 cannot enable automation.");
        var prior = Policy(db, tx, reference); PolicyRevision(prior, revision);
        if (prior.State == AutomationPolicyState.Revoked || prior.State == state) throw new AutomationException(AutomationError.IllegalTransition, "Revocation is terminal; unchanged policy state is not a transition.");
        var value = prior with { State = state, Revision = revision + 1 }; SavePolicyRow(db, tx, value); return value;
    });
    private static void PolicyRevision(StoredAutomationPolicy policy, long revision) { if (policy.Revision != revision) throw new AutomationException(AutomationError.RevisionConflict, "Policy revision is stale."); }
    private static void InsertPolicy(SqliteConnection db, SqliteTransaction tx, StoredAutomationPolicy value)
    {
        Execute(db, tx, "INSERT INTO policies VALUES($h,$i,$v,$p)", "$h", value.Reference.Hash.Hex, "$i", value.Reference.PolicyId, "$v", value.Reference.Version, "$p", AutomationJson.Encode(value));
        AppendPolicyHistory(db, tx, value);
    }
    private static void SavePolicyRow(SqliteConnection db, SqliteTransaction tx, StoredAutomationPolicy value)
    {
        Execute(db, tx, "UPDATE policies SET payload=$p WHERE hash=$h", "$p", AutomationJson.Encode(value), "$h", value.Reference.Hash.Hex);
        AppendPolicyHistory(db, tx, value);
    }
    private static void AppendPolicyHistory(SqliteConnection db, SqliteTransaction tx, StoredAutomationPolicy value) => Execute(db, tx,
        "INSERT INTO policy_history VALUES($h,$r,$p)", "$h", value.Reference.Hash.Hex, "$r", value.Revision, "$p", AutomationJson.Encode(value));
    private static StoredAutomationPolicy CurrentPolicy(SqliteConnection db, SqliteTransaction? tx) => AutomationJson.Decode<StoredAutomationPolicy>((string?)Scalar(db, tx, "SELECT p.payload FROM policies p JOIN queue_metadata m ON m.current_policy=p.hash WHERE m.id=1") ?? throw AutomationValidation.Corrupt("Missing current policy."));
    private static StoredAutomationPolicy Policy(SqliteConnection db, SqliteTransaction? tx, PolicyReference reference)
    {
        AutomationValidation.Reference(reference);
        var value = AutomationJson.Decode<StoredAutomationPolicy>((string?)Scalar(db, tx, "SELECT payload FROM policies WHERE hash=$h", "$h", reference.Hash.Hex) ?? throw new AutomationException(AutomationError.Missing, "Policy snapshot does not exist."));
        if (value.Reference != reference) throw new AutomationException(AutomationError.PolicyMismatch, "Policy identity differs from its hash."); return value;
    }
    public QueueBatch CreateBatch(AutomationQueueOwner owner) => Write(owner, (db, tx) =>
    {
        var batch = new QueueBatch(QueueBatchId.Create(), UniverseId, DateTimeOffset.UtcNow, null);
        Execute(db, tx, "INSERT INTO batches VALUES($i,$p)", "$i", batch.Id.Value, "$p", AutomationJson.Encode(batch)); return batch;
    });
    public QueueBatch CloseBatch(AutomationQueueOwner owner, QueueBatchId id) => Write(owner, (db, tx) =>
    {
        var prior = Batch(db, tx, id, false); if (prior.ClosedUtc is not null) return prior;
        var value = prior with { ClosedUtc = DateTimeOffset.UtcNow }; Execute(db, tx, "UPDATE batches SET payload=$p WHERE id=$i", "$p", AutomationJson.Encode(value), "$i", id.Value); return value;
    });
    public QueueBatch GetBatch(QueueBatchId id) => Read(db => Batch(db, null, id, false));
    public QueueBatchSummary BatchSummary(QueueBatchId id) => Read(db =>
    {
        Batch(db, null, id, false);
        var values = Payloads<QueueItem>(db, null, "SELECT DISTINCT i.payload FROM items i JOIN observations o ON o.item_id=i.id WHERE o.batch_id=$b", "$b", id.Value);
        var observations = Convert.ToInt32(Scalar(db, null, "SELECT count(*) FROM observations WHERE batch_id=$b", "$b", id.Value));
        return new QueueBatchSummary(observations, values.Count, values.Count(i => i.State == QueueState.Completed), values.Count(i => i.State == QueueState.Duplicate),
            values.Count(i => i.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource), values.Count(i => i.State is QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.Running or QueueState.RetryScheduled));
    });
    private static QueueBatch Batch(SqliteConnection db, SqliteTransaction? tx, QueueBatchId id, bool requireOpen)
    {
        var value = AutomationJson.Decode<QueueBatch>((string?)Scalar(db, tx, "SELECT payload FROM batches WHERE id=$i", "$i", id.Value) ?? throw new AutomationException(AutomationError.Missing, "Batch does not exist."));
        if (requireOpen && value.ClosedUtc is not null) throw new AutomationException(AutomationError.IllegalTransition, "Cannot observe into a closed cohort."); return value;
    }
    public WorkflowAttempt CreateAttempt(AutomationQueueOwner owner, QueueItemId itemId, long revision, EffectiveRuleSnapshot? profile = null,
        string? stagingRelativePath = null, string? extractionRelativePath = null, Sha256Digest? expectedEvidence = null) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, itemId); Revision(item, revision); if (profile is not null) { AutomationValidation.Scope(UniverseId, profile.UniverseId); profile.ValidateAndGetRule(); }
        AutomationValidation.Relative(stagingRelativePath); AutomationValidation.Relative(extractionRelativePath);
        if (item.State is not (QueueState.Queued or QueueState.NeedsReview)) throw new AutomationException(AutomationError.IllegalTransition, "Attempts are allocated before claim, only on Queued or NeedsReview.");
        if (item.ActiveAttempt is not null) throw new AutomationException(AutomationError.IllegalTransition, "An active attempt already exists; reuse its persisted JobId.");
        var prior = (string?)Scalar(db, tx, "SELECT id FROM attempts WHERE item_id=$i ORDER BY number DESC LIMIT 1", "$i", itemId.Value);
        var value = new WorkflowAttempt(WorkflowAttemptId.Create(), itemId, UniverseId, JobId.Create(), item.AttemptCount + 1, prior is null ? null : new WorkflowAttemptId(prior), AttemptStatus.Active, DateTimeOffset.UtcNow, profile, stagingRelativePath, extractionRelativePath, expectedEvidence);
        Execute(db, tx, "INSERT INTO attempts VALUES($i,$q,$j,$n,'Active',$p)", "$i", value.Id.Value, "$q", itemId.Value, "$j", value.JobId.Value, "$n", value.Number, "$p", AutomationJson.Encode(value));
        var updated = item with { ActiveAttempt = value.Id, AttemptCount = value.Number, Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
        SaveItem(db, tx, updated, revision); Event(db, tx, updated, QueueStage.Preparation, QueueEventCode.AttemptCreated, QueueEventResult.Recorded, value.Id); return value;
    });
    /// <summary>Resolve a profile once, without changing the already persisted attempt or Job identity.</summary>
    public WorkflowAttempt BindProfile(AutomationQueueOwner owner, QueueItemId itemId, long revision, EffectiveRuleSnapshot profile) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, itemId); Revision(item, revision); AutomationValidation.Scope(UniverseId, profile.UniverseId); profile.ValidateAndGetRule();
        if (item.State is not (QueueState.Queued or QueueState.NeedsReview) || item.ActiveAttempt is null) throw new AutomationException(AutomationError.IllegalTransition, "Profile resolution requires an unclaimed active attempt.");
        var attempt = Attempt(db, tx, item.ActiveAttempt);
        if (attempt.Profile is not null) throw new AutomationException(AutomationError.IllegalTransition, "Profile evidence is immutable once bound.");
        var value = attempt with { Profile = profile };
        Execute(db, tx, "UPDATE attempts SET payload=$p WHERE id=$i", "$p", AutomationJson.Encode(value), "$i", attempt.Id.Value);
        RecordEvidenceEvent(db, tx, item, QueueStage.Preparation); return value;
    });
    public WorkflowAttempt GetAttempt(WorkflowAttemptId id) => Read(db => Attempt(db, null, id));
    public void CloseAttempt(AutomationQueueOwner owner, QueueItemId itemId, long revision) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, itemId); Revision(item, revision);
        if (item.State is not (QueueState.Completed or QueueState.Rejected or QueueState.NeedsReview) || item.ActiveAttempt is null)
            throw new AutomationException(AutomationError.IllegalTransition, "Only stopped or completed work can close its active attempt.");
        if (Scalar(db, tx, "SELECT asset_id FROM reservations WHERE item_id=$i", "$i", itemId.Value) is not null) throw new AutomationException(AutomationError.ReservationOccupied, "Explicitly resolve the asset reservation before closing this attempt.");
        var attempt = Attempt(db, tx, item.ActiveAttempt); var closed = attempt with { Status = AttemptStatus.Closed };
        Execute(db, tx, "UPDATE attempts SET status='Closed',payload=$p WHERE id=$i", "$p", AutomationJson.Encode(closed), "$i", attempt.Id.Value);
        var updated = item with { ActiveAttempt = null, Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
        SaveItem(db, tx, updated, revision); Event(db, tx, updated, QueueStage.Review, QueueEventCode.AttemptClosed, QueueEventResult.Recorded, attempt.Id); return 0;
    });
    private static WorkflowAttempt Attempt(SqliteConnection db, SqliteTransaction? tx, WorkflowAttemptId id) => AutomationJson.Decode<WorkflowAttempt>((string?)Scalar(db, tx, "SELECT payload FROM attempts WHERE id=$i", "$i", id.Value) ?? throw new AutomationException(AutomationError.Missing, "Workflow attempt does not exist."));
    public AssetReservation ReserveAsset(AutomationQueueOwner owner, QueueItemId itemId, long revision, UniverseAssetKey asset, Sha256Digest contentHash) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Scope(UniverseId, asset.UniverseId); ArgumentNullException.ThrowIfNull(contentHash);
        var item = Item(db, tx, itemId); Revision(item, revision);
        if (item.ActiveAttempt is null || item.State is not (QueueState.Queued or QueueState.NeedsReview)) throw new AutomationException(AutomationError.IllegalTransition, "Reserve a validated asset identity before claim.");
        var attempt = Attempt(db, tx, item.ActiveAttempt);
        if (attempt.Profile is null) throw AutomationValidation.Invalid("Asset reservation requires a resolved complete profile.");
        var prior = (string?)Scalar(db, tx, "SELECT payload FROM reservations WHERE asset_id=$a", "$a", asset.AssetId);
        if (prior is not null)
        {
            var existing = AutomationJson.Decode<AssetReservation>(prior);
            if (existing.ContentHash != contentHash) throw new AutomationException(AutomationError.AssetCollision, "Same asset identity has different content.");
            if (existing.ItemId != itemId || existing.AttemptId != attempt.Id) throw new AutomationException(AutomationError.ReservationOccupied, "Asset is reserved by a different workflow.");
            return existing;
        }
        if (Scalar(db, tx, "SELECT asset_id FROM reservations WHERE item_id=$i", "$i", itemId.Value) is not null) throw new AutomationException(AutomationError.ReservationOccupied, "This workflow already owns a different asset identity.");
        var value = new AssetReservation(asset, itemId, attempt.Id, attempt.JobId, contentHash, DateTimeOffset.UtcNow);
        Execute(db, tx, "INSERT INTO reservations VALUES($a,$i,$t,$h,$p)", "$a", asset.AssetId, "$i", itemId.Value, "$t", attempt.Id.Value, "$h", contentHash.Hex, "$p", AutomationJson.Encode(value));
        var updated = item with { Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow }; SaveItem(db, tx, updated, revision);
        Event(db, tx, updated, QueueStage.Preparation, QueueEventCode.AssetReserved, QueueEventResult.Recorded); return value;
    });
    public AssetReservation? GetReservation(UniverseAssetKey asset) => Read(db =>
    {
        AutomationValidation.Scope(UniverseId, asset.UniverseId);
        var text = (string?)Scalar(db, null, "SELECT payload FROM reservations WHERE asset_id=$a", "$a", asset.AssetId); return text is null ? null : AutomationJson.Decode<AssetReservation>(text);
    });
    /// <summary>Explicit ledger resolution, not permission to discard/adopt any physical output.</summary>
    public void ReleaseReservation(AutomationQueueOwner owner, QueueItemId itemId, long revision) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, itemId); Revision(item, revision);
        if (item.State is not (QueueState.Completed or QueueState.Rejected or QueueState.NeedsReview)) throw new AutomationException(AutomationError.IllegalTransition, "Active or retrying workflows cannot release reservations.");
        if (Execute(db, tx, "DELETE FROM reservations WHERE item_id=$i", "$i", itemId.Value) != 1) throw new AutomationException(AutomationError.Missing, "Reservation does not exist.");
        var updated = item with { Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow }; SaveItem(db, tx, updated, revision); Event(db, tx, updated, QueueStage.Review, QueueEventCode.ReservationReleased, QueueEventResult.Recorded); return 0;
    });
}
