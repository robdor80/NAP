using Microsoft.Data.Sqlite;

namespace NAP.Core;

/// <summary>Additive workflow sidecar schema, independent of the unchanged queue schema and journals v1.
/// SQLite anchors filesystem receipts; append-only history makes interruptions and CAS explicit.</summary>
public sealed class PreparationStateStore
{
    private readonly UniverseStorageConfig _roots;
    private static readonly string[] Schema =
    [
        "CREATE TABLE metadata(id INTEGER PRIMARY KEY CHECK(id=1), roots TEXT NOT NULL)",
        "CREATE TABLE workflows(item TEXT PRIMARY KEY, attempt TEXT NOT NULL UNIQUE, job TEXT NOT NULL UNIQUE, revision INTEGER NOT NULL CHECK(revision>=0), payload TEXT NOT NULL, hash TEXT NOT NULL)",
        "CREATE TABLE history(item TEXT NOT NULL REFERENCES workflows(item), revision INTEGER NOT NULL, payload TEXT NOT NULL, hash TEXT NOT NULL, previous_hash TEXT, PRIMARY KEY(item,revision))",
        "CREATE TABLE grants(policy TEXT PRIMARY KEY, payload TEXT NOT NULL, hash TEXT NOT NULL)",
        "CREATE TABLE previews(decision TEXT PRIMARY KEY, payload TEXT NOT NULL, hash TEXT NOT NULL)"
    ];
    public PreparationStateStore(UniverseStorageConfig roots)
    { LocalUniverseSettingsStore.ValidateStructure([roots]); _roots = roots; }
    public string DatabasePath => Path.Combine(_roots.StateRoot, "automation", "preparation", "PreparationLedger.db");
    internal Action<string>? BeforeCommit { get; set; }
    public PreparationRecord? Get(QueueItemId item) => Read(db => ReadRecord(db, null, item));
    public IReadOnlyList<PreparationRecord> List() => Read(db => All(db));
    public PreparationOperationalGrant? Grant(PolicyReference policy) => Read(db =>
        Payload<PreparationOperationalGrant>(db, null, "SELECT payload FROM grants WHERE policy=$i", policy.Hash.Hex));
    public ExceptionalPreparationPreview? Preview(string decisionId) => Read(db =>
        Payload<ExceptionalPreparationPreview>(db, null, "SELECT payload FROM previews WHERE decision=$i", decisionId));
    public PreparationRecord Save(AutomationQueueOwner owner, PreparationRecord value, long? expectedRevision)
        => Write(owner, (db, tx) =>
        {
            Validate(value); var prior = ReadRecord(db, tx, value.ItemId);
            if (prior?.Revision != expectedRevision) throw new AutomationException(AutomationError.RevisionConflict, "Preparation revision changed.");
            if (prior is not null && !SameIdentity(prior, value))
                throw AutomationValidation.Invalid("Preparation identity and original scope are immutable.");
            if (prior is not null && (value.Checkpoint < prior.Checkpoint ||
                !UnchangedWhenBound(prior.Originals, value.Originals) || !UnchangedWhenBound(prior.Candidate, value.Candidate) ||
                !UnchangedWhenBound(prior.Snapshot, value.Snapshot) || !UnchangedWhenBound(prior.SemanticHash, value.SemanticHash) ||
                !UnchangedWhenBound(prior.ExpectedAuditReceipt, value.ExpectedAuditReceipt) || value.Calls.Count < prior.Calls.Count))
                throw AutomationValidation.Invalid("Durable intentions, frozen facts and audit history cannot be replaced.");
            var next = value with { Revision = (expectedRevision ?? -1) + 1, UpdatedUtc = DateTimeOffset.UtcNow };
            var json = AutomationJson.Encode(next); var hash = AutomationJson.Hash(next);
            if (next.Revision > 1024 || json.Length > 2 * 1024 * 1024) throw AutomationValidation.Invalid("Preparation history or payload exceeds its bounded format.");
            if (prior is null) Execute(db, tx, "INSERT INTO workflows VALUES($i,$a,$j,$r,$p,$h)",
                "$i", next.ItemId.Value, "$a", next.AttemptId.Value, "$j", next.JobId.Value, "$r", next.Revision, "$p", json, "$h", hash.Hex);
            else if (Execute(db, tx, "UPDATE workflows SET revision=$r,payload=$p,hash=$h WHERE item=$i AND revision=$v",
                "$r", next.Revision, "$p", json, "$h", hash.Hex, "$i", next.ItemId.Value, "$v", expectedRevision!.Value) != 1)
                throw new AutomationException(AutomationError.RevisionConflict, "Preparation compare-and-swap failed.");
            Execute(db, tx, "INSERT INTO history VALUES($i,$r,$p,$h,$b)", "$i", next.ItemId.Value, "$r", next.Revision,
                "$p", json, "$h", hash.Hex, "$b", prior is null ? null : AutomationJson.Hash(prior).Hex);
            return next;
        });
    public void RegisterGrant(AutomationQueueOwner owner, StoredAutomationPolicy policy, PreparationOperationalGrant grant)
    {
        AutomationValidation.Scope(_roots.UniverseId, policy.Definition.Roots.UniverseId);
        if (!AutomationValidation.SameRoots(_roots, policy.Definition.Roots))
            throw new AutomationException(AutomationError.PolicyMismatch, "Operational terms belong to different authorized roots.");
        ValidateGrant(grant);
        if (policy.Reference != grant.Policy || policy.Authorization is not { } authority || authority.AuthorizationId != grant.PolicyAuthorizationId ||
            grant.Actor != authority.Actor || policy.State is AutomationPolicyState.Revoked or AutomationPolicyState.Paused ||
            grant.RetryDelaySeconds.Count != policy.Definition.Audit.MaxAttempts - 1)
            throw new AutomationException(AutomationError.PolicyMismatch, "Operational grant must attest exact policy, actor and retry terms.");
        Write(owner, (db, tx) =>
        {
            var existing = Payload<PreparationOperationalGrant>(db, tx, "SELECT payload FROM grants WHERE policy=$i", grant.Policy.Hash.Hex);
            if (existing is not null)
            {
                if (existing.Hash != grant.Hash) throw new AutomationException(AutomationError.PolicyMismatch, "Operational terms are immutable; use a new policy version.");
                return 0;
            }
            Execute(db, tx, "INSERT INTO grants VALUES($i,$p,$h)", "$i", grant.Policy.Hash.Hex, "$p", AutomationJson.Encode(grant), "$h", grant.Hash.Hex); return 0;
        });
    }
    public void RegisterPreview(AutomationQueueOwner owner, ExceptionalPreparationPreview preview) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Scope(_roots.UniverseId, preview.UniverseId); preview.EffectiveRule.ValidateAndGetRule(); AutomationValidation.Normalization(preview.Parameters);
        AutomationValidation.Text(preview.DecisionId);
        var existing = Payload<ExceptionalPreparationPreview>(db, tx, "SELECT payload FROM previews WHERE decision=$i", preview.DecisionId);
        if (existing is not null) { if (existing.Hash != preview.Hash) throw AutomationValidation.Invalid("Exceptional preview is immutable."); return 0; }
        Execute(db, tx, "INSERT INTO previews VALUES($i,$p,$h)", "$i", preview.DecisionId, "$p", AutomationJson.Encode(preview), "$h", preview.Hash.Hex); return 0;
    });
    private T Read<T>(Func<SqliteConnection, T> operation) => AutomationQueueBoundary.Guard(() =>
    { using var lease = ExecutionMutex.Acquire("PreparationStateIO", _roots.StateRoot); using var db = Open(false); return operation(db); }, true);
    private T Write<T>(AutomationQueueOwner owner, Func<SqliteConnection, SqliteTransaction, T> operation)
    {
        owner.Require(_roots.StateRoot);
        return AutomationQueueBoundary.Guard(() =>
        {
            using var lease = ExecutionMutex.Acquire("PreparationStateIO", _roots.StateRoot); using var db = Open(true);
            using var tx = db.BeginTransaction(); var result = operation(db, tx); BeforeCommit?.Invoke("before-commit"); tx.Commit(); return result;
        });
    }
    private SqliteConnection Open(bool initialize)
    {
        AutomationQueueBoundary.Paths(_roots, false);
        var dir = Path.GetDirectoryName(DatabasePath)!;
        if (!ProductionPaths.FileExists(DatabasePath, NapIssueCodes.PackageStructureInvalid))
        {
            if (!initialize) throw new AutomationException(AutomationError.Missing, "Preparation ledger has not been initialized.");
            InboxAdmissionEvidence.EnsureParents(dir, Path.Combine(_roots.StateRoot, "automation"));
            var temp = Path.Combine(dir, ".pending_" + Guid.NewGuid().ToString("N") + ".db");
            using (var initial = Connect(temp, SqliteOpenMode.ReadWriteCreate))
            {
                using var tx = initial.BeginTransaction();
                foreach (var sql in Schema) Execute(initial, tx, sql);
                Execute(initial, tx, "PRAGMA user_version=1");
                Execute(initial, tx, "INSERT INTO metadata VALUES(1,$p)", "$p", AutomationJson.Encode(_roots)); tx.Commit();
            }
            using (var file = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            File.Move(temp, DatabasePath, false);
        }
        InboxAdmissionEvidence.DirectoryBoundary(dir);
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (ProductionPaths.CheckPath(DatabasePath + suffix, NapIssueCodes.PackageStructureInvalid) is not null)
                throw AutomationValidation.Corrupt("Unexpected preparation SQLite sidecar needs review.");
        var db = Connect(DatabasePath, SqliteOpenMode.ReadWrite);
        try { return AutomationQueueBoundary.Guard(() => { Check(db); return db; }, true); } catch { db.Dispose(); throw; }
    }
    private static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            db.Open(); Execute(db, null, "PRAGMA foreign_keys=ON"); Execute(db, null, "PRAGMA journal_mode=DELETE");
            Execute(db, null, "PRAGMA synchronous=FULL"); Execute(db, null, "PRAGMA temp_store=MEMORY"); return db;
        }
        catch { db.Dispose(); throw; }
    }
    private void Check(SqliteConnection db)
    {
        if (Convert.ToInt32(Scalar(db, "PRAGMA user_version")) != 1 || (string?)Scalar(db, "PRAGMA quick_check") != "ok" ||
            Convert.ToInt32(Scalar(db, "SELECT count(*) FROM pragma_foreign_key_check")) != 0 ||
            (string?)Scalar(db, "SELECT roots FROM metadata WHERE id=1") != AutomationJson.Encode(_roots) ||
            Convert.ToInt32(Scalar(db, "SELECT count(*) FROM metadata")) != 1) throw AutomationValidation.Corrupt("Preparation metadata or SQLite integrity mismatch.");
        using (var cmd = Command(db, null, "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name"))
        using (var rows = cmd.ExecuteReader())
        {
            var actual = new List<string>(); while (rows.Read()) actual.Add(rows.GetString(0));
            if (!actual.Order(StringComparer.Ordinal).SequenceEqual(Schema.Order(StringComparer.Ordinal))) throw AutomationValidation.Corrupt("Preparation schema changed.");
        }
        foreach (var record in All(db))
        {
            Validate(record); Sha256Digest? previous = null; int revision = 0; PreparationRecord? last = null;
            using var cmd = Command(db, null, "SELECT revision,payload,hash,previous_hash FROM history WHERE item=$i ORDER BY revision", "$i", record.ItemId.Value);
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
            {
                if (revision > 1024) throw AutomationValidation.Corrupt("Preparation history exceeds bounds.");
                var value = AutomationJson.Decode<PreparationRecord>(rows.GetString(1)); Validate(value);
                var hash = AutomationJson.Hash(value);
                if (rows.GetInt64(0) != revision || value.Revision != revision++ || !SameIdentity(value, record) ||
                    rows.GetString(2) != hash.Hex || (rows.IsDBNull(3) ? null : rows.GetString(3)) != previous?.Hex)
                    throw AutomationValidation.Corrupt("Preparation history is inconsistent.");
                previous = hash; last = value;
            }
            if (last is null || AutomationJson.Encode(last) != AutomationJson.Encode(record)) throw AutomationValidation.Corrupt("Preparation binding has no exact history.");
        }
        foreach (var table in new[] { "grants", "previews" })
        {
            using var cmd = Command(db, null, "SELECT " + (table == "grants" ? "policy" : "decision") + ",payload,hash FROM " + table); using var rows = cmd.ExecuteReader();
            while (rows.Read())
            {
                if (table == "grants") { var value = AutomationJson.Decode<PreparationOperationalGrant>(rows.GetString(1)); ValidateGrant(value); if (value.Policy.Hash.Hex != rows.GetString(0) || value.Hash.Hex != rows.GetString(2)) throw AutomationValidation.Corrupt("Operational grant indexed identity or hash mismatch."); }
                else { var value = AutomationJson.Decode<ExceptionalPreparationPreview>(rows.GetString(1)); value.EffectiveRule.ValidateAndGetRule(); AutomationValidation.Scope(_roots.UniverseId, value.UniverseId); if (value.DecisionId != rows.GetString(0) || value.Hash.Hex != rows.GetString(2)) throw AutomationValidation.Corrupt("Preview indexed identity or hash mismatch."); }
            }
        }
    }
    private void Validate(PreparationRecord record)
    {
        AutomationValidation.Scope(_roots.UniverseId, record.UniverseId); AutomationValidation.Id(record.OperationId, "preparation_");
        ArgumentNullException.ThrowIfNull(record.ItemId); ArgumentNullException.ThrowIfNull(record.AttemptId); ArgumentNullException.ThrowIfNull(record.JobId);
        ArgumentNullException.ThrowIfNull(record.AdmissionHash); record.BaseProfile.ValidateAndGetRule(); record.EffectiveProfile.ValidateAndGetRule();
        AutomationValidation.Scope(_roots.UniverseId, record.BaseProfile.UniverseId); AutomationValidation.Scope(_roots.UniverseId, record.EffectiveProfile.UniverseId);
        AutomationValidation.Reference(record.Policy); AutomationValidation.Defined(record.Checkpoint); AutomationValidation.Defined(record.Code);
        AutomationValidation.Defined(record.AuditState); AutomationValidation.Utc(record.UpdatedUtc);
        if (record.Parameters is not null) AutomationValidation.Normalization(record.Parameters);
        if (record.Authority is not null) AutomationValidation.Authority(record.Authority);
        if (record.RetryAtUtc is { } due) AutomationValidation.Utc(due);
        if (record.Originals is null || record.Checkpoint > PreparationCheckpoint.Preserved &&
            (record.SemanticHash is null || record.MasterHash is null) || record.Checkpoint >= PreparationCheckpoint.PlanningIntent && record.Snapshot is null ||
            record.Checkpoint == PreparationCheckpoint.CandidateIntent && record.Candidate is null ||
            record.Candidate is not null && (record.Parameters is null || record.Authority is null || record.Geometry is null))
            throw AutomationValidation.Invalid("Preparation checkpoint lacks its prior immutable evidence.");
        if (record.Originals is not null) ValidateBundle(record.Originals, "automation/originals/" + record.ItemId.Value);
        if (record.Candidate is not null) ValidateBundle(record.Candidate, "automation/preparation/packages/" + record.ItemId.Value + "/" + record.OperationId);
        if (record.AuditResponse is not null) ValidateBundle(record.AuditResponse, "automation/preparation/audit/" + record.ItemId.Value + "/" + record.Calls.Last().RequestId);
        if (record.Revision < 0 || record.Calls is null || record.Calls.Count > 100 || record.Calls.Any(c => c is null)) throw AutomationValidation.Corrupt("Invalid preparation record.");
        if (record.Calls.Count > 0 && (record.RequestJson is null || record.RequestHash is null || record.AuditGrant is null ||
            record.Snapshot is null || record.Checkpoint != PreparationCheckpoint.Planned ||
            AutomationJson.HashBytes(System.Text.Encoding.UTF8.GetBytes(record.RequestJson)) != record.RequestHash))
            throw AutomationValidation.Invalid("An audit call requires its exact planned request and grant.");
        if (record.AuditState is DurableAuditState.ResponseReady or DurableAuditState.Pass or DurableAuditState.Warning or DurableAuditState.Fail)
        {
            var receipt = record.ExpectedAuditReceipt;
            if (record.AuditResponse is null || receipt is null || record.Calls.Count == 0 || receipt.SchemaVersion != 1 ||
                receipt.RequestId != record.Calls.Last().RequestId || receipt.ItemId != record.ItemId || receipt.AttemptId != record.AttemptId ||
                receipt.JobId != record.JobId || receipt.UniverseId != record.UniverseId || receipt.Policy != record.Policy ||
                receipt.SnapshotHash != record.Snapshot!.Hash || receipt.RequestHash != record.RequestHash || receipt.GrantHash != record.AuditGrant!.Hash)
                throw AutomationValidation.Invalid("Audit decision lacks its complete bound response scope.");
            receipt.ValidateReport();
        }
        for (var i = 0; i < record.Calls.Count; i++)
        {
            var call = record.Calls[i]; AutomationValidation.Id(call.RequestId, "audit_"); AutomationValidation.Utc(call.StartedUtc); AutomationValidation.Defined(call.State);
            if (call.Error is { } error) AutomationValidation.Defined(error);
            if (call.FinishedUtc is { } finished) AutomationValidation.Utc(finished);
            if (call.State == AuditCallState.Started ? call.Error is not null || call.FinishedUtc is not null :
                call.FinishedUtc is null || (call.State == AuditCallState.Response ? call.Error is not null : call.Error is null))
                throw AutomationValidation.Corrupt("Audit call outcome lacks its typed durable completion.");
            if (call.Number != i + 1) throw AutomationValidation.Corrupt("Audit call sequence mismatch.");
        }
        if (record.Snapshot is { } snapshot && (snapshot.ItemId != record.ItemId || snapshot.AttemptId != record.AttemptId || snapshot.JobId != record.JobId ||
            !AutomationValidation.SameRoots(_roots, snapshot.Roots) || snapshot.Policy != record.Policy || snapshot.AdmissionHash != record.AdmissionHash))
            throw AutomationValidation.Corrupt("Preparation snapshot scope mismatch.");
    }
    private static bool SameIdentity(PreparationRecord a, PreparationRecord b) => a.ItemId == b.ItemId && a.AttemptId == b.AttemptId &&
        a.JobId == b.JobId && a.OperationId == b.OperationId && a.UniverseId == b.UniverseId && a.AdmissionHash == b.AdmissionHash &&
        a.Policy == b.Policy && a.BaseProfile == b.BaseProfile;
    private static bool UnchangedWhenBound<T>(T? prior, T? next) where T : class => prior is null ||
        (next is not null && AutomationJson.Encode(prior) == AutomationJson.Encode(next));
    private void ValidateBundle(PreparationBundle bundle, string expectedPath)
    {
        if (bundle.RelativeDirectory != expectedPath || bundle.ReceiptHash is null || bundle.Files is null ||
            bundle.Files.Count is < 1 or > 10001 || bundle.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bundle.Files.Count)
            throw AutomationValidation.Invalid("Bundle inventory and private namespace are required.");
        var root = ProductionPaths.Resolve(_roots.StateRoot, expectedPath);
        foreach (var file in bundle.Files)
        {
            if (file.SizeBytes < 0 || file.Hash is null || file.RelativePath == "control.json") throw AutomationValidation.Invalid("Bundle file identity is invalid.");
            ProductionPaths.Resolve(root, file.RelativePath);
        }
    }
    private static void ValidateGrant(PreparationOperationalGrant grant)
    {
        AutomationValidation.Text(grant.GrantId); AutomationValidation.Positive(grant.Version); AutomationValidation.Reference(grant.Policy);
        AutomationValidation.Text(grant.PolicyAuthorizationId); AutomationValidation.Text(grant.Actor); AutomationValidation.Utc(grant.AuthorizedUtc);
        AutomationValidation.Text(grant.ProviderId); AutomationValidation.Text(grant.ModelId);
        if (grant.MaximumCostPerRequest < 0 || grant.RetryDelaySeconds is null || grant.RetryDelaySeconds.Count > 99 ||
            grant.RetryDelaySeconds.Any(s => s is < 0 or > 86400) || grant.CircuitFailureThreshold is < 1 or > 100 || grant.CircuitOpenSeconds is < 1 or > 86400)
            throw AutomationValidation.Invalid("Operational terms must contain bounded, explicit budgets and circuit limits.");
    }
    private static PreparationRecord? ReadRecord(SqliteConnection db, SqliteTransaction? tx, QueueItemId item)
        => Payload<PreparationRecord>(db, tx, "SELECT payload FROM workflows WHERE item=$i", item.Value);
    private static IReadOnlyList<PreparationRecord> All(SqliteConnection db)
    {
        using var cmd = Command(db, null, "SELECT item,attempt,job,revision,payload,hash FROM workflows ORDER BY item"); using var rows = cmd.ExecuteReader(); var all = new List<PreparationRecord>();
        while (rows.Read())
        {
            if (all.Count >= 100000) throw AutomationValidation.Corrupt("Preparation scope exceeds its inspection budget.");
            var value = AutomationJson.Decode<PreparationRecord>(rows.GetString(4));
            if (value.ItemId.Value != rows.GetString(0) || value.AttemptId.Value != rows.GetString(1) || value.JobId.Value != rows.GetString(2) || value.Revision != rows.GetInt64(3) || AutomationJson.Hash(value).Hex != rows.GetString(5))
                throw AutomationValidation.Corrupt("Preparation indexed fields or anchor hash mismatch.");
            all.Add(value);
        }
        return all;
    }
    private static T? Payload<T>(SqliteConnection db, SqliteTransaction? tx, string sql, string id) where T : class
    { using var cmd = Command(db, tx, sql, "$i", id); return cmd.ExecuteScalar() is string json ? AutomationJson.Decode<T>(json) : null; }
    private static object? Scalar(SqliteConnection db, string sql) { using var cmd = Command(db, null, sql); return cmd.ExecuteScalar(); }
    private static int Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] parameters)
    { using var cmd = Command(db, tx, sql, parameters); return cmd.ExecuteNonQuery(); }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] parameters)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        for (var i = 0; i < parameters.Length; i += 2) cmd.Parameters.AddWithValue((string)parameters[i]!, parameters[i + 1] ?? DBNull.Value); return cmd;
    }
}
