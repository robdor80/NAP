using System.Text;

namespace NAP.Core;

internal sealed record ExecutionHandoff(PreparationRecord Preparation, PreparedAssetPlans Plans, AiAuditReport Audit);

public sealed partial class DurablePreparationService
{
    /// <summary>Read-only handoff, including recovery. Never calls Prepare or creates a Job or a new audit.</summary>
    internal ExecutionHandoff VerifyExecution(QueueItemId id, bool recovery)
    {
        AutomationQueueBoundary.Paths(_context.Storage, false);
        var item = _queue.Get(id); var r = _state.Get(id) ?? throw new InvalidDataException("Missing preparation evidence.");
        var policy = _queue.GetPolicy(item.Policy); var attempt = _queue.GetAttempt(item.ActiveAttempt ?? throw new InvalidDataException("Missing attempt."));
        if (item.State is not (QueueState.Queued or QueueState.Running or QueueState.Completed) ||
            r.Checkpoint != PreparationCheckpoint.Planned || r.AuditState != DurableAuditState.Pass || r.Code != PreparationCode.AuditPass ||
            r.ItemId != item.Id || r.AttemptId != attempt.Id || r.JobId != attempt.JobId || r.Policy != item.Policy ||
            r.AdmissionHash != attempt.ExpectedEvidence || attempt.Profile != r.BaseProfile || r.DuplicateOf is not null ||
            r.BaseProfile.ProfileJson != UniverseProfileSnapshotRenderer.Render(_context.Profile) || r.Snapshot is null ||
            !policy.IsExecutionAuthorized || _queue.CurrentPolicy().Reference != item.Policy)
            throw new InvalidDataException("Execution scope, preparation, profile or authority changed.");
        var admitted = _admission.VerifyAdmitted(id);
        var originalZip = ProductionPaths.Resolve(_context.Storage.StagingRoot, admitted.SnapshotRelativePath);
        PreparationFiles.VerifyZip(originalZip, admitted.Files, default);
        _files.Verify(r, "original", r.Originals!);
        var originalRoot = Path.Combine(_files.Resolve(r.Originals!), "package", admitted.AssetId!);
        PreparationFiles.VerifyZip(Path.Combine(_files.Resolve(r.Originals!), "received.zip"), admitted.Files, default);
        var original = _preparation.Validate(UniverseProfileSnapshotRenderer.Context(r.BaseProfile, _context.Storage), originalRoot, requireExactGeometry: false);
        if (Semantic(original, admitted.Files) != r.SemanticHash) throw new InvalidDataException("Original semantic identity changed.");
        var root = originalRoot;
        if (r.Candidate is not null)
        {
            _files.Verify(r, "candidate", r.Candidate);
            PreparationFiles.VerifyZip(Path.Combine(_files.Resolve(r.Candidate), "candidate.zip"), EffectiveFiles(r), default);
            root = Path.Combine(_files.Resolve(r.Candidate), "package", admitted.AssetId!);
            if (!AuthorityCurrent(r, policy)) throw new InvalidDataException("Derivative authority is no longer current.");
            VerifyDerivativePixels(r, originalRoot, root, default);
            var lineage = _queue.Lineage(id).Where(l => l.OperationId == NormalizationId(r)).OrderBy(l => l.Version).LastOrDefault();
            if (lineage is null || lineage.State != NormalizationOperationState.DerivativeDeclared || lineage.AttemptId != r.AttemptId ||
                lineage.JobId != r.JobId || lineage.Authority != r.Authority || lineage.CandidateZip?.Hash != r.Candidate.Files.Single(f => f.RelativePath == "candidate.zip").Hash)
                throw new InvalidDataException("Derivative lineage is incomplete.");
        }
        var effective = UniverseProfileSnapshotRenderer.Context(r.EffectiveProfile, _context.Storage);
        var package = _preparation.Validate(effective, root);
        if (Semantic(package, EffectiveFiles(r, admitted.Files)) != r.EffectiveSemanticHash) throw new InvalidDataException("Effective semantic identity changed.");
        var reservation = _queue.GetReservation(package.AssetKey);
        if (reservation is null || reservation.ItemId != id || reservation.AttemptId != r.AttemptId || reservation.JobId != r.JobId || reservation.ContentHash != r.SemanticHash)
            throw new InvalidDataException("The exact asset reservation is required.");
        var conversion = new ImageConversionResolver().Resolve(package, PackagePreparationService.MaxInputPixels)!;
        Decode(conversion.SourcePath, policy.Definition.Resources.MaxMemoryBytes);
        var info = new PngMasterValidator().Validate(conversion.SourcePath).ImageInfo!;
        var conversionMemory = checked(32L * 1024 * 1024 + 16L * info.Width * info.Height +
            16L * conversion.OutputWidth * conversion.OutputHeight + EffectiveFiles(r, admitted.Files).Sum(f => f.SizeBytes));
        if (conversionMemory > policy.Definition.Resources.MaxMemoryBytes) throw new InvalidDataException("Conversion exceeds the frozen resource budget.");
        var repositoryResult = new ProductionRepositoryValidator().Validate(effective);
        if (repositoryResult.Issues.ShouldStop || repositoryResult.Repository is not { } repository) throw new InvalidDataException("Production root invalid.");
        var processing = new ProcessingPlanBuilder().Build(package, repository, new ProductionDestinationResolver().Resolve(package, repository));
        var scan = new ProductionRepositoryScanner().Scan(repository);
        if (scan.Issues.ShouldStop || scan.Snapshot is not { } observed) throw new InvalidDataException("Production scan invalid.");
        // Recovery skips only the exact directory-exists check. Native production evidence still
        // verifies every existing file against this Job; casing, blocked ancestors and links remain STOP.
        var destination = processing.ProductionDestination.RelativeDirectory;
        var filtered = recovery ? new ProductionRepositorySnapshot(repository, observed.Entries.Where(e =>
            !(e.RelativePath == destination && e.Kind == ProductionRepositoryEntryKind.Directory))) : observed;
        var validation = new ProcessingPlanValidator().Validate(processing, filtered);
        var archive = new ArchiveMasterPlanner(effective).Plan(package, processing);
        if (validation.ShouldStop || archive.Issues.ShouldStop || !recovery &&
            (archive.Action != ArchiveMasterAction.CopyAndIndex || archive.FilesToCopy.Count != archive.Files.Count))
            throw new InvalidDataException("Fresh or recoverable plans failed validation.");
        var currentIssues = validation.Issues.Concat(archive.Issues.Issues).Select(i => new AiAuditIssueFact(i.Code, i.Severity, i.Disposition));
        if (currentIssues.Any(i => !r.Snapshot.Plan.Issues.Contains(i))) throw new InvalidDataException("New plan warning or error requires review.");
        var facts = new PreparationPlanFacts(destination, archive.RelativeDirectory, r.Snapshot.Plan.ArchiveAction,
            archive.Files.Select(f => new PreparationRoleFile(f.Role, f.FileName, f.Digest, f.SizeBytes)).ToArray(), r.Snapshot.Plan.Issues);
        var plans = new PreparedAssetPlans(package, processing, archive, new(r.Snapshot.Plan.Issues.Select(i =>
            new NapIssue(i.Code, i.Severity, i.Disposition, "Frozen audited plan issue."))));
        var fresh = Snapshot(r, item, admitted, plans) with { Plan = facts };
        if (fresh.Hash != r.Snapshot.Hash) throw new InvalidDataException("Frozen execution snapshot no longer matches source, roots, profile or engine.");
        var grant = _state.Grant(item.Policy) ?? throw new InvalidDataException("Missing operational grant.");
        if (r.AuditGrant?.Hash != grant.Hash || policy.Authorization is not { } authority ||
            grant.PolicyAuthorizationId != authority.AuthorizationId || grant.Actor != authority.Actor)
            throw new InvalidDataException("Operational grant no longer agrees with authorization.");
        var request = new WorkflowAiAuditRequestBuilder().Build(processing, plans.Validation, r.Snapshot, grant);
        var requestJson = new AiAuditRequestJsonRenderer().Render(request);
        var requestHash = AutomationJson.HashBytes(Encoding.UTF8.GetBytes(requestJson));
        if (r.RequestJson != requestJson || r.RequestHash != requestHash) throw new InvalidDataException("Audit request binding changed.");
        _files.Verify(r, "audit", r.AuditResponse ?? throw new InvalidDataException("Missing audit bundle."));
        var receipt = AutomationJson.Decode<DurableAuditReceipt>(File.ReadAllText(Path.Combine(_files.Resolve(r.AuditResponse), "report.json")));
        if (AutomationJson.Encode(receipt) != AutomationJson.Encode(r.ExpectedAuditReceipt) || receipt.SchemaVersion != 1 ||
            receipt.ItemId != id || receipt.AttemptId != r.AttemptId || receipt.JobId != r.JobId || receipt.UniverseId != _context.Id ||
            receipt.Policy != item.Policy || receipt.SnapshotHash != fresh.Hash || receipt.RequestHash != requestHash || receipt.GrantHash != grant.Hash ||
            receipt.ProviderId != grant.ProviderId || receipt.ModelId != grant.ModelId || receipt.RespondedUtc.Offset != TimeSpan.Zero ||
            r.Calls.LastOrDefault() is not { State: AuditCallState.Response } last || receipt.RequestId != last.RequestId)
            throw new InvalidDataException("Audit receipt identity or grant mismatch.");
        var report = receipt.ValidateReport(); ProductionAssetExecutor.RequirePass(report);
        return new(r, plans, report);
    }
}
