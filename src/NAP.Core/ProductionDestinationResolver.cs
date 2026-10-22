namespace NAP.Core;

/// <summary>Purely calculates the directory declared by the rule retained in a validated package.</summary>
public sealed class ProductionDestinationResolver
{
    public ProductionAssetDestination Resolve(
        ValidatedAssetPackage package,
        ValidatedProductionRepository repository)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(repository);
        if (package.AssetKey.UniverseId != repository.UniverseId)
            throw new ArgumentException("The package and production repository must belong to the same universe.", nameof(repository));

        var relativeDirectory = ResolveRelative(package.AssetRule, package.Manifest.Classification, package.AssetKey.AssetId);
        var fullDirectoryPath = repository.RootPath;
        foreach (var segment in relativeDirectory.Split('/'))
            fullDirectoryPath = Path.Combine(fullDirectoryPath, segment);
        fullDirectoryPath = Path.GetFullPath(fullDirectoryPath);
        if (!IsStrictlyWithinRoot(repository.RootPath, fullDirectoryPath))
            throw new InvalidOperationException("The destination must be strictly within the authorized production root.");

        return new ProductionAssetDestination(package.AssetKey, repository.RootPath, relativeDirectory, fullDirectoryPath);
    }

    // Shared calculation only. This internal function grants no validated package, root or write authority.
    internal static string ResolveRelative(UniverseAssetRule rule, IReadOnlyDictionary<string, string> classification, string assetId)
    {
        var routing = rule.Routing
            ?? throw new InvalidOperationException("The package's validated asset rule has no routing.");
        var segments = new string[routing.Segments.Count];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = routing.Segments[index];
            string value;
            switch (segment.Kind)
            {
                case AssetRouteSegmentKind.Literal:
                    value = segment.Value!;
                    break;
                case AssetRouteSegmentKind.Classification:
                    if (!classification.TryGetValue(segment.Value!, out var classifiedValue))
                        throw new InvalidOperationException("A routing classification dimension is missing from the validated manifest.");
                    value = classifiedValue;
                    break;
                case AssetRouteSegmentKind.AssetId:
                    value = assetId;
                    break;
                default:
                    throw new InvalidOperationException("The routing segment kind is invalid.");
            }

            var valid = segment.Kind == AssetRouteSegmentKind.AssetId
                ? AssetNamingRules.IsValidAssetId(value)
                : AssetNamingRules.IsValidMachineIdentifier(value);
            if (!valid || IsReservedDeviceName(value))
                throw new InvalidOperationException("A resolved routing segment is unsafe for a production directory.");
            segments[index] = value;
        }

        return string.Join("/", segments);
    }

    // Lexical containment only: links and filesystem state belong to future write boundaries.
    internal static bool IsStrictlyWithinRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(candidate), comparison))
            return false;
        var boundary = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(boundary, comparison);
    }

    private static bool IsReservedDeviceName(string value) =>
        value.Equals("con", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("prn", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("aux", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("nul", StringComparison.OrdinalIgnoreCase) ||
        (value.Length == 4 && value[3] is >= '1' and <= '9' &&
            (value.StartsWith("com", StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)));
}
