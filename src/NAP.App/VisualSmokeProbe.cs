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
    internal static async Task RunAsync(MainWindow window, ExplorerViewModel vm, ShellViewModel shell, BindingDiagnostics diagnostics, string? output, bool importProfile = false, string? normalizationProfile = null)
    {
        shell.NavigateTo("Catálogo"); await shell.LastRefresh;
        await RenderAsync(window);
        var capsule = (FrameworkElement)window.FindName("SingleUniverse"); var selector = (ComboBox)window.FindName("UniverseSelector");
        if (shell.Profiles.Count == 1 && (!capsule.IsVisible || selector.IsVisible)) throw new InvalidOperationException("Single-universe capsule is not static.");
        var selectorMode = shell.Profiles.Count == 1 ? "static" : "selector";
        if (importProfile)
        {
            var selected = shell.SelectedUniverse; shell.NavigateTo("Ajustes"); await shell.LastRefresh;
            await shell.ImportProfile.ExecuteAsync(); await RenderAsync(window);
            if (shell.Profiles.Count != 2 || !selector.IsVisible || capsule.IsVisible || shell.Context is not null)
                throw new InvalidOperationException("Imported universe/selector is not safely unconfigured.");
            shell.SelectedUniverse = selected; await shell.LastRefresh; shell.NavigateTo("Catálogo"); await shell.LastRefresh;
        }
        window.VerifyTrayRoundTrip();
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
        var normalizationChecks = 0;
        if (normalizationProfile is not null)
        {
            shell.NavigateTo("Producción"); await shell.LastRefresh;
            shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single();
            shell.Pipeline.NormalizationTarget = shell.Pipeline.NormalizationProfiles.Single(p => p.ProductionProfile == normalizationProfile);
            await shell.PrepareNormalization.ExecuteAsync();
            if (shell.Pipeline.Normalization is null || shell.Pipeline.Prepared is not null || shell.ExecutePackage.CanExecute(null))
                throw new InvalidOperationException("Normalization preview bypassed a human or production gate.");
        }
        var pages = 0; var mascotChecks = 0; var tooltipChecks = 0; RodoMascot? lastMascot = null;
        foreach (var size in new[] { (1200d, 700d), (2560d, 1600d) })
        {
            window.WindowState = WindowState.Normal; window.Width = size.Item1; window.Height = size.Item2;
            // Windows constrains native windows to the monitor work area. Measure the live WPF
            // content at the exact logical viewport as well, retaining Window-relative bindings.
            var surface = (FrameworkElement)window.Content; surface.Width = size.Item1 - 48; surface.Height = size.Item2 - 48;
            foreach (var page in shell.Pages)
            {
                shell.NavigateTo(page.Name); await shell.LastRefresh; await RenderAsync(window, new Size(size.Item1, size.Item2)); pages++;
                if (lastMascot is not null && lastMascot.IsAnimating) throw new InvalidOperationException("RoDo animation survives view unload.");
                lastMascot = null;
                if (page == shell.Pipeline && normalizationProfile is not null)
                {
                    var view = Find<Views.PipelineView>(window) ?? throw new InvalidOperationException("Production view missing.");
                    foreach (var name in new[] { "NormalizationOriginal", "NormalizationCandidate" })
                    {
                        var preview = (Image)view.FindName(name);
                        if (preview.Source is not BitmapSource { IsFrozen: true } || !preview.IsVisible || preview.ActualWidth <= 0 || preview.ActualHeight <= 0)
                            throw new InvalidOperationException("Normalization comparison image did not render.");
                    }
                    if (!shell.ApproveNormalization.CanExecute(null) || !shell.RejectNormalization.CanExecute(null) || shell.ExecutePackage.CanExecute(null))
                        throw new InvalidOperationException("Normalization approval/rejection controls are incorrect.");
                    foreach (var (scrollName, buttonName) in new[] { ("NormalizationInboxScroll", "CancelNormalizationButton"), ("NormalizationProductionScroll", "RejectNormalizationButton") })
                    {
                        var scroll = (ScrollViewer)view.FindName(scrollName); var button = (Button)view.FindName(buttonName);
                        button.BringIntoView(); await RenderAsync(window, new Size(size.Item1, size.Item2));
                        var bounds = button.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                        if (bounds.Top < -1 || bounds.Bottom > scroll.ActualHeight + 1)
                            throw new InvalidOperationException("Normalization controls cannot be reached by scrolling.");
                        scroll.ScrollToTop(); await RenderAsync(window, new Size(size.Item1, size.Item2));
                    }
                    normalizationChecks++;
                }
                if (page == shell.Rodo)
                {
                    lastMascot = Find<RodoMascot>(window) ?? throw new InvalidOperationException("RoDo mascot missing.");
                    if (lastMascot.Source is not BitmapSource source || source.PixelWidth != 1024 || source.PixelHeight != 1024 || !lastMascot.IsVisible || !lastMascot.IsAnimating || lastMascot.ActualHeight < 80)
                        throw new InvalidOperationException($"RoDo PNG/idle motion is not rendered: source={lastMascot.Source?.GetType().Name}, visible={lastMascot.IsVisible}, animated={lastMascot.IsAnimating}, height={lastMascot.ActualHeight}, width={lastMascot.ActualWidth}.");
                    var header = (FrameworkElement)window.FindName("PageHeader");
                    var view = Find<Views.RodoView>(window) ?? throw new InvalidOperationException("RoDo view missing.");
                    var availability = (FrameworkElement)view.FindName("AvailabilityFrame");
                    var conversation = (DependencyObject)view.FindName("ConversationPanel");
                    Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                    var portrait = Bounds(lastMascot); var headerBounds = Bounds(header);
                    if (lastMascot.Parent != header || Find<RodoMascot>(conversation) is not null ||
                        Math.Abs(portrait.Left + portrait.Width / 2 - headerBounds.Left - headerBounds.Width / 2) > 1 ||
                        portrait.Bottom + 3 > Bounds(availability).Top ||
                        portrait.IntersectsWith(Bounds((FrameworkElement)window.FindName("PageTitle"))) ||
                        portrait.IntersectsWith(Bounds((FrameworkElement)window.FindName("PageActions"))))
                        throw new InvalidOperationException("RoDo must be centered in the page header, outside conversation and clear of controls/message.");
                    if (size.Item1 == 2560 && (lastMascot.ActualHeight < 260 || lastMascot.ActualHeight > 340))
                        throw new InvalidOperationException($"RoDo desktop size outside 260–340 DIP: {lastMascot.ActualHeight}.");
                    mascotChecks++;
                }
                foreach (var button in FindAll<Button>(window).Where(b => b.IsVisible && b.Command is UiAsyncCommand && !b.IsEnabled))
                {
                    if (!ToolTipService.GetShowOnDisabled(button) || string.IsNullOrWhiteSpace(button.ToolTip as string)) throw new InvalidOperationException("Disabled action has no usable explanation.");
                    tooltipChecks++;
                }
                foreach (var text in FindAll<TextBlock>(window).Where(t => t.IsVisible))
                    if (text.Text.Contains("Empty", StringComparison.Ordinal) || text.Text.Contains("Pipeline", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("English UX label leaked.");
                if (page == shell.Catalog && originalCount > 0)
                {
                    await vm.SelectAsync(((AssetTileViewModel)vm.Assets![0]!).Asset);
                    await RenderAsync(window, new Size(size.Item1, size.Item2));
                    foreach (var preview in FindAll<ThumbnailView>(window)) await preview.VerifyRenderedAsync();
                    await RenderAsync(window, new Size(size.Item1, size.Item2));
                }
                foreach (var frame in FindAll<RobFrameOutline>(window))
                    if (frame.HeaderWidth <= 0 || frame.GapEnd <= frame.GapStart) throw new InvalidOperationException("TitledFrame has no measured title opening.");
                if (output is not null)
                {
                    System.IO.Directory.CreateDirectory(output);
                    var drawing = new DrawingVisual();
                    using (var dc = drawing.RenderOpen())
                    {
                        dc.DrawRectangle((Brush)window.FindResource("BackgroundBrush"), null, new Rect(0, 0, size.Item1, size.Item2));
                        dc.DrawRectangle(new VisualBrush(surface) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight) }, null, new Rect(24, 24, surface.ActualWidth, surface.ActualHeight));
                    }
                    var image = new RenderTargetBitmap((int)size.Item1, (int)size.Item2, 96, 96, PixelFormats.Pbgra32); image.Render(drawing);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                    using var file = System.IO.File.Create(System.IO.Path.Combine(output, size.Item1 + "-" + page.Name + ".png")); png.Save(file);
                }
            }
            if (Math.Abs(surface.ActualWidth - surface.Width) > 1 || Math.Abs(surface.ActualHeight - surface.Height) > 1) throw new InvalidOperationException("Exact viewport measurement failed.");
        }
        ((FrameworkElement)window.Content).Width = double.NaN; ((FrameworkElement)window.Content).Height = double.NaN;
        shell.NavigateTo("Catálogo"); await shell.LastRefresh; await RenderAsync(window);
        if (lastMascot?.IsAnimating == true) throw new InvalidOperationException("RoDo animation was not released.");
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
        if (diagnostics.Errors != 0) throw new InvalidOperationException($"WPF binding errors: {diagnostics.Errors}.");
        if (window.Background is not SolidColorBrush background || background.Color != (Color)ColorConverter.ConvertFromString("#05131A")) throw new InvalidOperationException("Canonical window surface is missing.");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { wpf = "rendered", width = (int)window.ActualWidth, height = (int)window.ActualHeight, pages, bindingErrors = diagnostics.Errors, tray = "restored", viewports = new[] { "1200x700", "2560x1600" },
            state = vm.State.ToString(), assets = originalCount, documents, virtualItems = 30000, realizedBefore = before, realizedAfter = after, selectorMode, importedProfile = importProfile, universeCount = shell.Profiles.Count, mascotChecks, tooltipChecks, normalizationChecks, mascotAnimationReleased = true }));
    }
    private static IEnumerable<T> FindAll<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in FindAll<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static async Task RenderAsync(Window window, Size? viewport = null)
    {
        await window.Dispatcher.InvokeAsync(() =>
        {
            window.UpdateLayout();
            if (viewport is { } size)
            {
                var surface = (FrameworkElement)window.Content;
                surface.Measure(size); surface.Arrange(new Rect(-24, -24, size.Width, size.Height));
            }
            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        }, DispatcherPriority.ContextIdle);
    }
    private static T? Find<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = Find<T>(VisualTreeHelper.GetChild(node, i)); if (child is not null) return child; }
        return null;
    }
}
