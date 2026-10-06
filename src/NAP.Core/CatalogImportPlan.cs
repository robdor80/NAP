namespace NAP.Core;

public sealed class CatalogImportPlan
{
    internal CatalogImportPlan(UniverseContext context, IEnumerable<CatalogAssetSnapshot> assets, NapIssueReport issues)
    { Context = context; Assets = Array.AsReadOnly(assets.OrderBy(a => a.AssetKey.AssetId, StringComparer.Ordinal).ToArray()); Issues = issues; }
    internal UniverseContext Context { get; }
    public UniverseId UniverseId => Context.Id;
    public IReadOnlyList<CatalogAssetSnapshot> Assets { get; }
    public NapIssueReport Issues { get; }
}
