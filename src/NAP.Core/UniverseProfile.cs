using System.Diagnostics.CodeAnalysis;

namespace NAP.Core;

/// <summary>Universe identity and generic classification configuration, independent of lore and storage.</summary>
public sealed record UniverseProfile
{
    public UniverseProfile(UniverseId id, string displayName)
        : this(id, displayName, [], [])
    {
    }

    public UniverseProfile(UniverseId id, string displayName,
        IEnumerable<string> classificationDimensions, IEnumerable<UniverseAssetRule> assetRules)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
        ClassificationDimensions = UniverseProfileIdentifiers.Snapshot(classificationDimensions, nameof(classificationDimensions));
        ArgumentNullException.ThrowIfNull(assetRules);
        var rules = assetRules.ToArray();
        var combinations = new HashSet<(string AssetType, string ProductionProfile)>();
        foreach (var rule in rules)
        {
            if (rule is null)
                throw new ArgumentException("Asset rules cannot contain null.", nameof(assetRules));
            if (!combinations.Add((rule.AssetType, rule.ProductionProfile)))
                throw new ArgumentException("Duplicate asset type / production profile combinations are not allowed.", nameof(assetRules));
            if (rule.AllowedClassification.Any(dimension => !ClassificationDimensions.Contains(dimension, StringComparer.Ordinal)))
                throw new ArgumentException("Rule dimensions must be registered by the universe profile.", nameof(assetRules));
        }
        AssetRules = Array.AsReadOnly(rules);
    }

    public UniverseId Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> ClassificationDimensions { get; }
    public IReadOnlyList<UniverseAssetRule> AssetRules { get; }

    public bool TryGetAssetRule(string assetType, string productionProfile,
        [NotNullWhen(true)] out UniverseAssetRule? rule)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(productionProfile);
        rule = AssetRules.FirstOrDefault(candidate =>
            string.Equals(candidate.AssetType, assetType, StringComparison.Ordinal) &&
            string.Equals(candidate.ProductionProfile, productionProfile, StringComparison.Ordinal));
        return rule is not null;
    }
}
