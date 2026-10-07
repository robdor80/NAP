using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.App.Controls;
using NAP.Presentation;

namespace NAP.App;

/// <summary>Explicit diagnostic mode, no fabricated asset data. Synthetic integers exercise layout only.</summary>
internal static class VisualSmokeProbe
{
    internal static async Task RunAsync(MainWindow window, ExplorerViewModel vm)
    {
        var originalCount = vm.Assets?.Count ?? 0;
        var documents = 0;
        if (originalCount > 0)
        {
            var tile = (AssetTileViewModel)vm.Assets![0]!;
            await vm.SelectAsync(tile.Asset);
            if (vm.DetailState != ExplorerState.Ready) throw new InvalidOperationException("Smoke detail failed.");
            await vm.Preview!.ActivateAsync(); documents = vm.Documents.Count;
            vm.AssetId = tile.Asset!.AssetKey.AssetId;
            vm.AssetType = tile.Asset.AssetType; vm.ProductionProfile = tile.Asset.ProductionProfile;
            foreach (var facet in vm.Facets) facet.Selected = facet.Choices.FirstOrDefault(choice => choice is not null);
            await vm.ApplyFilters.ExecuteAsync();
            if (vm.Assets?.Count != 1) throw new InvalidOperationException("Smoke exact filter failed.");
            await vm.ResetFilters.ExecuteAsync();
            if (vm.Assets?.Count != originalCount) throw new InvalidOperationException("Smoke reset failed.");
        }
        vm.Navigate.Execute("Estadísticas"); await RenderAsync(window);
        vm.Navigate.Execute("Catálogo"); await RenderAsync(window);
        var original = window.Content;
        var stress = new ListBox
        {
            // Integers test the recycling panel, independently from the real catalog test above.
            ItemsSource = Enumerable.Range(0, 30_000).ToArray(),
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel)))
        };
        ScrollViewer.SetCanContentScroll(stress, true); ScrollViewer.SetHorizontalScrollBarVisibility(stress, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(stress, true); VirtualizingPanel.SetVirtualizationMode(stress, VirtualizationMode.Recycling);
        window.Content = stress; await RenderAsync(window);
        var panel = Find<VirtualizingTilePanel>(stress) ?? throw new InvalidOperationException("Virtual panel missing.");
        var before = VisualTreeHelper.GetChildrenCount(panel);
        panel.SetVerticalOffset(panel.ExtentHeight); await RenderAsync(window);
        var after = VisualTreeHelper.GetChildrenCount(panel);
        if (before <= 0 || before > 200 || after <= 0 || after > 200 || panel.VerticalOffset <= 0) throw new InvalidOperationException($"Viewport virtualization failed: {before}/{after}; offset {panel.VerticalOffset}; extent {panel.ExtentHeight}; viewport {panel.ViewportHeight}.");
        foreach (var offset in new[] { 0d, panel.ExtentHeight / 2, 0d })
        {
            panel.SetVerticalOffset(offset); await RenderAsync(window);
            var realized = VisualTreeHelper.GetChildrenCount(panel);
            if (realized is <= 0 or > 200) throw new InvalidOperationException("Recycled viewport range is invalid.");
        }
        window.Content = original; await RenderAsync(window);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { wpf = "rendered", width = (int)window.ActualWidth, height = (int)window.ActualHeight,
            state = vm.State.ToString(), assets = originalCount, documents, virtualItems = 30000, realizedBefore = before, realizedAfter = after }));
    }
    private static async Task RenderAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() =>
        {
            window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        }, DispatcherPriority.ContextIdle);
    }
    private static T? Find<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = Find<T>(VisualTreeHelper.GetChild(node, i)); if (child is not null) return child; }
        return null;
    }
}
