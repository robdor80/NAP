using System.Text.Json;
using Microsoft.Data.Sqlite;
using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

internal sealed partial class ResourceRemoval
{
    private Inventory Inspect(RemovalSelection selection, IReadOnlyList<string> archived)
    {
        if ((selection.AssetId is null) == (selection.Notice is null))
            throw new ArgumentException("Selecciona exactamente un recurso o aviso.");
        CatalogBoundary.Paths(context);
        var indexes = new LegacyPortraitIndex(context);
        Inventory inventory;
        if (selection.AssetId is { } assetId)
        {
            if (!AssetNamingRules.IsValidAssetId(assetId))
                throw new ArgumentException("Identificador de recurso inválido.");
            inventory = new(selection, assetId.Replace('_', ' '), assetId);
            AddAsset(inventory, assetId, indexes);
        }
        else
        {
            var item = queue?.Get(selection.Notice!) ?? throw new InvalidOperationException("La cola no está disponible.");
            if (!archived.Contains(item.Id.Value) || !Incident(item.State))
                throw new InvalidOperationException("Archiva el aviso antes de eliminarlo.");
            var identity = FindAsset(item, queue!.RemovalAttempts(item.Id));
            inventory = new(selection, (identity ?? Path.GetFileNameWithoutExtension(item.SourcePath)).Replace('_', ' '), item.Id.Value);
            AddItem(inventory, item, identity, includeJobPublication: false);
        }
        CheckDependencies(inventory);
        if (inventory.RemovedZipHashes.Count > 0)
            inventory.Indexes.Add("Huella del ZIP para evitar que vuelva a aparecer desde Entrada.");
        return inventory;
    }

    private void AddAsset(Inventory inventory, string assetId, LegacyPortraitIndex legacyIndex)
    {
        CatalogAssetSnapshot? native = null;
        if (CatalogBoundary.Regular(_roots.CatalogPath))
        {
            using var db = CatalogBoundary.Open(context, false);
            native = CatalogAssetData.Read(db, null, context.Id, assetId);
        }
        var legacy = legacyIndex.Get(assetId);
        if (native is null && legacy is null) throw new InvalidOperationException("El recurso ya no figura en el catálogo.");
        if (native is not null && legacy is not null) throw new InvalidOperationException("El identificador figura en dos índices. Revisa su procedencia.");

        if (native is not null)
        {
            inventory.Native = true;
            inventory.Indexes.Add("Ficha, documentos y referencias en " + _roots.CatalogPath);
            var index = new ArchiveMasterIndexStore(context);
            var archive = index.Load();
            var entry = archive.Entries.SingleOrDefault(e => e.AssetId == assetId);
            if (archive.Entries.Any(other => other.AssetId != assetId &&
                    ProductionPaths.Overlaps(ProductionPaths.Resolve(_roots.ArchiveRoot, other.RelativeDirectory),
                        ProductionPaths.Resolve(_roots.ArchiveRoot, native.ArchiveRelativeDirectory))))
                throw new InvalidOperationException("Otro recurso comparte la carpeta del maestro.");
            if (entry is not null)
            {
                if (entry.RelativeDirectory != native.ArchiveRelativeDirectory || entry.MasterSha256 != native.MasterDigest)
                    throw new InvalidOperationException("El maestro del archivo y la ficha apuntan a recursos distintos.");
                inventory.Archive = new(context.Id, archive.Entries.Where(e => e.AssetId != assetId));
                inventory.Indexes.Add("Entrada del maestro en " + index.IndexPath);
            }
            foreach (var group in native.Files.GroupBy(f => f.Location))
            {
                var root = group.Key == CatalogFileLocation.Production ? _roots.ProductionRoot : _roots.ArchiveRoot;
                var folder = group.Key == CatalogFileLocation.Production ? native.ProductionRelativeDirectory : native.ArchiveRelativeDirectory;
                var directory = ProductionPaths.Resolve(root, folder);
                foreach (var file in group) AddFile(inventory, ProductionPaths.Resolve(root, file.RelativePath), file.Digest, file.SizeBytes);
                AddEmptyDirectory(inventory, directory);
            }
            var webp = native.Files.Single(f => f.Kind == "generated_webp");
            var summary = new CatalogAssetSummary(native.AssetKey, native.AssetType, native.ProductionProfile, webp.RelativePath, webp.Digest, webp.SizeBytes);
            var key = new ProductionThumbnailCache(context).CacheKey(summary);
            AddFile(inventory, Path.Combine(_roots.CacheRoot, "thumbnails", key + ".png"));
            AddManualProduction(inventory, assetId, native.ProductionRelativeDirectory);
        }
        else
        {
            inventory.Legacy = true;
            inventory.Indexes.Add("Ficha en " + legacyIndex.DatabasePath);
            var directory = Path.GetDirectoryName(legacy!.WebpPath)!;
            AddFile(inventory, legacy.WebpPath, new(legacy.WebpSha256), legacy.WebpBytes);
            foreach (var file in new[] { legacy.InfoPath, legacy.PromptPath, legacy.VisualIdentityPath })
                AddFile(inventory, file);
            AddEmptyDirectory(inventory, directory);
            AddFile(inventory, Path.Combine(_roots.CacheRoot, "legacy-thumbnails", assetId + "-" + legacy.WebpSha256 + ".png"));
            var evidence = new LegacyMasterEvidenceIndex(context);
            if (evidence.Get(assetId) is { } master)
            {
                inventory.LegacyMaster = true;
                inventory.Indexes.Add("Evidencia del maestro en " + evidence.DatabasePath);
                AddFile(inventory, evidence.SourcePath(master), new(master.PngSha256), master.SizeBytes);
            }
        }

        if (queue is not null)
        {
            for (var offset = 0; ; offset += 100)
            {
                var page = queue.List(offset, 100);
                foreach (var item in page)
                {
                    if (FindAsset(item, queue.RemovalAttempts(item.Id)) != assetId) continue;
                    if (item.State is not (QueueState.Completed or QueueState.Duplicate) && !Incident(item.State))
                        throw new InvalidOperationException("Hay un trabajo activo para este recurso. Espera a que termine o archiva su aviso.");
                    AddItem(inventory, item, assetId, includeJobPublication: true);
                }
                if (page.Count < 100) break;
            }
        }
        AddNormalization(inventory, assetId, null);
    }

    private static bool Incident(QueueState state) => state is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource;

    private string? FindAsset(QueueItem item, IReadOnlyList<WorkflowAttempt> attempts)
    {
        var evidence = new InboxAdmissionEvidence(context, queue!);
        foreach (var attempt in attempts.Reverse())
        {
            if (attempt.ExpectedEvidence is null) continue;
            var receipt = evidence.Load(item, attempt);
            if (receipt.AssetId is { } asset && receipt.DeclaredUniverse == context.Id.Value) return asset;
        }
        return null;
    }

    private void AddItem(Inventory inventory, QueueItem item, string? asset, bool includeJobPublication)
    {
        inventory.Items.Add(item);
        inventory.RemovedZipHashes.Add(item.Zip.Hash);
        inventory.Indexes.Add("Aviso o trabajo " + item.Id.Value + " y su actividad en " + queue!.DatabasePath);
        if (controls is not null) inventory.Indexes.Add("Archivados y controles asociados en " + controls.DatabasePath);
        var prep = new PreparationStateStore(_roots);
        var execution = new ExecutionStateStore(_roots);
        if (CatalogBoundary.Regular(prep.DatabasePath) && prep.Get(item.Id) is not null)
            inventory.Indexes.Add("Preparación y auditoría en " + prep.DatabasePath);
        var executed = CatalogBoundary.Regular(execution.DatabasePath) ? execution.Get(item.Id) : null;
        if (executed is not null)
        {
            if (!includeJobPublication)
                throw new InvalidOperationException("El aviso tiene una ejecución de producción asociada. Revisa el recurso en el catálogo.");
            inventory.Indexes.Add("Ejecución en " + execution.DatabasePath);
        }

        var evidence = new InboxAdmissionEvidence(context, queue);
        foreach (var attempt in queue.RemovalAttempts(item.Id))
        {
            if (attempt.StagingRelativePath is not null && attempt.ExtractionRelativePath is not null)
            {
                var operation = evidence.OperationRoot(attempt);
                var itemDirectory = Path.Combine(_roots.StagingRoot, "automation-admission", item.Id.Value);
                if (!ProductionPaths.Within(itemDirectory, operation))
                    throw new IOException("La carpeta de preparación no es exclusiva del aviso.");
                AddTree(inventory, operation);
                AddEmptyDirectory(inventory, itemDirectory);
            }
            var provenance = Path.Combine(_roots.StateRoot, "production-executions", attempt.JobId.Value + ".json");
            if (CatalogBoundary.Regular(provenance) && !includeJobPublication)
                throw new InvalidOperationException("El aviso conserva un recibo de publicación. Revisa el recurso en el catálogo.");
            AddFile(inventory, provenance);
            AddFile(inventory, Path.Combine(_roots.StateRoot, attempt.JobId.Value + ".json"));
            inventory.Jobs.Add(attempt.JobId);
        }
        foreach (var relative in new[] { "admission", "originals", "preparation/packages", "preparation/audit" })
            AddTree(inventory, ProductionPaths.Resolve(_roots.StateRoot, "automation/" + relative + "/" + item.Id.Value));
        foreach (var decision in queue.RemovalDecisionIds(item.Id))
        {
            if (!ProductionPaths.SafeSegment(decision)) throw new IOException("Identificador de vista previa inválido.");
            AddTree(inventory, ProductionPaths.Resolve(_roots.StateRoot, "automation/preparation/previews/" + decision));
        }

        var sources = queue.Observations(item.Id).Select(o => o.SourcePath).Append(item.SourcePath).Distinct(Paths);
        var names = sources.Select(Path.GetFileName).OfType<string>().ToArray();
        var receipts = mixedInbox?.FindReceipts(context.Id, names) ?? [];
        foreach (var source in sources)
        {
            if (receipts.Any(r => ProductionPaths.Same(r.Copy, source)) ||
                Path.GetFileName(source) == "nap-" + item.Zip.Hash.Hex + ".zip" && mixedInbox is not null)
                AddMatchingZip(inventory, source, item.Zip, remove: true, inbox: false);
            else AddMatchingZip(inventory, source, item.Zip, inventory.Selection.IncludeInbox, inbox: true);
        }
        foreach (var receipt in receipts)
        {
            AddMatchingZip(inventory, receipt.Original, item.Zip, inventory.Selection.IncludeInbox, inbox: true);
            inventory.MixedReceipts = inventory.MixedReceipts.Append(Path.GetFileName(receipt.Copy)).Distinct(Paths).ToArray();
        }
        if (receipts.Count > 0) inventory.Indexes.Add("Recibo de recepción en " + mixedInbox!.HistoryPath);
        if (asset is not null && !includeJobPublication) AddNormalization(inventory, asset, item.Zip.Hash);
    }

    private static void AddMatchingZip(Inventory inventory, string path, QueueZipIdentity zip, bool remove, bool inbox)
    {
        if (!CatalogBoundary.Regular(path)) return;
        using (var stream = File.OpenRead(path))
        {
            if (stream.Length != zip.SizeBytes || new Sha256Hasher().Compute(stream) != zip.Hash)
            {
                inventory.Preserved.Add(path + " (el ZIP actual tiene otros bytes)");
                return;
            }
        }
        if (remove) inventory.Entries.Add(new(path, false, inbox));
        else
        {
            inventory.Preserved.Add(path);
            inventory.CanIncludeInbox |= inbox;
        }
    }

    private static void AddFile(Inventory inventory, string path, Sha256Digest? hash = null, long size = 0)
    {
        if (!CatalogBoundary.Regular(path)) return;
        if (hash is not null) ProductionPaths.Verify(path, hash, size, NapIssueCodes.ProductionSourceChanged);
        inventory.Entries.Add(new(path, false));
    }

    private static void AddEmptyDirectory(Inventory inventory, string path)
    {
        if (!IsDirectory(path)) return;
        if (Directory.EnumerateFileSystemEntries(path).All(entry => inventory.Entries.Any(e => Paths.Equals(e.Path, entry))))
            inventory.Entries.Add(new(path, true));
        else inventory.Preserved.Add(path + " (contiene otros archivos)");
    }

    private static bool IsDirectory(string path)
    {
        var attrs = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogInvalid);
        if (attrs is null) return false;
        if (!attrs.Value.HasFlag(FileAttributes.Directory))
            throw new IOException("Se esperaba una carpeta en " + path);
        return true;
    }

    private static void AddTree(Inventory inventory, string path)
    {
        if (!IsDirectory(path)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if (!ProductionPaths.SafeSegment(Path.GetFileName(child)) || Path.GetFileName(child).Equals(".git", StringComparison.OrdinalIgnoreCase))
                throw new IOException("La carpeta contiene una ruta no admitida: " + child);
            var attributes = ProductionPaths.CheckPath(child, NapIssueCodes.CatalogInvalid)!.Value;
            if (attributes.HasFlag(FileAttributes.Directory)) AddTree(inventory, child);
            else AddFile(inventory, child);
        }
        inventory.Entries.Add(new(path, true));
    }

    private void AddManualProduction(Inventory inventory, string assetId, string productionDirectory)
    {
        var dir = Path.Combine(_roots.StateRoot, "production-executions");
        if (!IsDirectory(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "job_*.json"))
        {
            if (!CatalogBoundary.Regular(file)) continue;
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var receipt = document.RootElement;
            if (receipt.GetProperty("universe_id").GetString() != context.Id.Value ||
                receipt.GetProperty("asset_id").GetString() != assetId ||
                receipt.GetProperty("relative_directory").GetString() != productionDirectory) continue;
            var job = new JobId(receipt.GetProperty("job_id").GetString()!);
            if (Path.GetFileNameWithoutExtension(file) != job.Value) throw new IOException("Recibo de publicación ambiguo: " + file);
            inventory.Jobs.Add(job);
            AddFile(inventory, file);
            AddFile(inventory, Path.Combine(_roots.StateRoot, job.Value + ".json"));
        }
    }

    private void AddNormalization(Inventory inventory, string assetId, Sha256Digest? zip)
    {
        var dir = Path.Combine(_roots.WorkspaceRoot, "normalization");
        if (!IsDirectory(dir)) return;
        IEnumerable<string> Operations(string root) => IsDirectory(root) ? Directory.EnumerateDirectories(root) : [];
        foreach (var operation in Operations(dir).Concat(Operations(Path.Combine(dir, "failures"))))
        {
            var receiptPath = Path.Combine(operation, "control", "receipt.json");
            if (!CatalogBoundary.Regular(receiptPath)) continue;
            using var document = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
            var receipt = document.RootElement;
            if (receipt.GetProperty("universe_id").GetString() != context.Id.Value ||
                receipt.GetProperty("asset_id").GetString() != assetId ||
                zip is not null && !string.Equals(receipt.GetProperty("source_sha256").GetString(), zip.Hex, StringComparison.OrdinalIgnoreCase)) continue;
            var source = receipt.GetProperty("source_path").GetString();
            if (zip is null && receipt.GetProperty("is_package").GetBoolean() && source is not null &&
                Path.IsPathFullyQualified(source) && ProductionPaths.SafeSegment(Path.GetFileName(source)) &&
                Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                (ProductionPaths.Same(Path.GetDirectoryName(source)!, _roots.InboxRoot) || mixedInbox?.IsOriginal(source) == true))
            {
                var digest = new Sha256Digest(receipt.GetProperty("source_sha256").GetString()!);
                inventory.RemovedZipHashes.Add(digest);
                if (CatalogBoundary.Regular(source))
                    AddMatchingZip(inventory, source, new(digest, new FileInfo(source).Length), inventory.Selection.IncludeInbox, inbox: true);
            }
            var id = receipt.GetProperty("operation_id").GetString();
            if (!Guid.TryParseExact(id, "N", out _) ||
                !(Path.GetFileName(operation) == id || Path.GetFileName(operation).StartsWith(id + "-", StringComparison.Ordinal)))
                throw new IOException("Recibo de normalización ambiguo: " + receiptPath);
            AddTree(inventory, operation);
            AddTree(inventory, Path.Combine(_roots.WorkspaceRoot, "packages", "normalization", id!));
            AddTree(inventory, Path.Combine(_roots.StagingRoot, "normalization", id!));
        }
    }

    private void CheckDependencies(Inventory inventory)
    {
        if (inventory.Items.Count == 0 && !inventory.Native) return;
        var ids = inventory.Items.Select(i => i.Id).ToHashSet();
        var prep = new PreparationStateStore(_roots);
        if (ids.Count > 0 && CatalogBoundary.Regular(prep.DatabasePath) &&
            prep.List().Any(row => row.DuplicateOf is { } other && ids.Contains(other) && !ids.Contains(row.ItemId)))
            throw new InvalidOperationException("Otro trabajo depende de este como original de un duplicado.");
        var outbox = new GitOutboxStore(context).Read();
        if (outbox.Intents.Any(i => ids.Contains(i.ItemId) || inventory.Jobs.Contains(i.JobId)))
            throw new InvalidOperationException("Hay una publicación Git vinculada al recurso. Revisa su expediente en Git antes de retirarlo.");
        if (inventory.Native && GitProductionReceiptStore.List(context).Any(id =>
            GitProductionReceiptStore.Read(context, id).Plan.Selection.OwnedFiles.Any(file =>
                inventory.Entries.Any(e => !e.Directory && ProductionPaths.Same(e.Path, ProductionPaths.Resolve(_roots.ProductionRoot, file.RelativePath))))))
            throw new InvalidOperationException("Un recibo Git comparte los archivos seleccionados. Revisa su expediente antes de retirarlos.");
    }

    private void AttachDatabases(SqliteConnection db, Inventory inventory)
    {
        if (inventory.Native) Attach("catalog", _roots.CatalogPath);
        if (inventory.Legacy) Attach("legacy", new LegacyPortraitIndex(context).DatabasePath);
        if (inventory.LegacyMaster) Attach("masters", new LegacyMasterEvidenceIndex(context).DatabasePath);
        if (inventory.Items.Count == 0) return;
        Attach("work", queue!.DatabasePath);
        var prep = new PreparationStateStore(_roots).DatabasePath;
        if (CatalogBoundary.Regular(prep)) Attach("prep", prep);
        var execution = new ExecutionStateStore(_roots).DatabasePath;
        if (CatalogBoundary.Regular(execution)) Attach("execution", execution);
        if (controls is not null) Attach("host", controls.DatabasePath);

        void Attach(string schema, string path)
        {
            if (!CatalogBoundary.Regular(path)) throw new IOException("Falta el índice " + path);
            Execute(db, null, "ATTACH DATABASE $file AS " + schema, "$file", path);
            if (!string.Equals((string?)Scalar(db, null, "PRAGMA " + schema + ".journal_mode"), "delete", StringComparison.OrdinalIgnoreCase))
                throw new IOException("El índice debe estar en modo SQLite DELETE: " + path);
        }
    }

    private void UpdateDatabases(SqliteConnection db, SqliteTransaction tx, Inventory inventory, string? asset, string commit)
    {
        if (inventory.Native)
        {
            Execute(db, tx, "DELETE FROM catalog.campaign_objectives WHERE universe_id=$u AND objective_id IN (SELECT objective_id FROM catalog.objectives WHERE universe_id=$u AND asset_id=$a)", "$u", context.Id.Value, "$a", asset);
            Execute(db, tx, "DELETE FROM catalog.objectives WHERE universe_id=$u AND asset_id=$a", "$u", context.Id.Value, "$a", asset);
            Execute(db, tx, "DELETE FROM catalog.assets WHERE universe_id=$u AND asset_id=$a", "$u", context.Id.Value, "$a", asset);
        }
        if (inventory.Legacy) Execute(db, tx, "DELETE FROM legacy.portraits WHERE asset_id=$a", "$a", asset);
        if (inventory.LegacyMaster) Execute(db, tx, "DELETE FROM masters.masters WHERE asset_id=$a", "$a", asset);
        foreach (var digest in inventory.RemovedZipHashes)
            Execute(db, tx, "INSERT OR IGNORE INTO removed_zip VALUES($h)", "$h", digest.Hex);
        if (inventory.Items.Count > 0)
        {
            Execute(db, tx, "INSERT INTO event_floor VALUES(1,(SELECT coalesce(max(rowid),0) FROM work.events)) ON CONFLICT(id) DO UPDATE SET value=max(event_floor.value,excluded.value)");
            var hasPrep = File.Exists(new PreparationStateStore(_roots).DatabasePath);
            var hasExecution = File.Exists(new ExecutionStateStore(_roots).DatabasePath);
            foreach (var item in inventory.Items)
            {
                if (hasPrep)
                {
                    Execute(db, tx, "DELETE FROM prep.previews WHERE decision IN (SELECT decision_id FROM work.decisions WHERE item_id=$i)", "$i", item.Id.Value);
                    Execute(db, tx, "DELETE FROM prep.history WHERE item=$i", "$i", item.Id.Value);
                    Execute(db, tx, "DELETE FROM prep.workflows WHERE item=$i", "$i", item.Id.Value);
                }
                if (hasExecution)
                {
                    Execute(db, tx, "DELETE FROM execution.history WHERE item=$i", "$i", item.Id.Value);
                    Execute(db, tx, "DELETE FROM execution.workflows WHERE item=$i", "$i", item.Id.Value);
                }
                AutomationQueueStore.RemoveItem(db, tx, item);
            }
            controls?.ForgetItems(db, tx, context.Id, inventory.Items.Select(i => i.Id.Value).ToHashSet(StringComparer.Ordinal));
        }
        Execute(db, tx, "INSERT INTO removal_commit VALUES($id)", "$id", commit);
    }
}

public sealed partial class AutomationQueueStore
{
    internal IReadOnlyList<string> RemovalDecisionIds(QueueItemId id) => Read(db =>
    {
        using var cmd = Command(db, null, "SELECT DISTINCT decision_id FROM decisions WHERE item_id=$i", "$i", id.Value);
        using var rows = cmd.ExecuteReader();
        var results = new List<string>();
        while (rows.Read()) results.Add(rows.GetString(0));
        return results;
    });
}
