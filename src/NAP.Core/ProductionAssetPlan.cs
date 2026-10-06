namespace NAP.Core;

/// <summary>Immutable production expectations; execution must revalidate sources, archive and destination under its root mutex.</summary>
public sealed class ProductionAssetPlan
{
    internal ProductionAssetPlan(ValidatedAssetPackage package, ProcessingPlan processingPlan, ArchiveMasterResult archive,
        string productionRoot, ResolvedImageConversion conversion, IEnumerable<ProductionAssetFile> files,
        IEnumerable<ProductionAssetFile> filesToWrite, NapIssueReport issues, JobId? jobId = null)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(processingPlan);
        ArgumentNullException.ThrowIfNull(archive); ArgumentNullException.ThrowIfNull(conversion);
        ArgumentNullException.ThrowIfNull(files); ArgumentNullException.ThrowIfNull(filesToWrite); ArgumentNullException.ThrowIfNull(issues);
        var snapshot = files.ToArray(); var missing = filesToWrite.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(f => f is null) || missing.Any(f => f is null || !snapshot.Contains(f))) throw new ArgumentException("Invalid production file snapshot.");
        Package = package; Processing = processingPlan; Archive = archive;
        AssetKey = package.AssetKey; AssetType = processingPlan.AssetType; ProductionRoot = productionRoot;
        RelativeDirectory = processingPlan.ProductionDestination.RelativeDirectory;
        DestinationDirectory = ProductionPaths.Resolve(productionRoot, RelativeDirectory);
        Conversion = conversion; Files = Array.AsReadOnly(snapshot); FilesToWrite = Array.AsReadOnly(missing); Issues = issues; JobId = jobId;
        Action = !issues.ShouldStop && missing.Length == 0 ? ProductionAssetAction.AlreadyProduced : ProductionAssetAction.WriteAndVerify;
    }

    internal ValidatedAssetPackage Package { get; }
    internal ProcessingPlan Processing { get; }
    internal ArchiveMasterResult Archive { get; }
    public UniverseAssetKey AssetKey { get; }
    public JobId? JobId { get; }
    public string AssetType { get; }
    public string ProductionRoot { get; }
    public string RelativeDirectory { get; }
    public string DestinationDirectory { get; }
    public ResolvedImageConversion Conversion { get; }
    public IReadOnlyList<ProductionAssetFile> Files { get; }
    public IReadOnlyList<ProductionAssetFile> FilesToWrite { get; }
    public NapIssueReport Issues { get; }
    public ProductionAssetAction Action { get; }
}
