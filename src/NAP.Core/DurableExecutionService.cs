namespace NAP.Core;

public enum DurableExecutionCode { Completed, NeedsReview }
public sealed record DurableExecutionResult(QueueItemId ItemId, WorkflowAttemptId AttemptId, JobId JobId,
    DurableExecutionCode Code, string? Diagnostic = null);

/// <summary>Explicit synchronous execution of one item. No UI, scheduler, provider call or alternate Job.
/// The universe owner spans the workflow; DB locks/transactions never span engine execution.</summary>
public sealed class DurableExecutionService
{
    private readonly UniverseContext _context;
    private readonly AutomationQueueStore _queue;
    private readonly DurablePreparationService _preparation;
    public ExecutionStateStore State { get; }
    internal Action<string>? Checkpoint { get; set; }
    public DurableExecutionService(UniverseContext context, AutomationQueueStore queue)
    {
        if (!AutomationValidation.SameRoots(context.Storage, queue.Storage))
            throw new AutomationException(AutomationError.UniverseMismatch, "Execution context differs from queue roots.");
        _context = context; _queue = queue; _preparation = new(context, queue); State = new(context.Storage);
    }

    public DurableExecutionResult Execute(AutomationQueueOwner owner, QueueItemId id)
    {
        owner.Require(_context.Storage.StateRoot); SharedBoundary();
        var item = _queue.Get(id); var attempt = _queue.GetAttempt(item.ActiveAttempt ?? throw AutomationValidation.Invalid("Execution requires its active attempt."));
        if (item.State is not (QueueState.Queued or QueueState.Running or QueueState.Completed))
            return new(id, attempt.Id, attempt.JobId, DurableExecutionCode.NeedsReview, "queue_not_executable");
        ExecutionRecord? record;
        try { record = State.Get(id); }
        catch (AutomationException e) when (e.Code == AutomationError.Missing) { record = null; }
        QueueClaim? claim = null;
        try
        {
            RequireAuthority(item);
            var jobState = new JobStateStore(_context).Load(attempt.JobId).State;
            if (record is null && (item.State != QueueState.Queued || jobState != JobState.Planned))
                throw new InvalidDataException("An advanced Job or claim has no execution intention.");
            var handoff = _preparation.VerifyExecution(id, record is not null && jobState != JobState.Planned);
            var p = handoff.Preparation;
            var policy = _queue.GetPolicy(item.Policy);
            var binding = new ExecutionBinding(1, id, attempt.Id, attempt.JobId, _context.Id, _context.Storage, item.Policy,
                p.Snapshot!.Hash, AutomationJson.Hash(p.ExpectedAuditReceipt!), p.AuditGrant!.Hash, policy.Authorization!.AuthorizationId);
            if (record is null)
            {
                Checkpoint?.Invoke("before-execution-intent");
                record = State.Save(owner, new(binding, 0, [], [], false, null), null);
                Checkpoint?.Invoke("execution-intent");
            }
            else if (AutomationJson.Encode(binding) != AutomationJson.Encode(record.Binding))
                throw new InvalidDataException("Execution binding differs from current preparation.");
            if (jobState is JobState.Audited or JobState.Executed or JobState.Verified or JobState.Completed)
            {
                if (!record.Intents.Contains("journal-" + jobState) ||
                    jobState == JobState.Completed && !record.Intents.Contains("catalog-register"))
                    throw new InvalidDataException("Advanced journal lacks the same workflow's durable intention.");
            }
            else if (jobState != JobState.Planned) throw new InvalidDataException("Journal is not an executable checkpoint.");
            VerifyArchive(handoff.Plans.Archive);
            RejectUnknownProductionEntries(handoff);
            VerifyProductionOwnership(handoff, jobState);
            if (item.State == QueueState.Queued)
            {
                Checkpoint?.Invoke("before-claim"); claim = _queue.Claim(owner, id, item.Revision);
                Checkpoint?.Invoke("claimed");
            }
            else if (item.State == QueueState.Running)
            {
                claim = item.ClaimEpoch == owner.Epoch ? new(id, item.ClaimToken!, owner.Epoch, item.Revision) :
                    _queue.RecoverExecutionClaim(owner, id, item.Revision, binding);
                Checkpoint?.Invoke("claim-recovered");
            }
            if (record.Completed && jobState != JobState.Completed) throw new InvalidDataException("Completed execution disagrees with journal.");
            Progress(QueueStage.Execution);
            Checkpoint?.Invoke("before-coordinator");
            var effective = UniverseProfileSnapshotRenderer.Context(p.EffectiveProfile, _context.Storage);
            var result = new AssetExecutionCoordinator(effective).ExecuteControlled(attempt.JobId, handoff.Plans.Package,
                handoff.Plans.Processing, handoff.Plans.Archive, handoff.Audit, PackagePreparationService.MaxInputPixels,
                new(Before, After, VerifyArchive));
            Checkpoint?.Invoke("after-coordinator");
            Progress(QueueStage.Verification);
            VerifyCatalog(effective, handoff, result);
            After("catalog-verified"); Progress(QueueStage.Catalog);
            if (!record.Completed || record.Diagnostic is not null)
                record = State.Save(owner, record with { Completed = true, Diagnostic = null }, record.Revision);
            Checkpoint?.Invoke("execution-completed");
            item = _queue.Get(id);
            if (item.State != QueueState.Completed)
            {
                RequireAuthority(item);
                _queue.Transition(owner, id, item.Revision, QueueState.Completed, claim: claim);
                Checkpoint?.Invoke("queue-completed");
            }
            return new(id, attempt.Id, attempt.JobId, DurableExecutionCode.Completed);

            void Progress(QueueStage stage)
            {
                var current = _queue.Get(id);
                if (current.State == QueueState.Running) _queue.RecordProgress(owner, id, current.Revision, claim!, stage, QueueEventResult.Succeeded);
            }
            void Before(string effect)
            {
                owner.Require(_context.Storage.StateRoot); SharedBoundary(); RequireAuthority(_queue.Get(id));
                if (!record.Intents.Contains(effect)) record = State.Save(owner, record with { Intents = record.Intents.Append(effect).ToArray() }, record.Revision);
                Checkpoint?.Invoke("before:" + effect);
                // Recheck after the durable intent and immediately before returning to the effect.
                owner.Require(_context.Storage.StateRoot); SharedBoundary(); RequireAuthority(_queue.Get(id));
                var current = _preparation.VerifyExecution(id, true);
                if (current.Preparation.Snapshot!.Hash != binding.SnapshotHash) throw new InvalidDataException("Execution input changed.");
            }
            void After(string effect)
            {
                // This fault window precedes ownership recording. A final without its receipt remains ambiguous.
                Checkpoint?.Invoke("effect:" + effect);
                if (!record.Receipts.Contains(effect)) record = State.Save(owner, record with { Receipts = record.Receipts.Append(effect).ToArray() }, record.Revision);
                Checkpoint?.Invoke("after:" + effect);
            }
            void VerifyArchive(ArchiveMasterPlan plan)
            {
                foreach (var file in plan.Files)
                {
                    var exists = ProductionPaths.FileExists(file.DestinationPath, NapIssueCodes.ArchiveFileCollision);
                    var owns = record.Receipts.Contains("archive-file:" + file.FileName);
                    if (exists != owns) throw new InvalidDataException("Archive final lacks exact workflow ownership or a recorded final disappeared.");
                    if (exists) ArchiveMasterPlanner.VerifyFile(file.DestinationPath, file, NapIssueCodes.ArchiveVerificationFailed);
                }
                if (ProductionPaths.CheckPath(plan.DestinationDirectory, NapIssueCodes.ArchiveFileCollision) is not null)
                    foreach (var path in Directory.EnumerateFileSystemEntries(plan.DestinationDirectory))
                        if (!plan.Files.Any(f => ProductionPaths.Same(f.DestinationPath, path)))
                            throw new InvalidDataException("Ambiguous archive temporary or unknown entry requires review.");
            }
        }
        catch (Exception e) when (e is InvalidDataException or PackagePreparationStoppedException or ProductionStorageException or
            ArchiveStorageException or CatalogException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // A damaged shared boundary blocks the universe, even when first noticed by a native executor.
            Checkpoint?.Invoke("stopped:" + e.Message);
            SharedBoundary();
            var diagnostic = e switch
            {
                CatalogException => "catalog_inconsistent", ProductionStorageException => "production_inconsistent",
                ArchiveStorageException => "archive_inconsistent", IOException or UnauthorizedAccessException => "execution_storage_unavailable",
                _ => "execution_evidence_changed"
            };
            if (record is not null) record = State.Save(owner, record with { Diagnostic = diagnostic }, record.Revision);
            item = _queue.Get(id);
            if (item.State is QueueState.Queued or QueueState.Running)
            {
                if (item.State == QueueState.Running && item.ClaimEpoch != owner.Epoch)
                {
                    _queue.ParkInterruptedClaim(owner, id, item.Revision);
                    return new(id, attempt.Id, attempt.JobId, DurableExecutionCode.NeedsReview, diagnostic);
                }
                claim ??= item.State == QueueState.Running && item.ClaimEpoch == owner.Epoch ? new(id, item.ClaimToken!, owner.Epoch, item.Revision) : null;
                _queue.Transition(owner, id, item.Revision, QueueState.NeedsReview,
                    policyInactive() ? QueueIncident.PolicyInactive : QueueIncident.InvalidEvidence, claim: claim);
            }
            else if (item.State == QueueState.Completed) _queue.ReportCompletedInconsistent(owner, id, item.Revision);
            return new(id, attempt.Id, attempt.JobId, DurableExecutionCode.NeedsReview, diagnostic);
        }
        bool policyInactive() => !_queue.GetPolicy(item.Policy).IsExecutionAuthorized || _queue.CurrentPolicy().Reference != item.Policy;
    }

    private void RequireAuthority(QueueItem item)
    {
        var policy = _queue.GetPolicy(item.Policy);
        if (!policy.IsExecutionAuthorized || _queue.CurrentPolicy().Reference != item.Policy ||
            !AutomationValidation.SameRoots(policy.Definition.Roots, _context.Storage))
            throw new InvalidDataException("Productive authority is not current and exact.");
    }
    private void SharedBoundary()
    {
        AutomationQueueBoundary.Paths(_context.Storage, false);
        try
        {
            ProductionStorageRootValidator.Require(_context); ArchiveRootValidator.Require(_context);
            new ArchiveMasterIndexStore(_context).Load();
            if (ProductionPaths.FileExists(_context.Storage.CatalogPath, NapIssueCodes.CatalogInvalid)) new AssetCatalog(_context).CheckIntegrity();
        }
        catch (Exception e) when (e is ProductionStorageException or ArchiveStorageException or IOException or UnauthorizedAccessException)
        { throw new AutomationException(AutomationError.UnsafePath, "Shared execution roots require review; universe is blocked.", e); }
    }
    private void RejectUnknownProductionEntries(ExecutionHandoff handoff)
    {
        var root = handoff.Plans.Processing.ProductionDestination.FullDirectoryPath;
        if (ProductionPaths.CheckPath(root, NapIssueCodes.ProductionFileCollision) is null) return;
        var conversion = new ImageConversionResolver().Resolve(handoff.Plans.Package, PackagePreparationService.MaxInputPixels)!;
        var expected = handoff.Plans.Archive.Files.Where(f => f.Role != conversion.SourceRole).Select(f => f.FileName)
            .Append(handoff.Plans.Package.AssetKey.AssetId + ".webp").ToHashSet(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
            if (!expected.Contains(Path.GetFileName(path))) throw new InvalidDataException("Ambiguous production temporary or foreign entry requires review.");
    }
    private static void VerifyCatalog(UniverseContext context, ExecutionHandoff handoff, AssetExecutionResult result)
    {
        var catalog = new AssetCatalog(context); catalog.CheckIntegrity();
        var actual = catalog.Get(handoff.Plans.Package.AssetKey.AssetId);
        var expected = new CatalogAssetReader(context).FromVerified(handoff.Plans.Package, handoff.Plans.Processing, result.Archive, result.Production);
        if (actual is null || actual.AuditState != "PASS" || !CatalogAssetData.Equivalent(actual, expected))
            throw new InvalidDataException("Completed catalog entry is absent or differs from the same Job's verified outputs.");
    }
    private void VerifyProductionOwnership(ExecutionHandoff handoff, JobState state)
    {
        if (state == JobState.Planned) return; // Fresh destination was required by the handoff.
        var plans = handoff.Plans;
        if (plans.Archive.Action != ArchiveMasterAction.AlreadyArchived)
        {
            if (ProductionPaths.CheckPath(plans.Processing.ProductionDestination.FullDirectoryPath, NapIssueCodes.ProductionFileCollision) is not null)
                throw new InvalidDataException("Production exists before the exact archive was completed.");
            return;
        }
        var effective = UniverseProfileSnapshotRenderer.Context(handoff.Preparation.EffectiveProfile, _context.Storage);
        var plan = new ProductionAssetPlanner(effective).Plan(plans.Package, plans.Processing,
            new(plans.Archive, ArchiveMasterOutcome.AlreadyArchived), PackagePreparationService.MaxInputPixels, handoff.Preparation.JobId);
        if (plan.Issues.ShouldStop) throw new ProductionStorageException(plan.Issues);
    }
}
