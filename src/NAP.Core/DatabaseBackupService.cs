using Microsoft.Data.Sqlite;

namespace NAP.Core;

public sealed class DatabaseBackupService
{
    internal TimeProvider Clock { get; set; } = TimeProvider.System;
    internal Func<BackupId> NewId { get; set; } = BackupId.New;
    internal Action<string>? Observer { get; set; }
    public BackupHistoryEntry Create(UniverseContext context) => BackupStorage.Run(() =>
    {
        using var archive = BackupStorage.Archive(context, true);
        using var catalog = CatalogBoundary.Acquire(context);
        return CreateLocked(context, archive, catalog, DatabaseBackupPurpose.Manual);
    });

    internal BackupHistoryEntry CreateLocked(UniverseContext c, ArchiveLock archive, ExecutionMutex catalog, DatabaseBackupPurpose purpose)
    {
        archive.RequireWrites(c); catalog.Require("Catalog", c.Storage.StateRoot, "AssetCatalog.db");
        CatalogBoundary.Paths(c); BackupStorage.ValidateDatabase(c, c.Storage.CatalogPath);
        var before = BackupStorage.Fingerprint(c.Storage.CatalogPath);
        Observer?.Invoke("source_verified");
        var id = NewId(); var created = Clock.GetUtcNow().ToUniversalTime(); var temp = BackupStorage.Prepare(c, BackupKind.Database, id);
        var target = Path.Combine(temp, "catalog.db");
        using (var reserve = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using (var source = CatalogBoundary.Open(c, false))
        using (var destination = CatalogBoundary.Connect(target, SqliteOpenMode.ReadWrite)) source.BackupDatabase(destination);
        BackupStorage.ValidateDatabase(c, target); BackupStorage.Durable(target);
        CatalogBoundary.Paths(c); BackupStorage.VerifyFile(c.Storage.CatalogPath, before.Size, before.Sha);
        var fingerprint = BackupStorage.Fingerprint(target);
        var manifest = new BackupManifest(id, c.Id, BackupKind.Database, created, fingerprint.Size, fingerprint.Sha, CatalogSchema.Version, purpose);
        return BackupStorage.Publish(c, temp, manifest, archive);
    }
}
