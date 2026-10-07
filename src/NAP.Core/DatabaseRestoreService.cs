using Microsoft.Data.Sqlite;

namespace NAP.Core;

public enum ActiveCatalogState { Missing, Valid, Corrupt }
public sealed class DatabaseRestorePlan
{
    internal DatabaseRestorePlan(UniverseContext c, BackupHistoryEntry source, ActiveCatalogState state, (long Size, Sha256Digest Sha)? active, CatalogFileStamp? stamp)
    { Binding = BackupStorage.Binding(c); Source = source; ActiveState = state; Active = active; Stamp = stamp; }
    public BackupHistoryEntry Source { get; }
    public UniverseId UniverseId => Source.UniverseId;
    public ActiveCatalogState ActiveState { get; }
    public bool RequiresSafetyBackup => ActiveState == ActiveCatalogState.Valid;
    public bool RequiresRecoveryPreservation => ActiveState == ActiveCatalogState.Corrupt;
    internal string Binding { get; }
    internal (long Size, Sha256Digest Sha)? Active { get; }
    internal CatalogFileStamp? Stamp { get; }
}
public sealed record DatabaseRestoreResult(BackupId BackupId, BackupId? SafetyBackupId,
    ActiveCatalogState PriorActiveState, CatalogRecoveryArtifact? RecoveryArtifact)
{ public bool Restored => true; }

/// <summary>Read-only plans and atomic replacement, with mandatory valid safety backup or forensic preservation of prior corrupt bytes.</summary>
public sealed class DatabaseRestoreService
{
    internal Action<string>? Observer { get; set; }
    internal DatabaseBackupService SafetyBackups { get; set; } = new();
    internal CatalogRecoveryStore RecoveryArtifacts { get; set; } = new();
    public DatabaseRestorePlan PlanRestore(UniverseContext context, BackupId backupId) => BackupStorage.Run(() =>
    {
        using var archive = BackupStorage.Archive(context, false); using var catalog = CatalogBoundary.Acquire(context);
        var entry = BackupHistoryReader.Find(context, backupId);
        if (entry.Kind != BackupKind.Database) throw BackupException.Stop(NapIssueCodes.RestoreInvalid, "Only database backups may replace the active catalog.");
        CatalogBoundary.Paths(context); var active = Active(context);
        return new DatabaseRestorePlan(context, entry, active.State, active.Bytes, active.Bytes is null ? null : CatalogFileStamp.Capture(context.Storage.CatalogPath));
    });
    public DatabaseRestoreResult Execute(UniverseContext context, DatabaseRestorePlan plan) => BackupStorage.Run(() =>
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var archive = BackupStorage.Archive(context, true); using var catalog = CatalogBoundary.Acquire(context);
        CheckPlan(context, plan);
        BackupId? safety = null;
        CatalogRecoveryArtifact? recovery = null;
        if (plan.RequiresSafetyBackup) safety = SafetyBackups.CreateLocked(context, archive, catalog, DatabaseBackupPurpose.PreRestore).BackupId;
        if (plan.RequiresRecoveryPreservation) recovery = RecoveryArtifacts.Preserve(context, archive, catalog, plan.Active!.Value);
        Observer?.Invoke("safety_verified"); CheckPlan(context, plan);
        var candidate = Path.Combine(context.Storage.StateRoot, ".restore-" + Guid.NewGuid().ToString("N") + ".db");
        var source = Path.Combine(BackupStorage.DirectoryPath(context, BackupKind.Database, plan.Source.BackupId), plan.Source.Manifest.Artifact);
        BackupStorage.Copy(source, candidate);
        BackupStorage.VerifyFile(candidate, plan.Source.Size, plan.Source.Sha256); BackupStorage.ValidateDatabase(context, candidate);
        Observer?.Invoke("candidate_verified");
        CheckPlan(context, plan); BackupStorage.VerifyFile(candidate, plan.Source.Size, plan.Source.Sha256);
        BackupStorage.ValidateDatabase(context, candidate); BackupStorage.Durable(candidate);
        if (recovery is not null) CatalogRecoveryStore.Verify(context, CatalogRecoveryStore.DirectoryPath(context, recovery.RecoveryId), recovery);
        var target = context.Storage.CatalogPath; var rollback = Path.Combine(context.Storage.StateRoot, ".rollback-" + Guid.NewGuid().ToString("N") + ".db");
        catalog.Require("Catalog", context.Storage.StateRoot, "AssetCatalog.db"); CatalogBoundary.Paths(context);
        if (plan.Active is null) File.Move(candidate, target, overwrite: false);
        else File.Replace(candidate, target, rollback, ignoreMetadataErrors: false);
        try
        {
            Observer?.Invoke("published");
            BackupStorage.VerifyFile(target, plan.Source.Size, plan.Source.Sha256);
            using var db = CatalogBoundary.Open(context, false);
        }
        catch
        {
            // Prior bytes (valid or corrupt) are retained locally as well as in ArchiveRoot.
            // Roll back atomically if the old file existed; preserve the failed replacement for diagnosis.
            if (plan.Active is { } previous)
            {
                BackupStorage.VerifyFile(rollback, previous.Size, previous.Sha);
                BackupStorage.Check(target);
                File.Replace(rollback, target, candidate, ignoreMetadataErrors: false);
            }
            throw BackupException.Stop(NapIssueCodes.RestorePublicationFailed, "Post-publication validation failed; any prior catalog was restored from the local rollback copy.");
        }
        if (plan.Active is { } old)
        { BackupStorage.VerifyFile(rollback, old.Size, old.Sha); File.Delete(rollback); }
        return new DatabaseRestoreResult(plan.Source.BackupId, safety, plan.ActiveState, recovery);
    });
    private static (ActiveCatalogState State, (long Size, Sha256Digest Sha)? Bytes) Active(UniverseContext c)
    {
        CatalogBoundary.Paths(c);
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
            if (BackupStorage.Check(c.Storage.CatalogPath + suffix) is not null)
                throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Active SQLite sidecars require explicit recovery before restore.");
        if (!CatalogBoundary.Regular(c.Storage.CatalogPath)) return (ActiveCatalogState.Missing, null);
        var bytes = BackupStorage.Fingerprint(c.Storage.CatalogPath); var state = ActiveCatalogState.Valid;
        try { ValidateActiveIdentity(c); BackupStorage.ValidateDatabase(c, c.Storage.CatalogPath); }
        catch (SqliteException e) when (e.SqliteErrorCode is 1 or 11 or 26) { state = ActiveCatalogState.Corrupt; }
        catch (CatalogException e) when (e.Issues.Issues.All(i => i.Code is NapIssueCodes.CatalogCorrupt or NapIssueCodes.CatalogIntegrityFailed or NapIssueCodes.CatalogInvalid))
        { state = ActiveCatalogState.Corrupt; }
        // Wrong universe, unsupported schema, busy/IO, invalid paths and sidecars remain STOP.
        BackupStorage.VerifyFile(c.Storage.CatalogPath, bytes.Size, bytes.Sha);
        return (state, bytes);
    }
    private static void ValidateActiveIdentity(UniverseContext c)
    {
        // Probe identity independently of damaged schema objects, so readable foreign metadata
        // cannot be treated as local corrupt evidence merely because another column/table is broken.
        using var db = CatalogBoundary.Connect(c.Storage.CatalogPath, SqliteOpenMode.ReadOnly);
        var version = Convert.ToInt64(CatalogSql.Scalar(db, null, "PRAGMA user_version"));
        if (version is not (0 or CatalogSchema.Version))
            throw CatalogException.Stop(NapIssueCodes.CatalogSchemaUnsupported, "Unknown active catalog version requires explicit review.");
        using var command = CatalogSql.Command(db, null, "SELECT universe_id FROM catalog_metadata WHERE singleton=1");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetValue(0) is string universe && universe != c.Id.Value)
                throw CatalogException.Stop(NapIssueCodes.CatalogWrongUniverse, "The active catalog belongs to another universe.");
    }
    private static void CheckPlan(UniverseContext c, DatabaseRestorePlan plan)
    {
        if (BackupStorage.Binding(c) != plan.Binding) throw BackupException.Stop(NapIssueCodes.RestoreStalePlan, "Restore plan belongs to a different context.");
        var current = BackupHistoryReader.Find(c, plan.Source.BackupId);
        if (current.Kind != BackupKind.Database || current.ManifestSha256 != plan.Source.ManifestSha256 || current.Sha256 != plan.Source.Sha256)
            throw BackupException.Stop(NapIssueCodes.RestoreStalePlan, "Selected backup changed after planning.");
        CatalogBoundary.Paths(c);
        var exists = CatalogBoundary.Regular(c.Storage.CatalogPath);
        if (exists != (plan.Active is not null) || (exists && (BackupStorage.Fingerprint(c.Storage.CatalogPath) != plan.Active || CatalogFileStamp.Capture(c.Storage.CatalogPath) != plan.Stamp)))
            throw BackupException.Stop(NapIssueCodes.RestoreStalePlan, "Active catalog changed after planning.");
        if (Active(c).State != plan.ActiveState) throw BackupException.Stop(NapIssueCodes.RestoreStalePlan, "Active catalog validation state changed after planning.");
    }
}
