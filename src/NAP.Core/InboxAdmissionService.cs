using System.IO.Compression;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Explicit admission only. Call on the acquiring owner's thread; no async continuation owns the lease.</summary>
public sealed class InboxAdmissionService
{
    private readonly UniverseContext _context;
    private readonly AutomationQueueStore _queue;
    private readonly InboxAdmissionOptions _options;
    private readonly InboxAdmissionEvidence _evidence;
    internal Action<string>? Checkpoint { get; set; }
    public InboxAdmissionService(UniverseContext context, AutomationQueueStore queue, InboxAdmissionOptions? options = null)
    {
        _context = context; _queue = queue; _options = options ?? new(); _options.Validate();
        if (!AutomationValidation.SameRoots(context.Storage, queue.Storage))
            throw new AutomationException(AutomationError.UniverseMismatch, "Admission context and queue must share the exact universe workspace.");
        _evidence = new(context, queue);
    }
    public InboxAdmissionResult Admit(AutomationQueueOwner owner, string sourcePath, CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); ScopeBoundary(); token.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourcePath);
        if (!ProductionPaths.Same(Path.GetDirectoryName(source)!, _context.Storage.InboxRoot) ||
            !string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase) || !ProductionPaths.SafeSegment(Path.GetFileName(source)))
            return Result(source, InboxAdmissionCode.UnsafeZip);
        try { if (!ProductionPaths.FileExists(source, NapIssueCodes.PackageStructureInvalid)) return Result(source, InboxAdmissionCode.MissingSource); }
        catch (ProductionStorageException) { return Result(source, InboxAdmissionCode.UnsafeZip); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Result(source, InboxAdmissionCode.InUse); }
        InboxPackageReadinessResult readiness;
        try
        {
            readiness = new InboxPackageReadinessChecker(new() { RequiredSamples = _options.StabilitySamples, SampleInterval = _options.SampleInterval })
                .CheckAsync(new(Path.GetFileName(source), source), token).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Result(source, InboxAdmissionCode.InUse); }
        if (!readiness.IsReady) return Result(source, readiness.Status == InboxPackageReadinessStatus.Missing ? InboxAdmissionCode.MissingSource :
            readiness.Status == InboxPackageReadinessStatus.InUse ? InboxAdmissionCode.InUse : InboxAdmissionCode.WaitingStable);
        QueueItem? item = null;
        try
        {
            ProductionPaths.FileExists(source, NapIssueCodes.PackageStructureInvalid);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var size = input.Length;
            if (size == 0) return Result(source, InboxAdmissionCode.IncompleteZip);
            if (size > _options.MaxZipBytes) return Result(source, InboxAdmissionCode.ResourceLimit);
            var modified = File.GetLastWriteTimeUtc(source); var hash = new Sha256Hasher().Compute(input);
            if (input.Length != size || modified != File.GetLastWriteTimeUtc(source)) return Result(source, InboxAdmissionCode.SourceChanged);
            item = _queue.Observe(owner, _context.Id, source, new(hash, size)); Checkpoint?.Invoke("observation-recorded");
            if (item.ActiveAttempt is not null) return Resume(owner, item, token);
            if (item.State == QueueState.MissingSource) item = _queue.Transition(owner, item.Id, item.Revision, QueueState.WaitingStable);
            if (item.State is not (QueueState.Observed or QueueState.WaitingStable or QueueState.Queued)) return Result(source, InboxAdmissionCode.AlreadyKnown, item);
            try { input.Position = 0; ZipEntryIntegrity.ReadChecksums(input, _options.Extraction.MaxEntries, token); }
            catch (InvalidDataException)
            {
                if (DateTimeOffset.UtcNow - item.CreatedUtc >= _options.InvalidZipGrace)
                    return Park(owner, item, InboxAdmissionCode.InvalidZip, QueueIncident.InvalidZip, true);
                if (item.State == QueueState.Observed) _queue.Transition(owner, item.Id, item.Revision, QueueState.WaitingStable);
                return Result(source, InboxAdmissionCode.IncompleteZip, item);
            }
            catch (RejectedPackageException) { return Park(owner, item, InboxAdmissionCode.UnsafeZip, QueueIncident.UnsafeZip, true); }
            if (!HasCapacity(input)) return Park(owner, item, InboxAdmissionCode.ResourceLimit, QueueIncident.ResourceLimit);
            input.Position = 0;
            if (new Sha256Hasher().Compute(input) != hash || input.Length != size) return Park(owner, item, InboxAdmissionCode.SourceChanged, QueueIncident.SourceChanged);
            if (item.State != QueueState.Queued) item = _queue.Transition(owner, item.Id, item.Revision, QueueState.Queued);
            var relative = "automation-admission/" + item.Id.Value + "/operation_" + Guid.NewGuid().ToString("N");
            var attempt = _queue.CreateAttempt(owner, item.Id, item.Revision, stagingRelativePath: relative + "/snapshot", extractionRelativePath: relative + "/extracted");
            Checkpoint?.Invoke("attempt-recorded"); item = _queue.Get(item.Id);
            var receipt = _evidence.Start(owner, item, attempt); Checkpoint?.Invoke("intent-recorded");
            return Continue(owner, item, attempt, receipt, token, input);
        }
        catch (OperationCanceledException) { throw; }
        catch (AutomationException) { throw; } // integrity/CAS/ownership failures are scope failures, never isolated success
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ProductionStorageException)
        {
            return item is null ? Result(source, InboxAdmissionCode.InUse) : Park(owner, _queue.Get(item.Id),
                e is InvalidDataException or ProductionStorageException ? InboxAdmissionCode.AmbiguousEvidence : InboxAdmissionCode.StorageUnavailable,
                e is InvalidDataException or ProductionStorageException ? QueueIncident.InvalidEvidence : QueueIncident.StorageUnavailable);
        }
    }
    public InboxAdmissionResult Reconcile(AutomationQueueOwner owner, QueueItemId id, CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); ScopeBoundary();
        var item = _queue.Get(id);
        if (item.ActiveAttempt is null)
        {
            if (item.State is not (QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.MissingSource))
                return Result(item.SourcePath, InboxAdmissionCode.AlreadyKnown, item);
            try
            {
                if (!ProductionPaths.FileExists(item.SourcePath, NapIssueCodes.PackageStructureInvalid)) return Missing(owner, item);
                using var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length != item.Zip.SizeBytes || new Sha256Hasher().Compute(input) != item.Zip.Hash)
                    return Park(owner, item, InboxAdmissionCode.SourceChanged, QueueIncident.SourceChanged);
                return Result(item.SourcePath, InboxAdmissionCode.WaitingStable, item);
            }
            catch (ProductionStorageException) { return Park(owner, item, InboxAdmissionCode.UnsafeZip, QueueIncident.UnsafeZip); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return Missing(owner, item); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Result(item.SourcePath, InboxAdmissionCode.InUse, item); }
        }
        return Resume(owner, item, token);
    }
    private InboxAdmissionResult Resume(AutomationQueueOwner owner, QueueItem item, CancellationToken token)
    {
        if (item.State is not (QueueState.Queued or QueueState.MissingSource)) return Result(item.SourcePath, InboxAdmissionCode.AlreadyKnown, item);
        var attempt = _queue.GetAttempt(item.ActiveAttempt!);
        // An attempt created by other workflows is not adopted as an admission attempt.
        if (attempt.StagingRelativePath is null || !attempt.StagingRelativePath.StartsWith("automation-admission/" + item.Id.Value + "/operation_", StringComparison.Ordinal))
            return Park(owner, item, InboxAdmissionCode.AmbiguousEvidence, QueueIncident.InvalidEvidence);
        try
        {
            AdmissionReceipt receipt;
            try { receipt = _evidence.Load(item, attempt); }
            catch (AutomationException e) when (e.Code is AutomationError.CorruptStore or AutomationError.InvalidContract)
            { throw new InvalidDataException("This admission receipt is corrupt; shared queue integrity is handled separately.", e); }
            if (receipt.Checkpoint is AdmissionCheckpoint.Rejected or AdmissionCheckpoint.NeedsReview)
                return Park(owner, item, receipt.Code, receipt.Code == InboxAdmissionCode.UniverseMismatch ? QueueIncident.UniverseMismatch : QueueIncident.InvalidEvidence, receipt.Checkpoint == AdmissionCheckpoint.Rejected);
            return Continue(owner, item, attempt, receipt, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ProductionStorageException or JsonException)
        { return Park(owner, _queue.Get(item.Id), InboxAdmissionCode.AmbiguousEvidence, QueueIncident.InvalidEvidence); }
    }
    private InboxAdmissionResult Continue(AutomationQueueOwner owner, QueueItem item, WorkflowAttempt attempt, AdmissionReceipt receipt,
        CancellationToken token, FileStream? source = null)
    {
        token.ThrowIfCancellationRequested();
        if (receipt.Checkpoint == AdmissionCheckpoint.Intent)
        {
            var operation = _evidence.OperationRoot(attempt);
            if (ProductionPaths.CheckPath(operation, NapIssueCodes.PackageStructureInvalid) is not null)
                throw new InvalidDataException("Interrupted operation has artifacts without a finalized checkpoint.");
            FileStream? opened = null;
            try
            {
                if (source is null)
                {
                    if (!ProductionPaths.FileExists(item.SourcePath, NapIssueCodes.PackageStructureInvalid))
                        return Missing(owner, item);
                    opened = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read); source = opened;
                }
                source.Position = 0;
                if (source.Length != item.Zip.SizeBytes || new Sha256Hasher().Compute(source) != item.Zip.Hash)
                    return Park(owner, _queue.Get(item.Id), InboxAdmissionCode.SourceChanged, QueueIncident.SourceChanged);
                if (item.State == QueueState.MissingSource)
                {
                    item = _queue.Transition(owner, item.Id, item.Revision, QueueState.WaitingStable);
                    item = _queue.Transition(owner, item.Id, item.Revision, QueueState.Queued);
                }
                if (!HasCapacity(source)) return Park(owner, _queue.Get(item.Id), InboxAdmissionCode.ResourceLimit, QueueIncident.ResourceLimit);
                InboxAdmissionEvidence.EnsureParents(Path.Combine(operation, "snapshot"), _context.Storage.StagingRoot);
                Checkpoint?.Invoke("snapshot-starting"); source.Position = 0;
                InboxPackageStager.StageSnapshotAsync(source, _evidence.SnapshotPath(attempt), _options.MaxZipBytes, token).GetAwaiter().GetResult();
                Checkpoint?.Invoke("snapshot-published"); source.Position = 0;
                if (source.Length != item.Zip.SizeBytes || new Sha256Hasher().Compute(source) != item.Zip.Hash)
                    return Park(owner, _queue.Get(item.Id), InboxAdmissionCode.SourceChanged, QueueIncident.SourceChanged);
                ProductionPaths.Verify(item.SourcePath, item.Zip.Hash, item.Zip.SizeBytes, NapIssueCodes.PackageStructureInvalid);
                ProductionPaths.Verify(_evidence.SnapshotPath(attempt), item.Zip.Hash, item.Zip.SizeBytes, NapIssueCodes.PackageStructureInvalid);
                receipt = _evidence.Advance(owner, receipt, AdmissionCheckpoint.SnapshotReady); Checkpoint?.Invoke("snapshot-recorded");
            }
            finally { opened?.Dispose(); }
        }
        _evidence.Verify(receipt, attempt, _options);
        if (receipt.Checkpoint == AdmissionCheckpoint.SnapshotReady)
        {
            if (ProductionPaths.CheckPath(_evidence.ExtractionRoot(attempt), NapIssueCodes.PackageStructureInvalid) is not null)
                throw new InvalidDataException("Extraction artifacts lack their finalized durable receipt.");
            Checkpoint?.Invoke("extraction-starting");
            var extracted = new StagedPackageExtractor(_options.Extraction).ExtractAsync(_evidence.SnapshotPath(attempt), _evidence.ExtractionRoot(attempt), token).GetAwaiter().GetResult();
            if (extracted.Status != StagedPackageExtractionStatus.Extracted)
                return Reject(owner, receipt, extracted.Status == StagedPackageExtractionStatus.InvalidArchive ? InboxAdmissionCode.InvalidZip : InboxAdmissionCode.UnsafeZip,
                    extracted.Status == StagedPackageExtractionStatus.InvalidArchive ? QueueIncident.InvalidZip : QueueIncident.UnsafeZip);
            Checkpoint?.Invoke("extraction-published");
            var root = extracted.FinalPath!;
            IReadOnlyList<AdmissionFile> files;
            try { files = _evidence.Inventory(root, _options, true); }
            catch (InvalidDataException) { return Reject(owner, receipt, InboxAdmissionCode.InvalidPackage, QueueIncident.InvalidManifest); }
            var manifests = files.Where(f => f.RelativePath.EndsWith("_manifest.json", StringComparison.Ordinal)).ToArray();
            if (manifests.Length != 1 || manifests[0].SizeBytes > 1024 * 1024) return Reject(owner, receipt, InboxAdmissionCode.InvalidManifest, QueueIncident.InvalidManifest);
            AssetManifestV2 manifest;
            try { manifest = AssetManifestV2Loader.Load(Path.Combine(root, manifests[0].RelativePath)); }
            catch (Exception e) when (e is JsonException or InvalidDataException) { return Reject(owner, receipt, InboxAdmissionCode.InvalidManifest, QueueIncident.InvalidManifest); }
            if (manifests[0].RelativePath != manifest.AssetId + "_manifest.json") return Reject(owner, receipt, InboxAdmissionCode.InvalidManifest, QueueIncident.InvalidManifest);
            var canonical = Path.Combine(_evidence.ExtractionRoot(attempt), manifest.AssetId);
            if (ProductionPaths.CheckPath(canonical, NapIssueCodes.PackageStructureInvalid) is not null) throw new InvalidDataException("Canonical extraction destination is occupied.");
            Directory.Move(root, canonical);
            receipt = _evidence.Advance(owner, receipt, AdmissionCheckpoint.ExtractionReady, files, manifest.UniverseId, manifest.AssetId,
                Path.GetRelativePath(_context.Storage.StagingRoot, canonical).Replace(Path.DirectorySeparatorChar, '/'));
            Checkpoint?.Invoke("extraction-recorded");
        }
        if (receipt.DeclaredUniverse != _context.Id.Value) return Reject(owner, receipt, InboxAdmissionCode.UniverseMismatch, QueueIncident.UniverseMismatch);
        // Only the envelope is checked here; package files/PNG/geometry/plans belong to Phase 3.
        var manifestFinal = AssetManifestV2Loader.Load(Path.Combine(ProductionPaths.Resolve(_context.Storage.StagingRoot, receipt.ExtractionRelativePath), receipt.AssetId + "_manifest.json"));
        if (!_context.Profile.TryGetAssetRule(manifestFinal.AssetType, manifestFinal.ProductionProfile, out _))
            return Reject(owner, receipt, InboxAdmissionCode.ProfileUnknown, QueueIncident.ProfileUnknown, review: true);
        if (receipt.Checkpoint == AdmissionCheckpoint.Admitted)
        {
            var finalJournal = Path.Combine(_context.Storage.StateRoot, attempt.JobId.Value + ".json");
            if (!ProductionPaths.FileExists(finalJournal, NapIssueCodes.PackageStructureInvalid)) throw new InvalidDataException("An admitted Job journal is missing; it must not be recreated.");
            var state = new JobStateStore(_context).Load(attempt.JobId).State;
            if (state is JobState.Detected or JobState.Failed) throw new InvalidDataException("An admitted Job has an inconsistent journal checkpoint.");
            return Result(item.SourcePath, InboxAdmissionCode.AlreadyKnown, item, attempt.JobId, receipt.DeclaredUniverse);
        }
        var journal = Path.Combine(_context.Storage.StateRoot, attempt.JobId.Value + ".json");
        ProductionPaths.CheckPath(journal, NapIssueCodes.PackageStructureInvalid);
        if (receipt.Checkpoint == AdmissionCheckpoint.ExtractionReady)
        {
            if (ProductionPaths.CheckPath(journal, NapIssueCodes.PackageStructureInvalid) is not null)
                throw new InvalidDataException("Journal exists before its durable creation intent; it is not adopted.");
            receipt = _evidence.Advance(owner, receipt, AdmissionCheckpoint.JournalIntent);
        }
        using (var lease = ExecutionMutex.Acquire("Job", _context.Storage.StateRoot, attempt.JobId.Value))
        {
            var jobs = new JobStateStore(_context);
            var state = File.Exists(journal) ? jobs.Load(attempt.JobId).State : jobs.Create(attempt.JobId).State;
            Checkpoint?.Invoke("journal-created");
            if (state == JobState.Detected) jobs.Transition(attempt.JobId, JobState.Staged);
            else if (state != JobState.Staged) throw new InvalidDataException("Admission journal has an unexpected checkpoint.");
        }
        receipt = _evidence.Advance(owner, receipt, AdmissionCheckpoint.Admitted); Checkpoint?.Invoke("admission-recorded");
        return Result(item.SourcePath, InboxAdmissionCode.Admitted, _queue.Get(item.Id), attempt.JobId, receipt.DeclaredUniverse);
    }
    public AdmissionReceipt GetReceipt(QueueItemId id)
    {
        var item = _queue.Get(id); return _evidence.Load(item, _queue.GetAttempt(item.ActiveAttempt ?? throw AutomationValidation.Invalid("No admission attempt.")));
    }
    private InboxAdmissionResult Reject(AutomationQueueOwner owner, AdmissionReceipt receipt, InboxAdmissionCode code, QueueIncident incident, bool review = false)
    {
        _evidence.Advance(owner, receipt, review ? AdmissionCheckpoint.NeedsReview : AdmissionCheckpoint.Rejected, code: code);
        return Park(owner, _queue.Get(receipt.ItemId), code, incident, !review, receipt.DeclaredUniverse);
    }
    private InboxAdmissionResult Missing(AutomationQueueOwner owner, QueueItem item)
    {
        item = _queue.Get(item.Id);
        if (item.State != QueueState.MissingSource) _queue.Transition(owner, item.Id, item.Revision, QueueState.MissingSource, QueueIncident.MissingSource);
        return Result(item.SourcePath, InboxAdmissionCode.MissingSource, item);
    }
    private InboxAdmissionResult Park(AutomationQueueOwner owner, QueueItem item, InboxAdmissionCode code, QueueIncident incident, bool rejected = false, string? declared = null)
    {
        if (item.State is not (QueueState.Rejected or QueueState.Completed or QueueState.Duplicate))
        {
            var state = rejected ? QueueState.Rejected : QueueState.NeedsReview;
            if (item.State != state) item = _queue.Transition(owner, item.Id, item.Revision, state, incident);
        }
        return Result(item.SourcePath, code, item, item.ActiveAttempt is null ? null : _queue.GetAttempt(item.ActiveAttempt).JobId, declared);
    }
    private InboxAdmissionResult Result(string source, InboxAdmissionCode code, QueueItem? item = null, JobId? job = null, string? declared = null) => new(_context.Id, source, code, item?.Id, job, declared);
    private void ScopeBoundary()
    {
        AutomationQueueBoundary.Paths(_context.Storage, false);
        InboxAdmissionEvidence.DirectoryBoundary(_context.Storage.InboxRoot);
        InboxAdmissionEvidence.DirectoryBoundary(_context.Storage.StagingRoot, true);
    }
    private bool HasCapacity(Stream source)
    {
        try { return HasCapacityCore(source); }
        catch (ProductionStorageException e)
        { throw new AutomationException(AutomationError.UnsafePath, "Shared admission storage cannot be safely accounted for.", e); }
    }
    private bool HasCapacityCore(Stream source)
    {
        source.Position = 0; long declared = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > _options.Extraction.MaxEntryBytes || entry.Length > _options.Extraction.MaxTotalBytes - declared) return false;
                declared += entry.Length;
            }
        }
        var bytesNeeded = checked(source.Length + declared);
        if (bytesNeeded > _options.MaxRetainedBytes) return false;
        var directory = Path.Combine(_context.Storage.StagingRoot, "automation-admission"); long retained = 0; int count = 0;
        var pending = new Stack<string>(); if (Directory.Exists(directory)) pending.Push(directory);
        while (pending.TryPop(out var path))
        {
            InboxAdmissionEvidence.DirectoryBoundary(path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (++count > 100000) return false;
                var a = ProductionPaths.CheckPath(entry, NapIssueCodes.PackageStructureInvalid)!.Value;
                if ((a & FileAttributes.Directory) != 0) pending.Push(entry);
                else { retained = checked(retained + new FileInfo(entry).Length); if (retained > _options.MaxRetainedBytes - bytesNeeded) return false; }
            }
        }
        return new DriveInfo(_context.Storage.WorkspaceRoot).AvailableFreeSpace >= _options.MinFreeBytes + bytesNeeded;
    }
}
