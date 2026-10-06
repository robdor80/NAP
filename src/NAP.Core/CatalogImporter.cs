namespace NAP.Core;

/// <summary>Whole-import preflight and atomic registration. Does not depend on original Jobs or production receipts.</summary>
public sealed class CatalogImporter
{
    private readonly UniverseContext _context;
    public CatalogImporter(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }
    public CatalogImportPlan Plan()
    {
        using var sources = CatalogSourceLease.Acquire(_context); return Discover();
    }
    internal CatalogImportPlan Discover()
    {
        ProductionStorageRootValidator.Require(_context); ArchiveRootValidator.Require(_context);
        var repository = new ProductionRepositoryValidator().Validate(_context).Repository!;
        var scan = new ProductionRepositoryScanner().Scan(repository);
        if (scan.Issues.ShouldStop) return new CatalogImportPlan(_context, [], scan.Issues);
        var entries = scan.Snapshot!.Entries.Where(e => e.Kind == ProductionRepositoryEntryKind.File &&
            !e.RelativePath.Split('/').Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase) || s.Equals("_nap", StringComparison.OrdinalIgnoreCase))).ToArray();
        var candidates = entries.Where(e => e.RelativePath.EndsWith("_manifest.json", StringComparison.Ordinal)).Select(e => Parent(e.RelativePath)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var assets = new List<CatalogAssetSnapshot>(); var issues = new List<NapIssue>();
        var reader = new CatalogAssetReader(_context);
        foreach (var relative in candidates)
        {
            try { assets.Add(reader.Capture(relative)); }
            catch (CatalogException ex) { issues.AddRange(ex.Issues.Issues); }
        }
        foreach (var file in entries.Where(e => e.RelativePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || e.RelativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            if (!candidates.Contains(Parent(file.RelativePath), StringComparer.Ordinal))
                issues.Add(new NapIssue(NapIssueCodes.CatalogSourceInvalid, NapIssueSeverity.Error, NapIssueDisposition.Stop, "An image has no canonical asset manifest; it cannot be silently omitted.", file.FullPath));
        if (assets.Select(a => a.AssetKey).Distinct().Count() != assets.Count)
            issues.Add(new NapIssue(NapIssueCodes.CatalogSourceInvalid, NapIssueSeverity.Error, NapIssueDisposition.Stop, "Duplicate physical asset identity."));
        return new CatalogImportPlan(_context, assets, new NapIssueReport(issues));
    }
    public int Import(CatalogImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Context != _context) throw new ArgumentException("Import plan belongs to another context.", nameof(plan));
        if (plan.Issues.ShouldStop) throw new CatalogException(plan.Issues);
        using var sources = CatalogSourceLease.Acquire(_context);
        var current = Discover(); if (current.Issues.ShouldStop) throw new CatalogException(current.Issues);
        if (!CatalogAssetData.Equivalent(plan.Assets, current.Assets)) throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "The complete import snapshot changed after preflight.");
        return new AssetCatalog(_context).RegisterSnapshots(current.Assets);
    }
    private static string Parent(string relative) { var slash = relative.LastIndexOf('/'); return slash < 0 ? "" : relative[..slash]; }
}
