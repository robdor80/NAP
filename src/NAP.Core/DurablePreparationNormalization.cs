using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NAP.Core;

public sealed partial class DurablePreparationService
{
    private ProfileNormalizationPolicy? NormalizationParameters(PreparationRecord record, StoredAutomationPolicy policy)
    {
        if (record.ExceptionalDecisionId is not null)
            return AuthorityCurrent(record, policy) ? record.Parameters : null;
        if (!policy.IsPreproductionAuthorized || policy.Authorization is null || _queue.CurrentPolicy().Reference != policy.Reference ||
            !policy.Definition.Operations.HasFlag(AutomationOperations.Normalize)) return null;
        var rule = record.EffectiveProfile.ValidateAndGetRule();
        return policy.Definition.Normalization.SingleOrDefault(p => p.AssetType == rule.AssetType && p.ProductionProfile == rule.ProductionProfile &&
            p.OperationallyValidated && p.Ratio.Matches(rule.Conversion!.OutputWidth, rule.Conversion.OutputHeight));
    }
    private bool AuthorityCurrent(PreparationRecord record, StoredAutomationPolicy policy)
    {
        if (record.Authority is not { } authority || record.Parameters is null) return false;
        if (authority.Kind == NormalizationAuthorityKind.AuthorizedPolicy)
            return policy.IsPreproductionAuthorized && policy.Authorization is { } grant && authority.Policy == policy.Reference &&
                grant.AuthorizationId == authority.AuthorizationId && grant.Actor == authority.Actor && grant.AuthorizedUtc == authority.Utc &&
                policy.Definition.Operations.HasFlag(AutomationOperations.Normalize) && record.Parameters.OperationallyValidated &&
                policy.Definition.Normalization.Any(p => AutomationJson.Encode(p) == AutomationJson.Encode(record.Parameters));
        var decision = _queue.Decisions(record.ItemId).Where(d => d.DecisionId == record.ExceptionalDecisionId).OrderBy(d => d.Version).LastOrDefault();
        return decision is { State: ExceptionalDecisionState.Approved, Scope: ExceptionalScope.SingleAsset } && decision.Actor == authority.Actor &&
            decision.Utc == authority.Utc && decision.PreviewEvidence == record.ExceptionalPreviewHash && decision.EffectiveRule == record.EffectiveProfile;
    }
    private static void RequireNormalizationMemory(PngImageInfo info, ImageNormalizationGeometry geometry,
        ProfileNormalizationPolicy parameters, long scopeMemory)
    {
        var estimate = checked(32L * 1024 * 1024 + 2 * parameters.Limits.MaxPackageBytes + 4 * parameters.Limits.MaxFileBytes +
            8 * ((long)info.Width * info.Height + 2L * geometry.CanvasWidth * geometry.CanvasHeight));
        if (estimate > Math.Min(scopeMemory, parameters.MaxMemoryBytes))
            throw ImageNormalizationException.Stop("normalization_memory_limit", "Conservative normalization reservation exceeds its independent memory budget.");
    }
    private static void RequireNormalizationPackage(AdmissionReceipt admitted, QueueZipIdentity zip, ProfileNormalizationPolicy parameters)
    {
        var limits = parameters.Limits;
        if (admitted.Files.Count > limits.MaxPackageEntries || admitted.Files.Any(f => f.SizeBytes > limits.MaxFileBytes) ||
            admitted.Files.Sum(f => f.SizeBytes) > limits.MaxPackageBytes || zip.SizeBytes > limits.MaxPackageBytes)
            throw ImageNormalizationException.Stop("normalization_package_limit", "The original package exceeds independent entries/bytes limits.");
    }
    private static string NormalizationId(PreparationRecord record) => "normalization_" + record.OperationId["preparation_".Length..];
    private PreparationRecord BuildCandidateIntent(AutomationQueueOwner owner, PreparationRecord record, AdmissionReceipt admitted,
        string preserved, ImageNormalizationPreview preview, ProfileNormalizationPolicy parameters, NormalizationAuthority authority, CancellationToken token)
    {
        var rule = record.EffectiveProfile.ValidateAndGetRule();
        var generated = CandidateData(record with { Parameters = parameters, Authority = authority }, admitted, preserved, preview, token);
        record = record with { Parameters = parameters, Authority = authority, Geometry = preview.Geometry };
        var inventory = generated.Select(f => new AdmissionFile(f.Key, AutomationJson.HashBytes(f.Value), f.Value.LongLength))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
        var bundle = _files.Describe(record, "candidate", "automation/preparation/packages/" + record.ItemId.Value + "/" + record.OperationId, inventory, authority);
        var originalMaster = Path.Combine(preserved, rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(admitted.AssetId!));
        var zip = _queue.Get(record.ItemId).Zip;
        var lineage = new NormalizationLineage(NormalizationId(record), 1, record.ItemId, record.AttemptId, record.UniverseId, record.JobId,
            new(zip.Hash, zip.SizeBytes, record.Originals!.RelativeDirectory + "/received.zip"),
            new(new Sha256Hasher().Compute(originalMaster), new FileInfo(originalMaster).Length,
                record.Originals.RelativeDirectory + "/package/" + admitted.AssetId + "/" + Path.GetFileName(originalMaster)),
            null, null, NormalizationOperationState.Proposed, parameters, authority, bundle.ReceiptHash, DateTimeOffset.UtcNow, preview.Geometry);
        var existing = _queue.Lineage(record.ItemId).Where(l => l.OperationId == lineage.OperationId).OrderBy(l => l.Version).LastOrDefault();
        if (existing is null) _queue.RecordLineage(owner, _queue.Get(record.ItemId).Revision, lineage);
        else if (existing.EvidenceHash != bundle.ReceiptHash || existing.Authority != authority || existing.State != NormalizationOperationState.Proposed)
            throw new InvalidDataException("Normalization intention conflicts with its lineage.");
        record = Save(owner, record with { Candidate = bundle, Checkpoint = PreparationCheckpoint.CandidateIntent }, QueueStage.Normalization);
        Checkpoint?.Invoke("candidate-intent"); CheckStorage(bundle, _queue.GetPolicy(record.Policy).Definition.Resources.MaxStagingBytes);
        _files.Publish(record, "candidate", bundle, new Dictionary<string, string>(), generated,
            _queue.GetPolicy(record.Policy).Definition.Resources.MaxStagingBytes, token, Checkpoint);
        return record;
    }
    private Dictionary<string, byte[]> CandidateData(PreparationRecord record, AdmissionReceipt admitted, string preserved,
        ImageNormalizationPreview preview, CancellationToken token)
    {
        var parameters = record.Parameters!; var limits = parameters.Limits;
        if (admitted.Files.Count > limits.MaxPackageEntries || admitted.Files.Sum(f => f.SizeBytes) > limits.MaxPackageBytes || _queue.Get(record.ItemId).Zip.SizeBytes > limits.MaxPackageBytes)
            throw ImageNormalizationException.Stop("normalization_package_limit", "Original package exceeds the exact normalization contract.");
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in admitted.Files)
        {
            token.ThrowIfCancellationRequested(); if (file.SizeBytes > limits.MaxFileBytes) throw ImageNormalizationException.Stop("normalization_file_limit", "Companion exceeds normalization file budget.");
            content.Add(file.RelativePath, ImageNormalizationFiles.Read(Path.Combine(preserved, file.RelativePath), limits.MaxFileBytes, token));
        }
        var rule = record.EffectiveProfile.ValidateAndGetRule();
        var png = rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(admitted.AssetId!);
        if (!content.ContainsKey(png)) throw new InvalidDataException("Exceptional rule has no compatible original PNG role.");
        content[png] = preview.GetCandidatePng();
        if (rule.ProductionProfile != record.BaseProfile.ProductionProfile)
        {
            if (record.Authority?.Kind != NormalizationAuthorityKind.HumanDecision) throw new InvalidDataException("Metadata correction needs its individual decision.");
            var manifest = admitted.AssetId + "_manifest.json"; var node = JsonNode.Parse(content[manifest])!;
            node["production_profile"] = rule.ProductionProfile; content[manifest] = JsonSerializer.SerializeToUtf8Bytes(node);
        }
        if (content.Values.Sum(b => b.LongLength) > limits.MaxPackageBytes) throw ImageNormalizationException.Stop("normalization_package_limit", "Candidate package exceeds bytes budget.");
        var generated = content.ToDictionary(f => "package/" + admitted.AssetId + "/" + f.Key, f => f.Value, StringComparer.Ordinal);
        generated.Add("candidate.zip", PreparationFiles.ZipBytes(content, limits.MaxPackageBytes)); return generated;
    }
    private PreparationRecord FinishCandidate(AutomationQueueOwner owner, PreparationRecord record, AdmissionReceipt admitted, string preserved, CancellationToken token)
    {
        if (!AuthorityCurrent(record, _queue.GetPolicy(record.Policy))) throw new InvalidDataException("Normalization authority is no longer current.");
        if (ProductionPaths.CheckPath(_files.Resolve(record.Candidate!), NapIssueCodes.PackageStructureInvalid) is null)
        {
            var rule = record.EffectiveProfile.ValidateAndGetRule();
            var png = rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(admitted.AssetId!);
            var preview = new ImageNormalizationEngine().Prepare(Path.Combine(preserved, png), rule.Conversion!, record.Parameters!.Limits, token);
            var generated = CandidateData(record, admitted, preserved, preview, token);
            var inventory = generated.Select(f => new AdmissionFile(f.Key, AutomationJson.HashBytes(f.Value), f.Value.LongLength)).OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
            if (AutomationJson.Encode(inventory) != AutomationJson.Encode(record.Candidate!.Files)) throw new InvalidDataException("Recomputed candidate differs from its persisted intention.");
            CheckStorage(record.Candidate, _queue.GetPolicy(record.Policy).Definition.Resources.MaxStagingBytes);
            _files.Publish(record, "candidate", record.Candidate, new Dictionary<string, string>(), generated,
                _queue.GetPolicy(record.Policy).Definition.Resources.MaxStagingBytes, token, Checkpoint);
        }
        _files.Verify(record, "candidate", record.Candidate!);
        var lineage = _queue.Lineage(record.ItemId).Where(l => l.OperationId == NormalizationId(record)).OrderBy(l => l.Version).Last();
        if (lineage.State == NormalizationOperationState.Proposed)
        {
            var rule = record.EffectiveProfile.ValidateAndGetRule();
            var pngName = rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(admitted.AssetId!);
            var png = record.Candidate!.Files.Single(f => f.RelativePath == "package/" + admitted.AssetId + "/" + pngName);
            var zip = record.Candidate.Files.Single(f => f.RelativePath == "candidate.zip");
            _queue.RecordLineage(owner, _queue.Get(record.ItemId).Revision, lineage with { Version = 2, State = NormalizationOperationState.DerivativeDeclared,
                DerivedPng = new(png.Hash, png.SizeBytes, record.Candidate.RelativeDirectory + "/" + png.RelativePath),
                CandidateZip = new(zip.Hash, zip.SizeBytes, record.Candidate.RelativeDirectory + "/candidate.zip"), Utc = DateTimeOffset.UtcNow });
        }
        record = Save(owner, record with { Checkpoint = PreparationCheckpoint.CandidateReady }, QueueStage.Normalization);
        Checkpoint?.Invoke("candidate-ready"); return record;
    }
    private static void VerifyDerivativePixels(PreparationRecord record, string original, string candidate, CancellationToken token)
    {
        var rule = record.EffectiveProfile.ValidateAndGetRule();
        var name = rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(Path.GetFileName(original));
        var preview = new ImageNormalizationEngine().Prepare(Path.Combine(original, name), rule.Conversion!, record.Parameters!.Limits, token);
        var bytes = ImageNormalizationFiles.Read(Path.Combine(candidate, name), record.Parameters.Limits.MaxFileBytes, token);
        if (preview.Geometry != record.Geometry || ImageNormalizationEngine.Hash(bytes) != preview.CandidateSha256 || !bytes.AsSpan().SequenceEqual(preview.GetCandidatePng()))
            throw new InvalidDataException("Derived PNG is not the exact, pixel-verified minimal canvas.");
    }
    public ExceptionalPreparationPreview ProposeException(AutomationQueueOwner owner, QueueItemId itemId, EffectiveRuleSnapshot rule,
        ProfileNormalizationPolicy parameters, CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); var item = _queue.Get(itemId); RequireExceptionalScope(item);
        var record = GetRecord(itemId) ?? throw AutomationValidation.Invalid("A preserved admitted package is required.");
        if (item.State != QueueState.NeedsReview || record.Checkpoint != PreparationCheckpoint.Preserved || record.Candidate is not null)
            throw new AutomationException(AutomationError.IllegalTransition, "Exceptions must precede irreversible validation/planning evidence.");
        ValidateExceptionalRule(record, rule); AutomationValidation.Normalization(parameters);
        var target = rule.ValidateAndGetRule();
        if (parameters.AssetType != target.AssetType || parameters.ProductionProfile != target.ProductionProfile || !parameters.Ratio.Matches(target.Conversion!.OutputWidth, target.Conversion.OutputHeight))
            throw AutomationValidation.Invalid("Preview parameters need the complete effective rule, not a ratio alone.");
        _files.Verify(record, "original", record.Originals!); var admitted = _admission.VerifyAdmitted(itemId);
        var root = Path.Combine(_files.Resolve(record.Originals!), "package", admitted.AssetId!);
        RequireNormalizationPackage(admitted, _queue.Get(itemId).Zip, parameters);
        var png = Path.Combine(root, target.PackageFiles.Single(f => f.Role == target.Conversion.SourceRole).ResolveFileName(admitted.AssetId!));
        var validatedPng = new PngMasterValidator().Validate(png);
        if (!validatedPng.IsValid || new Sha256Hasher().Compute(png) != record.MasterHash)
            throw AutomationValidation.Invalid("The selected complete rule must use the same preserved original master.");
        var info = validatedPng.ImageInfo!;
        var geometry = ImageNormalizationGeometry.Calculate(info, target.Conversion, parameters.Limits);
        RequireNormalizationMemory(info, geometry, parameters, _queue.GetPolicy(item.Policy).Definition.Resources.MaxMemoryBytes);
        var preview = new ImageNormalizationEngine().Prepare(png, target.Conversion, parameters.Limits, token);
        var id = "decision_" + Guid.NewGuid().ToString("N"); var bytes = preview.GetCandidatePng();
        var inventory = new[] { new AdmissionFile("preview.png", AutomationJson.HashBytes(bytes), bytes.LongLength) };
        var bundle = _files.Describe(record, "preview", "automation/preparation/previews/" + id, inventory);
        var value = new ExceptionalPreparationPreview(id, itemId, record.AttemptId, _context.Id, rule, parameters,
            new(preview.OriginalSha256), new(preview.CandidateSha256), geometry, bundle);
        _state.RegisterPreview(owner, value);
        _files.Publish(record, "preview", bundle, new Dictionary<string, string>(), new Dictionary<string, byte[]> { ["preview.png"] = bytes },
            _queue.GetPolicy(item.Policy).Definition.Resources.MaxStagingBytes, token, Checkpoint);
        _queue.RecordDecision(owner, _queue.Get(itemId).Revision, new(id, 1, itemId, record.AttemptId, _context.Id,
            record.BaseProfile.ProductionProfile, rule, parameters.Ratio, target.Conversion.OutputWidth, target.Conversion.OutputHeight,
            value.Hash, ExceptionalScope.SingleAsset, ExceptionalDecisionState.Proposed, null, DateTimeOffset.UtcNow)); return value;
    }
    public PreparationResult ResumeException(AutomationQueueOwner owner, QueueItemId itemId, string decisionId, CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); var item = _queue.Get(itemId); RequireExceptionalScope(item);
        var record = GetRecord(itemId) ?? throw AutomationValidation.Invalid("No preparation evidence.");
        var decision = _queue.Decisions(itemId).Where(d => d.DecisionId == decisionId).OrderBy(d => d.Version).LastOrDefault();
        var preview = _state.Preview(decisionId) ?? throw AutomationValidation.Invalid("No durable exceptional preview.");
        if (item.State != QueueState.NeedsReview || record.Checkpoint != PreparationCheckpoint.Preserved || record.Candidate is not null ||
            decision is not { State: ExceptionalDecisionState.Approved, Scope: ExceptionalScope.SingleAsset } ||
            decision.AttemptId != record.AttemptId || preview.ItemId != itemId || preview.AttemptId != record.AttemptId || preview.Hash != decision.PreviewEvidence ||
            preview.EffectiveRule != decision.EffectiveRule) throw AutomationValidation.Invalid("Only the exact, approved single-asset preview can resume.");
        ValidateExceptionalRule(record, decision.EffectiveRule); _files.Verify(record, "preview", preview.Bundle);
        record = Save(owner, record with { EffectiveProfile = decision.EffectiveRule, Parameters = preview.Parameters, ExceptionalDecisionId = decisionId,
            ExceptionalPreviewHash = preview.Hash, Authority = new(NormalizationAuthorityKind.HumanDecision, decisionId, decision.Actor!, decision.Utc, null),
            Code = PreparationCode.Pending }, QueueStage.Review);
        _queue.Transition(owner, itemId, _queue.Get(itemId).Revision, QueueState.Queued); return Prepare(owner, itemId, token);
    }
    public void RevokeException(AutomationQueueOwner owner, QueueItemId itemId, string decisionId, string actor)
    {
        owner.Require(_context.Storage.StateRoot); AutomationValidation.Text(actor);
        var item = _queue.Get(itemId); var record = GetRecord(itemId) ?? throw AutomationValidation.Invalid("No preparation evidence.");
        var decision = _queue.Decisions(itemId).Where(d => d.DecisionId == decisionId).OrderBy(d => d.Version).LastOrDefault();
        if (item.State is not (QueueState.Queued or QueueState.NeedsReview) || decision is not { State: ExceptionalDecisionState.Approved } ||
            decision.AttemptId != record.AttemptId) throw AutomationValidation.Invalid("Revocation requires this attempt's approved individual decision.");
        // Park first: a cut before the durable revocation remains closed, and the same command can be repeated.
        Park(owner, item, record, PreparationCode.PolicyInactive);
        _queue.RecordDecision(owner, _queue.Get(itemId).Revision, decision with { Version = decision.Version + 1,
            State = ExceptionalDecisionState.Revoked, Actor = actor, Utc = DateTimeOffset.UtcNow });
    }
    private void ValidateExceptionalRule(PreparationRecord record, EffectiveRuleSnapshot effective)
    {
        AutomationValidation.Scope(_context.Id, effective.UniverseId); var selected = effective.ValidateAndGetRule();
        var baseline = UniverseProfileSnapshotRenderer.Context(record.BaseProfile, _context.Storage).Profile;
        if (selected.AssetType != record.BaseProfile.AssetType || !baseline.TryGetAssetRule(selected.AssetType, selected.ProductionProfile, out _))
            throw AutomationValidation.Invalid("An exception selects an existing rule of the same asset family.");
        var expected = JsonNode.Parse(record.BaseProfile.ProfileJson)!;
        foreach (var node in expected["asset_rules"]!.AsArray())
            if ((string?)node!["asset_type"] == selected.AssetType && (string?)node["production_profile"] == selected.ProductionProfile)
            { node["conversion"]!["output_width"] = selected.Conversion!.OutputWidth; node["conversion"]!["output_height"] = selected.Conversion.OutputHeight; }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(expected.ToJsonString()));
        if (UniverseProfileSnapshotRenderer.Render(UniverseProfileLoader.Load(stream)) != UniverseProfileSnapshotRenderer.Render(UniverseProfileSnapshotRenderer.Context(effective, _context.Storage).Profile))
            throw AutomationValidation.Invalid("Only dimensions of the selected complete existing rule may vary for this asset; routing/roles/conversion/other profiles are preserved.");
    }
    private void RequireExceptionalScope(QueueItem item)
    {
        if (_queue.CurrentPolicy().Reference != item.Policy)
            throw new AutomationException(AutomationError.PolicyMismatch, "The exceptional workflow requires its current exact policy scope.");
        if (_queue.GetPolicy(item.Policy).State is AutomationPolicyState.Paused or AutomationPolicyState.Revoked)
            throw new AutomationException(AutomationError.Disabled, "Paused or revoked scope cannot create or resume a normalization preview.");
    }
}
