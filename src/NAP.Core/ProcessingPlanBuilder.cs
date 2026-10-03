namespace NAP.Core;

/// <summary>Combines coherent validation boundaries without re-routing or consulting the filesystem.</summary>
public sealed class ProcessingPlanBuilder
{
    public ProcessingPlan Build(
        ValidatedAssetPackage package,
        ValidatedProductionRepository repository,
        ProductionAssetDestination destination)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(destination);
        if (package.AssetKey.UniverseId != repository.UniverseId)
            throw new ArgumentException("The package and production repository must belong to the same universe.", nameof(repository));
        if (destination.AssetKey != package.AssetKey)
            throw new ArgumentException("The destination must identify the package's complete asset key.", nameof(destination));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.RootPath));
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination.RootPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(root, destinationRoot, comparison))
            throw new ArgumentException("The destination must use the authorized production root.", nameof(destination));

        var manifest = package.Manifest;
        return new ProcessingPlan(package.AssetKey, manifest.AssetType, manifest.ProductionProfile,
            manifest.Classification, package.PackageRoot, package.ManifestPath, package.FilesByRole, destination);
    }
}
