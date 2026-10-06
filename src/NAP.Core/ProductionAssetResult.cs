namespace NAP.Core;

public sealed class ProductionAssetResult
{
    internal ProductionAssetResult(ProductionAssetPlan plan, ProductionAssetOutcome outcome)
    {
        if (plan.Issues.ShouldStop || !Enum.IsDefined(outcome)) throw new ArgumentException("Expected a verified successful production result.");
        AssetKey = plan.AssetKey; Outcome = outcome; RelativeDirectory = plan.RelativeDirectory;
        FilesVerified = Array.AsReadOnly(plan.Files.ToArray()); Issues = plan.Issues;
    }
    public UniverseAssetKey AssetKey { get; }
    public ProductionAssetOutcome Outcome { get; }
    public string RelativeDirectory { get; }
    public IReadOnlyList<ProductionAssetFile> FilesVerified { get; }
    public NapIssueReport Issues { get; }
}
