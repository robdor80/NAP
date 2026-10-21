using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App;

/// <summary>Explicit capture-only synthetic preview. No host, settings load, assets, ZIPs or writable service.</summary>
internal static class Phase6Preview
{
    internal static ShellViewModel Create(string mode)
    {
        if (mode is not ("active" or "paused" or "review" or "offline" or "local")) throw new ArgumentException("Unknown preview mode.");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused");
        UniverseContext Context(string id, string label) => new(new UniverseProfile(new(id), label),
            new(new(id), System.IO.Path.Combine(root, id, "workspace"), System.IO.Path.Combine(root, id, "production"), System.IO.Path.Combine(root, id, "archive")));
        var contexts = new[] { Context("preview", "Universo de muestra"), Context("other_preview", "Segundo universo") };
        var explorer = new ExplorerViewModel(contexts.Select(c => c.Profile), new(System.IO.Path.Combine(root, "unused-settings.json")), new PreviewExplorer(), new NoFolders(), hostConfigurations: contexts.Select(c => c.Storage).ToArray());
        var confirmation = new NoConfirmation();
        var automation = mode == "local" ? null : new AutomationControlViewModel(() => explorer.Context, confirmation, new PreviewSource(mode, contexts));
        var service = DispatchProxy.Create<IProfessionalUiService, Phase6ReadOnlyPreviewService>();
        return new(explorer, service, confirmation, automation: automation) { ReviewExpanded = mode == "review", ToolsExpanded = mode == "review" };
    }
    internal static async Task CaptureAsync(MainWindow window, BindingDiagnostics diagnostics, string mode, string output)
    {
        // This is a capture, never acceptance on the responsible person's MSI.
        var shell = (ShellViewModel)window.DataContext;
        if (shell.CurrentPage != shell.Dashboard || shell.Navigation.Count != 5 || shell.Tools.Count != 5)
            throw new InvalidOperationException("Preview navigation differs from the reviewed shell.");
        if (mode is "active" or "paused" or "review" && shell.Automation?.Completed != "14")
            throw new InvalidOperationException("Synthetic projection was not displayed.");
        if (mode == "review" && shell.Automation?.Review.Count != 1)
            throw new InvalidOperationException("Synthetic incident was not displayed.");
        if (mode == "offline" && shell.Automation?.Completed != "—" || mode == "local" && shell.Automation is not null)
            throw new InvalidOperationException("Unavailable automation must not invent counters.");
        // Render the real visual tree at the requested logical client size, independent of
        // the runner's smaller desktop. Keep the Window ancestor for all product bindings.
        var compact = window.Tag is true;
        var width = compact ? 1200 : 1440; var height = compact ? 700 : 900;
        var content = (UIElement)window.Content;
        var capture = new Grid { Background = window.Background, Width = width, Height = height };
        window.Content = capture; capture.Children.Add(content);
        var image = await window.Dispatcher.InvokeAsync(() =>
        {
            capture.UpdateLayout();
            capture.Measure(new Size(width, height)); capture.Arrange(new Rect(0, 0, width, height));
            if (capture.ActualWidth != width || capture.ActualHeight != height) throw new InvalidOperationException("Synthetic layout was constrained by the runner desktop.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(capture); return bitmap;
        }, DispatcherPriority.Render);
        if (diagnostics.Errors != 0) throw new InvalidOperationException("Preview binding errors: " + diagnostics.Errors);
        System.IO.Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = System.IO.File.Create(System.IO.Path.Combine(output, $"inicio-{mode}-{width}x{height}.png"));
        encoder.Save(file);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { preview = mode, synthetic = true, offscreenLayout = true, bindingErrors = diagnostics.Errors, width = image.PixelWidth, height = image.PixelHeight }));
    }
    private sealed class PreviewSource(string mode, IReadOnlyList<UniverseContext> contexts) : IAutomationOverviewClient
    {
        public Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation)
        {
            if (mode == "offline") throw new System.IO.IOException("Synthetic disconnection.");
            var c = contexts.Single(c => c.Id == universe);
            var definition = AutomationPolicyDefinition.DisabledDefault(c.Storage) with { Operations = AutomationOperations.Admit };
            var auth = new AutomationAuthorization("synthetic", universe, definition.Reference, "Preview", DateTimeOffset.UtcNow);
            var policy = new StoredAutomationPolicy(definition, definition.Reference, AutomationPolicyState.PreparationOnly, auth, 1);
            var state = mode == "paused" ? AutomationHostState.Paused : AutomationHostState.Running;
            var review = mode == "review" ? new[] { new AutomationReviewItem(QueueItemId.Create(), "Retrato de muestra.zip", QueueIncident.RatioUnsupported, new DateTimeOffset(2026, 10, 21, 12, 30, 0, TimeSpan.Zero)) } : [];
            var counts = mode == "active" ? new AutomationQueueCounts(2, 1, 14, 0, 0) : new(0, 0, 14, review.Length, 0);
            return Task.FromResult(new AutomationDesktopOverview(new(null, "synthetic", AutomationHostState.Running,
                [new(universe, state, 0, 0, null, null, null), new(contexts.Single(other => other.Id != universe).Id, AutomationHostState.Running, 0, 0, null, null, null)], null),
                new(universe, DateTimeOffset.UtcNow, counts, review, policy),
                new(universe, 1, mode == "paused", false, new(universe, c.Storage, new(new string('a', 64)), policy.Reference, auth.AuthorizationId, auth.Actor, false), "synthetic", [])));
        }
        public Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation) =>
            throw new InvalidOperationException("Capture-only preview cannot send commands.");
    }
    private sealed class NoFolders : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class NoConfirmation : IUserConfirmation { public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation) => Task.FromResult(false); }
    private sealed class PreviewExplorer : IExplorerService
    {
        public Task<CatalogPage> PageAsync(UniverseContext context, CatalogFilter filter, int offset, int limit, CancellationToken cancellation) => Task.FromResult(new CatalogPage(0, offset, []));
        public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext context, string id, CancellationToken cancellation) => Task.FromResult<CatalogAssetSnapshot?>(null);
        public Task<CatalogStatistics> StatisticsAsync(UniverseContext context, CatalogFilter filter, CancellationToken cancellation) => Task.FromResult(new CatalogStatistics(0, [], [], [], []));
        public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext context, CancellationToken cancellation) => Task.FromResult(new CatalogPlanningReadModel(context.Id, [], []));
        public Task<ThumbnailResult> ThumbnailAsync(UniverseContext context, CatalogAssetSummary asset, CancellationToken cancellation) => throw new InvalidOperationException("No assets in preview.");
    }
}

public class Phase6ReadOnlyPreviewService : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_AuditAvailable" => false,
        nameof(IProfessionalUiService.DashboardAsync) => Task.FromResult(new DashboardSnapshot(((UniverseContext)args![0]!).Id, null, null, null, null, [])),
        _ => throw new InvalidOperationException("Capture-only preview has no manual writer.")
    };
}
