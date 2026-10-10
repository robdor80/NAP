using System.Text;

namespace NAP.Core;

internal sealed class InboxAdmissionEvidence(UniverseContext context, AutomationQueueStore queue)
{
    internal string OperationRoot(WorkflowAttempt attempt)
    {
        var snapshot = ProductionPaths.Resolve(context.Storage.StagingRoot, attempt.StagingRelativePath!);
        var extracted = ProductionPaths.Resolve(context.Storage.StagingRoot, attempt.ExtractionRelativePath!);
        if (!ProductionPaths.Same(Path.GetDirectoryName(snapshot)!, Path.GetDirectoryName(extracted)!))
            throw AutomationValidation.Corrupt("Admission attempt paths do not share an operation boundary.");
        return Path.GetDirectoryName(snapshot)!;
    }
    internal string SnapshotPath(WorkflowAttempt attempt) => Path.Combine(OperationRoot(attempt), "snapshot", "package.zip");
    internal string ExtractionRoot(WorkflowAttempt attempt) => Path.Combine(OperationRoot(attempt), "extracted");
    private string ReceiptDirectory(WorkflowAttempt attempt) => Path.Combine(context.Storage.StateRoot, "automation", "admission", attempt.ItemId.Value, attempt.Id.Value);
    internal static void DirectoryBoundary(string path, bool create = false)
    {
        var attributes = ProductionPaths.CheckPath(path, NapIssueCodes.PackageRootInvalid);
        if (attributes is null && create)
        {
            var parent = Path.GetDirectoryName(path)!; DirectoryBoundary(parent);
            ProductionPaths.CheckCasing(parent, Path.GetFileName(path)); Directory.CreateDirectory(path);
            attributes = ProductionPaths.CheckPath(path, NapIssueCodes.PackageRootInvalid);
        }
        if (attributes is null || (attributes & FileAttributes.Directory) == 0) throw new IOException("A controlled admission directory is unavailable.");
    }
    internal static void EnsureParents(string path, string boundary)
    {
        DirectoryBoundary(boundary);
        var relative = Path.GetRelativePath(boundary, path).Replace(Path.DirectorySeparatorChar, '/');
        ProductionPaths.Resolve(boundary, relative);
        var current = boundary;
        foreach (var part in relative.Split('/')) { current = Path.Combine(current, part); DirectoryBoundary(current, true); }
    }
    internal AdmissionReceipt Start(AutomationQueueOwner owner, QueueItem item, WorkflowAttempt attempt)
    {
        var dir = ReceiptDirectory(attempt);
        if (ProductionPaths.CheckPath(dir, NapIssueCodes.PackageRootInvalid) is not null ||
            ProductionPaths.CheckPath(OperationRoot(attempt), NapIssueCodes.PackageRootInvalid) is not null)
            throw new InvalidDataException("A reserved admission namespace already has unknown contents.");
        EnsureParents(dir, Path.Combine(context.Storage.StateRoot, "automation"));
        var receipt = new AdmissionReceipt(1, item.Id, attempt.Id, attempt.JobId, context.Id, item.Zip, item.SourcePath,
            Path.GetRelativePath(context.Storage.StagingRoot, SnapshotPath(attempt)).Replace(Path.DirectorySeparatorChar, '/'),
            attempt.ExtractionRelativePath!, AdmissionCheckpoint.Intent, DateTimeOffset.UtcNow, null, [], null, null, InboxAdmissionCode.WaitingStable);
        return Publish(owner, item, attempt, receipt);
    }
    internal AdmissionReceipt Advance(AutomationQueueOwner owner, AdmissionReceipt prior, AdmissionCheckpoint checkpoint,
        IReadOnlyList<AdmissionFile>? files = null, string? declared = null, string? assetId = null,
        string? extractionRelative = null, InboxAdmissionCode code = InboxAdmissionCode.Admitted)
    {
        var item = queue.Get(prior.ItemId); var attempt = queue.GetAttempt(prior.AttemptId);
        var value = prior with { Version = prior.Version + 1, PreviousHash = AutomationJson.Hash(prior), Checkpoint = checkpoint,
            Files = files ?? prior.Files, DeclaredUniverse = declared ?? prior.DeclaredUniverse, AssetId = assetId ?? prior.AssetId,
            ExtractionRelativePath = extractionRelative ?? prior.ExtractionRelativePath, Utc = DateTimeOffset.UtcNow, Code = code };
        return Publish(owner, item, attempt, value);
    }
    private AdmissionReceipt Publish(AutomationQueueOwner owner, QueueItem item, WorkflowAttempt attempt, AdmissionReceipt receipt)
    {
        if (receipt.Version > 16) throw new InvalidDataException("Admission checkpoint inventory exceeded its bound.");
        var directory = ReceiptDirectory(attempt); DirectoryBoundary(directory);
        var path = Path.Combine(directory, receipt.Version.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".json");
        if (ProductionPaths.CheckPath(path, NapIssueCodes.PackageRootInvalid) is not null) throw new InvalidDataException("Admission receipt already exists.");
        var bytes = Encoding.UTF8.GetBytes(AutomationJson.Encode(receipt));
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        DirectoryBoundary(directory); File.Move(temp, path, false);
        queue.RecordAdmissionEvidence(owner, item.Id, item.Revision, attempt.Id, AutomationJson.HashBytes(bytes));
        return receipt;
    }
    internal AdmissionReceipt Load(QueueItem item, WorkflowAttempt attempt)
    {
        if (attempt.ExpectedEvidence is null) throw new InvalidDataException("Admission intent has no bound durable receipt.");
        var directory = ReceiptDirectory(attempt); DirectoryBoundary(directory);
        AdmissionReceipt? previous = null; Sha256Digest? previousHash = null;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!ProductionPaths.FileExists(entry, NapIssueCodes.PackageStructureInvalid) || new FileInfo(entry).Length > 2 * 1024 * 1024)
                throw new InvalidDataException("Receipt namespace has unknown or excessive artifacts.");
            var bytes = File.ReadAllBytes(entry); var value = AutomationJson.Decode<AdmissionReceipt>(Encoding.UTF8.GetString(bytes));
            if (string.IsNullOrWhiteSpace(value.SnapshotRelativePath) || string.IsNullOrWhiteSpace(value.ExtractionRelativePath) ||
                value.Files is null || value.Files.Any(f => f is null || f.Hash is null || string.IsNullOrWhiteSpace(f.RelativePath)))
                throw new InvalidDataException("Admission receipt has missing paths or inventory fields.");
            AutomationValidation.Defined(value.Checkpoint); AutomationValidation.Defined(value.Code); AutomationValidation.Utc(value.Utc);
            if (value.Version != (previous?.Version ?? 0) + 1 || value.Version > 16 || value.PreviousHash != previousHash ||
                Path.GetFileName(entry) != value.Version.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + ".json" ||
                value.ItemId != item.Id || value.AttemptId != attempt.Id || value.JobId != attempt.JobId || value.UniverseId != context.Id ||
                value.Zip != item.Zip || value.SourcePath != item.SourcePath ||
                !ProductionPaths.Same(ProductionPaths.Resolve(context.Storage.StagingRoot, value.SnapshotRelativePath), SnapshotPath(attempt)) ||
                !(ProductionPaths.Same(ProductionPaths.Resolve(context.Storage.StagingRoot, value.ExtractionRelativePath), ExtractionRoot(attempt)) ||
                  ProductionPaths.Within(ExtractionRoot(attempt), ProductionPaths.Resolve(context.Storage.StagingRoot, value.ExtractionRelativePath))))
                throw new InvalidDataException("Admission receipt identity, scope or chain mismatch.");
            if (previous is null && value.Checkpoint != AdmissionCheckpoint.Intent || previous is not null && !CanAdvance(previous.Checkpoint, value.Checkpoint))
                throw new InvalidDataException("Admission checkpoint order is inconsistent.");
            if (value.Files.Count > 10000 || value.Files.Any(f => !ProductionPaths.SafeSegment(f.RelativePath) || f.SizeBytes < 0) ||
                value.Files.Select(f => f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Files.Count)
                throw new InvalidDataException("Invalid admission file inventory.");
            previous = value; previousHash = AutomationJson.HashBytes(bytes);
        }
        if (previous is null || previousHash != attempt.ExpectedEvidence) throw new InvalidDataException("Unbound receipt publication needs review.");
        return previous;
    }
    private static bool CanAdvance(AdmissionCheckpoint from, AdmissionCheckpoint to) =>
        from == AdmissionCheckpoint.Intent && to == AdmissionCheckpoint.SnapshotReady ||
        from == AdmissionCheckpoint.SnapshotReady && to == AdmissionCheckpoint.ExtractionReady ||
        from == AdmissionCheckpoint.ExtractionReady && to == AdmissionCheckpoint.JournalIntent ||
        from == AdmissionCheckpoint.JournalIntent && to == AdmissionCheckpoint.Admitted ||
        from is not (AdmissionCheckpoint.Admitted or AdmissionCheckpoint.Rejected or AdmissionCheckpoint.NeedsReview) && to is AdmissionCheckpoint.Rejected or AdmissionCheckpoint.NeedsReview;
    internal IReadOnlyList<AdmissionFile> Inventory(string path, InboxAdmissionOptions options, bool flush)
    {
        DirectoryBoundary(path); var files = new List<AdmissionFile>(); long total = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (files.Count >= options.Extraction.MaxEntries || !ProductionPaths.FileExists(entry, NapIssueCodes.PackageStructureInvalid))
                throw new InvalidDataException("Admission envelope must be flat and bounded.");
            using var input = new FileStream(entry, FileMode.Open, flush ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
            var size = input.Length;
            if (size < 0 || size > options.Extraction.MaxEntryBytes || size > options.Extraction.MaxTotalBytes - total)
                throw new InvalidDataException("Admission envelope exceeds limits.");
            total += size; var hash = new Sha256Hasher().Compute(input); if (input.Length != size) throw new InvalidDataException("Admission file changed.");
            if (flush) input.Flush(true);
            files.Add(new(Path.GetFileName(entry), hash, size));
        }
        return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
    }
    internal void Verify(AdmissionReceipt receipt, WorkflowAttempt attempt, InboxAdmissionOptions options)
    {
        DirectoryBoundary(OperationRoot(attempt));
        ProductionPaths.Verify(SnapshotPath(attempt), receipt.Zip.Hash, receipt.Zip.SizeBytes, NapIssueCodes.PackageStructureInvalid);
        if (receipt.Checkpoint is AdmissionCheckpoint.ExtractionReady or AdmissionCheckpoint.JournalIntent or AdmissionCheckpoint.Admitted)
        {
            var extracted = ProductionPaths.Resolve(context.Storage.StagingRoot, receipt.ExtractionRelativePath);
            if (AutomationJson.Encode(Inventory(extracted, options, false)) != AutomationJson.Encode(receipt.Files))
                throw new InvalidDataException("Extracted admission inventory differs from its durable receipt.");
        }
    }
}
