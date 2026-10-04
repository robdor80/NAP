namespace NAP.Core;

/// <summary>Generic classification, package file, routing and conversion requirements for one exact type / profile pair.</summary>
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
        : this(assetType, productionProfile, allowedClassification, requiredClassification, packageFiles, null)
    {
    }

    public UniverseAssetRule(string assetType, string productionProfile,
        IEnumerable<string> allowedClassification, IEnumerable<string> requiredClassification,
        IEnumerable<AssetPackageFileRule> packageFiles, AssetRoutingRule? routing)
        : this(assetType, productionProfile, allowedClassification, requiredClassification, packageFiles, routing, null)
    {
    }

    public UniverseAssetRule(string assetType, string productionProfile,
        IEnumerable<string> allowedClassification, IEnumerable<string> requiredClassification,
        IEnumerable<AssetPackageFileRule> packageFiles, AssetRoutingRule? routing, ImageConversionRule? conversion)
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
        if (routing is not null && routing.Segments.Any(segment =>
            segment.Kind == AssetRouteSegmentKind.Classification &&
            !RequiredClassification.Contains(segment.Value!, StringComparer.Ordinal)))
            throw new ArgumentException("Routing classification dimensions must be required by this asset rule.", nameof(routing));
        Routing = routing;
        if (conversion is not null)
        {
            var source = PackageFiles.SingleOrDefault(file => string.Equals(file.Role, conversion.SourceRole, StringComparison.Ordinal));
            if (source is null || !source.Required ||
                !string.Equals(source.Extension, ".png", StringComparison.Ordinal) ||
                !string.Equals(source.ContentValidator, "png_master", StringComparison.Ordinal))
                throw new ArgumentException("Conversion requires a matching required PNG source with png_master validation.", nameof(conversion));
        }
        Conversion = conversion;
    }

    public string AssetType { get; }
    public string ProductionProfile { get; }
    public IReadOnlyList<string> AllowedClassification { get; }
    public IReadOnlyList<string> RequiredClassification { get; }
    public IReadOnlyList<AssetPackageFileRule> PackageFiles { get; }
    public AssetRoutingRule? Routing { get; }
    public ImageConversionRule? Conversion { get; }

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
