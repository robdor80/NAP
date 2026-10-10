using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    public NormalizationLineage RecordLineage(AutomationQueueOwner owner, long itemRevision, NormalizationLineage lineage, QueueClaim? claim = null) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Lineage(lineage); var item = Item(db, tx, lineage.ItemId); Revision(item, itemRevision);
        if (item.State == QueueState.Running) RequireClaim(owner, item, claim);
        else if (item.State is not (QueueState.Queued or QueueState.NeedsReview)) throw new AutomationException(AutomationError.IllegalTransition, "Normalization evidence requires an active preparatory workflow.");
        var attempt = BindAttempt(db, tx, lineage.ItemId, lineage.AttemptId, lineage.UniverseId);
        if (attempt.JobId != lineage.JobId || item.Zip.Hash != lineage.OriginalZip.Hash || item.Zip.SizeBytes != lineage.OriginalZip.SizeBytes)
            throw AutomationValidation.Invalid("Lineage must bind the original observation and preassigned Job.");
        var rule = (attempt.Profile ?? throw AutomationValidation.Invalid("Normalization requires a resolved complete rule.")).ValidateAndGetRule();
        if (rule.AssetType != lineage.Parameters.AssetType || rule.ProductionProfile != lineage.Parameters.ProductionProfile ||
            !lineage.Parameters.Ratio.Matches(rule.Conversion!.OutputWidth, rule.Conversion.OutputHeight)) throw AutomationValidation.Invalid("Lineage parameters do not match the frozen attempt rule.");
        if (lineage.Authority.Kind == NormalizationAuthorityKind.AuthorizedPolicy)
        {
            var policy = Policy(db, tx, lineage.Authority.Policy!);
            if (policy.Reference != item.Policy || policy.State == AutomationPolicyState.Revoked || policy.Authorization is not { } grant ||
                grant.AuthorizationId != lineage.Authority.AuthorizationId || grant.Actor != lineage.Authority.Actor || grant.AuthorizedUtc != lineage.Authority.Utc ||
                !policy.Definition.Normalization.Any(p => AutomationJson.Encode(p) == AutomationJson.Encode(lineage.Parameters)))
                throw new AutomationException(AutomationError.PolicyMismatch, "Recorded policy authority does not cover this exact transformation.");
        }
        var prior = Payloads<NormalizationLineage>(db, tx, "SELECT payload FROM lineage WHERE operation_id=$i ORDER BY version DESC LIMIT 1", "$i", lineage.OperationId).SingleOrDefault();
        if (prior is null && (lineage.Version != 1 || lineage.State != NormalizationOperationState.Proposed)) throw AutomationValidation.Invalid("First operation version must be 1.");
        if (prior is not null)
        {
            if (lineage.Version != prior.Version + 1 || prior.State != NormalizationOperationState.Proposed || lineage.State == NormalizationOperationState.Proposed ||
                AutomationJson.Encode(prior with { Version = lineage.Version, State = lineage.State, DerivedPng = lineage.DerivedPng, CandidateZip = lineage.CandidateZip, Geometry = lineage.Geometry, Utc = lineage.Utc }) != AutomationJson.Encode(lineage))
                throw new AutomationException(AutomationError.IllegalTransition, "Lineage identity/authority is immutable and only a proposed operation may advance.");
        }
        Execute(db, tx, "INSERT INTO lineage VALUES($o,$v,$i,$a,$p)", "$o", lineage.OperationId, "$v", lineage.Version, "$i", item.Id.Value, "$a", attempt.Id.Value, "$p", AutomationJson.Encode(lineage));
        RecordEvidenceEvent(db, tx, item, QueueStage.Normalization); return lineage;
    });
    public IReadOnlyList<NormalizationLineage> Lineage(QueueItemId id) => Read(db => { Item(db, null, id); return Payloads<NormalizationLineage>(db, null, "SELECT payload FROM lineage WHERE item_id=$i ORDER BY operation_id,version", "$i", id.Value); });
    public ExceptionalConfigurationDecision RecordDecision(AutomationQueueOwner owner, long itemRevision, ExceptionalConfigurationDecision decision) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Decision(decision); var item = Item(db, tx, decision.ItemId); Revision(item, itemRevision);
        var attempt = BindAttempt(db, tx, decision.ItemId, decision.AttemptId, decision.UniverseId);
        if (item.State != QueueState.NeedsReview || attempt.Profile is not null && (attempt.Profile.ProductionProfile != decision.OriginalProfile || attempt.Profile.AssetType != decision.EffectiveRule.AssetType))
            throw new AutomationException(AutomationError.IllegalTransition, "Exceptions require an individually parked item and compatible asset family.");
        var prior = Payloads<ExceptionalConfigurationDecision>(db, tx, "SELECT payload FROM decisions WHERE decision_id=$i ORDER BY version DESC LIMIT 1", "$i", decision.DecisionId).SingleOrDefault();
        if (prior is null && (decision.Version != 1 || decision.State != ExceptionalDecisionState.Proposed)) throw AutomationValidation.Invalid("A decision starts as a version-1 proposal.");
        if (prior is not null)
        {
            if (decision.Version != prior.Version + 1 || !(prior.State == ExceptionalDecisionState.Proposed && decision.State is ExceptionalDecisionState.Approved or ExceptionalDecisionState.Revoked || prior.State == ExceptionalDecisionState.Approved && decision.State == ExceptionalDecisionState.Revoked) ||
                AutomationJson.Encode(prior with { Version = decision.Version, State = decision.State, Actor = decision.Actor, Utc = decision.Utc }) != AutomationJson.Encode(decision))
                throw new AutomationException(AutomationError.IllegalTransition, "Configuration and preview are immutable; editing requires a new proposal identity.");
        }
        Execute(db, tx, "INSERT INTO decisions VALUES($d,$v,$i,$a,$p)", "$d", decision.DecisionId, "$v", decision.Version, "$i", item.Id.Value, "$a", attempt.Id.Value, "$p", AutomationJson.Encode(decision));
        RecordEvidenceEvent(db, tx, item, QueueStage.Review); return decision;
    });
    public IReadOnlyList<ExceptionalConfigurationDecision> Decisions(QueueItemId id) => Read(db => { Item(db, null, id); return Payloads<ExceptionalConfigurationDecision>(db, null, "SELECT payload FROM decisions WHERE item_id=$i ORDER BY decision_id,version", "$i", id.Value); });
    private static void RecordEvidenceEvent(SqliteConnection db, SqliteTransaction tx, QueueItem item, QueueStage stage)
    {
        var updated = item with { Revision = item.Revision + 1, UpdatedUtc = DateTimeOffset.UtcNow }; SaveItem(db, tx, updated, item.Revision);
        Event(db, tx, updated, stage, QueueEventCode.EvidenceRecorded, QueueEventResult.Recorded);
    }
    private WorkflowAttempt BindAttempt(SqliteConnection db, SqliteTransaction? tx, QueueItemId itemId, WorkflowAttemptId attemptId, UniverseId universe)
    {
        AutomationValidation.Scope(UniverseId, universe); var attempt = Attempt(db, tx, attemptId);
        if (attempt.ItemId != itemId || attempt.Status != AttemptStatus.Active || Item(db, tx, itemId).ActiveAttempt != attemptId)
            throw AutomationValidation.Invalid("Evidence must belong to the same active workflow attempt."); return attempt;
    }

    private void ValidateData(SqliteConnection db)
    {
        long ownerEpoch;
        using (var metadata = Command(db, null, "SELECT schema_version,universe,roots,owner_epoch FROM queue_metadata WHERE id=1"))
        using (var rows = metadata.ExecuteReader())
        {
            if (!rows.Read() || rows.GetInt64(0) != 1 || rows.GetString(1) != UniverseId.Value || rows.GetInt64(3) < 0 ||
                !AutomationValidation.SameRoots(_roots, AutomationJson.Decode<UniverseStorageConfig>(rows.GetString(2)))) throw AutomationValidation.Corrupt("Queue identity, roots or metadata is inconsistent.");
            ownerEpoch = rows.GetInt64(3);
            if (rows.Read()) throw AutomationValidation.Corrupt("Duplicate metadata.");
        }
        var policies = new Dictionary<string, StoredAutomationPolicy>();
        using (var cmd = Command(db, null, "SELECT hash,policy_id,version,payload FROM policies"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<StoredAutomationPolicy>(rows.GetString(3)); AutomationValidation.Policy(value.Definition, _roots);
            AutomationValidation.Defined(value.State);
            if (value.State == AutomationPolicyState.Enabled || value.Reference != value.Definition.Reference || rows.GetString(0) != value.Reference.Hash.Hex || rows.GetString(1) != value.Reference.PolicyId || rows.GetInt32(2) != value.Reference.Version || value.Revision < 0) throw AutomationValidation.Corrupt("Policy payload or phase-1 activation state is inconsistent.");
            if (value.Authorization is { } a) { AutomationValidation.Scope(UniverseId, a.UniverseId); AutomationValidation.Utc(a.AuthorizedUtc); AutomationValidation.Text(a.Actor); AutomationValidation.Text(a.AuthorizationId); if (a.Policy != value.Reference) throw AutomationValidation.Corrupt("Authorization hash/scope mismatch."); }
            policies.Add(value.Reference.Hash.Hex, value);
        }
        foreach (var policy in policies.Values)
        {
            var history = Payloads<StoredAutomationPolicy>(db, null, "SELECT payload FROM policy_history WHERE policy_hash=$h ORDER BY revision", "$h", policy.Reference.Hash.Hex);
            if (history.Count != policy.Revision + 1 || AutomationJson.Encode(history.Last()) != AutomationJson.Encode(policy)) throw AutomationValidation.Corrupt("Policy state/history diverged.");
            StoredAutomationPolicy? previous = null;
            foreach (var entry in history)
            {
                AutomationValidation.Defined(entry.State);
                if (entry.Reference != policy.Reference || entry.Definition.Reference != policy.Reference || entry.Revision != (previous?.Revision ?? -1) + 1 || entry.State == AutomationPolicyState.Enabled ||
                    previous is null && (entry.State != AutomationPolicyState.Disabled || entry.Authorization is not null) ||
                    previous is not null && (previous.State == AutomationPolicyState.Revoked || previous.Authorization is not null && entry.Authorization != previous.Authorization))
                    throw AutomationValidation.Corrupt("Policy history contains incompatible authority/state changes.");
                previous = entry;
            }
        }
        CurrentPolicy(db, null);
        var batches = new Dictionary<string, QueueBatch>();
        using (var cmd = Command(db, null, "SELECT id,payload FROM batches"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<QueueBatch>(rows.GetString(1)); AutomationValidation.Scope(UniverseId, value.UniverseId); AutomationValidation.Utc(value.CreatedUtc);
            if (rows.GetString(0) != value.Id.Value || value.ClosedUtc is { } closed && (closed.Offset != TimeSpan.Zero || closed < value.CreatedUtc)) throw AutomationValidation.Corrupt("Batch metadata mismatch."); batches.Add(value.Id.Value, value);
        }
        var items = new Dictionary<string, QueueItem>();
        using (var cmd = Command(db, null, "SELECT id,zip_hash,state,revision,batch_id,policy_hash,payload FROM items"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<QueueItem>(rows.GetString(6)); AutomationValidation.Scope(UniverseId, value.UniverseId); AutomationValidation.Zip(value.Zip); AutomationValidation.Defined(value.State); AutomationValidation.Defined(value.Incident); AutomationValidation.Utc(value.CreatedUtc); AutomationValidation.Utc(value.UpdatedUtc);
            if (rows.GetString(0) != value.Id.Value || rows.GetString(1) != value.Zip.Hash.Hex || rows.GetString(2) != value.State.ToString() || rows.GetInt64(3) != value.Revision || (rows.IsDBNull(4) ? null : rows.GetString(4)) != value.BatchId?.Value || rows.GetString(5) != value.Policy.Hash.Hex || !policies.TryGetValue(value.Policy.Hash.Hex, out var policy) || policy.Reference != value.Policy || value.Revision < 0 || value.AttemptCount < 0 || value.RetryCount < 0 || value.UpdatedUtc < value.CreatedUtc)
                throw AutomationValidation.Corrupt("Queue item metadata/policy mismatch.");
            if (!ProductionPaths.Same(Path.GetDirectoryName(value.SourcePath)!, _roots.InboxRoot) || !ProductionPaths.SafeSegment(Path.GetFileName(value.SourcePath)) || !string.Equals(Path.GetExtension(value.SourcePath), ".zip", StringComparison.OrdinalIgnoreCase)) throw AutomationValidation.Corrupt("Observation source scope mismatch.");
            if ((value.State == QueueState.Running) != (value.ClaimToken is not null && value.ClaimEpoch is > 0) || value.State != QueueState.Running && (value.ClaimToken is not null || value.ClaimEpoch is not null)) throw AutomationValidation.Corrupt("Running state and claim do not agree.");
            if (value.ClaimEpoch > ownerEpoch) throw AutomationValidation.Corrupt("Claim generation exceeds its recorded owner.");
            if (value.ClaimToken is { } token) AutomationValidation.Id("claim_" + token, "claim_");
            if ((value.State == QueueState.RetryScheduled) != (value.RetryAtUtc is not null)) throw AutomationValidation.Corrupt("Retry schedule mismatch.");
            if (value.RetryAtUtc is { } time) AutomationValidation.Utc(time);
            if (value.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource && value.Incident == QueueIncident.None) throw AutomationValidation.Corrupt("Missing incident reason.");
            if (value.State is not (QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource or QueueState.RetryScheduled) && value.Incident != QueueIncident.None) throw AutomationValidation.Corrupt("Unexpected incident reason.");
            items.Add(value.Id.Value, value);
        }
        var attempts = new Dictionary<string, WorkflowAttempt>();
        using (var cmd = Command(db, null, "SELECT id,item_id,job_id,number,status,payload FROM attempts ORDER BY number"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<WorkflowAttempt>(rows.GetString(5)); AutomationValidation.Scope(UniverseId, value.UniverseId); if (value.Profile is not null) { value.Profile.ValidateAndGetRule(); AutomationValidation.Scope(UniverseId, value.Profile.UniverseId); } AutomationValidation.Utc(value.CreatedUtc); AutomationValidation.Defined(value.Status); AutomationValidation.Relative(value.StagingRelativePath); AutomationValidation.Relative(value.ExtractionRelativePath);
            if (rows.GetString(0) != value.Id.Value || rows.GetString(1) != value.ItemId.Value || rows.GetString(2) != value.JobId.Value || rows.GetInt32(3) != value.Number || rows.GetString(4) != value.Status.ToString() || !items.TryGetValue(value.ItemId.Value, out var item) || value.Number > item.AttemptCount || value.Status == AttemptStatus.Active && item.ActiveAttempt != value.Id)
                throw AutomationValidation.Corrupt("Attempt identity/checkpoint mismatch.");
            if (value.ParentId is { } parent && (!attempts.TryGetValue(parent.Value, out var prior) || prior.ItemId != value.ItemId || prior.Status != AttemptStatus.Closed || prior.Number != value.Number - 1) || value.Number > 1 && value.ParentId is null || value.Number == 1 && value.ParentId is not null) throw AutomationValidation.Corrupt("Attempt lineage mismatch.");
            attempts.Add(value.Id.Value, value);
        }
        foreach (var item in items.Values)
        {
            if (item.AttemptCount != attempts.Values.Count(a => a.ItemId == item.Id) || item.ActiveAttempt is { } id && (!attempts.TryGetValue(id.Value, out var attempt) || attempt.Status != AttemptStatus.Active || attempt.ItemId != item.Id)) throw AutomationValidation.Corrupt("Item/attempt relationship mismatch.");
        }
        var reservations = new Dictionary<string, AssetReservation>();
        using (var cmd = Command(db, null, "SELECT asset_id,item_id,attempt_id,content_hash,payload FROM reservations"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<AssetReservation>(rows.GetString(4)); AutomationValidation.Scope(UniverseId, value.AssetKey.UniverseId); AutomationValidation.Utc(value.CreatedUtc);
            if (rows.GetString(0) != value.AssetKey.AssetId || rows.GetString(1) != value.ItemId.Value || rows.GetString(2) != value.AttemptId.Value || rows.GetString(3) != value.ContentHash.Hex || !attempts.TryGetValue(value.AttemptId.Value, out var attempt) || attempt.Status != AttemptStatus.Active || attempt.Profile is null || attempt.ItemId != value.ItemId || attempt.JobId != value.JobId) throw AutomationValidation.Corrupt("Reservation ownership mismatch."); reservations.Add(value.ItemId.Value, value);
        }
        foreach (var item in items.Values.Where(i => i.State == QueueState.Running)) if (item.ActiveAttempt is null || !reservations.ContainsKey(item.Id.Value)) throw AutomationValidation.Corrupt("Productive claim lacks reserved identity and attempt.");
        using (var cmd = Command(db, null, "SELECT item_id,source_path,batch_id,payload FROM observations"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<QueueObservation>(rows.GetString(3)); AutomationValidation.Utc(value.ObservedUtc);
            if (rows.GetString(0) != value.ItemId.Value || rows.GetString(1) != value.SourcePath || (rows.IsDBNull(2) ? null : rows.GetString(2)) != value.BatchId?.Value || !items.ContainsKey(value.ItemId.Value) || !ProductionPaths.Same(Path.GetDirectoryName(value.SourcePath)!, _roots.InboxRoot) || !string.Equals(Path.GetExtension(value.SourcePath), ".zip", StringComparison.OrdinalIgnoreCase)) throw AutomationValidation.Corrupt("Observation mismatch.");
        }
        var lastEvents = new Dictionary<string, QueueEvent>();
        using (var cmd = Command(db, null, "SELECT item_id,sequence,payload FROM events ORDER BY item_id,sequence"))
        using (var rows = cmd.ExecuteReader()) while (rows.Read())
        {
            var value = AutomationJson.Decode<QueueEvent>(rows.GetString(2)); AutomationValidation.Utc(value.Utc); AutomationValidation.Defined(value.Code); AutomationValidation.Defined(value.Result); AutomationValidation.Defined(value.Stage); AutomationValidation.Defined(value.State); AutomationValidation.Defined(value.Incident);
            lastEvents.TryGetValue(value.ItemId.Value, out var prior);
            if (rows.GetString(0) != value.ItemId.Value || rows.GetInt64(1) != value.Sequence || value.Sequence != (prior?.Sequence ?? 0) + 1 || value.AttemptId is { } aid && (!attempts.TryGetValue(aid.Value, out var a) || a.ItemId != value.ItemId)) throw AutomationValidation.Corrupt("Event sequence or attempt binding mismatch.");
            if (prior is null && (value.Code != QueueEventCode.Observed || value.State != QueueState.Observed) ||
                prior is not null && value.State != prior.State && !QueueTransitions.CanTransition(prior.State, value.State) ||
                prior is not null && value.State == prior.State && value.Code is QueueEventCode.Transitioned or QueueEventCode.Claimed or QueueEventCode.ClaimInterrupted)
                throw AutomationValidation.Corrupt("Event history contains illegal queue transitions.");
            lastEvents[value.ItemId.Value] = value;
        }
        foreach (var item in items.Values) if (!lastEvents.TryGetValue(item.Id.Value, out var last) || last.State != item.State || last.Incident != item.Incident || last.Sequence != item.Revision + 1) throw AutomationValidation.Corrupt("Queue transition/event ledger diverged.");
        ValidateEvidenceRows(db, "lineage", items, attempts);
        ValidateEvidenceRows(db, "decisions", items, attempts);
    }
    private void ValidateEvidenceRows(SqliteConnection db, string table, Dictionary<string, QueueItem> items, Dictionary<string, WorkflowAttempt> attempts)
    {
        var identity = table == "lineage" ? "operation_id" : "decision_id";
        using var cmd = Command(db, null, $"SELECT {identity},version,item_id,attempt_id,payload FROM {table} ORDER BY {identity},version"); using var rows = cmd.ExecuteReader();
        var versions = new Dictionary<string, int>();
        var previousLineage = new Dictionary<string, NormalizationLineage>();
        var previousDecisions = new Dictionary<string, ExceptionalConfigurationDecision>();
        while (rows.Read())
        {
            string id; int version; QueueItemId item; WorkflowAttemptId attempt; UniverseId universe;
            if (table == "lineage")
            {
                var value = AutomationJson.Decode<NormalizationLineage>(rows.GetString(4)); AutomationValidation.Lineage(value);
                if (previousLineage.TryGetValue(value.OperationId, out var old))
                {
                    if (old.State != NormalizationOperationState.Proposed || value.State == NormalizationOperationState.Proposed || AutomationJson.Encode(old with { Version = value.Version, State = value.State, DerivedPng = value.DerivedPng, CandidateZip = value.CandidateZip, Geometry = value.Geometry, Utc = value.Utc }) != AutomationJson.Encode(value)) throw AutomationValidation.Corrupt("Lineage history has incompatible changes.");
                }
                else if (value.State != NormalizationOperationState.Proposed) throw AutomationValidation.Corrupt("Lineage must start as a proposal.");
                previousLineage[value.OperationId] = value;
                (id, version, item, attempt, universe) = (value.OperationId, value.Version, value.ItemId, value.AttemptId, value.UniverseId);
                if (!attempts.TryGetValue(attempt.Value, out var a) || a.JobId != value.JobId || value.OriginalZip.Hash != items[item.Value].Zip.Hash || value.OriginalZip.SizeBytes != items[item.Value].Zip.SizeBytes) throw AutomationValidation.Corrupt("Lineage identity mismatch.");
                var rule = (a.Profile ?? throw AutomationValidation.Corrupt("Lineage has no resolved rule.")).ValidateAndGetRule();
                if (rule.AssetType != value.Parameters.AssetType || rule.ProductionProfile != value.Parameters.ProductionProfile || !value.Parameters.Ratio.Matches(rule.Conversion!.OutputWidth, rule.Conversion.OutputHeight)) throw AutomationValidation.Corrupt("Lineage rule parameters mismatch.");
                if (value.Authority.Policy is { } p)
                {
                    var policy = Policy(db, null, p);
                    if (items[item.Value].Policy != p || policy.Authorization is not { } grant || grant.AuthorizationId != value.Authority.AuthorizationId || grant.Actor != value.Authority.Actor || grant.AuthorizedUtc != value.Authority.Utc || !policy.Definition.Normalization.Any(n => AutomationJson.Encode(n) == AutomationJson.Encode(value.Parameters))) throw AutomationValidation.Corrupt("Lineage policy authority mismatch.");
                }
            }
            else
            {
                var value = AutomationJson.Decode<ExceptionalConfigurationDecision>(rows.GetString(4)); AutomationValidation.Decision(value);
                if (previousDecisions.TryGetValue(value.DecisionId, out var old))
                {
                    if (!(old.State == ExceptionalDecisionState.Proposed && value.State is ExceptionalDecisionState.Approved or ExceptionalDecisionState.Revoked || old.State == ExceptionalDecisionState.Approved && value.State == ExceptionalDecisionState.Revoked) || AutomationJson.Encode(old with { Version = value.Version, State = value.State, Actor = value.Actor, Utc = value.Utc }) != AutomationJson.Encode(value)) throw AutomationValidation.Corrupt("Exceptional decision history has incompatible changes.");
                }
                else if (value.State != ExceptionalDecisionState.Proposed) throw AutomationValidation.Corrupt("Decision must start as a proposal.");
                previousDecisions[value.DecisionId] = value;
                (id, version, item, attempt, universe) = (value.DecisionId, value.Version, value.ItemId, value.AttemptId, value.UniverseId);
            }
            AutomationValidation.Scope(UniverseId, universe);
            if (rows.GetString(0) != id || rows.GetInt32(1) != version || rows.GetString(2) != item.Value || rows.GetString(3) != attempt.Value || !items.ContainsKey(item.Value) || !attempts.TryGetValue(attempt.Value, out var bound) || bound.ItemId != item || version != versions.GetValueOrDefault(id) + 1) throw AutomationValidation.Corrupt("Evidence version or workflow binding mismatch.");
            versions[id] = version;
        }
    }
}
