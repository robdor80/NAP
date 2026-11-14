using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.App.Controls;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App;

/// <summary>Explicit test entry point. Real WPF and ExplorerService over a disposable SQLite fixture; never attaches to the installed HOST.</summary>
internal static class Nap208UxEvidence
{
    internal static ShellViewModel Create(string settings)
    {
        if (!Path.GetFullPath(settings).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UX evidence requires a temporary test settings path.");
        var profile = UniverseProfileLoader.Load(Path.Combine(AppContext.BaseDirectory, "config/universes/nimroel/profile.json"));
        var explorer = new ExplorerViewModel([profile], new(settings), new CountingExplorer(), new NoFolders());
        var automation = new AutomationControlViewModel(() => explorer.Context, new Decline(), new Overview(() => explorer.Context));
        return new(explorer, new ProfessionalUiService(), new Decline(), automation: automation,
            workflowClient: new Phase6WorkflowPreview("production"), licenses: new LicenseViewModel(null), syntheticReadOnly: true);
    }

    internal static async Task Capture(MainWindow window, ShellViewModel shell, BindingDiagnostics diagnostics, string output)
    {
        Directory.CreateDirectory(output);
        var process = Process.GetCurrentProcess();
        var initialMemory = process.WorkingSet64;
        var vm = shell.Explorer;
        var clock = Stopwatch.StartNew();
        await vm.RefreshAllAsync();
        var refreshMs = clock.Elapsed.TotalMilliseconds;
        Require(vm.Assets?.Count == 5000, "Synthetic SQLite fixture must contain 5000 records.");
        Require(shell.Tools.All(t => t.Name != "RoDo"), "RoDo must be outside everyday navigation.");
        var records = vm.Assets!.Count;
        var measured = new List<object>();
        var surface = (FrameworkElement)window.Content;
        var navigation = shell.Navigation.Concat(shell.Tools).Select(n => n.Name).ToArray();
        var screenshots = new List<string>();
        foreach (var (width, height, dpiScale) in new[] { (1200, 700, 1d), (1536, 960, 1d), (2560, 1600, 1.25d) })
        {
            window.WindowState = WindowState.Normal;
            window.Width = width; window.Height = height;
            surface.Width = width / dpiScale - 48;
            surface.Height = height / dpiScale - 80;
            shell.IsCompact = surface.Width < 1220 || surface.Height < 760;
            foreach (var page in navigation)
            {
                clock.Restart(); shell.NavigateTo(page); await shell.LastRefresh;
                await Layout(window, surface);
                Require(surface.ActualWidth > 600 && surface.ActualHeight > 300, "Viewport was lost.");
                var pageMs = clock.Elapsed.TotalMilliseconds;
                // Keyboard focus must reach a visible action in every page shell.
                var action = Descendants<Button>(surface).FirstOrDefault(b => b.IsVisible && b.IsEnabled && b.Focusable);
                Require(action is not null && action.Focus(), "Keyboard focus cannot reach an action.");
                Save(surface, Path.Combine(output, $"{width}-{page}.png"), dpiScale);
                screenshots.Add($"{width}-{page}.png");
                measured.Add(new { page, width, height, dpiScale, layoutMilliseconds = pageMs });
                if (page == "Catálogo")
                {
                    var panel = Descendants<VirtualizingTilePanel>(surface).Single();
                    var realized = VisualTreeHelper.GetChildrenCount(panel);
                    Require(realized is > 0 and < 100 && vm.Assets!.CachedPages <= 4, "Gallery must remain virtualized.");
                    clock.Restart(); panel.SetVerticalOffset(panel.ExtentHeight / 2); await Layout(window, surface);
                    measured.Add(new { page = "scroll-middle", width, milliseconds = clock.Elapsed.TotalMilliseconds, realized = VisualTreeHelper.GetChildrenCount(panel), cachedPages = vm.Assets!.CachedPages });
                    panel.SetVerticalOffset(panel.ExtentHeight); await Layout(window, surface);
                    Require(VisualTreeHelper.GetChildrenCount(panel) is > 0 and < 100 && vm.Assets.CachedPages <= 4, "Scrolled gallery is not bounded.");
                    panel.SetVerticalOffset(0); await Layout(window, surface);
                    var tile = (AssetTileViewModel)vm.Assets[0]!;
                    Require(tile.Asset is not null, "First page did not populate.");
                    vm.SelectedAsset = tile; await vm.SelectAsync(tile.Asset); await Layout(window, surface);
                    Require(vm.Detail is not null && vm.CanOpenWebp && vm.CanOpenMaster, "Selected resource did not preserve file access.");
                    await vm.Preview!.ActivateAsync();
                    foreach (var thumbnail in Descendants<ThumbnailView>(surface).Where(t => t.DataContext is AssetTileViewModel { Asset.WebpAvailable: true }).ToArray())
                        await thumbnail.VerifyRenderedAsync();
                    Save(surface, Path.Combine(output, $"{width}-Catálogo-ficha.png"), dpiScale);
                    vm.SelectedAsset = null; await Layout(window, surface);
                    var filters = Descendants<ToggleButton>(surface).Single(t => t.Name == "FiltersToggle");
                    filters.IsChecked = true; await Layout(window, surface);
                    Require(Descendants<ComboBox>(surface).Any(c => c.IsVisible), "Catalog filters did not open.");
                    Save(surface, Path.Combine(output, $"{width}-Catálogo-filtros.png"), dpiScale);
                    filters.IsChecked = false;
                }
                if (page == "Producción" || page == "Historial")
                {
                    var work = page == "Producción" ? shell.Pipeline.Work! : shell.History.Work!;
                    if (work.Rows.Count > 0)
                    {
                        work.Selected = work.Rows[0]; await work.LastDetail; await Layout(window, surface);
                        Require(work.HasDetail, "Workflow detail did not open.");
                        Save(surface, Path.Combine(output, $"{width}-{page}-detalle.png"), dpiScale);
                        work.CloseDetail.Execute(null); await Layout(window, surface);
                        Require(!work.HasDetail, "Workflow detail did not close.");
                    }
                }
                if (page == "Ajustes")
                {
                    shell.Settings.Section = "Avanzado"; await Layout(window, surface);
                    Save(surface, Path.Combine(output, $"{width}-Ajustes-avanzado.png"), dpiScale);
                    shell.Settings.Section = "General";
                }
            }
        }
        shell.NavigateTo("Catálogo"); await shell.LastRefresh;
        clock.Restart(); vm.SearchText = "pescadora"; await vm.ApplyFilters.ExecuteAsync(); await Layout(window, surface);
        var searchMs = clock.Elapsed.TotalMilliseconds;
        Require(vm.Assets?.Count == 2500 && vm.CatalogTotal == "5.000" || vm.Assets?.Count == 2500 && vm.CatalogTotal == "5,000", "Text search or scope count is wrong.");
        Save(surface, Path.Combine(output, "busqueda-2500.png"), 1);
        vm.SearchText = "sin-coincidencia-ux208"; await vm.ApplyFilters.ExecuteAsync(); await Layout(window, surface);
        Require(vm.Assets?.Count == 0 && vm.CatalogTotal.Replace(",", "").Replace(".", "") == "5000", "Empty filtered state lost the total.");
        Save(surface, Path.Combine(output, "catalogo-sin-resultados.png"), 1);
        await vm.ResetFilters.ExecuteAsync();
        // A stopped read must remain visible and cannot alter SQLite.
        var originalPath = vm.WorkspaceRoot;
        vm.WorkspaceRoot = Path.Combine(Path.GetTempPath(), "nap208-unavailable-" + Guid.NewGuid().ToString("N"));
        shell.NavigateTo("Ajustes"); await shell.LastRefresh; await Layout(window, surface);
        vm.WorkspaceRoot = originalPath;
        Require(diagnostics.Errors == 0, "WPF binding errors: " + diagnostics.Errors);
        var result = new { result = "PASS", records, bindingErrors = diagnostics.Errors, virtualizationVerified = true,
            fixture = "Temporary synthetic SQLite; actual WPF controls; no Gemini network call", refreshMilliseconds = refreshMs,
            textSearchMilliseconds = searchMs, initialWorkingSetBytes = initialMemory, finalWorkingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64, counters = CountingExplorer.Counters(), viewports = measured, screenshots,
            platform = Environment.OSVersion.ToString(), runtime = Environment.Version.ToString(), physicalWindows11Validation = false };
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    private static async Task Layout(Window window, FrameworkElement surface)
    {
        await window.Dispatcher.InvokeAsync(() => { surface.Measure(new(surface.Width, surface.Height)); surface.Arrange(new(0, 0, surface.Width, surface.Height)); surface.UpdateLayout(); }, DispatcherPriority.Render);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static void Save(FrameworkElement surface, string path, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private sealed class Decline : IUserConfirmation { public Task<bool> ConfirmAsync(UiConfirmation c, CancellationToken ct) => Task.FromResult(false); }
    private sealed class NoFolders : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class Overview(Func<UniverseContext?> context) : IAutomationOverviewClient
    {
        public Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken ct)
        {
            var c = context()!;
            var definition = AutomationPolicyDefinition.DisabledDefault(c.Storage) with { Operations = AutomationOperations.Admit };
            var auth = new AutomationAuthorization("synthetic", universe, definition.Reference, "UX test", DateTimeOffset.UtcNow);
            var policy = new StoredAutomationPolicy(definition, definition.Reference, AutomationPolicyState.PreparationOnly, auth, 1);
            return Task.FromResult(new AutomationDesktopOverview(new(null, "synthetic", AutomationHostState.Running,
                [new(universe, AutomationHostState.Running, 0, 0, null, null, null)], null),
                new(universe, DateTimeOffset.UtcNow, new(1, 1, 5, 0, 0), [], policy),
                new(universe, 1, false, false, new(universe, c.Storage, new(new string('a', 64)), policy.Reference, auth.AuthorizationId, auth.Actor, false), "synthetic", [])));
        }
        public Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken ct) => throw new InvalidOperationException("Visual fixture cannot send HOST commands.");
    }
    private sealed class CountingExplorer : IExplorerService, IRemovalClient
    {
        private readonly ExplorerService _inner = new();
        private static int _pages, _details, _thumbnails, _maxLimit;
        internal static object Counters() => new { pageRequests = _pages, detailRequests = _details, thumbnailRequests = _thumbnails, maximumPageSize = _maxLimit };
        public Task BeginSessionAsync(UniverseContext c, CancellationToken ct) => _inner.BeginSessionAsync(c, ct);
        public Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter f, int offset, int limit, CancellationToken ct) { Interlocked.Increment(ref _pages); _maxLimit = Math.Max(_maxLimit, limit); return _inner.PageAsync(c, f, offset, limit, ct); }
        public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) { Interlocked.Increment(ref _details); return _inner.DetailAsync(c, id, ct); }
        public Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter f, CancellationToken ct) => _inner.StatisticsAsync(c, f, ct);
        public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => _inner.PlanningAsync(c, ct);
        public Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary a, CancellationToken ct) { Interlocked.Increment(ref _thumbnails); return _inner.ThumbnailAsync(c, a, ct); }
        public Task<LegacyPortrait?> LegacyDetailAsync(UniverseContext c, string id, CancellationToken ct) => _inner.LegacyDetailAsync(c, id, ct);
        public Task<RemovalPlan> PreviewRemovalAsync(UniverseContext c, RemovalSelection s, CancellationToken ct) => _inner.PreviewRemovalAsync(c, s, ct);
        public Task<RemovalResult> RemoveAsync(UniverseContext c, RemovalRequest r, CancellationToken ct) => throw new InvalidOperationException("Visual evidence never removes resources.");
    }
}
