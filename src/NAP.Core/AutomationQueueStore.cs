using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

/// <summary>Universe-bound passive ledger. All writes require exclusive ownership; no service consumes this queue.</summary>
public sealed partial class AutomationQueueStore : IAutomationQueueStore, IAutomationPolicyStore, IAutomationEvidenceStore
{
    private readonly UniverseStorageConfig _roots;
    internal Action<string>? BeforeCommit { get; set; }
    public UniverseId UniverseId => _roots.UniverseId;
    public string DatabasePath => AutomationQueueBoundary.DatabasePath(_roots);
    public AutomationQueueStore(UniverseStorageConfig roots)
    { _roots = AutomationQueueBoundary.Guard(() => { ArgumentNullException.ThrowIfNull(roots); LocalUniverseSettingsStore.ValidateStructure([roots]); return roots; }); }

    public AutomationQueueOwner AcquireOwner() => AutomationQueueBoundary.Guard(() =>
    {
        ExecutionMutex lease;
        try { lease = ExecutionMutex.Acquire("AutomationOwner", _roots.StateRoot); }
        catch (IOException e) { throw new AutomationException(AutomationError.Busy, "Another queue owner is active.", e); }
        try
        {
            using var operation = AcquireIo();
            InitializeLocked();
            using var db = Open(true); using var tx = db.BeginTransaction();
            Execute(db, tx, "UPDATE queue_metadata SET owner_epoch=owner_epoch+1 WHERE id=1");
            var epoch = Convert.ToInt64(Scalar(db, tx, "SELECT owner_epoch FROM queue_metadata WHERE id=1")); tx.Commit();
            return new AutomationQueueOwner(lease, _roots.StateRoot, epoch);
        }
        catch { lease.Dispose(); throw; }
    });
    private void InitializeLocked()
    {
        AutomationQueueBoundary.Paths(_roots, true);
        if (File.Exists(DatabasePath)) return;
        var temp = Path.Combine(Path.GetDirectoryName(DatabasePath)!, "AutomationQueue." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var reserved = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using (var db = AutomationQueueBoundary.Connect(temp, true))
        {
            Create(db); using var tx = db.BeginTransaction();
            var definition = AutomationPolicyDefinition.DisabledDefault(_roots);
            var policy = new StoredAutomationPolicy(definition, definition.Reference, AutomationPolicyState.Disabled, null, 0);
            InsertPolicy(db, tx, policy);
            Execute(db, tx, "INSERT INTO queue_metadata VALUES(1,1,$u,$r,0,$p)", "$u", UniverseId.Value, "$r", AutomationJson.Encode(_roots), "$p", policy.Reference.Hash.Hex);
            tx.Commit(); Validate(db);
        }
        using (var durable = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) durable.Flush(true);
        AutomationQueueBoundary.Paths(_roots, false); File.Move(temp, DatabasePath, false);
    }
    private SqliteConnection Open(bool write)
    {
        AutomationQueueBoundary.Paths(_roots, false);
        if (!File.Exists(DatabasePath)) throw new AutomationException(AutomationError.Missing, "Automation queue is not initialized.");
        var db = AutomationQueueBoundary.Connect(DatabasePath, write);
        try
        {
            Validate(db);
            AutomationQueueBoundary.Guard(() => { ValidateData(db); return 0; }, true);
            return db;
        }
        catch { db.Dispose(); throw; }
    }
    private ExecutionMutex AcquireIo()
    { try { return ExecutionMutex.Acquire("AutomationQueueIO", _roots.StateRoot); } catch (IOException e) { throw new AutomationException(AutomationError.Busy, "Another queue operation is active.", e); } }
    private T Read<T>(Func<SqliteConnection, T> action) => AutomationQueueBoundary.Guard(() => { using var operation = AcquireIo(); using var db = Open(false); return action(db); }, true);
    private T Write<T>(AutomationQueueOwner owner, Func<SqliteConnection, SqliteTransaction, T> action) => AutomationQueueBoundary.Guard(() =>
    {
        ArgumentNullException.ThrowIfNull(owner); owner.Require(_roots.StateRoot);
        using var operation = AcquireIo(); using var db = Open(true); using var tx = db.BeginTransaction();
        if (Convert.ToInt64(Scalar(db, tx, "SELECT owner_epoch FROM queue_metadata WHERE id=1")) != owner.Epoch) throw new AutomationException(AutomationError.InvalidClaim, "Owner generation is stale.");
        var value = action(db, tx); BeforeCommit?.Invoke("before-commit"); tx.Commit(); return value;
    });
    public QueueItem Get(QueueItemId id) => Read(db => Item(db, null, id));
    public IReadOnlyList<QueueItem> List(int offset = 0, int limit = 100)
    {
        if (offset < 0 || limit is < 1 or > 1000) throw AutomationValidation.Invalid("Invalid queue page.");
        return Read(db => Payloads<QueueItem>(db, null, "SELECT payload FROM items ORDER BY rowid LIMIT $l OFFSET $o", "$l", limit, "$o", offset));
    }
    public IReadOnlyList<QueueEvent> Events(QueueItemId id) => Read(db => { Item(db, null, id); return Payloads<QueueEvent>(db, null, "SELECT payload FROM events WHERE item_id=$i ORDER BY sequence", "$i", id.Value); });
    public IReadOnlyList<QueueObservation> Observations(QueueItemId id) => Read(db => { Item(db, null, id); return Payloads<QueueObservation>(db, null, "SELECT payload FROM observations WHERE item_id=$i ORDER BY rowid", "$i", id.Value); });
    public QueueItem Observe(AutomationQueueOwner owner, UniverseId universe, string sourcePath, QueueZipIdentity zip, QueueBatchId? batch = null) => Write(owner, (db, tx) =>
    {
        AutomationValidation.Scope(UniverseId, universe); AutomationValidation.Zip(zip);
        var source = Path.GetFullPath(sourcePath);
        if (!ProductionPaths.Same(Path.GetDirectoryName(source)!, _roots.InboxRoot) || !string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase) || !ProductionPaths.SafeSegment(Path.GetFileName(source)))
            throw new AutomationException(AutomationError.UnsafePath, "Observation must identify a ZIP directly in this configured Inbox.");
        // Metadata registration does not inspect, hash, copy or admit the referenced ZIP.
        if (batch is not null) Batch(db, tx, batch, requireOpen: true);
        var existing = (string?)Scalar(db, tx, "SELECT payload FROM items WHERE zip_hash=$h", "$h", zip.Hash.Hex);
        QueueItem value;
        if (existing is not null)
        {
            value = AutomationJson.Decode<QueueItem>(existing);
            if (value.Zip.SizeBytes != zip.SizeBytes) throw AutomationValidation.Invalid("Same ZIP hash has conflicting size metadata.");
        }
        else
        {
            var policy = CurrentPolicy(db, tx); var now = DateTimeOffset.UtcNow;
            value = new(QueueItemId.Create(), UniverseId, source, zip, QueueState.Observed, now, now, 0, 0, null, QueueIncident.None, batch, policy.Reference, 0, null, null, null);
            Execute(db, tx, "INSERT INTO items VALUES($i,$h,$s,0,$b,$p,$v)", "$i", value.Id.Value, "$h", zip.Hash.Hex, "$s", value.State.ToString(), "$b", batch?.Value, "$p", value.Policy.Hash.Hex, "$v", AutomationJson.Encode(value));
            Event(db, tx, value, QueueStage.Observation, QueueEventCode.Observed, QueueEventResult.Recorded);
        }
        var observation = new QueueObservation(value.Id, source, DateTimeOffset.UtcNow, batch);
        Execute(db, tx, "INSERT OR IGNORE INTO observations VALUES($i,$s,$b,$v)", "$i", value.Id.Value, "$s", source, "$b", batch?.Value, "$v", AutomationJson.Encode(observation));
        return value;
    });
    public QueueItem Transition(AutomationQueueOwner owner, QueueItemId id, long revision, QueueState next,
        QueueIncident incident = QueueIncident.None, DateTimeOffset? retryAtUtc = null, QueueClaim? claim = null) => Write(owner, (db, tx) =>
    {
        var current = Item(db, tx, id); Revision(current, revision);
        if (!QueueTransitions.CanTransition(current.State, next) || next == QueueState.Running) throw new AutomationException(AutomationError.IllegalTransition, "Use Claim for Running; terminal or illegal transitions are rejected.");
        if (current.State == QueueState.Running) RequireClaim(owner, current, claim);
        AutomationValidation.Defined(incident);
        if (next is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource && incident == QueueIncident.None) throw AutomationValidation.Invalid("Incident transitions require a typed reason.");
        if (next is not (QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource or QueueState.RetryScheduled) && incident != QueueIncident.None) throw AutomationValidation.Invalid("Incident not valid for this state.");
        if ((next == QueueState.RetryScheduled) != (retryAtUtc is not null)) throw AutomationValidation.Invalid("Retry state requires a UTC schedule, and only retry state may carry one.");
        if (retryAtUtc is { } time) { AutomationValidation.Utc(time); if (time <= DateTimeOffset.UtcNow) throw AutomationValidation.Invalid("Retry must be in the future."); }
        if (current.State == QueueState.RetryScheduled && next == QueueState.Queued && current.RetryAtUtc > DateTimeOffset.UtcNow) throw new AutomationException(AutomationError.IllegalTransition, "Retry is not due yet.");
        var value = current with { State = next, Incident = incident, RetryAtUtc = retryAtUtc, RetryCount = current.RetryCount + (next == QueueState.RetryScheduled ? 1 : 0), UpdatedUtc = DateTimeOffset.UtcNow, Revision = current.Revision + 1, ClaimToken = null, ClaimEpoch = null };
        SaveItem(db, tx, value, revision); Event(db, tx, value, QueueStage.Review, QueueEventCode.Transitioned, next switch { QueueState.Completed => QueueEventResult.Succeeded, QueueState.Rejected => QueueEventResult.Rejected, QueueState.NeedsReview => QueueEventResult.ReviewRequired, QueueState.RetryScheduled => QueueEventResult.Waiting, _ => QueueEventResult.Recorded });
        return value;
    });
    public QueueClaim Claim(AutomationQueueOwner owner, QueueItemId id, long revision) => Write(owner, (db, tx) =>
    {
        var current = Item(db, tx, id); Revision(current, revision);
        if (current.State != QueueState.Queued) throw new AutomationException(AutomationError.IllegalTransition, "Only Queued can be claimed.");
        if (current.ActiveAttempt is null || Scalar(db, tx, "SELECT asset_id FROM reservations WHERE item_id=$i", "$i", id.Value) is null) throw new AutomationException(AutomationError.ReservationOccupied, "A productive claim requires an active attempt and reserved asset identity.");
        var value = current with { State = QueueState.Running, Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow, ClaimToken = Guid.NewGuid().ToString("N"), ClaimEpoch = owner.Epoch };
        SaveItem(db, tx, value, revision); Event(db, tx, value, QueueStage.Preparation, QueueEventCode.Claimed, QueueEventResult.Recorded);
        return new QueueClaim(id, value.ClaimToken, owner.Epoch, value.Revision);
    });
    /// <summary>Explicitly parks a stale infrastructure claim; never reconciles any filesystem/production effect.</summary>
    public QueueItem ParkInterruptedClaim(AutomationQueueOwner owner, QueueItemId id, long revision) => Write(owner, (db, tx) =>
    {
        var current = Item(db, tx, id); Revision(current, revision);
        if (current.State != QueueState.Running || current.ClaimEpoch >= owner.Epoch) throw new AutomationException(AutomationError.InvalidClaim, "Only a claim from a previous exclusive owner may be parked.");
        var value = current with { State = QueueState.NeedsReview, Incident = QueueIncident.InterruptedClaim, Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow, ClaimToken = null, ClaimEpoch = null };
        SaveItem(db, tx, value, revision); Event(db, tx, value, QueueStage.Review, QueueEventCode.ClaimInterrupted, QueueEventResult.ReviewRequired); return value;
    });
    public QueueItem RecordProgress(AutomationQueueOwner owner, QueueItemId id, long revision, QueueClaim claim, QueueStage stage, QueueEventResult result) => Write(owner, (db, tx) =>
    {
        var item = Item(db, tx, id); Revision(item, revision); RequireClaim(owner, item, claim);
        if (item.State != QueueState.Running) throw new AutomationException(AutomationError.IllegalTransition, "Stage progress requires Running state.");
        AutomationValidation.Defined(stage); AutomationValidation.Defined(result);
        var value = item with { Revision = revision + 1, UpdatedUtc = DateTimeOffset.UtcNow };
        SaveItem(db, tx, value, revision); Event(db, tx, value, stage, QueueEventCode.StageRecorded, result); return value;
    });
    private static void Revision(QueueItem value, long revision) { if (value.Revision != revision) throw new AutomationException(AutomationError.RevisionConflict, "Queue item revision is stale."); }
    private static void RequireClaim(AutomationQueueOwner owner, QueueItem item, QueueClaim? claim)
    {
        if (claim is null || claim.ItemId != item.Id || claim.OwnerEpoch != owner.Epoch || item.ClaimEpoch != owner.Epoch || claim.Token != item.ClaimToken)
            throw new AutomationException(AutomationError.InvalidClaim, "Exact live claim ownership is required.");
    }
    private static QueueItem Item(SqliteConnection db, SqliteTransaction? tx, QueueItemId id) => AutomationJson.Decode<QueueItem>((string?)Scalar(db, tx, "SELECT payload FROM items WHERE id=$i", "$i", id.Value) ?? throw new AutomationException(AutomationError.Missing, "Queue item does not exist."));
    private static IReadOnlyList<T> Payloads<T>(SqliteConnection db, SqliteTransaction? tx, string sql, params object?[] args)
    {
        using var command = Command(db, tx, sql, args); using var rows = command.ExecuteReader(); var values = new List<T>();
        while (rows.Read()) values.Add(AutomationJson.Decode<T>(rows.GetString(0))); return values.AsReadOnly();
    }
    private static void SaveItem(SqliteConnection db, SqliteTransaction tx, QueueItem value, long revision)
    {
        if (Execute(db, tx, "UPDATE items SET state=$s,revision=$r,payload=$v WHERE id=$i AND revision=$old", "$s", value.State.ToString(), "$r", value.Revision, "$v", AutomationJson.Encode(value), "$i", value.Id.Value, "$old", revision) != 1)
            throw new AutomationException(AutomationError.RevisionConflict, "Queue item revision changed.");
    }
    private static void Event(SqliteConnection db, SqliteTransaction tx, QueueItem value, QueueStage stage, QueueEventCode code, QueueEventResult result, WorkflowAttemptId? attempt = null)
    {
        var seq = Convert.ToInt64(Scalar(db, tx, "SELECT ifnull(max(sequence),0)+1 FROM events WHERE item_id=$i", "$i", value.Id.Value));
        var e = new QueueEvent(value.Id, seq, DateTimeOffset.UtcNow, stage, code, result, value.State, value.Incident, attempt ?? value.ActiveAttempt);
        Execute(db, tx, "INSERT INTO events VALUES($i,$s,$v)", "$i", value.Id.Value, "$s", seq, "$v", AutomationJson.Encode(e));
    }
}
