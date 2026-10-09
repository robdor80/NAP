using System.Text;

namespace NAP.Core;

public sealed record RationalProportion
{
    public int Width { get; }
    public int Height { get; }
    public RationalProportion(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 16383 || height > 16383) throw AutomationValidation.Invalid("Ratio must have bounded positive terms.");
        var a = width; var b = height; while (b != 0) (a, b) = (b, a % b);
        Width = width / a; Height = height / a;
    }
    public bool Matches(int width, int height) => width > 0 && height > 0 && (long)width * Height == (long)height * Width;
}
/// <summary>Design canon only; these ratios do not create or enable production profiles.</summary>
public static class NimroelAutomationCanon
{
    public static RationalProportion Portrait => new(4, 5);
    public static RationalProportion Narrative => new(16, 10);
    public static RationalProportion Cartography => new(16, 10);
    public static RationalProportion Seal => new(1, 1);
}
public sealed record AutomationResourceLimits(long MaxMemoryBytes, long MaxStagingBytes, int MaxPendingItems);
public sealed record AutomationAuditBudget(int MaxRequests, int MaxAttempts, int TimeoutSeconds, decimal MaxCost);
public sealed record ProfileNormalizationPolicy(string AssetType, string ProductionProfile, RationalProportion Ratio,
    ImageNormalizationPolicy Limits, long MaxMemoryBytes, string Method, bool OperationallyValidated);
public sealed record AutomationPolicyDefinition(string PolicyId, int Version, UniverseStorageConfig Roots,
    AutomationOperations Operations, AutomationResourceLimits Resources, AutomationAuditBudget Audit,
    IReadOnlyList<ProfileNormalizationPolicy> Normalization)
{
    private IReadOnlyList<ProfileNormalizationPolicy> _normalization = Array.AsReadOnly(Normalization.ToArray());
    public IReadOnlyList<ProfileNormalizationPolicy> Normalization { get => _normalization; init => _normalization = Array.AsReadOnly(value.ToArray()); }
    [System.Text.Json.Serialization.JsonIgnore]
    public PolicyReference Reference => new(PolicyId, Version, AutomationJson.Hash(this));
    public static AutomationPolicyDefinition DisabledDefault(UniverseStorageConfig roots) => new("default", 1, roots,
        AutomationOperations.None, new(512L * 1024 * 1024, 2L * 1024 * 1024 * 1024, 1000), new(0, 1, 60, 0), []);
}
public sealed record AutomationAuthorization(string AuthorizationId, UniverseId UniverseId, PolicyReference Policy,
    string Actor, DateTimeOffset AuthorizedUtc);
public sealed record StoredAutomationPolicy(AutomationPolicyDefinition Definition, PolicyReference Reference,
    AutomationPolicyState State, AutomationAuthorization? Authorization, long Revision)
{
    /// <summary>Phase 1 never grants execution authority, even if an authorization reference is recorded.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsExecutionAuthorized => false;
}
public sealed record NormalizationAuthority(NormalizationAuthorityKind Kind, string AuthorizationId, string Actor,
    DateTimeOffset Utc, PolicyReference? Policy);

/// <summary>Full declarative profile bytes, validated by the existing loader. A ratio alone is insufficient.</summary>
public sealed record EffectiveRuleSnapshot(UniverseId UniverseId, int Version, string AssetType,
    string ProductionProfile, string ProfileJson, Sha256Digest Hash)
{
    public UniverseAssetRule ValidateAndGetRule() => AutomationQueueBoundary.Guard(() =>
    {
        AutomationValidation.Positive(Version);
        if (string.IsNullOrWhiteSpace(ProfileJson) || ProfileJson.Length > 1024 * 1024) throw AutomationValidation.Invalid("Profile snapshot must be bounded JSON.");
        if (AutomationJson.HashBytes(Encoding.UTF8.GetBytes(ProfileJson)) != Hash) throw AutomationValidation.Invalid("Profile snapshot hash mismatch.");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(ProfileJson));
        var profile = UniverseProfileLoader.Load(input);
        if (profile.Id != UniverseId) throw new AutomationException(AutomationError.UniverseMismatch, "Profile snapshot belongs to another universe.");
        if (!profile.TryGetAssetRule(AssetType, ProductionProfile, out var rule) || rule.Routing is null || rule.Conversion is null)
            throw AutomationValidation.Invalid("A complete production rule with routing and conversion is required.");
        return rule;
    });
    public static EffectiveRuleSnapshot Create(UniverseId universe, int version, string type, string profile, string json)
    {
        var value = new EffectiveRuleSnapshot(universe, version, type, profile, json, AutomationJson.HashBytes(Encoding.UTF8.GetBytes(json)));
        value.ValidateAndGetRule(); return value;
    }
}
public sealed record WorkflowContentReference(Sha256Digest Hash, long SizeBytes, string? PreservationReference);
/// <summary>Metadata only: neither a digest nor a reference proves physical preservation or verified publication.</summary>
public sealed record NormalizationLineage(string OperationId, int Version, QueueItemId ItemId, WorkflowAttemptId AttemptId,
    UniverseId UniverseId, JobId JobId, WorkflowContentReference OriginalZip, WorkflowContentReference OriginalPng,
    WorkflowContentReference? DerivedPng, WorkflowContentReference? CandidateZip, NormalizationOperationState State,
    ProfileNormalizationPolicy Parameters, NormalizationAuthority Authority, Sha256Digest EvidenceHash, DateTimeOffset Utc,
    ImageNormalizationGeometry? Geometry);
public sealed record ExceptionalConfigurationDecision(string DecisionId, int Version, QueueItemId ItemId,
    WorkflowAttemptId AttemptId, UniverseId UniverseId, string OriginalProfile, EffectiveRuleSnapshot EffectiveRule,
    RationalProportion Ratio, int OutputWidth, int OutputHeight, Sha256Digest PreviewEvidence,
    ExceptionalScope Scope, ExceptionalDecisionState State, string? Actor, DateTimeOffset Utc);
