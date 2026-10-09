using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace NAP.Core;

internal static class AutomationJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, MaxDepth = 48
    };
    internal static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Decode<T>(string json)
    {
        if (json.Length > 2 * 1024 * 1024) throw AutomationValidation.Corrupt("Stored contract exceeds format bounds.");
        using var document = JsonDocument.Parse(json);
        Unique(document.RootElement);
        var value = JsonSerializer.Deserialize<T>(json, Options) ?? throw AutomationValidation.Corrupt("Null persisted contract.");
        if (Encode(value) != json) throw AutomationValidation.Corrupt("Noncanonical, missing or inconsistent persisted fields.");
        return value;
    }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw AutomationValidation.Corrupt("Duplicate JSON field."); Unique(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Unique(child);
    }
    internal static Sha256Digest Hash<T>(T value) => HashBytes(Encoding.UTF8.GetBytes(Encode(value)));
    internal static Sha256Digest HashBytes(byte[] bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}
internal static class AutomationValidation
{
    internal static AutomationException Invalid(string message) => new(AutomationError.InvalidContract, message);
    internal static AutomationException Corrupt(string message) => new(AutomationError.CorruptStore, message);
    internal static void Id(string value, string prefix)
    {
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 32 ||
            !Guid.TryParseExact(value[prefix.Length..], "N", out var id) || id == Guid.Empty || value[prefix.Length..].Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Invalid("Noncanonical workflow identity.");
    }
    internal static void Text(string value, int max = 256) { if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl)) throw Invalid("Invalid bounded text."); }
    internal static void Positive(long value) { if (value <= 0) throw Invalid("Positive value required."); }
    internal static void Utc(DateTimeOffset value) { if (value.Offset != TimeSpan.Zero || value == default) throw Invalid("An explicit UTC timestamp is required."); }
    internal static void Defined<T>(T value) where T : struct, Enum { if (!Enum.IsDefined(value)) throw Invalid("Unknown enum value."); }
    internal static void Scope(UniverseId expected, UniverseId actual) { if (expected is null || actual != expected) throw new AutomationException(AutomationError.UniverseMismatch, "Contract belongs to another universe."); }
    internal static void Reference(PolicyReference value) { ArgumentNullException.ThrowIfNull(value); UniverseProfileIdentifiers.Require(value.PolicyId, nameof(value.PolicyId)); Positive(value.Version); ArgumentNullException.ThrowIfNull(value.Hash); }
    internal static void Normalization(ProfileNormalizationPolicy value)
    {
        ArgumentNullException.ThrowIfNull(value); UniverseProfileIdentifiers.Require(value.AssetType, nameof(value.AssetType)); UniverseProfileIdentifiers.Require(value.ProductionProfile, nameof(value.ProductionProfile));
        ArgumentNullException.ThrowIfNull(value.Ratio); ArgumentNullException.ThrowIfNull(value.Limits); value.Limits.Validate(); Positive(value.MaxMemoryBytes);
        if (value.Method != "nearest-edge-canvas-v1") throw Invalid("Unsupported normalization method.");
    }
    internal static void Policy(AutomationPolicyDefinition value, UniverseStorageConfig roots)
    {
        ArgumentNullException.ThrowIfNull(value); Reference(value.Reference); Scope(roots.UniverseId, value.Roots.UniverseId);
        if (!SameRoots(roots, value.Roots)) throw new AutomationException(AutomationError.PolicyMismatch, "Policy roots do not match this queue.");
        if (((int)value.Operations & ~63) != 0) throw Invalid("Unknown automation operations.");
        ArgumentNullException.ThrowIfNull(value.Resources); Positive(value.Resources.MaxMemoryBytes); Positive(value.Resources.MaxStagingBytes); Positive(value.Resources.MaxPendingItems);
        ArgumentNullException.ThrowIfNull(value.Audit); if (value.Audit.MaxRequests < 0 || value.Audit.MaxCost < 0 || value.Audit.MaxAttempts is < 1 or > 100 || value.Audit.TimeoutSeconds is < 1 or > 3600) throw Invalid("Invalid audit budget.");
        ArgumentNullException.ThrowIfNull(value.Normalization);
        var keys = new HashSet<(string, string)>();
        foreach (var entry in value.Normalization)
        {
            Normalization(entry);
            if (!keys.Add((entry.AssetType, entry.ProductionProfile))) throw Invalid("Duplicate normalization profile policy.");
            if (roots.UniverseId.Value == "nimroel")
            {
                var canon = entry.ProductionProfile switch { "portrait_npc" => NimroelAutomationCanon.Portrait, "scene_cartography" => NimroelAutomationCanon.Cartography, "scene_narrative" => NimroelAutomationCanon.Narrative, _ => null };
                if (canon is not null && entry.Ratio != canon) throw Invalid("Nimroel normalization policy cannot replace its approved canonical ratio.");
            }
        }
        if (value.Normalization.Count > 256) throw Invalid("Normalization policy inventory too large.");
    }
    internal static bool SameRoots(UniverseStorageConfig a, UniverseStorageConfig b) => a.UniverseId == b.UniverseId &&
        ProductionPaths.Same(a.WorkspaceRoot, b.WorkspaceRoot) && ProductionPaths.Same(a.ProductionRoot, b.ProductionRoot) && ProductionPaths.Same(a.ArchiveRoot, b.ArchiveRoot);
    internal static void Zip(QueueZipIdentity zip) { ArgumentNullException.ThrowIfNull(zip); ArgumentNullException.ThrowIfNull(zip.Hash); Positive(zip.SizeBytes); }
    internal static void Relative(string? path) { if (path is not null) { Text(path, 2048); ProductionPaths.Resolve(Path.GetFullPath(Path.GetTempPath()), path); } }
    internal static void Content(WorkflowContentReference value) { ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Hash); Positive(value.SizeBytes); if (value.PreservationReference is not null) Text(value.PreservationReference, 2048); }
    internal static void Authority(NormalizationAuthority value)
    {
        ArgumentNullException.ThrowIfNull(value); Defined(value.Kind); Text(value.AuthorizationId); Text(value.Actor); Utc(value.Utc);
        if (value.Kind == NormalizationAuthorityKind.AuthorizedPolicy) Reference(value.Policy ?? throw Invalid("Policy authority requires exact policy binding."));
        else if (value.Policy is not null) throw Invalid("An individual decision must not impersonate global authorization.");
    }
    internal static void Lineage(NormalizationLineage value)
    {
        Id(value.OperationId, "normalization_"); Positive(value.Version); Utc(value.Utc); Defined(value.State); Content(value.OriginalZip); Content(value.OriginalPng);
        Normalization(value.Parameters); Authority(value.Authority); ArgumentNullException.ThrowIfNull(value.EvidenceHash);
        if ((value.DerivedPng is null) != (value.CandidateZip is null)) throw Invalid("Derivative PNG and ZIP must be declared together.");
        if (value.DerivedPng is not null) { Content(value.DerivedPng); Content(value.CandidateZip!); }
        if (value.State == NormalizationOperationState.DerivativeDeclared && (value.DerivedPng is null || value.Geometry is null)) throw Invalid("Derivative declaration requires geometry and identities.");
        if (value.Geometry is { } g)
        {
            var conversion = new ImageConversionRule(ImageConversionKind.PngToWebp, "master", value.Parameters.Ratio.Width, value.Parameters.Ratio.Height, 90);
            if (ImageNormalizationGeometry.Calculate(new(g.OriginalWidth, g.OriginalHeight, 8, 6, 0), conversion, value.Parameters.Limits) != g) throw Invalid("Geometry does not match frozen normalization parameters.");
        }
    }
    internal static void Decision(ExceptionalConfigurationDecision value)
    {
        Id(value.DecisionId, "decision_"); Positive(value.Version); Defined(value.Scope); Defined(value.State); Text(value.OriginalProfile); Utc(value.Utc);
        Scope(value.UniverseId, value.EffectiveRule.UniverseId);
        var rule = value.EffectiveRule.ValidateAndGetRule();
        if (!value.Ratio.Matches(value.OutputWidth, value.OutputHeight) || rule.Conversion!.OutputWidth != value.OutputWidth || rule.Conversion.OutputHeight != value.OutputHeight)
            throw Invalid("Ratio and dimensions must match the complete effective rule.");
        ArgumentNullException.ThrowIfNull(value.PreviewEvidence);
        if (value.State != ExceptionalDecisionState.Proposed) Text(value.Actor ?? "");
    }
}
