namespace NAP.Core;

/// <summary>Versioned workflow facts, independent of the unchanged manual request builder API.</summary>
public sealed class WorkflowAiAuditRequestBuilder
{
    public AiAuditRequest Build(ProcessingPlan plan, NapIssueReport report, PreparationSnapshot snapshot,
        PreparationOperationalGrant grant)
    {
        var legacy = new AiAuditRequestBuilder().Build(plan, report);
        if (snapshot.UniverseId != plan.AssetKey.UniverseId || snapshot.Plan.DestinationRelativeDirectory != plan.ProductionDestination.RelativeDirectory || snapshot.Policy != grant.Policy)
            throw AutomationValidation.Invalid("Audit workflow facts must match the complete plan scope.");
        return new(legacy.AssetKey, legacy.AssetType, legacy.ProductionProfile, legacy.Classification, legacy.InputRoles,
            legacy.DestinationRelativeDirectory, legacy.ValidationIssues) { WorkflowEvidenceJson = AutomationJson.Encode(new
            {
                SnapshotHash = snapshot.Hash, snapshot.SchemaVersion, snapshot.ItemId, snapshot.AttemptId, snapshot.JobId,
                snapshot.UniverseId, RootsHash = AutomationJson.Hash(snapshot.Roots), snapshot.Policy, snapshot.AdmissionHash,
                snapshot.OriginalZip, snapshot.OriginalFiles, snapshot.EffectiveFiles, snapshot.BaseProfile, snapshot.EffectiveProfile,
                snapshot.EngineIdentity, snapshot.ManifestJson, snapshot.Plan, snapshot.NormalizationOperation,
                AuthorityKind = snapshot.Authority?.Kind, AuthorityHash = snapshot.Authority is null ? null : AutomationJson.Hash(snapshot.Authority),
                snapshot.Parameters, snapshot.Geometry, snapshot.ExceptionalDecisionId, snapshot.ExceptionalPreviewHash,
                GrantHash = grant.Hash, grant.ProviderId, grant.ModelId
            }) };
    }
}
