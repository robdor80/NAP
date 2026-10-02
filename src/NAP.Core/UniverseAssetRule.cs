namespace NAP.Core;

/// <summary>Generic classification requirements for one exact asset type / production profile pair.</summary>
public sealed record UniverseAssetRule
{
    public UniverseAssetRule(string assetType, string productionProfile,
        IEnumerable<string> allowedClassification, IEnumerable<string> requiredClassification)
    {
        UniverseProfileIdentifiers.Require(assetType, nameof(assetType));
        UniverseProfileIdentifiers.Require(productionProfile, nameof(productionProfile));
        AssetType = assetType;
        ProductionProfile = productionProfile;
        AllowedClassification = UniverseProfileIdentifiers.Snapshot(allowedClassification, nameof(allowedClassification));
        RequiredClassification = UniverseProfileIdentifiers.Snapshot(requiredClassification, nameof(requiredClassification));
        if (RequiredClassification.Any(dimension => !AllowedClassification.Contains(dimension, StringComparer.Ordinal)))
            throw new ArgumentException("Required dimensions must be allowed.", nameof(requiredClassification));
    }

    public string AssetType { get; }
    public string ProductionProfile { get; }
    public IReadOnlyList<string> AllowedClassification { get; }
    public IReadOnlyList<string> RequiredClassification { get; }

    /// <summary>Checks dimension presence/permission only. Values and package contents are not inspected.</summary>
    public ClassificationValidationResult ValidateClassification(IReadOnlyDictionary<string, string> classification)
    {
        ArgumentNullException.ThrowIfNull(classification);
        // Use ordinal identity even when the caller's dictionary uses another comparer.
        var keys = classification.Keys.ToArray();
        return new ClassificationValidationResult(
            RequiredClassification.Where(dimension => !keys.Contains(dimension, StringComparer.Ordinal)),
            keys.Where(dimension => !AllowedClassification.Contains(dimension, StringComparer.Ordinal)));
    }
}
