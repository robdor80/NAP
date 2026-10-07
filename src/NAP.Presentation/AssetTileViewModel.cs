using NAP.Core;

namespace NAP.Presentation;

public sealed class AssetTileViewModel : ObservableObject, IDisposable
{
    private readonly IExplorerService _service;
    private readonly UniverseContext _context;
    private CatalogAssetSummary? _asset;
    private CancellationTokenSource? _thumbnail;
    private string? _cachePath;
    private ExplorerError? _error;
    private bool _visible;
    private bool _disposed;
    public AssetTileViewModel(IExplorerService service, UniverseContext context) { _service = service; _context = context; }
    public CatalogAssetSummary? Asset => _asset;
    public string Title => _asset?.AssetKey.AssetId ?? "Cargando…";
    public string Subtitle => _asset is null ? "" : _asset.AssetType + " · " + _asset.ProductionProfile;
    public string? CachePath { get => _cachePath; private set => Set(ref _cachePath, value); }
    public ExplorerError? Error { get => _error; private set => Set(ref _error, value); }
    public void Populate(CatalogAssetSummary asset)
    {
        if (_disposed) return;
        _asset = asset; Notify(nameof(Asset)); Notify(nameof(Title)); Notify(nameof(Subtitle));
        if (_visible) _ = LoadThumbnailAsync();
    }
    public void Fail(Exception error) { Error = ExplorerError.From(error); }
    public Task ActivateAsync() { if (_disposed) return Task.CompletedTask; _visible = true; return LoadThumbnailAsync(); }
    public void Deactivate() { _visible = false; _thumbnail?.Cancel(); CachePath = null; }
    private async Task LoadThumbnailAsync()
    {
        _thumbnail?.Cancel(); _thumbnail?.Dispose();
        var source = _thumbnail = new(); var asset = _asset;
        if (asset is null) return;
        Error = null;
        try
        {
            var result = await _service.ThumbnailAsync(_context, asset, source.Token);
            if (!source.IsCancellationRequested && _visible && _asset == asset) CachePath = result.CachePath;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!source.IsCancellationRequested) Error = ExplorerError.From(ex); }
    }
    public void Dispose() { if (_disposed) return; Deactivate(); _thumbnail?.Dispose(); _thumbnail = null; _disposed = true; }
}
