using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App.Views;

public partial class CatalogView : UserControl
{
    private AssetTileViewModel? _reference;
    private UniverseContext? _referenceContext;

    public CatalogView()
    {
        InitializeComponent();
        Unloaded += (_, _) => ReleaseReference();
    }

    private ExplorerViewModel? CurrentExplorer() => (Content as FrameworkElement)?.DataContext as ExplorerViewModel;

    private async void DeleteResource(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || CurrentExplorer() is not
            { Context: { } context, RemovalClient: { } client, SelectedAsset.Asset: { } asset } explorer) return;
        var id = asset.AssetKey.AssetId;
        await RemovalDialog.RunAsync(button, client, context, new(AssetId: id),
            () => explorer.Context == context && explorer.SelectedAsset?.Asset?.AssetKey.AssetId == id,
            async () =>
            {
                if (_reference?.Asset?.AssetKey.AssetId == id && _referenceContext == context) ReleaseReference();
                await explorer.RefreshAllAsync();
            });
    }

    private void CloseDetail(object sender, RoutedEventArgs e) { if (CurrentExplorer() is { } explorer) explorer.SelectedAsset = null; }

    private void OpenResource(object sender, RoutedEventArgs e)
    {
        if (CurrentExplorer() is not { } explorer || sender is not Button { Tag: string target }) return;
        var path = target switch { "folder" => explorer.ResourceFolder, "webp" => explorer.WebpPath, _ => explorer.MasterPath };
        try
        {
            if (path is null || !(target == "folder" ? Directory.Exists(path) : File.Exists(path)))
                throw new IOException("El archivo o la carpeta ya no está disponible. Actualiza el catálogo.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(Window.GetWindow(this), error.Message, "NAP · Abrir recurso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReleaseReference()
    {
        _reference?.Dispose();
        _reference = null;
        _referenceContext = null;
        ComparisonHint.Text = "Primero fija un recurso y después selecciona otro";
    }

    private void PinComparison(object sender, RoutedEventArgs e)
    {
        var explorer = CurrentExplorer();
        if (explorer?.SelectedAsset?.Asset is not { } asset || explorer.Context is null)
        {
            MessageBox.Show("Selecciona primero un recurso del catálogo.", "NAP · Laboratorio visual");
            return;
        }
        ReleaseReference();
        _reference = explorer.CreateComparisonTile(asset);
        _referenceContext = explorer.Context;
        ComparisonHint.Text = "Fijado: " + asset.AssetKey.AssetId;
    }

    private void OpenCatalogComparison(object sender, RoutedEventArgs e)
    {
        var explorer = CurrentExplorer();
        if (_reference is null || explorer is null || !ReferenceEquals(_referenceContext, explorer.Context))
        {
            ReleaseReference();
            MessageBox.Show("Fija una referencia del universo actual antes de comparar.", "NAP · Laboratorio visual");
            return;
        }
        if (explorer.SelectedAsset?.Asset is not { } asset)
        {
            MessageBox.Show("Selecciona un segundo asset para comparar.", "NAP · Laboratorio visual");
            return;
        }
        if (asset.AssetKey.AssetId == _reference.Asset?.AssetKey.AssetId)
        {
            MessageBox.Show("Selecciona un recurso diferente a la referencia fijada.", "NAP · Laboratorio visual");
            return;
        }
        var right = explorer.CreateComparisonTile(asset);
        try
        {
            new CatalogComparisonWindow(_reference, right) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        finally { right.Dispose(); }
    }


}
