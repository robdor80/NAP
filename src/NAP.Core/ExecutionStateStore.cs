using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed record ExecutionBinding(int Version, QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId,
    UniverseId UniverseId, UniverseStorageConfig Roots, PolicyReference Policy, Sha256Digest SnapshotHash,
    Sha256Digest AuditReceiptHash, Sha256Digest GrantHash, string AuthorizationId);
public sealed record ExecutionRecord(ExecutionBinding Binding, long Revision, IReadOnlyList<string> Intents,
    IReadOnlyList<string> Receipts, bool Completed, string? Diagnostic);

/// <summary>Independent execution ledger: immutable binding, CAS, chained history, no productive IO inside transactions.</summary>
public sealed class ExecutionStateStore
{
    private readonly UniverseStorageConfig _roots;
    private static readonly string[] Schema =
    [
        "CREATE TABLE metadata(id INTEGER PRIMARY KEY CHECK(id=1), roots TEXT NOT NULL)",
        "CREATE TABLE workflows(item TEXT PRIMARY KEY, attempt TEXT NOT NULL UNIQUE, job TEXT NOT NULL UNIQUE, revision INTEGER NOT NULL, payload TEXT NOT NULL, hash TEXT NOT NULL)",
        "CREATE TABLE history(item TEXT NOT NULL REFERENCES workflows(item), revision INTEGER NOT NULL, payload TEXT NOT NULL, hash TEXT NOT NULL, previous_hash TEXT, PRIMARY KEY(item,revision))"
    ];
    public ExecutionStateStore(UniverseStorageConfig roots) { LocalUniverseSettingsStore.ValidateStructure([roots]); _roots = roots; }
    public string DatabasePath => Path.Combine(_roots.StateRoot, "automation", "execution", "ExecutionLedger.db");
    internal Action<string>? BeforeCommit { get; set; }

    public ExecutionRecord? Get(QueueItemId item) => Access(false, null, (db, tx) => Load(db, tx, item));
    internal ExecutionRecord Save(AutomationQueueOwner owner, ExecutionRecord value, long? revision) => Access(true, owner, (db, tx) =>
    {
        Validate(value); var prior = Load(db, tx, value.Binding.ItemId);
        if (prior?.Revision != revision) throw new AutomationException(AutomationError.RevisionConflict, "Execution CAS revision changed.");
        if (prior is not null) Continuity(prior, value);
        var next = value with { Revision = (revision ?? -1) + 1 };
        var json = AutomationJson.Encode(next); var hash = AutomationJson.Hash(next).Hex;
        if (next.Revision > 1024 || json.Length > 2 * 1024 * 1024) throw AutomationValidation.Invalid("Execution evidence exceeds bounds.");
        if (prior is null) Execute(db, tx, "INSERT INTO workflows VALUES($i,$a,$j,$r,$p,$h)", "$i", next.Binding.ItemId.Value,
            "$a", next.Binding.AttemptId.Value, "$j", next.Binding.JobId.Value, "$r", next.Revision, "$p", json, "$h", hash);
        else if (Execute(db, tx, "UPDATE workflows SET revision=$r,payload=$p,hash=$h WHERE item=$i AND revision=$old",
            "$r", next.Revision, "$p", json, "$h", hash, "$i", next.Binding.ItemId.Value, "$old", revision) != 1)
            throw new AutomationException(AutomationError.RevisionConflict, "Execution compare-and-swap failed.");
        Execute(db, tx, "INSERT INTO history VALUES($i,$r,$p,$h,$b)", "$i", next.Binding.ItemId.Value, "$r", next.Revision,
            "$p", json, "$h", hash, "$b", prior is null ? null : AutomationJson.Hash(prior).Hex);
        return next;
    });

    private T Access<T>(bool write, AutomationQueueOwner? owner, Func<SqliteConnection, SqliteTransaction?, T> action)
    {
        if (write) owner!.Require(_roots.StateRoot);
        return AutomationQueueBoundary.Guard(() =>
        {
            using var mutex = ExecutionMutex.Acquire("ExecutionStateIO", _roots.StateRoot);
            using var db = Open(write); using var tx = write ? db.BeginTransaction() : null;
            var result = action(db, tx); if (write) { BeforeCommit?.Invoke("before-commit"); tx!.Commit(); } return result;
        }, !write);
    }
    private SqliteConnection Open(bool initialize)
    {
        AutomationQueueBoundary.Paths(_roots, false); var dir = Path.GetDirectoryName(DatabasePath)!;
        if (!ProductionPaths.FileExists(DatabasePath, NapIssueCodes.PackageStructureInvalid))
        {
            if (!initialize) throw new AutomationException(AutomationError.Missing, "Execution ledger is not initialized.");
            InboxAdmissionEvidence.EnsureParents(dir, Path.Combine(_roots.StateRoot, "automation"));
            // An unknown initialization residue is retained, never promoted.
            if (Directory.EnumerateFileSystemEntries(dir).Any()) throw AutomationValidation.Corrupt("Ambiguous execution ledger initialization.");
            var temp = Path.Combine(dir, ".pending_" + Guid.NewGuid().ToString("N") + ".db");
            using (var initial = Connect(temp, SqliteOpenMode.ReadWriteCreate))
            {
                using var tx = initial.BeginTransaction(); foreach (var sql in Schema) Execute(initial, tx, sql);
                Execute(initial, tx, "PRAGMA user_version=1");
                Execute(initial, tx, "INSERT INTO metadata VALUES(1,$p)", "$p", AutomationJson.Encode(_roots)); tx.Commit();
            }
            using (var file = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) file.Flush(true);
            File.Move(temp, DatabasePath, false);
        }
        InboxAdmissionEvidence.DirectoryBoundary(dir);
        if (Directory.EnumerateFileSystemEntries(dir).Any(p => !ProductionPaths.Same(p, DatabasePath)))
            throw AutomationValidation.Corrupt("Unexpected execution ledger sidecar requires review.");
        var db = Connect(DatabasePath, SqliteOpenMode.ReadWrite);
        try { AutomationQueueBoundary.Guard(() => { Check(db); return 0; }, true); return db; }
        catch { db.Dispose(); throw; }
    }
    private static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 2 }.ToString());
        db.Open(); Execute(db, null, "PRAGMA foreign_keys=ON"); Execute(db, null, "PRAGMA journal_mode=DELETE");
        Execute(db, null, "PRAGMA synchronous=FULL"); return db;
    }
    private void Check(SqliteConnection db)
    {
        if (Convert.ToInt32(Scalar(db, null, "PRAGMA user_version")) != 1 || (string?)Scalar(db, null, "PRAGMA quick_check") != "ok" ||
            Convert.ToInt32(Scalar(db, null, "SELECT count(*) FROM pragma_foreign_key_check")) != 0 ||
            (string?)Scalar(db, null, "SELECT roots FROM metadata WHERE id=1") != AutomationJson.Encode(_roots) ||
            Convert.ToInt32(Scalar(db, null, "SELECT count(*) FROM metadata")) != 1)
            throw AutomationValidation.Corrupt("Execution ledger metadata or integrity mismatch.");
        using (var cmd = Command(db, null, "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name"))
        using (var rows = cmd.ExecuteReader())
        {
            var actual = new List<string>(); while (rows.Read()) actual.Add(rows.GetString(0));
            if (!actual.Order(StringComparer.Ordinal).SequenceEqual(Schema.Order(StringComparer.Ordinal))) throw AutomationValidation.Corrupt("Execution schema changed.");
        }
        using var all = Command(db, null, "SELECT item,attempt,job,revision,payload,hash FROM workflows"); using var workflows = all.ExecuteReader(); int count = 0;
        while (workflows.Read())
        {
            if (++count > 100000) throw AutomationValidation.Corrupt("Execution ledger inventory exceeds bounds.");
            var record = AutomationJson.Decode<ExecutionRecord>(workflows.GetString(4)); Validate(record);
            if (record.Binding.ItemId.Value != workflows.GetString(0) || record.Binding.AttemptId.Value != workflows.GetString(1) ||
                record.Binding.JobId.Value != workflows.GetString(2) || record.Revision != workflows.GetInt64(3) ||
                AutomationJson.Hash(record).Hex != workflows.GetString(5)) throw AutomationValidation.Corrupt("Execution indexed identity mismatch.");
            using var cmd = Command(db, null, "SELECT revision,payload,hash,previous_hash FROM history WHERE item=$i ORDER BY revision", "$i", record.Binding.ItemId.Value);
            using var rows = cmd.ExecuteReader(); ExecutionRecord? previous = null; long revision = 0;
            while (rows.Read())
            {
                if (revision > 1024 || rows.GetString(1).Length > 2 * 1024 * 1024) throw AutomationValidation.Corrupt("Execution history exceeds bounds.");
                var entry = AutomationJson.Decode<ExecutionRecord>(rows.GetString(1)); Validate(entry);
                if (rows.GetInt64(0) != revision || entry.Revision != revision++ || AutomationJson.Hash(entry).Hex != rows.GetString(2) ||
                    (rows.IsDBNull(3) ? null : rows.GetString(3)) != (previous is null ? null : AutomationJson.Hash(previous).Hex))
                    throw AutomationValidation.Corrupt("Execution history hash or sequence mismatch.");
                if (previous is not null) Continuity(previous, entry); previous = entry;
            }
            if (previous is null || AutomationJson.Encode(previous) != AutomationJson.Encode(record)) throw AutomationValidation.Corrupt("Execution history differs from current row.");
        }
    }
    private void Validate(ExecutionRecord value)
    {
        var b = value.Binding;
        if (b.Version != 1 || b.UniverseId != _roots.UniverseId || !AutomationValidation.SameRoots(_roots, b.Roots) ||
            value.Revision < 0 || value.Revision > 1024 || value.Intents.Count > 256 || value.Receipts.Count > 256 ||
            value.Intents.Distinct(StringComparer.Ordinal).Count() != value.Intents.Count || value.Receipts.Distinct(StringComparer.Ordinal).Count() != value.Receipts.Count ||
            value.Completed && !value.Receipts.Contains("catalog-verified")) throw AutomationValidation.Invalid("Execution record scope or checkpoint invalid.");
        ArgumentNullException.ThrowIfNull(b.ItemId); ArgumentNullException.ThrowIfNull(b.AttemptId); ArgumentNullException.ThrowIfNull(b.JobId);
        ArgumentNullException.ThrowIfNull(b.SnapshotHash); ArgumentNullException.ThrowIfNull(b.AuditReceiptHash); ArgumentNullException.ThrowIfNull(b.GrantHash);
        AutomationValidation.Text(b.AuthorizationId); AutomationValidation.Reference(b.Policy);
        foreach (var entry in value.Intents.Concat(value.Receipts)) AutomationValidation.Text(entry);
        if (value.Diagnostic is not null) AutomationValidation.Text(value.Diagnostic);
    }
    private static void Continuity(ExecutionRecord prior, ExecutionRecord next)
    {
        if (AutomationJson.Encode(prior.Binding) != AutomationJson.Encode(next.Binding) || prior.Completed && !next.Completed ||
            !prior.Intents.SequenceEqual(next.Intents.Take(prior.Intents.Count)) || !prior.Receipts.SequenceEqual(next.Receipts.Take(prior.Receipts.Count)))
            throw AutomationValidation.Invalid("Execution binding and recorded effects are immutable.");
    }
    private static ExecutionRecord? Load(SqliteConnection db, SqliteTransaction? tx, QueueItemId item)
    {
        var json = (string?)Scalar(db, tx, "SELECT payload FROM workflows WHERE item=$i", "$i", item.Value);
        return json is null ? null : AutomationJson.Decode<ExecutionRecord>(json);
    }
}
