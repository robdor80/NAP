using System.Reflection;
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
        var id = new UniverseId("preview"); var profile = new UniverseProfile(id, "Universo de muestra");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused");
        var storage = new UniverseStorageConfig(id, System.IO.Path.Combine(root, "workspace"), System.IO.Path.Combine(root, "production"), System.IO.Path.Combine(root, "archive"));
        var explorer = new ExplorerViewModel([profile], new(System.IO.Path.Combine(root, "unused-settings.json")), new PreviewExplorer(), new NoFolders(), hostConfigurations: [storage]);
        var confirmation = new NoConfirmation();
        var automation = mode == "local" ? null : new AutomationControlViewModel(new PreviewSource(mode), () => explorer.Context, confirmation);
        var service = DispatchProxy.Create<IProfessionalUiService, Phase6ReadOnlyPreviewService>();
        return new(explorer, service, confirmation, automation: automation) { ReviewExpanded = mode == "review", ToolsExpanded = mode == "review" };
    }
    internal static async Task CaptureAsync(MainWindow window, BindingDiagnostics diagnostics, string mode, string output)
    {
        // This is a capture, never acceptance on the responsible person's MSI.
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        if (diagnostics.Errors != 0) throw new InvalidOperationException("Preview binding errors: " + diagnostics.Errors);
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        System.IO.Directory.CreateDirectory(output);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = System.IO.File.Create(System.IO.Path.Combine(output, $"inicio-{mode}-{(int)window.ActualWidth}x{(int)window.ActualHeight}.png"));
        encoder.Save(file);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { preview = mode, synthetic = true, bindingErrors = diagnostics.Errors, width = image.PixelWidth, height = image.PixelHeight }));
    }
    private sealed class PreviewSource(string mode) : IAutomationOverviewClient
    {
        public Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation)
        {
            if (mode == "offline") throw new IOException("Synthetic disconnection.");
            var c = new UniverseContext(new UniverseProfile(universe, "Universo de muestra"),
                new(universe, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused", "workspace"),
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused", "production"), System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused", "archive")));
            var definition = AutomationPolicyDefinition.DisabledDefault(c.Storage) with { Operations = AutomationOperations.Admit };
            var auth = new AutomationAuthorization("synthetic", universe, definition.Reference, "Preview", DateTimeOffset.UtcNow);
            var policy = new StoredAutomationPolicy(definition, definition.Reference, AutomationPolicyState.PreparationOnly, auth, 1);
            var state = mode == "paused" ? AutomationHostState.Paused : AutomationHostState.Running;
            var review = mode == "review" ? new[] { new AutomationReviewItem(QueueItemId.Create(), "Retrato de muestra.zip", QueueIncident.RatioUnsupported, new DateTimeOffset(2026, 10, 21, 12, 30, 0, TimeSpan.Zero)) } : [];
            var counts = mode == "active" ? new AutomationQueueCounts(2, 1, 14, 0, 0) : new(0, 0, 14, review.Length, 0);
            return Task.FromResult(new AutomationDesktopOverview(new(null, "synthetic", AutomationHostState.Running,
                [new(universe, state, 0, 0, null, null, null), new(new("other_preview"), AutomationHostState.Running, 0, 0, null, null, null)], null),
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
