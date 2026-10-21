using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed record AutomationCapture(QueueBatchId BatchId, UniverseId UniverseId, string StartedSession,
    string LastSession, bool Closed, long Revision);
public sealed record AutomationBatchProjection(AutomationCapture Capture, QueueBatchSummary Summary);

/// <summary>Capture intent precedes queue cohort creation. No asset or workflow authority is stored here.</summary>
public sealed class AutomationRuntimeStateStore(UniverseContext context)
{
    private static readonly string[] Ddl =
    [
        "CREATE TABLE metadata(id INTEGER PRIMARY KEY CHECK(id=1), version INTEGER NOT NULL CHECK(version=1), binding TEXT NOT NULL)",
        "CREATE TABLE captures(id TEXT PRIMARY KEY, closed INTEGER NOT NULL CHECK(closed IN (0,1)), payload TEXT NOT NULL, hash TEXT NOT NULL)"
    ];
    public string DatabasePath => Path.Combine(context.Storage.StateRoot, "automation", "runtime", "RuntimeState.db");
    private string Binding => AutomationJson.Encode(new { context.Storage, Profile = AutomationHostControlStore.ProfileHash(context) });
    public QueueBatchId OpenCapture(AutomationQueueOwner owner, AutomationQueueStore queue, string session)
    {
        RequireQueue(queue); AutomationValidation.Id(session, "session_");
        var capture = Access(owner, true, db =>
        {
            var prior = ReadAll(db).SingleOrDefault(c => !c.Closed);
            if (prior is not null) return prior;
            var next = new AutomationCapture(QueueBatchId.Create(), context.Id, session, session, false, 0);
            Save(db, next, true); return next;
        });
        var batch = queue.CreateBatch(owner, capture.BatchId);
        if (batch.ClosedUtc is not null) { CloseCapture(owner, queue, capture.BatchId); return OpenCapture(owner, queue, session); }
        if (capture.LastSession != session) Access(owner, false, db => { Save(db, capture with { LastSession = session, Revision = capture.Revision + 1 }, false); return 0; });
        return capture.BatchId;
    }

    public void CloseCapture(AutomationQueueOwner owner, AutomationQueueStore queue, QueueBatchId id)
    {
        RequireQueue(queue); queue.CloseBatch(owner, id);
        Access(owner, false, db =>
        {
            var current = ReadAll(db).Single(c => c.BatchId == id);
            if (!current.Closed) Save(db, current with { Closed = true, Revision = current.Revision + 1 }, false);
            return 0;
        });
    }

    public IReadOnlyList<AutomationCapture> List(AutomationQueueOwner owner, int offset = 0, int limit = 100)
    {
        if (offset < 0 || limit is < 1 or > 1000) throw AutomationValidation.Invalid("Invalid capture page.");
        return Access(owner, true, db => ReadAll(db).Skip(offset).Take(limit).ToArray());
    }

    private void RequireQueue(AutomationQueueStore queue)
    { if (!AutomationValidation.SameRoots(context.Storage, queue.Storage)) throw new AutomationException(AutomationError.UniverseMismatch, "Capture queue differs."); }

    private static void Save(SqliteConnection db, AutomationCapture capture, bool insert)
    {
        using var tx = db.BeginTransaction(); var json = AutomationJson.Encode(capture); var hash = AutomationJson.Hash(capture).Hex;
        Execute(db, tx, insert ? "INSERT INTO captures VALUES($i,$c,$p,$h)" : "UPDATE captures SET closed=$c,payload=$p,hash=$h WHERE id=$i",
            "$i", capture.BatchId.Value, "$c", capture.Closed ? 1 : 0, "$p", json, "$h", hash); tx.Commit();
    }

    private IEnumerable<AutomationCapture> ReadAll(SqliteConnection db)
    {
        int count = 0, active = 0;
        using var cmd = Command(db, null, "SELECT id,closed,payload,hash FROM captures ORDER BY rowid"); using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            var capture = AutomationJson.Decode<AutomationCapture>(rows.GetString(2));
            AutomationValidation.Id(capture.StartedSession, "session_"); AutomationValidation.Id(capture.LastSession, "session_");
            if (capture.UniverseId != context.Id || capture.BatchId.Value != rows.GetString(0) || capture.Closed != (rows.GetInt32(1) == 1) ||
                capture.Revision < 0 || AutomationJson.Hash(capture).Hex != rows.GetString(3)) throw AutomationValidation.Corrupt("Capture binding changed.");
            if (++count > 100000 || !capture.Closed && ++active > 1) throw AutomationValidation.Corrupt("Capture inventory exceeds bounds.");
            yield return capture;
        }
    }

    private T Access<T>(AutomationQueueOwner owner, bool initialize, Func<SqliteConnection, T> operation) => AutomationQueueBoundary.Guard(() =>
    {
        owner.Require(context.Storage.StateRoot); AutomationQueueBoundary.Paths(context.Storage, false);
        var directory = Path.GetDirectoryName(DatabasePath)!;
        if (initialize) InboxAdmissionEvidence.EnsureParents(directory, context.Storage.StateRoot);
        InboxAdmissionEvidence.DirectoryBoundary(directory);
        using var lease = ExecutionMutex.Acquire("RuntimeStateIO", context.Storage.StateRoot);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            if (!ProductionPaths.Same(entry, DatabasePath)) throw AutomationValidation.Corrupt("Unknown runtime artifact requires review.");
        var exists = ProductionPaths.FileExists(DatabasePath, NapIssueCodes.CatalogInvalid);
        if (!exists && !initialize) throw new AutomationException(AutomationError.Missing, "Runtime ledger is absent.");
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false,
            Mode = exists ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate }.ToString());
        db.Open(); Execute(db, null, "PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE");
        if (!exists)
        {
            using var tx = db.BeginTransaction(); foreach (var sql in Ddl) Execute(db, tx, sql);
            Execute(db, tx, "PRAGMA user_version=1"); Execute(db, tx, "INSERT INTO metadata VALUES(1,1,$b)", "$b", Binding); tx.Commit();
        }
        if (Convert.ToInt64(Scalar(db, null, "PRAGMA user_version")) != 1 || (string?)Scalar(db, null, "PRAGMA quick_check") != "ok" ||
            (string?)Scalar(db, null, "SELECT binding FROM metadata WHERE id=1") != Binding) throw AutomationValidation.Corrupt("Runtime ledger is inconsistent.");
        using (var cmd = Command(db, null, "SELECT sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY sql"))
        using (var rows = cmd.ExecuteReader())
        {
            var actual = new List<string>(); while (rows.Read()) actual.Add(rows.GetString(0));
            if (!actual.SequenceEqual(Ddl.OrderBy(s => s, StringComparer.Ordinal))) throw AutomationValidation.Corrupt("Runtime schema changed.");
        }
        foreach (var capture in ReadAll(db)) { } return operation(db);
    }, true);
}
