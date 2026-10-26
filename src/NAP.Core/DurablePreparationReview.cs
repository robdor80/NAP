using System.Text.Json.Nodes;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace NAP.Core;

/// <summary>Individual exceptional preparation only. No global policy change or audit override.</summary>
public sealed partial class DurablePreparationService
{
    private sealed record Choice(ReviewChoice View, EffectiveRuleSnapshot Rule, ProfileNormalizationPolicy Parameters);
    private string OriginalMaster(PreparationRecord record)
    {
        _files.Verify(record, "original", record.Originals ?? throw AutomationValidation.Invalid("No preserved original."));
        var admission = _admission.VerifyAdmitted(record.ItemId);
        var rule = record.BaseProfile.ValidateAndGetRule();
        var path = Path.Combine(_files.Resolve(record.Originals!), "package", admission.AssetId!, rule.PackageFiles.Single(f => f.Role == rule.Conversion!.SourceRole).ResolveFileName(admission.AssetId!));
        var info = new PngMasterValidator().Validate(path).ImageInfo ?? throw AutomationValidation.Invalid("Invalid original image.");
        var pixels=checked((long)info.Width*info.Height);
        if (pixels>PackagePreparationService.MaxInputPixels || new Sha256Hasher().Compute(path) != record.MasterHash || checked(32L * 1024 * 1024 + 16L * pixels) > _queue.GetPolicy(record.Policy).Definition.Resources.MaxMemoryBytes)
            throw AutomationValidation.Invalid("Original identity or preview memory budget differs.");
        return path;
    }
    private byte[] Thumbnail(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        image.Mutate(c => c.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(256, 256) }));
        using var output = new MemoryStream(); image.SaveAsPng(output);
        if (output.Length > 300_000) throw AutomationValidation.Invalid("Preview exceeds its bounded transport.");
        return output.ToArray();
    }
    public byte[]? ReadPreservedPreview(QueueItemId item)
    {
        var record = GetRecord(item);
        return record?.Originals is null || record.MasterHash is null ? null : Thumbnail(OriginalMaster(record));
    }
    public bool CanResolveException(QueueItemId id)
    {
        var item = _queue.Get(id); var record = GetRecord(id);
        return item.State == QueueState.NeedsReview && record is { Checkpoint: PreparationCheckpoint.Preserved, Candidate: null } &&
            item.Incident is QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded or QueueIncident.ExceptionDecisionRequired;
    }
    private IReadOnlyList<Choice> ReviewOptions(QueueItemId itemId)
    {
        var item = _queue.Get(itemId); var record = GetRecord(itemId);
        if (item.State != QueueState.NeedsReview || record is not { Checkpoint: PreparationCheckpoint.Preserved, Candidate: null } ||
            item.Incident is not (QueueIncident.RatioUnsupported or QueueIncident.NormalizationLimitExceeded or QueueIncident.ExceptionDecisionRequired)) return [];
        var path = OriginalMaster(record); var info = new PngMasterValidator().Validate(path).ImageInfo!;
        var baseline = UniverseProfileSnapshotRenderer.Context(record.BaseProfile, _context.Storage).Profile;
        var baseRule = record.BaseProfile.ValidateAndGetRule(); var policy = _queue.GetPolicy(item.Policy);
        var result = new List<Choice>();
        foreach (var target in baseline.AssetRules.Where(r => r.AssetType == baseRule.AssetType && r.Conversion is not null &&
            r.Conversion.SourceRole == baseRule.Conversion!.SourceRole &&
            AutomationJson.Encode(r.PackageFiles) == AutomationJson.Encode(baseRule.PackageFiles) &&
            AutomationJson.Encode(r.AllowedClassification) == AutomationJson.Encode(baseRule.AllowedClassification) &&
            AutomationJson.Encode(r.RequiredClassification) == AutomationJson.Encode(baseRule.RequiredClassification)))
        {
            foreach (var size in new[] { (target.Conversion!.OutputWidth, target.Conversion.OutputHeight), (info.Width, info.Height) }.Distinct())
            {
                try
                {
                    var node = JsonNode.Parse(record.BaseProfile.ProfileJson)!;
                    foreach (var r in node["asset_rules"]!.AsArray())
                        if ((string?)r!["asset_type"] == target.AssetType && (string?)r["production_profile"] == target.ProductionProfile)
                        { r["conversion"]!["output_width"] = size.Item1; r["conversion"]!["output_height"] = size.Item2; }
                    var rule = EffectiveRuleSnapshot.Create(_context.Id, record.BaseProfile.Version, target.AssetType, target.ProductionProfile, node.ToJsonString());
                    ValidateExceptionalRule(record, rule);
                    var parameters = policy.Definition.Normalization.FirstOrDefault(p => p.AssetType == target.AssetType && p.ProductionProfile == target.ProductionProfile)
                        ?? new(target.AssetType, target.ProductionProfile, new(size.Item1, size.Item2), new() { MaxFileBytes=4L*1024*1024, MaxPackageBytes=8L*1024*1024, MaxPackageEntries=32 }, Math.Min(policy.Definition.Resources.MaxMemoryBytes, 128L * 1024 * 1024), "nearest-edge-canvas-v1", false);
                    parameters = parameters with { Ratio = new(size.Item1, size.Item2), Limits = parameters.Limits with
                    { MaxFileBytes=Math.Min(parameters.Limits.MaxFileBytes,4L*1024*1024), MaxPackageBytes=Math.Min(parameters.Limits.MaxPackageBytes,8L*1024*1024), MaxPackageEntries=Math.Min(parameters.Limits.MaxPackageEntries,32) } };
                    var geometry = ImageNormalizationGeometry.Calculate(info, rule.ValidateAndGetRule().Conversion!, parameters.Limits);
                    RequireNormalizationMemory(info, geometry, parameters, policy.Definition.Resources.MaxMemoryBytes);
                    RequireNormalizationPackage(_admission.VerifyAdmitted(itemId), item.Zip, parameters);
                    var key = AutomationJson.Hash(new { rule, parameters }).Hex;
                    var ratio = parameters.Ratio;
                    var label = $"{ProfileLabel(target.ProductionProfile)} · {size.Item1} × {size.Item2} ({ratio.Width}:{ratio.Height})";
                    result.Add(new(new(key, label, ProfileLabel(target.ProductionProfile), size.Item1, size.Item2), rule, parameters));
                }
                catch (Exception e) when (e is ImageNormalizationException or ArgumentException) { /* This option is outside the engine's limits. */ }
            }
        }
        return result.DistinctBy(c => c.View.Key).Take(32).ToArray();
    }
    public static string ProfileLabel(string profile) => profile switch { "portrait_npc" => "Retrato", "scene_cartography" => "Cartografía", "scene_narrative" => "Escena", _ => profile.Replace('_', ' ') };
    public IReadOnlyList<ReviewChoice> ReadReviewChoices(QueueItemId item) => ReviewOptions(item).Select(c => c.View).ToArray();
    public ExceptionalPreparationPreview ProposeReviewChoice(AutomationQueueOwner owner, ReviewProposalRequest request, string actor)
    {
        var item = _queue.Get(request.ItemId);
        if (item.Revision != request.Revision) throw new AutomationException(AutomationError.RevisionConflict, "Review changed.");
        var choice = ReviewOptions(item.Id).SingleOrDefault(c => c.View.Key == request.ChoiceKey) ?? throw AutomationValidation.Invalid("Choice is not compatible.");
        foreach (var prior in _queue.Decisions(item.Id).GroupBy(d => d.DecisionId).Select(g => g.OrderBy(d => d.Version).Last())
            .Where(d => d.State is ExceptionalDecisionState.Proposed or ExceptionalDecisionState.Approved))
            _queue.RecordDecision(owner, _queue.Get(item.Id).Revision, prior with { Version = prior.Version + 1, State = ExceptionalDecisionState.Revoked, Actor = actor, Utc = DateTimeOffset.UtcNow });
        return ProposeException(owner, item.Id, choice.Rule, choice.Parameters);
    }
    public ReviewPreview? ReadReviewPreview(QueueItemId itemId)
    {
        var decision = _queue.LatestReviewDecision(itemId);
        if (decision is null) return null;
        if (decision.State == ExceptionalDecisionState.Revoked) return null;
        var record = GetRecord(itemId) ?? throw AutomationValidation.Invalid("No preparation record.");
        var preview = _state.Preview(decision.DecisionId) ?? throw AutomationValidation.Invalid("Missing preview.");
        if (preview.ItemId != itemId || preview.AttemptId != record.AttemptId || preview.UniverseId != _context.Id || preview.EffectiveRule != decision.EffectiveRule || preview.Hash != decision.PreviewEvidence)
            throw AutomationValidation.Invalid("Preview binding changed.");
        var original = OriginalMaster(record); _files.Verify(record, "preview", preview.Bundle);
        ValidateExceptionalRule(record, preview.EffectiveRule);
        var g = preview.Geometry;
        return new(decision.DecisionId, decision.Version, preview.Hash, decision.State, g.OriginalWidth, g.OriginalHeight, g.CanvasWidth, g.CanvasHeight,
            decision.OutputWidth, decision.OutputHeight, g.Left, g.Top, g.Right, g.Bottom, ProfileLabel(decision.EffectiveRule.ProductionProfile), Thumbnail(original), Thumbnail(Path.Combine(_files.Resolve(preview.Bundle), "preview.png")),ProfileLabel(record.BaseProfile.ProductionProfile));
    }
    public void ApproveReview(AutomationQueueOwner owner, ReviewDecisionRequest request, string actor)
    {
        var item = _queue.Get(request.ItemId);
        if (item.Revision != request.Revision) throw new AutomationException(AutomationError.RevisionConflict, "Review changed.");
        var preview = ReadReviewPreview(item.Id) ?? throw AutomationValidation.Invalid("No verified preview.");
        if (!request.Confirmed || !CanResolveException(item.Id) || item.State != QueueState.NeedsReview || preview.State != ExceptionalDecisionState.Proposed || preview.DecisionId != request.DecisionId || preview.Version != request.Version || preview.Evidence != request.Evidence)
            throw AutomationValidation.Invalid("Approval must bind the exact displayed preview.");
        var record = GetRecord(item.Id)!;
        if (record.Checkpoint != PreparationCheckpoint.Preserved || record.Candidate is not null) throw AutomationValidation.Invalid("Preparation is already bound.");
        var decision = _queue.Decisions(item.Id).Single(d => d.DecisionId == preview.DecisionId && d.Version == preview.Version);
        _queue.RecordDecision(owner, item.Revision, decision with { Version = decision.Version + 1, State = ExceptionalDecisionState.Approved, Actor = actor, Utc = DateTimeOffset.UtcNow });
    }
    public PreparationResult ContinueReview(AutomationQueueOwner owner, ReviewDecisionRequest request)
    {
        var item = _queue.Get(request.ItemId); var preview = ReadReviewPreview(item.Id);
        if (!CanResolveException(item.Id) || item.Revision != request.Revision || preview is null || preview.State != ExceptionalDecisionState.Approved || preview.DecisionId != request.DecisionId || preview.Version != request.Version || preview.Evidence != request.Evidence)
            throw new AutomationException(AutomationError.RevisionConflict, "The approved preview changed.");
        return ResumeException(owner, item.Id, preview.DecisionId);
    }
}
