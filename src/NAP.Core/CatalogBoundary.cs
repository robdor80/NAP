using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogBoundary
{
    internal static ExecutionMutex Acquire(UniverseContext context)
    {
        try { return ExecutionMutex.Acquire("Catalog", context.Storage.StateRoot, "AssetCatalog.db"); }
        catch (IOException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogBusy, "Another catalog operation is active; retry explicitly.", inner: ex); }
    }

    internal static void Paths(UniverseContext context, bool createState = false)
    {
        var storage = context.Storage;
        if (storage.WorkspaceRoot.StartsWith("\\\\", StringComparison.Ordinal) ||
            storage.WorkspaceRoot.StartsWith("//", StringComparison.Ordinal) ||
            ProductionPaths.Overlaps(storage.StateRoot, storage.ProductionRoot) || ProductionPaths.Overlaps(storage.StateRoot, storage.ArchiveRoot))
            throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "The catalog must remain in the local isolated Workspace StateRoot.");
        if (new DriveInfo(storage.WorkspaceRoot).DriveType == DriveType.Network)
            throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "A network workspace cannot contain the active catalog.");
        try
        {
            RequireDirectory(storage.WorkspaceRoot);
            var state = ProductionPaths.CheckPath(storage.StateRoot, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
            if (state is null && createState) { Directory.CreateDirectory(storage.StateRoot); state = ProductionPaths.CheckPath(storage.StateRoot, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid); }
            if (state is null || (state & FileAttributes.Directory) == 0) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "StateRoot must be a controlled directory.");
            ProductionPaths.CheckCasing(storage.WorkspaceRoot, "state");
            ProductionPaths.CheckCasing(storage.StateRoot, "AssetCatalog.db");
            foreach (var path in new[] { storage.CatalogPath, storage.CatalogPath + "-journal", storage.CatalogPath + "-wal", storage.CatalogPath + "-shm" }) Regular(path);
            if (Regular(storage.CatalogPath + "-wal") || Regular(storage.CatalogPath + "-shm"))
                throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "WAL artifacts require explicit review; schema v1 uses only DELETE journaling.");
        }
        catch (ProductionStorageException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Catalog paths cannot traverse links or invalid components.", inner: ex); }
    }

    private static void RequireDirectory(string path)
    {
        var value = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        if (value is null || (value & FileAttributes.Directory) == 0) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "WorkspaceRoot must already exist as a controlled directory.", path);
    }
    internal static bool Regular(string path)
    {
        var value = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid, NapIssueCodes.CatalogInvalid);
        if (value is null) return false;
        if ((value & (FileAttributes.Directory | FileAttributes.Device)) != 0) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Expected a regular catalog file.", path);
        return true;
    }
    internal static SqliteConnection Open(UniverseContext context, bool writable)
    {
        Paths(context);
        if (!Regular(context.Storage.CatalogPath)) throw CatalogException.Stop(NapIssueCodes.CatalogMissing, "The catalog does not exist.");
        var connection = Connect(context.Storage.CatalogPath, writable ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly);
        try
        {
            CatalogSchema.Validate(connection, context.Id);
            CatalogAssetData.ValidateLogical(connection, context);
            if (!string.Equals((string?)CatalogSql.Scalar(connection, null, "PRAGMA journal_mode"), "delete", StringComparison.OrdinalIgnoreCase))
                throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Schema v1 requires DELETE journal mode.");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    internal static SqliteConnection Connect(string controlledPath, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = controlledPath, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false, ForeignKeys = true, DefaultTimeout = 1 }.ToString());
        try
        {
            connection.Open();
            CatalogSql.Execute(connection, null, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=1000");
            if (Convert.ToInt64(CatalogSql.Scalar(connection, null, "PRAGMA foreign_keys")) != 1) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Foreign keys must be enabled.");
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    internal static string Temporary(UniverseContext context)
    {
        Paths(context, createState: true);
        var temp = Path.Combine(context.Storage.StateRoot, "AssetCatalog." + Guid.NewGuid().ToString("N") + ".tmp");
        using var reserved = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None); return temp;
    }
    internal static void Publish(UniverseContext context, string temp, bool overwrite, ExecutionMutex lease)
    {
        lease.Require("Catalog", context.Storage.StateRoot, "AssetCatalog.db"); Paths(context);
        if (!ProductionPaths.Same(Path.GetDirectoryName(temp)!, context.Storage.StateRoot) || !Regular(temp)) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Catalog temporary is not controlled.");
        // Connections are closed (pooling=false), so SQLite has no outstanding handles at publication.
        using (var durable = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) durable.Flush(flushToDisk: true);
        Paths(context); File.Move(temp, context.Storage.CatalogPath, overwrite);
    }
}
