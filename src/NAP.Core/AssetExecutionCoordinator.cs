namespace NAP.Core;

/// <summary>Small synchronous MVP lifecycle boundary; durable checkpoints survive recoverable failures.</summary>
public sealed class AssetExecutionCoordinator
{
    private readonly UniverseContext _context;
    public AssetExecutionCoordinator(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }

    public AssetExecutionResult Execute(JobId jobId, ValidatedAssetPackage package, ProcessingPlan processingPlan,
        ArchiveMasterPlan archivePlan, AiAuditReport auditReport, long maxInputPixels)
    {
        ArgumentNullException.ThrowIfNull(jobId); ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(processingPlan); ArgumentNullException.ThrowIfNull(archivePlan);
        ProductionAssetExecutor.RequirePass(auditReport);
        if (maxInputPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maxInputPixels));
        if (package.AssetKey.UniverseId != _context.Id || processingPlan.AssetKey != package.AssetKey || archivePlan.AssetKey != package.AssetKey ||
            archivePlan.RelativeDirectory != processingPlan.ProductionDestination.RelativeDirectory || !ProductionPaths.Same(archivePlan.ArchiveRoot, _context.Storage.ArchiveRoot))
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionArchiveMismatch, "The coordinator inputs belong to different assets, routes or contexts.");
        if (archivePlan.Issues.ShouldStop) throw new ArchiveStorageException(archivePlan.Issues);
        using var coordination = ExecutionMutex.Acquire("Job", _context.Storage.StateRoot, jobId.Value);
        RequireJournal(jobId);
        var store = new JobStateStore(_context);
        var state = store.Load(jobId).State;
        if (state is JobState.Detected or JobState.Staged or JobState.Validated or JobState.Failed)
            throw new InvalidOperationException("Only PLANNED, AUDITED, EXECUTED, VERIFIED or a coherent COMPLETED Job may execute.");
        if (state == JobState.Completed) return VerifyCompleted(jobId, package, processingPlan, maxInputPixels, coordination);

        ProductionStorageRootValidator.Require(_context);
        ProductionPaths.Resolve(_context.Storage.ProductionRoot, processingPlan.ProductionDestination.RelativeDirectory);
        var archivePlanner = new ArchiveMasterPlanner(_context);
        var packageSnapshot = archivePlanner.Plan(package, processingPlan);
        if (packageSnapshot.AssetType != archivePlan.AssetType || packageSnapshot.MasterDigest != archivePlan.MasterDigest ||
            packageSnapshot.Files.Count != archivePlan.Files.Count || packageSnapshot.Files.Any(file => !archivePlan.Files.Any(f =>
                f.Role == file.Role && f.FileName == file.FileName && ProductionPaths.Same(f.SourcePath, file.SourcePath) &&
                ProductionPaths.Same(f.DestinationPath, file.DestinationPath) && f.Digest == file.Digest && f.SizeBytes == file.SizeBytes)))
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionArchiveMismatch, "The supplied archive plan must freeze exactly the current validated package.");
        // Reject changed frozen archive sources before advancing the journal.
        var archiveCurrent = archivePlanner.Revalidate(archivePlan, new ArchiveMasterIndexStore(_context).Load());
        if (archiveCurrent.Issues.ShouldStop) throw new ArchiveStorageException(archiveCurrent.Issues);
        if (state == JobState.Planned)
        {
            ProductionAssetPlanner.RequireFreshDestination(_context, processingPlan.ProductionDestination.RelativeDirectory);
            Transition(JobState.Audited); state = JobState.Audited;
        }
        var archive = new ArchiveMasterExecutor(_context).Execute(archivePlan, auditReport);
        var productionPlan = new ProductionAssetPlanner(_context).Plan(package, processingPlan, archive, maxInputPixels, jobId);
        var executor = new ProductionAssetExecutor(_context);
        var production = executor.ExecuteCoordinated(productionPlan, auditReport, coordination);
        if (state == JobState.Audited) { Transition(JobState.Executed); state = JobState.Executed; }
        executor.VerifyCoordinated(productionPlan, coordination);
        if (state == JobState.Executed) { Transition(JobState.Verified); state = JobState.Verified; }
        executor.VerifyCoordinated(productionPlan, coordination);
        if (state == JobState.Verified)
        {
            new AssetCatalog(_context).RegisterVerified(package, processingPlan, archive, production, auditReport);
            Transition(JobState.Completed);
        }
        return new AssetExecutionResult(jobId, archive, production);

        void Transition(JobState next) { RequireJournal(jobId); store.Transition(jobId, next); }
    }

    private AssetExecutionResult VerifyCompleted(JobId jobId, ValidatedAssetPackage package, ProcessingPlan processing, long maxInputPixels, ExecutionMutex coordination)
    {
        try
        {
            var archivePlan = new ArchiveMasterPlanner(_context).Plan(package, processing);
            if (archivePlan.Issues.ShouldStop || archivePlan.Action != ArchiveMasterAction.AlreadyArchived)
                throw new InvalidOperationException("The completed Job archive is no longer exact.");
            var archive = new ArchiveMasterResult(archivePlan, ArchiveMasterOutcome.AlreadyArchived);
            var productionPlan = new ProductionAssetPlanner(_context).Plan(package, processing, archive, maxInputPixels, jobId);
            if (productionPlan.Issues.ShouldStop || productionPlan.Action != ProductionAssetAction.AlreadyProduced)
                throw new InvalidOperationException("The completed Job production is no longer exact.");
            var production = new ProductionAssetExecutor(_context).VerifyCoordinated(productionPlan, coordination);
            return new AssetExecutionResult(jobId, archive, production);
        }
        catch (Exception ex) when (ex is ProductionStorageException or ArchiveStorageException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { throw ProductionStorageException.Stop(NapIssueCodes.ProductionCompletedInconsistent, "A COMPLETED Job no longer has exact verified physical outputs; explicit review is required.", inner: ex); }
    }

    private void RequireJournal(JobId jobId)
    {
        var root = ProductionPaths.CheckPath(_context.Storage.StateRoot, NapIssueCodes.ProductionRootInvalid);
        if (root is null || (root & FileAttributes.Directory) == 0) throw new InvalidOperationException("The Job StateRoot must already exist as a non-reparse directory.");
        if (!ProductionPaths.FileExists(Path.Combine(_context.Storage.StateRoot, jobId.Value + ".json"), NapIssueCodes.ProductionVerificationFailed))
            throw new FileNotFoundException("The persisted Job must already exist.");
    }
}
