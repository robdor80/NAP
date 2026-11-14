using System.Globalization;
using NAP.Core;
namespace NAP.Presentation;

/// <summary>Read-only text adapter over bounded summary pages. Retains positions, never all tiles, documents or images.</summary>
internal sealed class CatalogTextSearch(IExplorerService inner, string text) : IExplorerService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<int> _matches = [];
    private readonly Dictionary<string, long> _types = [], _profiles = [];
    private bool _indexed;
    private long _sourceCount;
    private async Task Index(UniverseContext c, CatalogFilter f, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_indexed) return;
            _matches.Clear(); _types.Clear(); _profiles.Clear();
            for (var offset = 0; ; offset += 240)
            {
                var page = await inner.PageAsync(c, f, offset, 240, ct).ConfigureAwait(false);
                if (offset == 0) _sourceCount = page.TotalCount;
                if (page.TotalCount != _sourceCount) throw new InvalidDataException("El catálogo cambió durante la búsqueda. Actualiza para repetirla.");
                for (var i = 0; i < page.Items.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var asset = page.Items[i];
                    if (asset.AssetKey.UniverseId != c.Id) throw new InvalidDataException("Search scope mismatch.");
                    var compare = CultureInfo.GetCultureInfo("es-ES").CompareInfo;
                    if (compare.IndexOf(UiLabels.AssetName(asset.AssetKey.AssetId) + " " + asset.AssetKey.AssetId, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) continue;
                    _matches.Add(offset + i);
                    _types[asset.AssetType] = _types.GetValueOrDefault(asset.AssetType) + 1;
                    _profiles[asset.ProductionProfile] = _profiles.GetValueOrDefault(asset.ProductionProfile) + 1;
                }
                if (offset + page.Items.Count >= page.TotalCount) break;
                if (page.Items.Count == 0) throw new InvalidDataException("Incomplete search page.");
            }
            _indexed = true;
        }
        finally { _gate.Release(); }
    }
    public async Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter f, int offset, int limit, CancellationToken ct)
    {
        await Index(c, f, ct).ConfigureAwait(false);
        var rows = new List<CatalogAssetSummary>(limit);
        CatalogPage? page = null;
        foreach (var position in _matches.Skip(offset).Take(limit))
        {
            var start = position / 240 * 240;
            if (page is null || page.Offset != start)
                page = await inner.PageAsync(c, f, start, 240, ct).ConfigureAwait(false);
            if (page.TotalCount != _sourceCount) throw new InvalidDataException("Catalog changed; refresh the search.");
            rows.Add(page.Items[position - start]);
        }
        return new(_matches.Count, offset, rows);
    }
    public async Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter f, CancellationToken ct)
    {
        await Index(c, f, ct).ConfigureAwait(false);
        return new(_matches.Count, _types.Select(p => new CatalogDistribution("asset_type", p.Key, p.Value)).ToArray(),
            _profiles.Select(p => new CatalogDistribution("production_profile", p.Key, p.Value)).ToArray(), [], []);
    }
    public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) => inner.DetailAsync(c, id, ct);
    public Task<LegacyPortrait?> LegacyDetailAsync(UniverseContext c, string id, CancellationToken ct) => inner.LegacyDetailAsync(c, id, ct);
    public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => inner.PlanningAsync(c, ct);
    public Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary a, CancellationToken ct) => inner.ThumbnailAsync(c, a, ct);
}
