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
}
/// <summary>Serializes short catalog leases locally. No write catalog or pipeline capabilities.</summary>
public sealed class ExplorerService : IExplorerService
{
    private readonly SemaphoreSlim _catalog = new(1, 1);
    private UniverseContext? _context;
    private CatalogExplorerReader? _reader;
    // Access exclusively under _catalog. Retain one bounded session, never a connection.
    private CatalogExplorerReader Reader(UniverseContext context)
    {
        if (_reader is null || _context != context) { _context = context; _reader = new(context); }
        return _reader;
    }
    public Task BeginSessionAsync(UniverseContext c, CancellationToken ct) => Run(() => { Reader(c).InvalidateValidation(); return 0; }, ct);
    private async Task<T> Run<T>(Func<T> operation, CancellationToken cancellation)
    {
        await _catalog.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); var value = operation(); cancellation.ThrowIfCancellationRequested(); return value; }, cancellation).ConfigureAwait(false); }
        finally { _catalog.Release(); }
    }
    public Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter f, int offset, int limit, CancellationToken ct) => Run(() => Reader(c).Page(f, offset, limit, ct), ct);
    public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) => Run(() => Reader(c).Detail(id, ct), ct);
    public Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter f, CancellationToken ct) => Run(() => Reader(c).Statistics(f, ct), ct);
    public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => Run(() => Reader(c).Planning(ct), ct);
    public Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary a, CancellationToken ct) => new ProductionThumbnailCache(c).GetAsync(a, ct);
}
public sealed record ExplorerError(string Message, string Code)
{
    public static ExplorerError From(Exception error)
    {
        var issues = error switch { CatalogException e => e.Issues, ProductionStorageException e => e.Issues, ArchiveStorageException e => e.Issues, _ => null };
        var code = issues?.Issues.FirstOrDefault(i => i.StopsProcessing)?.Code ?? (error is UnauthorizedAccessException ? "access_denied" : "explorer_invalid_or_unavailable");
        var message = code.Contains("missing", StringComparison.Ordinal) ? "El catálogo o una carpeta configurada no está disponible. Revisa Ajustes." :
            code.Contains("busy", StringComparison.Ordinal) ? "El catálogo está ocupado. Reintenta cuando termine la operación." :
            "No se puede leer esta información con seguridad. Revisa la configuración o el origen; NAP no lo reparará automáticamente.";
        return new(message, code);
    }
}
