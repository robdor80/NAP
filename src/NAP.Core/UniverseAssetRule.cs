namespace NAP.Core;

/// <summary>Generic classification and package file requirements for one exact asset type / production profile pair.</summary>
public sealed record UniverseAssetRule
{
    public UniverseAssetRule(string assetType, string productionProfile,
        IEnumerable<string> allowedClassification, IEnumerable<string> requiredClassification)
        : this(assetType, productionProfile, allowedClassification, requiredClassification, [])
    {
    }

    public UniverseAssetRule(string assetType, string productionProfile,
        IEnumerable<string> allowedClassification, IEnumerable<string> requiredClassification,
        IEnumerable<AssetPackageFileRule> packageFiles)
    {
        UniverseProfileIdentifiers.Require(assetType, nameof(assetType));
        UniverseProfileIdentifiers.Require(productionProfile, nameof(productionProfile));
        AssetType = assetType;
        ProductionProfile = productionProfile;
        AllowedClassification = UniverseProfileIdentifiers.Snapshot(allowedClassification, nameof(allowedClassification));
        RequiredClassification = UniverseProfileIdentifiers.Snapshot(requiredClassification, nameof(requiredClassification));
        if (RequiredClassification.Any(dimension => !AllowedClassification.Contains(dimension, StringComparer.Ordinal)))
            throw new ArgumentException("Required dimensions must be allowed.", nameof(requiredClassification));

        ArgumentNullException.ThrowIfNull(packageFiles);
        var snapshot = packageFiles.ToArray();
        var roles = new HashSet<string>(StringComparer.Ordinal);
        var filenames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in snapshot)
        {
            if (file is null)
                throw new ArgumentException("Package file rules must not be null.", nameof(packageFiles));
            if (!roles.Add(file.Role))
                throw new ArgumentException("Package file roles must be unique.", nameof(packageFiles));
            var ending = file.Suffix + file.Extension;
            if (ending == "_manifest.json")
                throw new ArgumentException("The universal manifest must not be declared as a profile package file.", nameof(packageFiles));
            if (!filenames.Add(ending))
                throw new ArgumentException("Package file names must not collide.", nameof(packageFiles));
        }
        PackageFiles = Array.AsReadOnly(snapshot);
    }

    public string AssetType { get; }
    public string ProductionProfile { get; }
    public IReadOnlyList<string> AllowedClassification { get; }
    public IReadOnlyList<string> RequiredClassification { get; }
    public IReadOnlyList<AssetPackageFileRule> PackageFiles { get; }

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
