using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class AutomationQueueSchema
{
    internal const int Version = 1;
    internal static readonly string[] Statements =
    [
        "CREATE TABLE queue_metadata(id INTEGER PRIMARY KEY CHECK(id=1), schema_version INTEGER NOT NULL CHECK(schema_version=1), universe TEXT NOT NULL, roots TEXT NOT NULL, owner_epoch INTEGER NOT NULL CHECK(owner_epoch>=0), current_policy TEXT NOT NULL REFERENCES policies(hash))",
        "CREATE TABLE policies(hash TEXT PRIMARY KEY, policy_id TEXT NOT NULL, version INTEGER NOT NULL CHECK(version>0), payload TEXT NOT NULL, UNIQUE(policy_id,version))",
        "CREATE TABLE policy_history(policy_hash TEXT NOT NULL REFERENCES policies(hash), revision INTEGER NOT NULL CHECK(revision>=0), payload TEXT NOT NULL, PRIMARY KEY(policy_hash,revision))",
        "CREATE TABLE batches(id TEXT PRIMARY KEY, payload TEXT NOT NULL)",
        "CREATE TABLE items(id TEXT PRIMARY KEY, zip_hash TEXT NOT NULL UNIQUE, state TEXT NOT NULL CHECK(state IN ('Observed','WaitingStable','Queued','Running','RetryScheduled','NeedsReview','Rejected','Completed','Duplicate','MissingSource')), revision INTEGER NOT NULL CHECK(revision>=0), batch_id TEXT REFERENCES batches(id), policy_hash TEXT NOT NULL REFERENCES policies(hash), payload TEXT NOT NULL)",
        "CREATE TABLE observations(item_id TEXT NOT NULL REFERENCES items(id), source_path TEXT NOT NULL, batch_id TEXT REFERENCES batches(id), payload TEXT NOT NULL)",
        "CREATE UNIQUE INDEX observation_identity ON observations(item_id,source_path,ifnull(batch_id,''))",
        "CREATE INDEX queue_page ON items(state,id)",
        "CREATE TABLE attempts(id TEXT PRIMARY KEY, item_id TEXT NOT NULL REFERENCES items(id), job_id TEXT NOT NULL UNIQUE, number INTEGER NOT NULL CHECK(number>0), status TEXT NOT NULL CHECK(status IN ('Active','Closed')), payload TEXT NOT NULL, UNIQUE(item_id,number))",
        "CREATE UNIQUE INDEX active_attempt ON attempts(item_id) WHERE status='Active'",
        "CREATE TABLE reservations(asset_id TEXT PRIMARY KEY, item_id TEXT NOT NULL UNIQUE REFERENCES items(id), attempt_id TEXT NOT NULL UNIQUE REFERENCES attempts(id), content_hash TEXT NOT NULL, payload TEXT NOT NULL)",
        "CREATE TABLE events(item_id TEXT NOT NULL REFERENCES items(id), sequence INTEGER NOT NULL CHECK(sequence>0), payload TEXT NOT NULL, PRIMARY KEY(item_id,sequence))",
        "CREATE TABLE lineage(operation_id TEXT NOT NULL, version INTEGER NOT NULL CHECK(version>0), item_id TEXT NOT NULL REFERENCES items(id), attempt_id TEXT NOT NULL REFERENCES attempts(id), payload TEXT NOT NULL, PRIMARY KEY(operation_id,version))",
        "CREATE TABLE decisions(decision_id TEXT NOT NULL, version INTEGER NOT NULL CHECK(version>0), item_id TEXT NOT NULL REFERENCES items(id), attempt_id TEXT NOT NULL REFERENCES attempts(id), payload TEXT NOT NULL, PRIMARY KEY(decision_id,version))"
    ];
    internal static void Create(SqliteConnection db)
    {
        foreach (var statement in Statements) Execute(db, null, statement);
        Execute(db, null, "PRAGMA user_version=1; PRAGMA journal_mode=DELETE");
    }
    internal static void Validate(SqliteConnection db)
    {
        if (Convert.ToInt64(Scalar(db, null, "PRAGMA user_version")) != Version) throw new AutomationException(AutomationError.UnsupportedSchema, "Unknown automation queue schema version.");
        using (var cmd = Command(db, null, "SELECT sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY sql"))
        using (var rows = cmd.ExecuteReader())
        {
            var actual = new List<string>(); while (rows.Read()) actual.Add(rows.GetString(0));
            if (!actual.SequenceEqual(Statements.OrderBy(s => s, StringComparer.Ordinal))) throw AutomationValidation.Corrupt("Automation queue schema differs from the supported contract.");
        }
        if (!string.Equals((string?)Scalar(db, null, "PRAGMA journal_mode"), "delete", StringComparison.OrdinalIgnoreCase)) throw AutomationValidation.Corrupt("Queue requires DELETE journaling.");
        if ((string?)Scalar(db, null, "PRAGMA quick_check") != "ok") throw AutomationValidation.Corrupt("Queue integrity check failed.");
        using var fk = Command(db, null, "PRAGMA foreign_key_check"); using var violations = fk.ExecuteReader();
        if (violations.Read()) throw AutomationValidation.Corrupt("Queue foreign keys are inconsistent.");
    }
    internal static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] args)
    {
        var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        for (var i = 0; i < args.Length; i += 2) cmd.Parameters.AddWithValue((string)args[i]!, args[i + 1] ?? DBNull.Value);
        return cmd;
    }
    internal static int Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] args)
    { using var cmd = Command(db, tx, sql, args); return cmd.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] args)
    { using var cmd = Command(db, tx, sql, args); var value = cmd.ExecuteScalar(); return value == DBNull.Value ? null : value; }
}

/// <summary>Exclusive, thread-affine lifetime ownership; no TTL can steal a live owner.</summary>
public sealed class AutomationQueueOwner : IDisposable
{
    private readonly ExecutionMutex _lease;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    internal string Root { get; }
    public long Epoch { get; }
    internal AutomationQueueOwner(ExecutionMutex lease, string root, long epoch) { _lease = lease; Root = root; Epoch = epoch; }
    internal void Require(string root)
    {
        if (_disposed || _thread != Environment.CurrentManagedThreadId || !ProductionPaths.Same(root, Root)) throw new AutomationException(AutomationError.InvalidClaim, "A live owner on its acquiring thread is required.");
        _lease.Require("AutomationOwner", Root);
    }
    public void Dispose() { if (_disposed) return; Require(Root); _lease.Dispose(); _disposed = true; }
}

internal static class AutomationQueueBoundary
{
    internal static string DirectoryPath(UniverseStorageConfig roots) => Path.Combine(roots.StateRoot, "automation");
    internal static string DatabasePath(UniverseStorageConfig roots) => Path.Combine(DirectoryPath(roots), "AutomationQueue.db");
    internal static void Paths(UniverseStorageConfig roots, bool create)
    {
        LocalUniverseSettingsStore.ValidateStructure([roots]);
        if (roots.WorkspaceRoot.StartsWith("\\\\", StringComparison.Ordinal) || roots.WorkspaceRoot.StartsWith("//", StringComparison.Ordinal) || new DriveInfo(roots.WorkspaceRoot).DriveType == DriveType.Network)
            throw new AutomationException(AutomationError.UnsafePath, "Queue must be on a local isolated workspace.");
        RequireDirectory(roots.WorkspaceRoot, false);
        ProductionPaths.CheckCasing(roots.WorkspaceRoot, "state"); RequireDirectory(roots.StateRoot, create);
        ProductionPaths.CheckCasing(roots.StateRoot, "automation"); RequireDirectory(DirectoryPath(roots), create);
        ProductionPaths.CheckCasing(DirectoryPath(roots), "AutomationQueue.db");
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            var path = DatabasePath(roots) + suffix; var a = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid);
            if (a is not null && (a.Value & (FileAttributes.Directory | FileAttributes.Device)) != 0) throw new AutomationException(AutomationError.UnsafePath, "Queue artifact is not a regular file.");
            if (a is not null && suffix is "-wal" or "-shm") throw AutomationValidation.Corrupt("WAL artifacts require explicit review.");
        }
    }
    private static void RequireDirectory(string path, bool create)
    {
        var a = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid);
        if (a is null && create) { Directory.CreateDirectory(path); a = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid); }
        if (a is null || (a.Value & FileAttributes.Directory) == 0) throw new AutomationException(AutomationError.UnsafePath, "Queue workspace must be a controlled directory.");
    }
    internal static SqliteConnection Connect(string path, bool write)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = write ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false, ForeignKeys = true, DefaultTimeout = 1 }.ToString());
        try { db.Open(); AutomationQueueSchema.Execute(db, null, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=1000"); return db; }
        catch { db.Dispose(); throw; }
    }
    internal static T Guard<T>(Func<T> action, bool persisted = false)
    {
        try { return action(); }
        catch (AutomationException e) when (persisted && e.Code is AutomationError.InvalidContract or AutomationError.PolicyMismatch or AutomationError.IllegalTransition) { throw new AutomationException(AutomationError.CorruptStore, "Persisted automation contract is invalid.", e); }
        catch (AutomationException) { throw; }
        catch (SqliteException e) { throw new AutomationException(e.SqliteErrorCode is 5 or 6 ? AutomationError.Busy : AutomationError.CorruptStore, "Automation SQLite operation failed; no implicit repair is allowed.", e); }
        catch (ProductionStorageException e) { throw new AutomationException(AutomationError.UnsafePath, "Queue paths cannot traverse unsafe components.", e); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException or KeyNotFoundException or FormatException)
        { throw new AutomationException(persisted ? AutomationError.CorruptStore : AutomationError.InvalidContract, "Automation contract is invalid.", e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new AutomationException(AutomationError.UnsafePath, "Queue storage cannot be safely accessed.", e); }
    }
}
