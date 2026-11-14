using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

/// <summary>Only ZIP fingerprints are kept so a retained Inbox original cannot recreate a deleted item.</summary>
internal static class RemovedInboxPackages
{
    internal static string PathFor(UniverseStorageConfig roots) => Path.Combine(roots.StateRoot, "RemovedInboxPackages.db");

    internal static bool Contains(UniverseStorageConfig roots, Sha256Digest digest)
    {
        var path = PathFor(roots);
        if (!CatalogBoundary.Regular(path)) return false;
        using var db = CatalogBoundary.Connect(path, SqliteOpenMode.ReadOnly);
        return Scalar(db, null, "SELECT 1 FROM removed_zip WHERE digest=$digest", "$digest", digest.Hex) is not null;
    }

    internal static long EventFloor(UniverseStorageConfig roots)
    {
        var path = PathFor(roots);
        if (!CatalogBoundary.Regular(path)) return 0;
        using var db = CatalogBoundary.Connect(path, SqliteOpenMode.ReadOnly);
        return Convert.ToInt64(Scalar(db, null, "SELECT value FROM event_floor WHERE id=1"));
    }
    internal static void Initialize(UniverseStorageConfig roots)
    {
        var path = PathFor(roots);
        CatalogBoundary.Regular(path);
        using var db = CatalogBoundary.Connect(path, SqliteOpenMode.ReadWriteCreate);
        Execute(db, null, "PRAGMA journal_mode=DELETE; CREATE TABLE IF NOT EXISTS removed_zip(digest TEXT PRIMARY KEY); CREATE TABLE IF NOT EXISTS removal_commit(id TEXT PRIMARY KEY); CREATE TABLE IF NOT EXISTS event_floor(id INTEGER PRIMARY KEY CHECK(id=1), value INTEGER NOT NULL)");
    }
}

public sealed partial class AutomationQueueStore
{
    internal IReadOnlyList<WorkflowAttempt> RemovalAttempts(QueueItemId id) => Read(db =>
        Payloads<WorkflowAttempt>(db, null, "SELECT payload FROM attempts WHERE item_id=$i ORDER BY number", "$i", id.Value));

    internal SqliteConnection OpenRemoval(AutomationQueueOwner owner)
    {
        owner.Require(_roots.StateRoot);
        var connection = Open(true);
        if (Convert.ToInt64(Scalar(connection, null, "SELECT owner_epoch FROM queue_metadata WHERE id=1")) == owner.Epoch)
            return connection;
        connection.Dispose();
        throw new AutomationException(AutomationError.InvalidClaim, "La cola cambió de propietario. Actualiza y vuelve a intentarlo.");
    }

    internal static void RemoveItem(SqliteConnection db, SqliteTransaction tx, QueueItem item)
    {
        if (Convert.ToInt64(Scalar(db, tx, "SELECT revision FROM work.items WHERE id=$i", "$i", item.Id.Value) ?? -1L) != item.Revision)
            throw new AutomationException(AutomationError.RevisionConflict, "El aviso cambió. Actualiza antes de eliminarlo.");
        foreach (var table in new[] { "observations", "reservations", "events", "lineage", "decisions", "attempts" })
            Execute(db, tx, "DELETE FROM work." + table + " WHERE item_id=$i", "$i", item.Id.Value);
        Execute(db, tx, "DELETE FROM work.items WHERE id=$i", "$i", item.Id.Value);
    }
}
