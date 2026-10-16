using System.Text.Json;
namespace NAP.Core;

public sealed class ProductionAssetResult
{
    internal ProductionAssetResult(System.Text.Json.JsonElement e, System.Text.Json.JsonSerializerOptions o)
    {
        AssetKey = e.GetProperty(nameof(AssetKey)).Deserialize<UniverseAssetKey>(o)!;
        Outcome = e.GetProperty(nameof(Outcome)).Deserialize<ProductionAssetOutcome>(o);
        RelativeDirectory = e.GetProperty(nameof(RelativeDirectory)).GetString()!;
        FilesVerified = e.GetProperty(nameof(FilesVerified)).Deserialize<ProductionAssetFile[]>(o)!;
        Issues = e.GetProperty(nameof(Issues)).Deserialize<NapIssueReport>(o)!;
    }
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
