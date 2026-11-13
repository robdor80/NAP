using System.Security.Cryptography;
using NAP.Core;

namespace NAP.Presentation;

public interface IExplorerService
{
    Task BeginSessionAsync(UniverseContext context, CancellationToken cancellation) => Task.CompletedTask;
    Task<CatalogPage> PageAsync(UniverseContext context, CatalogFilter filter, int offset, int limit, CancellationToken cancellation);
    Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext context, string id, CancellationToken cancellation);
    Task<CatalogStatistics> StatisticsAsync(UniverseContext context, CatalogFilter filter, CancellationToken cancellation);
    Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext context, CancellationToken cancellation);
    Task<ThumbnailResult> ThumbnailAsync(UniverseContext context, CatalogAssetSummary asset, CancellationToken cancellation);
    Task<LegacyPortrait?> LegacyDetailAsync(UniverseContext context, string id, CancellationToken cancellation)
        => Task.FromResult<LegacyPortrait?>(null);
}

/// <summary>
/// One catalog page combines native certified records with indexed historical records.
/// The original strict AssetCatalog.db v1 remains unchanged.
/// </summary>
public sealed class ExplorerService : IExplorerService
{
    private readonly SemaphoreSlim _catalog = new(1, 1);
    private UniverseContext? _context;
    private CatalogExplorerReader? _reader;
    private CatalogExplorerReader Reader(UniverseContext context)
    {
        if (_reader is null || _context != context) { _context = context; _reader = new(context); }
        return _reader;
    }
    private async Task<T> Run<T>(Func<T> operation, CancellationToken cancellation)
    {
        await _catalog.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); var value = operation(); cancellation.ThrowIfCancellationRequested(); return value; }, cancellation).ConfigureAwait(false); }
        finally { _catalog.Release(); }
    }
    public Task BeginSessionAsync(UniverseContext c, CancellationToken ct) =>
        Run(() => { Reader(c).InvalidateValidation(); return 0; }, ct);

    public Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter f, int offset, int limit, CancellationToken ct) => Run(() =>
    {
        var native = Reader(c);
        var index = new LegacyPortraitIndex(c);
        var nativeCount = checked((int)native.Page(f, 0, 1, ct).TotalCount);
        var historic = index.Query(f);
        var total = checked(nativeCount + historic.Count);
        var items = new List<CatalogAssetSummary>(limit);
        if (offset < nativeCount)
        {
            var count = Math.Min(limit, nativeCount - offset);
            items.AddRange(native.Page(f, offset, count, ct).Items);
        }
        if (items.Count < limit && offset + items.Count >= nativeCount)
        {
            var start = Math.Max(0, offset + items.Count - nativeCount);
            items.AddRange(historic.Skip(start).Take(limit - items.Count).Select(p =>
                new CatalogAssetSummary(new(c.Id, p.AssetId), "portrait", "portrait_npc",
                    p.RelativeDirectory + "/" + p.AssetId + ".webp",
                    new Sha256Digest(p.WebpSha256), p.WebpBytes)));
        }
        return new CatalogPage(total, offset, items.AsReadOnly());
    }, ct);

    public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) =>
        Run(() => new LegacyPortraitIndex(c).Get(id) is not null ? null : Reader(c).Detail(id, ct), ct);

    public Task<LegacyPortrait?> LegacyDetailAsync(UniverseContext c, string id, CancellationToken ct) =>
        Run(() =>
        {
            var index = new LegacyPortraitIndex(c);
            var value = index.Get(id);
            if (value is not null) index.Verify(value);
            return value;
        }, ct);

    public Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter f, CancellationToken ct) => Run(() =>
    {
        var native = Reader(c).Statistics(f, ct);
        var historic = new LegacyPortraitIndex(c).Statistics(f);
        static IReadOnlyList<CatalogDistribution> Merge(IReadOnlyList<CatalogDistribution> a, IReadOnlyList<CatalogDistribution> b) =>
            Array.AsReadOnly(a.Concat(b).GroupBy(x => (x.Dimension, x.Value, x.TraitType))
                .Select(group => new CatalogDistribution(group.Key.Dimension, group.Key.Value,
                    group.Sum(x => x.Count), group.Key.TraitType))
                .OrderBy(x => x.Dimension, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal).ToArray());
        return new CatalogStatistics(native.TotalAssets + historic.TotalAssets,
            Merge(native.AssetTypes, historic.AssetTypes), Merge(native.ProductionProfiles, historic.ProductionProfiles),
            Merge(native.Classifications, historic.Classifications), Merge(native.Traits, historic.Traits));
    }, ct);

    public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) =>
        Run(() => Reader(c).Planning(ct), ct);

    public async Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary a, CancellationToken ct)
    {
        var legacy = await Run(() =>
        {
            var index = new LegacyPortraitIndex(c);
            var portrait = index.Get(a.AssetKey.AssetId);
            if (portrait is not null) index.Verify(portrait);
            return portrait;
        }, ct).ConfigureAwait(false);
        if (legacy is null) return await new ProductionThumbnailCache(c).GetAsync(a, ct).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var cache = Path.Combine(c.Storage.CacheRoot, "legacy-thumbnails");
            var name = legacy.AssetId + "-" + legacy.WebpSha256 + ".png";
            var path = Path.Combine(cache, name);
            if (File.Exists(path)) return new ThumbnailResult(path, 300, 375, true);
            var bytes = LegacyPortraitReader.PreviewPng(legacy);
            Directory.CreateDirectory(cache);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new ThumbnailResult(path, 300, 375, false);
        }, ct).ConfigureAwait(false);
    }
}
public sealed record ExplorerError(string Message, string Code)
{
    public static ExplorerError From(Exception error)
    {
        if (error is UniverseProfileStoreException profile) return new(profile.Message, profile.Code);
        var issues = error switch { CatalogException e => e.Issues, ProductionStorageException e => e.Issues, ArchiveStorageException e => e.Issues, _ => null };
        var code = issues?.Issues.FirstOrDefault(i => i.StopsProcessing)?.Code ?? (error is UnauthorizedAccessException ? "access_denied" : "explorer_invalid_or_unavailable");
        var message = code.Contains("missing", StringComparison.Ordinal) ? "El catálogo o una carpeta configurada no está disponible. Revisa Ajustes." :
            code.Contains("busy", StringComparison.Ordinal) ? "El catálogo está ocupado. Reintenta cuando termine la operación." :
            "No se puede leer esta información. Revisa la configuración o el origen; NAP no la reparará automáticamente.";
        return new(message, code);
    }
}
