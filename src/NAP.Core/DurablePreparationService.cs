using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NAP.Core;

/// <summary>Explicit, synchronous preparatory workflow. Ownership stays on its acquiring thread.
/// No production coordinator, host, archive writer, catalog writer or image converter is composed.</summary>
public sealed partial class DurablePreparationService
{
    private readonly UniverseContext _context;
    private readonly AutomationQueueStore _queue;
    private readonly PreparationStateStore _state;
    private readonly InboxAdmissionService _admission;
    private readonly PreparationFiles _files;
    private readonly PackagePreparationService _preparation = new();
    private readonly TimeProvider _clock;
    internal Action<string>? Checkpoint { get; set; }
    public DurablePreparationService(UniverseContext context, AutomationQueueStore queue, TimeProvider? clock = null)
    {
        if (!AutomationValidation.SameRoots(context.Storage, queue.Storage)) throw new AutomationException(AutomationError.UniverseMismatch, "Preparation queue scope differs.");
        _context = context; _queue = queue; _clock = clock ?? TimeProvider.System; _state = new(context.Storage); _admission = new(context, queue); _files = new(context);
    }
    public PreparationStateStore State => _state;
    public PreparationResult Prepare(AutomationQueueOwner owner, QueueItemId id, CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); AutomationQueueBoundary.Paths(_context.Storage, false); token.ThrowIfCancellationRequested();
        var item = _queue.Get(id); var record = GetRecord(id);
        if (item.State == QueueState.Duplicate && record?.DuplicateOf is not null) return Result(record, PreparationCode.Duplicate);
        if (item.State == QueueState.NeedsReview) return record is null ? Result(item, PreparationCode.NeedsReview) :
            Result(record, record.Code is PreparationCode.Planned or PreparationCode.AuditPass ? PreparationCode.NeedsReview : record.Code);
        if (item.State != QueueState.Queued || item.ActiveAttempt is null) return Result(item, PreparationCode.InvalidAdmission);
        if (_queue.GetPolicy(item.Policy).State is AutomationPolicyState.Paused or AutomationPolicyState.Revoked)
            return Park(owner, item, record, PreparationCode.PolicyInactive);
        try
        {
            var receipt = _admission.VerifyAdmitted(id); var attempt = _queue.GetAttempt(item.ActiveAttempt);
            var originalRoot = ProductionPaths.Resolve(_context.Storage.StagingRoot, receipt.ExtractionRelativePath);
            var originalZip = ProductionPaths.Resolve(_context.Storage.StagingRoot, receipt.SnapshotRelativePath);
            PreparationFiles.VerifyZip(originalZip, receipt.Files, token);
            var baseJson = UniverseProfileSnapshotRenderer.Render(_context.Profile);
            var manifest = AssetManifestV2Loader.Load(Path.Combine(originalRoot, receipt.AssetId + "_manifest.json"));
            var baseRule = EffectiveRuleSnapshot.Create(_context.Id, 1, manifest.AssetType, manifest.ProductionProfile, baseJson);
            if (record is null)
            {
                if (new JobStateStore(_context).Load(attempt.JobId).State != JobState.Staged) return Park(owner, item, null, PreparationCode.EvidenceChanged);
                record = new() { ItemId = id, AttemptId = attempt.Id, JobId = attempt.JobId, UniverseId = _context.Id,
                    OperationId = "preparation_" + Guid.NewGuid().ToString("N"), AdmissionHash = attempt.ExpectedEvidence!,
                    BaseProfile = baseRule, EffectiveProfile = baseRule, Policy = item.Policy,
                    Checkpoint = PreparationCheckpoint.Intent, Code = PreparationCode.Pending, UpdatedUtc = DateTimeOffset.UtcNow };
                var originals = receipt.Files.Select(f => f with { RelativePath = "package/" + manifest.AssetId + "/" + f.RelativePath })
                    .Append(new AdmissionFile("received.zip", item.Zip.Hash, item.Zip.SizeBytes)).OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
                record = record with { Originals = _files.Describe(record, "original", "automation/originals/" + id.Value, originals) };
                record = _state.Save(owner, record, null); Checkpoint?.Invoke("preparation-intent");
            }
            if (record.AttemptId != attempt.Id || record.JobId != attempt.JobId || record.AdmissionHash != attempt.ExpectedEvidence ||
                record.BaseProfile != baseRule || record.Policy != item.Policy || _queue.CurrentPolicy().Reference != item.Policy)
                return Park(owner, item, record, PreparationCode.EvidenceChanged);
            if (attempt.Profile is null) _queue.BindProfile(owner, id, _queue.Get(id).Revision, record.BaseProfile);
            else if (attempt.Profile != record.BaseProfile) return Park(owner, item, record, PreparationCode.EvidenceChanged);
            var policy = _queue.GetPolicy(item.Policy);
            if (record.Checkpoint == PreparationCheckpoint.Intent)
            {
                var sources = receipt.Files.ToDictionary(f => "package/" + manifest.AssetId + "/" + f.RelativePath,
                    f => Path.Combine(originalRoot, f.RelativePath), StringComparer.Ordinal); sources.Add("received.zip", originalZip);
                CheckStorage(record.Originals!, policy.Definition.Resources.MaxStagingBytes);
                _files.Publish(record, "original", record.Originals!, sources, null, policy.Definition.Resources.MaxStagingBytes, token, Checkpoint);
                record = Save(owner, record with { Checkpoint = PreparationCheckpoint.Preserved }, QueueStage.Preparation);
                Checkpoint?.Invoke("original-preserved");
            }
            _files.Verify(record, "original", record.Originals!);
            var preserved = Path.Combine(_files.Resolve(record.Originals!), "package", manifest.AssetId);
            PreparationFiles.VerifyZip(Path.Combine(_files.Resolve(record.Originals!), "received.zip"), receipt.Files, token);
            var effectiveContext = UniverseProfileSnapshotRenderer.Context(record.EffectiveProfile, _context.Storage);
            var package = _preparation.Validate(UniverseProfileSnapshotRenderer.Context(record.BaseProfile, _context.Storage), preserved, requireExactGeometry: false);
            var conversion = new ImageConversionResolver().Resolve(package, PackagePreparationService.MaxInputPixels)
                ?? throw new InvalidDataException("A complete image production rule is required.");
            Decode(conversion.SourcePath, policy.Definition.Resources.MaxMemoryBytes);
            var semantic = Semantic(package, receipt.Files); var master = receipt.Files.Single(f => f.RelativePath == Path.GetFileName(conversion.SourcePath)).Hash;
            if (record.SemanticHash is null)
            {
                record = Save(owner, record with { SemanticHash = semantic, MasterHash = master }, QueueStage.Preparation);
            }
            if (semantic != record.SemanticHash || master != record.MasterHash) return Park(owner, item, record, PreparationCode.EvidenceChanged);
            {
                var reservation = _queue.GetReservation(package.AssetKey);
                if (reservation is not null && reservation.ItemId != id)
                {
                    if (reservation.ContentHash != semantic) return Park(owner, item, record, PreparationCode.AssetCollision);
                    var primary = GetRecord(reservation.ItemId);
                    if (primary is null || primary.SemanticHash != semantic || primary.BaseProfile != record.BaseProfile)
                        return Park(owner, item, record, PreparationCode.EvidenceChanged);
                    _files.Verify(primary, "original", primary.Originals!);
                    record = Save(owner, record with { DuplicateOf = primary.ItemId, Code = PreparationCode.Duplicate }, QueueStage.Preparation);
                    _queue.Transition(owner, id, _queue.Get(id).Revision, QueueState.Duplicate); return Result(record, PreparationCode.Duplicate);
                }
            }
            var duplicateMaster = _state.List().Any(r => r.ItemId != id && r.MasterHash == master && r.SemanticHash != semantic && r.DuplicateOf is null);
            if (duplicateMaster && !AllowsDuplicateMaster(policy)) return Park(owner, item, record, PreparationCode.MasterDuplicate);
            _queue.ReserveAsset(owner, id, _queue.Get(id).Revision, package.AssetKey, record.SemanticHash!);
            if (record.Checkpoint == PreparationCheckpoint.Preserved)
            {
                var info = new PngMasterValidator().Validate(conversion.SourcePath).ImageInfo!;
                if (!info.HasAspectRatio(conversion.OutputWidth, conversion.OutputHeight) || record.ExceptionalDecisionId is not null)
                {
                    var parameters = NormalizationParameters(record, policy);
                    if (parameters is null) return Park(owner, item, record, PreparationCode.GeometryPolicyRequired);
                    var rule = record.EffectiveProfile.ValidateAndGetRule().Conversion!;
                    RequireNormalizationPackage(receipt, item.Zip, parameters);
                    var geometry = ImageNormalizationGeometry.Calculate(info, rule, parameters.Limits);
                    RequireNormalizationMemory(info, geometry, parameters, policy.Definition.Resources.MaxMemoryBytes);
                    var preview = new ImageNormalizationEngine().Prepare(conversion.SourcePath, rule, parameters.Limits, token);
                    if (record.ExceptionalDecisionId is { } decisionId)
                    {
                        var previewEvidence = _state.Preview(decisionId) ?? throw new InvalidDataException("Exceptional preview is missing.");
                        if (previewEvidence.Hash != record.ExceptionalPreviewHash || previewEvidence.CandidateHash.Hex != preview.CandidateSha256 || previewEvidence.Geometry != preview.Geometry)
                            return Park(owner, item, record, PreparationCode.EvidenceChanged);
                    }
                    var grant = policy.Authorization;
                    var authority = record.Authority ?? new(NormalizationAuthorityKind.AuthorizedPolicy, grant!.AuthorizationId, grant.Actor, grant.AuthorizedUtc, policy.Reference);
                    record = BuildCandidateIntent(owner, record, receipt, preserved, preview, parameters, authority, token);
                }
                else record = Save(owner, record with { Checkpoint = PreparationCheckpoint.CandidateReady }, QueueStage.Preparation);
            }
            if (record.Checkpoint == PreparationCheckpoint.CandidateIntent)
                record = FinishCandidate(owner, record, receipt, preserved, token);
            var packageRoot = record.Candidate is null ? preserved : Path.Combine(_files.Resolve(record.Candidate), "package", manifest.AssetId);
            if (record.Candidate is not null)
            {
                _files.Verify(record, "candidate", record.Candidate);
                PreparationFiles.VerifyZip(Path.Combine(_files.Resolve(record.Candidate), "candidate.zip"), EffectiveFiles(record), token);
                VerifyDerivativePixels(record, preserved, packageRoot, token);
                if (!AuthorityCurrent(record, policy)) return Park(owner, item, record, PreparationCode.PolicyInactive);
            }
            package = _preparation.Validate(effectiveContext, packageRoot);
            Decode(new ImageConversionResolver().Resolve(package, PackagePreparationService.MaxInputPixels)!.SourcePath, policy.Definition.Resources.MaxMemoryBytes);
            var effectiveSemantic = Semantic(package, EffectiveFiles(record, receipt.Files));
            if (record.EffectiveSemanticHash is not null && record.EffectiveSemanticHash != effectiveSemantic)
                return Park(owner, item, record, PreparationCode.EvidenceChanged);
            if (record.EffectiveSemanticHash is null) record = Save(owner, record with { EffectiveSemanticHash = effectiveSemantic }, QueueStage.Preparation);
            if (record.Checkpoint == PreparationCheckpoint.CandidateReady)
                record = Save(owner, record with { Checkpoint = PreparationCheckpoint.ValidationIntent }, QueueStage.Preparation);
            if (record.Checkpoint == PreparationCheckpoint.ValidationIntent)
            {
                Journal(record.JobId, JobState.Staged, JobState.Validated); Checkpoint?.Invoke("journal-validated");
                record = Save(owner, record with { Checkpoint = PreparationCheckpoint.Validated }, QueueStage.Preparation);
            }
            var plans = _preparation.Plan(effectiveContext, package);
            if (plans.Archive.Action != ArchiveMasterAction.CopyAndIndex || plans.Archive.FilesToCopy.Count != plans.Archive.Files.Count)
                return Park(owner, item, record, PreparationCode.ExistingStorage);
            if (plans.Archive.Issues.Issues.Any(i => i.Code == NapIssueCodes.ArchivePossibleDuplicate) && !AllowsDuplicateMaster(policy))
                return Park(owner, item, record, PreparationCode.MasterDuplicate);
            var snapshot = Snapshot(record, item, receipt, plans);
            if (record.Snapshot is not null && record.Snapshot.Hash != snapshot.Hash) return Park(owner, item, record, PreparationCode.EvidenceChanged);
            if (record.Checkpoint == PreparationCheckpoint.Validated)
                record = Save(owner, record with { Snapshot = snapshot, Checkpoint = PreparationCheckpoint.PlanningIntent }, QueueStage.Preparation);
            if (record.Checkpoint == PreparationCheckpoint.PlanningIntent)
            {
                Journal(record.JobId, JobState.Validated, JobState.Planned); Checkpoint?.Invoke("journal-planned");
                record = Save(owner, record with { Checkpoint = PreparationCheckpoint.Planned, Code = PreparationCode.Planned }, QueueStage.Preparation);
            }
            if (new JobStateStore(_context).Load(record.JobId).State != JobState.Planned) return Park(owner, item, record, PreparationCode.EvidenceChanged);
            return Result(record, PreparationCode.Planned);
        }
        catch (OperationCanceledException) { throw; }
        catch (AutomationException e) when (e.Code is AutomationError.AssetCollision or AutomationError.ReservationOccupied)
        { return Park(owner, _queue.Get(id), record, PreparationCode.AssetCollision); }
        catch (AutomationException e) when (e.Code == AutomationError.InvalidContract)
        { return Park(owner, _queue.Get(id), record, PreparationCode.InvalidPackage); }
        catch (PackagePreparationStoppedException stopped)
        {
            record = GetRecord(id);
            if (record is not null) record = Save(owner, record with { DiagnosticCodes = stopped.Issues.Issues.Select(i => i.Code).ToArray() }, QueueStage.Preparation);
            var existing = stopped.Issues.Issues.Any(i => i.Code is NapIssueCodes.PlanDestinationExists or NapIssueCodes.PlanDestinationBlocked or NapIssueCodes.PlanDestinationCasingConflict);
            return Park(owner, _queue.Get(id), record, existing ? PreparationCode.ExistingStorage : PreparationCode.InvalidPackage);
        }
        catch (ImageNormalizationException stopped)
        {
            record = GetRecord(id);
            if (record is not null) record = Save(owner, record with { DiagnosticCodes = [stopped.Code] }, QueueStage.Normalization);
            return Park(owner, _queue.Get(id), record, stopped.Code is "preparation_memory_limit" or "preparation_storage_limit" or "normalization_memory_limit" ? PreparationCode.ResourceLimit : PreparationCode.NormalizationLimitExceeded);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or ProductionStorageException or ArchiveStorageException)
        { return Park(owner, _queue.Get(id), record, PreparationCode.EvidenceChanged); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Park(owner, _queue.Get(id), record, PreparationCode.StorageUnavailable); }
    }
    private PreparationRecord? GetRecord(QueueItemId id)
    { try { return _state.Get(id); } catch (AutomationException e) when (e.Code == AutomationError.Missing) { return null; } }
    private PreparationRecord Save(AutomationQueueOwner owner, PreparationRecord record, QueueStage stage)
    {
        var next = _state.Save(owner, record, record.Revision);
        var item = _queue.Get(record.ItemId); _queue.RecordPreparationProgress(owner, item.Id, item.Revision, record.AttemptId, stage, QueueEventResult.Recorded); return next;
    }
    private PreparationResult Park(AutomationQueueOwner owner, QueueItem item, PreparationRecord? record, PreparationCode code)
    {
        record = GetRecord(item.Id); // An uncommitted first intention is not a durable record with a CAS revision.
        if (record is not null) record = Save(owner, record with { Code = code }, code.ToString().StartsWith("Audit", StringComparison.Ordinal) ? QueueStage.Audit : QueueStage.Preparation);
        item = _queue.Get(item.Id);
        if (item.State != QueueState.NeedsReview)
            _queue.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, code switch
            {
                PreparationCode.AssetCollision => QueueIncident.AssetCollision, PreparationCode.MasterDuplicate => QueueIncident.MasterDuplicate,
                PreparationCode.GeometryPolicyRequired => QueueIncident.RatioUnsupported, PreparationCode.NormalizationLimitExceeded => QueueIncident.NormalizationLimitExceeded,
                PreparationCode.ResourceLimit => QueueIncident.ResourceLimit, PreparationCode.ExistingStorage => QueueIncident.ExistingStorage,
                PreparationCode.PolicyInactive => QueueIncident.PolicyInactive, PreparationCode.AuditWarning => QueueIncident.AuditWarning,
                PreparationCode.AuditFail => QueueIncident.AuditFail, PreparationCode.AuditBudgetExhausted => QueueIncident.AuditBudgetExhausted,
                PreparationCode.AuditDependencyBlocked => QueueIncident.AuditDependencyBlocked, PreparationCode.AuditInvalidResponse => QueueIncident.AuditInvalidResponse,
                PreparationCode.StorageUnavailable => QueueIncident.StorageUnavailable, _ => QueueIncident.InvalidEvidence
            });
        return record is null ? Result(item, code) : Result(record, code);
    }
    private static PreparationResult Result(PreparationRecord record, PreparationCode code) => new(record.ItemId, record.AttemptId, record.JobId, code, record.RetryAtUtc, record.DuplicateOf);
    private PreparationResult Result(QueueItem item, PreparationCode code)
    { var attempt = _queue.GetAttempt(item.ActiveAttempt ?? throw AutomationValidation.Invalid("Preparation has no active attempt.")); return new(item.Id, attempt.Id, attempt.JobId, code); }
    private void Journal(JobId job, JobState from, JobState to)
    {
        using var lease = ExecutionMutex.Acquire("Job", _context.Storage.StateRoot, job.Value); var store = new JobStateStore(_context);
        var state = store.Load(job).State; if (state == from) store.Transition(job, to);
        else if (state != to) throw new InvalidDataException("Journal has a checkpoint without the expected preparation intention.");
    }
    private static Sha256Digest Semantic(ValidatedAssetPackage package, IReadOnlyList<AdmissionFile> files) => AutomationJson.Hash(new
    {
        package.AssetKey, package.Manifest.AssetType, package.Manifest.ProductionProfile,
        Classification = package.Manifest.Classification.OrderBy(p => p.Key, StringComparer.Ordinal),
        Files = files.Where(f => f.RelativePath != Path.GetFileName(package.ManifestPath)).OrderBy(f => f.RelativePath, StringComparer.Ordinal)
    });
    private bool AllowsDuplicateMaster(StoredAutomationPolicy policy) => policy.IsPreparationAuthorized &&
        policy.Authorization is { } authority && _state.Grant(policy.Reference) is { AllowDuplicateMasters: true } grant &&
        grant.Policy == policy.Reference && grant.PolicyAuthorizationId == authority.AuthorizationId && grant.Actor == authority.Actor;
    private static string EngineIdentity => "preparation-v1|" + Environment.Version + "|" + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier + "|" +
        new Sha256Hasher().Compute(typeof(DurablePreparationService).Assembly.Location).Hex + "|" + new Sha256Hasher().Compute(typeof(Image).Assembly.Location).Hex;
    private PreparationSnapshot Snapshot(PreparationRecord record, QueueItem item, AdmissionReceipt receipt, PreparedAssetPlans plans) => new(1,
        item.Id, record.AttemptId, record.JobId, _context.Id, _context.Storage, item.Policy, record.AdmissionHash, item.Zip, receipt.Files,
        EffectiveFiles(record, receipt.Files), record.BaseProfile, record.EffectiveProfile, EngineIdentity, File.ReadAllText(plans.Package.ManifestPath),
        new(plans.Processing.ProductionDestination.RelativeDirectory, plans.Archive.RelativeDirectory, plans.Archive.Action,
            plans.Archive.Files.Select(f => new PreparationRoleFile(f.Role, f.FileName, f.Digest, f.SizeBytes)).ToArray(),
            plans.Validation.Issues.Select(i => new AiAuditIssueFact(i.Code, i.Severity, i.Disposition)).ToArray()),
        record.Candidate is null ? null : record.OperationId, record.Authority, record.Parameters, record.Geometry, record.ExceptionalDecisionId, record.ExceptionalPreviewHash);
    private static IReadOnlyList<AdmissionFile> EffectiveFiles(PreparationRecord record, IReadOnlyList<AdmissionFile>? originals = null) =>
        record.Candidate is null ? originals ?? throw new InvalidDataException("Original inventory missing.") : record.Candidate.Files
            .Where(f => f.RelativePath.StartsWith("package/", StringComparison.Ordinal)).Select(f => f with { RelativePath = Path.GetFileName(f.RelativePath) }).OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
    private static void Decode(string path, long memory)
    {
        var info = new PngMasterValidator().Validate(path).ImageInfo ?? throw new InvalidDataException("PNG container invalid.");
        if (checked((long)info.Width * info.Height * 8 + 16 * 1024 * 1024) > memory) throw ImageNormalizationException.Stop("preparation_memory_limit", "PNG decode exceeds the approved resource budget.");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var image = PngDecoder.Instance.Decode<Rgba32>(new DecoderOptions { SkipMetadata = true, MaxFrames = 2 }, input);
            if (image.Frames.Count != 1 || image.Width != info.Width || image.Height != info.Height) throw new InvalidDataException("PNG decode is inconsistent.");
        }
        catch (ImageFormatException e) { throw new InvalidDataException("PNG decode invalid.", e); }
    }
    private void CheckStorage(PreparationBundle bundle, long maximum)
    {
        if (ProductionPaths.CheckPath(_files.Resolve(bundle), NapIssueCodes.PackageStructureInvalid) is not null) return;
        long bytes = bundle.Files.Sum(f => f.SizeBytes); var dirs = new Stack<string>(); int count = 0;
        foreach (var root in new[] { Path.Combine(_context.Storage.StateRoot, "automation", "originals"), Path.Combine(_context.Storage.StateRoot, "automation", "preparation", "packages"), Path.Combine(_context.Storage.StagingRoot, "automation-admission") })
            if (ProductionPaths.CheckPath(root, NapIssueCodes.PackageStructureInvalid) is not null) dirs.Push(root);
        while (dirs.TryPop(out var dir))
        {
            InboxAdmissionEvidence.DirectoryBoundary(dir);
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if (++count > 100000) throw new InvalidDataException("Preparation storage inventory exceeds bounds.");
                var a = ProductionPaths.CheckPath(entry, NapIssueCodes.PackageStructureInvalid)!.Value;
                if ((a & FileAttributes.Directory) != 0) dirs.Push(entry); else bytes = checked(bytes + new FileInfo(entry).Length);
            }
        }
        if (bytes > maximum || new DriveInfo(_context.Storage.StateRoot).AvailableFreeSpace < bundle.Files.Sum(f => f.SizeBytes))
            throw ImageNormalizationException.Stop("preparation_storage_limit", "Preparation exceeds available space or approved storage budget.");
    }
}
