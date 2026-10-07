using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NAP.Presentation;

namespace NAP.App.Controls;

/// <summary>Strictly visual adapter: frozen WPF bitmap decoded off the dispatcher; detach on recycling/unload.</summary>
public partial class ThumbnailView : UserControl
{
    private AssetTileViewModel? _tile;
    private long _version;
    private CancellationTokenSource? _renderCancellation;
    private static readonly SemaphoreSlim BitmapWorkers = new(2, 2);
    public ThumbnailView()
    {
        InitializeComponent(); Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach(); DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }
    private async void Attach()
    {
        if (_tile is not null || DataContext is not AssetTileViewModel tile) return;
        _tile = tile; tile.PropertyChanged += Changed; await tile.ActivateAsync(); await RenderAsync();
    }
    private void Detach()
    {
        _renderCancellation?.Cancel(); _renderCancellation?.Dispose(); _renderCancellation = null;
        ++_version; if (_tile is not null) { _tile.PropertyChanged -= Changed; _tile.Deactivate(); _tile = null; }
        PreviewImage.Source = null; Placeholder.Visibility = Visibility.Visible;
    }
    private async void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(AssetTileViewModel.CachePath)) await RenderAsync(); }
    private async Task RenderAsync()
    {
        _renderCancellation?.Cancel(); _renderCancellation?.Dispose();
        var source = _renderCancellation = new(); var cancellation = source.Token;
        var version = ++_version; var tile = _tile; var path = tile?.CachePath;
        if (path is null) { PreviewImage.Source = null; Placeholder.Visibility = Visibility.Visible; return; }
        try
        {
            await BitmapWorkers.WaitAsync(cancellation);
            BitmapImage bitmap;
            try { bitmap = await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(path);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
                cancellation.ThrowIfCancellationRequested(); return image;
            }, cancellation); }
            finally { BitmapWorkers.Release(); }
            if (cancellation.IsCancellationRequested || version != _version || tile != _tile) return;
            PreviewImage.Source = bitmap; Placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == _version) tile?.Fail(ex); }
    }
}
