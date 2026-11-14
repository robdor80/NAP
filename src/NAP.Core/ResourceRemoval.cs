using System.Text.Json;
using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

public sealed record RemovalSelection(string? AssetId = null, QueueItemId? Notice = null, bool IncludeInbox = false);
public sealed record RemovalEntry(string Path, bool Directory, bool Inbox = false);
public sealed record RemovalPlan(RemovalSelection Selection, string Name, string Identifier, IReadOnlyList<RemovalEntry> Entries,
    IReadOnlyList<string> Indexes, IReadOnlyList<string> Preserved, bool CanIncludeInbox, string Fingerprint);
public sealed record RemovalRequest(RemovalSelection Selection, string Fingerprint);
public sealed record RemovalResult(bool Removed, IReadOnlyList<string> Changes, IReadOnlyList<string> Problems);

/// <summary>Deletion runs on the universe owner. Known files are first moved on their own volume;
/// SQLite indexes are committed together, then the temporary files are permanently unlinked.</summary>
internal sealed partial class ResourceRemoval(UniverseContext context, AutomationQueueStore? queue = null,
    AutomationQueueOwner? owner = null, AutomationHostControlStore? controls = null, MixedUniverseInboxRouter? mixedInbox = null)
{
    private readonly UniverseStorageConfig _roots = context.Storage;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private string JournalPath => Path.Combine(_roots.StateRoot, "resource-removal.json");
    private string CoordinatorPath => RemovedInboxPackages.PathFor(_roots);

    private sealed class Inventory(RemovalSelection selection, string name, string identifier)
    {
        internal RemovalSelection Selection => selection;
        internal readonly List<RemovalEntry> Entries = [];
        internal readonly List<string> Indexes = [];
        internal readonly List<string> Preserved = [];
        internal readonly List<QueueItem> Items = [];
        internal readonly HashSet<JobId> Jobs = [];
        internal readonly HashSet<Sha256Digest> RemovedZipHashes = [];
        internal bool CanIncludeInbox;
        internal bool Native;
        internal bool Legacy;
        internal bool LegacyMaster;
        internal ArchiveMasterIndex? Archive;
        internal IReadOnlyList<string> MixedReceipts = [];
        internal RemovalPlan Plan()
        {
            var entries = Entries.DistinctBy(e => e.Path, Paths).OrderBy(e => e.Path, Paths).ToArray();
            var indexes = Indexes.Distinct().Order(StringComparer.Ordinal).ToArray();
            var preserved = Preserved.Distinct(Paths).Order(Paths).ToArray();
            var fingerprint = AutomationJson.Hash(new
            {
                selection, identifier, indexes, preserved,
                Items = Items.Select(i => new { i.Id, i.Revision, i.Zip }).ToArray(),
                Files = entries.Select(e => new { e, Modified = File.GetLastWriteTimeUtc(e.Path),
                    Size = e.Directory ? 0 : new FileInfo(e.Path).Length }).ToArray(),
                ZIP = RemovedZipHashes.Select(digest => digest.Hex).Order(StringComparer.Ordinal),
                Archive = Archive?.Entries
            }).Hex;
            return new(selection, name, identifier, entries, indexes, preserved, CanIncludeInbox, fingerprint);
        }
    }

    internal RemovalPlan Preview(RemovalSelection selection, IReadOnlyList<string> archived)
    {
        using var lease = CatalogBoundary.Acquire(context);
        return Inspect(selection, archived).Plan();
    }

    internal RemovalResult Remove(RemovalRequest request, IReadOnlyList<string> archived)
    {
        using var archiveLease = Directory.Exists(_roots.ArchiveRoot) ? ArchiveLock.Acquire(context) : null;
        using var productionLease = ExecutionMutex.Acquire("Production", _roots.ProductionRoot);
        using var catalogLease = CatalogBoundary.Acquire(context);
        Recover();
        var inventory = Inspect(request.Selection, archived);
        var plan = inventory.Plan();
        if (plan.Fingerprint != request.Fingerprint)
            return new(false, [], ["La lista cambió. Revisa el nuevo inventario antes de confirmar."]);

        var id = Guid.NewGuid().ToString("N");
        var entries = plan.Entries.Where(e => !plan.Entries.Any(parent =>
            parent.Directory && ProductionPaths.Within(parent.Path, e.Path))).ToArray();
        var moves = entries.Select(e => new Move(e.Path, Temp(e.Path, id), e.Directory)).ToArray();
        var archivePath = inventory.Archive is null ? null : new ArchiveMasterIndexStore(context).IndexPath;
        var journal = new Journal(id, moves, archivePath, inventory.MixedReceipts);
        var changes = plan.Entries.Select(e => e.Path).Concat(plan.Indexes).ToArray();
        RemovedInboxPackages.Initialize(_roots);
        using var queueIo = inventory.Items.Count > 0 ? ExecutionMutex.Acquire("AutomationQueueIO", _roots.StateRoot) : null;
        using var queueDb = inventory.Items.Count > 0 ? queue!.OpenRemoval(owner!) : null;
        using var preparationIo = inventory.Items.Count > 0 ? ExecutionMutex.Acquire("PreparationStateIO", _roots.StateRoot) : null;
        using var executionIo = inventory.Items.Count > 0 ? ExecutionMutex.Acquire("ExecutionStateIO", _roots.StateRoot) : null;
        using var db = CatalogBoundary.Connect(CoordinatorPath, SqliteOpenMode.ReadWrite);
        AttachDatabases(db, inventory);
        using var tx = db.BeginTransaction();
        UpdateDatabases(db, tx, inventory, plan.Selection.AssetId, journal.Id);
        WriteJournal(journal);
        try
        {
            foreach (var move in journal.Moves) MoveEntry(move.Source, move.Temporary, move.Directory);
            if (archivePath is not null)
            {
                archiveLease!.EnableWrites();
                ArchiveMasterIndexStore.Write(inventory.Archive!, NewIndex(journal));
                File.Replace(NewIndex(journal), archivePath, Temp(archivePath, id));
            }
            tx.Commit();
        }
        catch (Exception error)
        {
            tx.Dispose();
            try { Recover(db); }
            catch (Exception recovery) { return new(false, [], [error.Message, recovery.Message, "La recuperación sigue pendiente en " + JournalPath]); }
            return new(false, [], [error.Message]);
        }

        var problems = new List<string>();
        try { Recover(db); }
        catch (Exception error) { problems.Add(error.Message + " Recuperación pendiente en " + JournalPath); }
        return new(true, changes, problems);
    }

    internal void RecoverPending()
    {
        if (!CatalogBoundary.Regular(JournalPath)) return;
        using var archiveLease = Directory.Exists(_roots.ArchiveRoot) ? ArchiveLock.Acquire(context) : null;
        using var productionLease = ExecutionMutex.Acquire("Production", _roots.ProductionRoot);
        using var catalogLease = CatalogBoundary.Acquire(context);
        Recover();
    }

    private void Recover()
    {
        if (!CatalogBoundary.Regular(JournalPath)) return;
        using var db = CatalogBoundary.Connect(CoordinatorPath, SqliteOpenMode.ReadWrite);
        // Opening attached databases can finish a SQLite super-journal rollback after a crash.
        foreach (var path in RecoveryDatabases())
            if (CatalogBoundary.Regular(path)) using (CatalogBoundary.Connect(path, SqliteOpenMode.ReadWrite)) { }
        Recover(db);
    }

    private IEnumerable<string> RecoveryDatabases() =>
        [_roots.CatalogPath, new LegacyPortraitIndex(context).DatabasePath,
            new LegacyMasterEvidenceIndex(context).DatabasePath, new PreparationStateStore(_roots).DatabasePath,
            new ExecutionStateStore(_roots).DatabasePath, AutomationQueueBoundary.DatabasePath(_roots), controls?.DatabasePath ?? CoordinatorPath];

    private void Recover(SqliteConnection db)
    {
        if (!CatalogBoundary.Regular(JournalPath)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(JournalPath))
            ?? throw new IOException("Inventario de recuperación ilegible.");
        if (!Guid.TryParseExact(journal.Id, "N", out _) || journal.Moves.Any(move => move.Temporary != Temp(move.Source, journal.Id)) ||
            journal.ArchivePath is not null && journal.ArchivePath != new ArchiveMasterIndexStore(context).IndexPath)
            throw new IOException("Inventario de recuperación inválido.");
        var committed = Scalar(db, null, "SELECT 1 FROM removal_commit WHERE id=$id", "$id", journal.Id) is not null;
        foreach (var move in journal.Moves.Reverse())
        {
            if (ProductionPaths.CheckPath(move.Temporary, NapIssueCodes.CatalogInvalid) is null) continue;
            if (committed) DeleteTemporary(move.Temporary, move.Directory);
            else MoveEntry(move.Temporary, move.Source, move.Directory);
        }
        if (journal.ArchivePath is { } archive)
        {
            var before = Temp(archive, journal.Id);
            if (CatalogBoundary.Regular(before))
            {
                if (committed) File.Delete(before);
                else File.Move(before, archive, overwrite: true);
            }
            if (CatalogBoundary.Regular(NewIndex(journal))) File.Delete(NewIndex(journal));
        }
        if (committed) mixedInbox?.Forget(context.Id, journal.MixedReceipts);
        File.Delete(JournalPath);
        if (committed) Execute(db, null, "DELETE FROM removal_commit WHERE id=$id", "$id", journal.Id);
    }

    private sealed record Move(string Source, string Temporary, bool Directory);
    private sealed record Journal(string Id, Move[] Moves, string? ArchivePath, IReadOnlyList<string> MixedReceipts);
    private static string Temp(string path, string id) => path + ".nap-remove-" + id;
    private static string NewIndex(Journal journal) => Temp(journal.ArchivePath!, journal.Id) + ".new";
    private void WriteJournal(Journal journal)
    {
        var temporary = Temp(JournalPath, journal.Id);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, journal);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, JournalPath);
    }
    private static void MoveEntry(string source, string destination, bool directory)
    {
        if (ProductionPaths.CheckPath(source, NapIssueCodes.CatalogInvalid) is null)
            throw new IOException("Falta " + source);
        try
        {
            if (directory) Directory.Move(source, destination);
            else File.Move(source, destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new IOException("No se pudo mover " + source + ". " + error.Message, error); }
    }
    private static void DeleteTemporary(string path, bool directory)
    {
        try
        {
            if (directory)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                {
                    var attributes = ProductionPaths.CheckPath(entry, NapIssueCodes.CatalogInvalid)!.Value;
                    DeleteTemporary(entry, attributes.HasFlag(FileAttributes.Directory));
                }
                Directory.Delete(path);
            }
            else
            {
                var attr = File.GetAttributes(path);
                if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new IOException("No se pudo retirar " + path + ". " + error.Message, error); }
    }
}
