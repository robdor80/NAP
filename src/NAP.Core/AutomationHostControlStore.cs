using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public enum AutomationControlAction { Activate, Pause, Resume, Stop, CancelItem, ResumeItem }
public sealed record AutomationHostGrant(UniverseId UniverseId, UniverseStorageConfig Roots, Sha256Digest ProfileHash,
    PolicyReference Policy, string PolicyAuthorizationId, string Actor, bool ResumeOnRestart);
public sealed record AutomationHostControl(UniverseId UniverseId, long Revision, bool Paused, bool Stopped,
    AutomationHostGrant? Grant, string? StartedSession, IReadOnlyList<string> CancelledItems);
public sealed record AutomationControlCommand(string Id, UniverseId UniverseId, long ExpectedRevision,
    AutomationControlAction Action, string SessionId, AutomationHostGrant? Grant = null, QueueItemId? ItemId = null);
internal sealed record AutomationControlReceipt(AutomationControlCommand Command, AutomationHostControl Result);

/// <summary>Versioned inhibition and command ledger. Never confers workflow or filesystem authority.
/// Its IO lease is short and independent of universe owners, so pause intent can be persisted while a writer drains.</summary>
public sealed class AutomationHostControlStore
{
    private static readonly string[] Ddl =
    [
        "CREATE TABLE metadata(id INTEGER PRIMARY KEY CHECK(id=1), version INTEGER NOT NULL CHECK(version=1), installation TEXT NOT NULL, binding TEXT NOT NULL)",
        "CREATE TABLE controls(universe TEXT PRIMARY KEY, payload TEXT NOT NULL, hash TEXT NOT NULL)",
        "CREATE TABLE history(universe TEXT NOT NULL REFERENCES controls(universe), revision INTEGER NOT NULL, payload TEXT NOT NULL, hash TEXT NOT NULL, previous_hash TEXT, PRIMARY KEY(universe,revision))",
        "CREATE TABLE commands(id TEXT PRIMARY KEY, payload TEXT NOT NULL, hash TEXT NOT NULL)",
        "CREATE TABLE sessions(id TEXT PRIMARY KEY, started TEXT NOT NULL)"
    ];
    private readonly string _directory;
    private readonly string _binding;
    private readonly UniverseContext[] _contexts;
    private readonly object _gate = new();
    public string DatabasePath => Path.Combine(_directory, "HostControl.db");
    internal Action? BeforeCommit { get; set; }

    public AutomationHostControlStore(string directory, IEnumerable<UniverseContext> contexts)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Explicit absolute host storage is required.");
        _directory = Path.GetFullPath(directory); _contexts = contexts.OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray();
        LocalUniverseSettingsStore.ValidateStructure(_contexts.Select(c => c.Storage));
        if (_contexts.Length is < 1 or > 64 || _contexts.Any(c => new[] { c.Storage.WorkspaceRoot, c.Storage.ProductionRoot, c.Storage.ArchiveRoot }
            .Any(r => ProductionPaths.Overlaps(r, _directory)))) throw AutomationValidation.Invalid("Host control storage must be disjoint from all universe roots.");
        _binding = AutomationJson.Encode(_contexts.Select(c => new { c.Storage, Profile = ProfileHash(c) }).ToArray());
    }

    public static Sha256Digest ProfileHash(UniverseContext context) => AutomationJson.HashBytes(
        System.Text.Encoding.UTF8.GetBytes(UniverseProfileSnapshotRenderer.Render(context.Profile)));

    public void Initialize() => Access(true, db =>
    {
        if (Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM metadata")) != 0) return 0;
        using var tx = db.BeginTransaction();
        Execute(db, tx, "INSERT INTO metadata VALUES(1,1,$i,$b)", "$i", "installation_" + Guid.NewGuid().ToString("N"), "$b", _binding);
        foreach (var c in _contexts)
        {
            var initial = new AutomationHostControl(c.Id, 0, true, true, null, null, []);
            var json = AutomationJson.Encode(initial); var hash = AutomationJson.Hash(initial).Hex;
            Execute(db, tx, "INSERT INTO controls VALUES($u,$p,$h)", "$u", c.Id.Value, "$p", json, "$h", hash);
            Execute(db, tx, "INSERT INTO history VALUES($u,0,$p,$h,NULL)", "$u", c.Id.Value, "$p", json, "$h", hash);
        }
        BeforeCommit?.Invoke(); tx.Commit(); return 0;
    });

    public string BeginSession() => Access(false, db =>
    {
        var id = "session_" + Guid.NewGuid().ToString("N");
        Execute(db, null, "INSERT INTO sessions VALUES($i,$s)", "$i", id, "$s", DateTimeOffset.UtcNow.ToString("O")); return id;
    });
    public string InstallationId => Access(false, db => (string)Scalar(db, null, "SELECT installation FROM metadata WHERE id=1")!);
    public AutomationHostControl Read(UniverseId universe) => Access(false, db => Current(db, null, universe));

    public AutomationHostControl Apply(AutomationControlCommand command) => Access(false, db =>
    {
        AutomationValidation.Id(command.Id, "command_"); AutomationValidation.Id(command.SessionId, "session_");
        AutomationValidation.Defined(command.Action);
        if (Scalar(db, null, "SELECT id FROM sessions WHERE id=$i", "$i", command.SessionId) is null)
            throw AutomationValidation.Invalid("Unknown host session.");
        using var tx = db.BeginTransaction();
        var existing = (string?)Scalar(db, tx, "SELECT payload FROM commands WHERE id=$i", "$i", command.Id);
        if (existing is not null)
        {
            var receipt = AutomationJson.Decode<AutomationControlReceipt>(existing);
            if (AutomationJson.Encode(receipt.Command) != AutomationJson.Encode(command)) throw AutomationValidation.Invalid("Command identity was reused with different content.");
            return receipt.Result;
        }
        var prior = Current(db, tx, command.UniverseId);
        if (prior.Revision != command.ExpectedRevision) throw new AutomationException(AutomationError.RevisionConflict, "Control revision is stale.");
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM commands")) >= 100000) throw AutomationValidation.Invalid("Control ledger requires explicit archival before accepting more commands.");
        var cancelled = prior.CancelledItems.ToList();
        if (command.Action is AutomationControlAction.CancelItem or AutomationControlAction.ResumeItem)
        {
            if (command.ItemId is null) throw AutomationValidation.Invalid("Item control requires an item identity.");
            if (command.Action == AutomationControlAction.CancelItem && !cancelled.Contains(command.ItemId.Value)) cancelled.Add(command.ItemId.Value);
            if (command.Action == AutomationControlAction.ResumeItem) cancelled.Remove(command.ItemId.Value);
            if (cancelled.Count > 4096) throw AutomationValidation.Invalid("Too many durable item inhibitions.");
        }
        else if (command.ItemId is not null) throw AutomationValidation.Invalid("Unexpected item identity.");
        if ((command.Action == AutomationControlAction.Activate) != (command.Grant is not null)) throw AutomationValidation.Invalid("Only activation carries an exact grant.");
        var grant = command.Grant ?? prior.Grant;
        if (grant is not null) ValidateGrant(grant, command.UniverseId);
        if (command.Action is AutomationControlAction.Activate or AutomationControlAction.Resume && grant is null)
            throw AutomationValidation.Invalid("Resume requires a previously recorded exact grant.");
        var next = prior with
        {
            Revision = prior.Revision + 1, Grant = grant,
            Paused = command.Action switch { AutomationControlAction.Activate or AutomationControlAction.Resume => false, AutomationControlAction.Pause or AutomationControlAction.Stop => true, _ => prior.Paused },
            Stopped = command.Action switch { AutomationControlAction.Activate or AutomationControlAction.Resume => false, AutomationControlAction.Stop => true, _ => prior.Stopped },
            StartedSession = command.Action is AutomationControlAction.Activate or AutomationControlAction.Resume ? command.SessionId : prior.StartedSession,
            CancelledItems = cancelled.OrderBy(i => i, StringComparer.Ordinal).ToArray()
        };
        var json = AutomationJson.Encode(next); var hash = AutomationJson.Hash(next).Hex;
        Execute(db, tx, "UPDATE controls SET payload=$p,hash=$h WHERE universe=$u", "$p", json, "$h", hash, "$u", next.UniverseId.Value);
        Execute(db, tx, "INSERT INTO history VALUES($u,$r,$p,$h,$b)", "$u", next.UniverseId.Value, "$r", next.Revision, "$p", json, "$h", hash, "$b", AutomationJson.Hash(prior).Hex);
        var result = new AutomationControlReceipt(command, next);
        Execute(db, tx, "INSERT INTO commands VALUES($i,$p,$h)", "$i", command.Id, "$p", AutomationJson.Encode(result), "$h", AutomationJson.Hash(result).Hex);
        BeforeCommit?.Invoke(); tx.Commit(); return next;
    });

    private void ValidateGrant(AutomationHostGrant grant, UniverseId universe)
    {
        var c = _contexts.SingleOrDefault(c => c.Id == universe) ?? throw AutomationValidation.Invalid("Unknown universe.");
        if (grant.UniverseId != universe || !AutomationValidation.SameRoots(grant.Roots, c.Storage) || grant.ProfileHash != ProfileHash(c))
            throw new AutomationException(AutomationError.UniverseMismatch, "Host grant differs from its immutable context.");
        AutomationValidation.Reference(grant.Policy); AutomationValidation.Text(grant.Actor); AutomationValidation.Text(grant.PolicyAuthorizationId);
    }

    private AutomationHostControl Current(SqliteConnection db, SqliteTransaction? tx, UniverseId universe)
    {
        if (!_contexts.Any(c => c.Id == universe)) throw AutomationValidation.Invalid("Unknown universe.");
        return AutomationJson.Decode<AutomationHostControl>((string?)Scalar(db, tx, "SELECT payload FROM controls WHERE universe=$u", "$u", universe.Value)
            ?? throw AutomationValidation.Corrupt("Missing universe control."));
    }

    private T Access<T>(bool initialize, Func<SqliteConnection, T> operation) => AutomationQueueBoundary.Guard(() =>
    {
        lock (_gate)
        {
        InboxAdmissionEvidence.DirectoryBoundary(_directory);
        using var lease = AcquireIo();
        foreach (var entry in Directory.EnumerateFileSystemEntries(_directory))
            if (!ProductionPaths.Same(entry, DatabasePath)) throw AutomationValidation.Corrupt("Unknown control artifact must be reviewed, not adopted or deleted.");
        var exists = ProductionPaths.FileExists(DatabasePath, NapIssueCodes.CatalogInvalid);
        if (!exists && !initialize) throw new AutomationException(AutomationError.Missing, "Host control store is absent.");
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath,
            Mode = exists ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true }.ToString());
        db.Open(); Execute(db, null, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE; PRAGMA busy_timeout=1000");
        if (!exists) { using var tx = db.BeginTransaction(); foreach (var sql in Ddl) Execute(db, tx, sql); Execute(db, tx, "PRAGMA user_version=1"); tx.Commit(); }
        Check(db, initialize && !exists); return operation(db);
        }
    }, !initialize);

    private ExecutionMutex AcquireIo()
    {
        try { return ExecutionMutex.Acquire("HostControlIO", _directory); }
        catch (IOException error) { throw new AutomationException(AutomationError.Busy, "Host control IO is occupied.", error); }
    }

    private void Check(SqliteConnection db, bool initialize)
    {
        if (Convert.ToInt64(Scalar(db, null, "PRAGMA user_version")) != 1) throw new AutomationException(AutomationError.UnsupportedSchema, "Unknown host control schema.");
        using (var cmd = Command(db, null, "SELECT sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY sql"))
        using (var rows = cmd.ExecuteReader())
        {
            var actual = new List<string>(); while (rows.Read()) actual.Add(rows.GetString(0));
            if (!actual.SequenceEqual(Ddl.OrderBy(s => s, StringComparer.Ordinal))) throw AutomationValidation.Corrupt("Host control schema changed.");
        }
        if ((string?)Scalar(db, null, "PRAGMA quick_check") != "ok") throw AutomationValidation.Corrupt("Host control integrity failed.");
        using (var fk = Command(db, null, "PRAGMA foreign_key_check")) using (var rows = fk.ExecuteReader())
            if (rows.Read()) throw AutomationValidation.Corrupt("Host control foreign keys failed.");
        if (Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM metadata")) == 0)
        { if (initialize) return; throw AutomationValidation.Corrupt("Unbound control database."); }
        if ((string?)Scalar(db, null, "SELECT binding FROM metadata WHERE id=1") != _binding) throw AutomationValidation.Corrupt("Host storage binding changed.");
        if (Convert.ToInt64(Scalar(db, null, "SELECT version FROM metadata WHERE id=1")) != 1) throw AutomationValidation.Corrupt("Host metadata version changed.");
        AutomationValidation.Id((string)Scalar(db, null, "SELECT installation FROM metadata WHERE id=1")!, "installation_");
        if (Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM controls")) != _contexts.Length) throw AutomationValidation.Corrupt("Control scope count changed.");
        foreach (var context in _contexts)
        {
            string? previous = null; long revision = 0; AutomationHostControl? last = null;
            using var cmd = Command(db, null, "SELECT revision,payload,hash,previous_hash FROM history WHERE universe=$u ORDER BY revision", "$u", context.Id.Value);
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
            {
                var value = AutomationJson.Decode<AutomationHostControl>(rows.GetString(1));
                if (rows.GetInt64(0) != revision || value.Revision != revision++ || value.UniverseId != context.Id ||
                    AutomationJson.Hash(value).Hex != rows.GetString(2) || (rows.IsDBNull(3) ? null : rows.GetString(3)) != previous ||
                    value.CancelledItems.Count > 4096 || value.CancelledItems.Distinct().Count() != value.CancelledItems.Count)
                    throw AutomationValidation.Corrupt("Control history changed.");
                foreach (var id in value.CancelledItems) _ = new QueueItemId(id);
                if (value.Grant is not null) ValidateGrant(value.Grant, context.Id);
                previous = rows.GetString(2); last = value;
            }
            var current = Current(db, null, context.Id);
            if (last is null || AutomationJson.Encode(current) != AutomationJson.Encode(last) ||
                (string?)Scalar(db, null, "SELECT hash FROM controls WHERE universe=$u", "$u", context.Id.Value) != previous)
                throw AutomationValidation.Corrupt("Control head differs from history.");
        }
        using var commands = Command(db, null, "SELECT id,payload,hash FROM commands"); using var receipts = commands.ExecuteReader();
        while (receipts.Read())
        {
            var receipt = AutomationJson.Decode<AutomationControlReceipt>(receipts.GetString(1));
            if (receipt.Command.Id != receipts.GetString(0) || AutomationJson.Hash(receipt).Hex != receipts.GetString(2) ||
                receipt.Result.UniverseId != receipt.Command.UniverseId || receipt.Result.Revision != receipt.Command.ExpectedRevision + 1 ||
                (string?)Scalar(db, null, "SELECT hash FROM history WHERE universe=$u AND revision=$r", "$u", receipt.Result.UniverseId.Value, "$r", receipt.Result.Revision) != AutomationJson.Hash(receipt.Result).Hex)
                throw AutomationValidation.Corrupt("Command receipt changed.");
        }
        if (Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM commands")) != Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM history WHERE revision>0")))
            throw AutomationValidation.Corrupt("Control command history is incomplete.");
    }
}
