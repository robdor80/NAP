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
        if (mode is not ("active" or "paused" or "review" or "offline" or "local") && !Phase6WorkflowPreview.Modes.Contains(mode)) throw new ArgumentException("Unknown preview mode.");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-phase6-preview-unused");
        UniverseContext Context(string id, string label) => new(new UniverseProfile(new(id), label),
            new(new(id), System.IO.Path.Combine(root, id, "workspace"), System.IO.Path.Combine(root, id, "production"), System.IO.Path.Combine(root, id, "archive")));
        var contexts = new[] { Context("preview", "Universo de muestra"), Context("other_preview", "Segundo universo") };
        var explorer = new ExplorerViewModel(contexts.Select(c => c.Profile), new(System.IO.Path.Combine(root, "unused-settings.json")), new PreviewExplorer(), new NoFolders(), hostConfigurations: contexts.Select(c => c.Storage).ToArray());
        var confirmation = new NoConfirmation();
        var workflow=new Phase6WorkflowPreview(mode);
        var automation = mode == "local" ? null : new AutomationControlViewModel(() => explorer.Context, confirmation, new PreviewSource(mode, contexts,workflow));
        var service = DispatchProxy.Create<IProfessionalUiService, Phase6ReadOnlyPreviewService>();
        return new(explorer, service, confirmation, automation: automation, normalization: new ReadOnlyNormalization(), workflowClient: workflow) { ReviewExpanded = mode == "review", ToolsExpanded = mode == "review" };
    }
    internal static async Task CaptureAsync(MainWindow window, BindingDiagnostics diagnostics, string mode, string output)
    {
        // This is a capture, never acceptance on the responsible person's MSI.
        var shell = (ShellViewModel)window.DataContext;
        if(Phase6WorkflowPreview.Modes.Contains(mode))
        {
            shell.NavigateTo(mode=="history"||mode.StartsWith("activity")?"Historial":"Producción"); await shell.LastRefresh;
            var work=mode=="history"||mode.StartsWith("activity")?shell.History.Work!:shell.Pipeline.Work!;
            if(mode=="manual")shell.Pipeline.ManualExpanded=true;
            else if(work.Rows.Count>0){work.Selected=mode=="history"?work.Rows.First(r=>r.Value.State==QueueState.Completed):work.Rows[0];await work.LastDetail;}
            if(mode.StartsWith("activity"))
            {
                await work.NextEvents.ExecuteAsync();
                if(work.EventPageLabel!="21–24"||work.NextEvents.CanExecute(null)||work.DecisionPageLabel!=(mode=="activity"?"1–3":"1–20")||work.NextDecisions.CanExecute(null)!=(mode=="activity-decisions"))
                    throw new InvalidOperationException("Independent activity pages differ.");
            }
            if(work.Rows.Any(r=>r.Value.State==QueueState.Running)&&shell.Automation?.Status!="Procesando imágenes")throw new InvalidOperationException("Synthetic overview contradicts its workflow rows.");
            if(mode is "exception" or "approved" && !work.HasPreview)throw new InvalidOperationException("Missing synthetic correction preview.");
            if(mode=="choice"&&(!work.HasOptions||work.HasPreview))throw new InvalidOperationException("Missing compatible choice state.");
            if(mode=="blocked"&&(work.CanApprove||work.CanContinue||work.HasOptions))throw new InvalidOperationException("A failed audit must not offer an override.");
        }
        if ((!Phase6WorkflowPreview.Modes.Contains(mode)&&shell.CurrentPage != shell.Dashboard) || shell.Navigation.Count != 5 || shell.Tools.Count != 5)
            throw new InvalidOperationException("Preview navigation differs from the reviewed shell.");
        if (mode is "active" or "paused" or "review" && shell.Automation?.Completed != "14")
            throw new InvalidOperationException("Synthetic projection was not displayed.");
        if (mode == "review" && shell.Automation?.Review.Count != 1)
            throw new InvalidOperationException("Synthetic incident was not displayed.");
        if (mode == "offline" && shell.Automation?.Completed != "—" || mode == "local" && shell.Automation is not null)
            throw new InvalidOperationException("Unavailable automation must not invent counters.");
        // Render the real visual tree at the requested logical client size, independent of
        // the runner's smaller desktop. Keep the Window ancestor for all product bindings.
        var compact = window.Tag is true; shell.IsCompact=compact;
        var width = compact ? 1200 : 1440; var height = compact ? 700 : 900;
        var content = (UIElement)window.Content;
        var capture = new Grid { Background = window.Background, Width = width, Height = height };
        window.Content = capture; capture.Children.Add(content);
        System.IO.Directory.CreateDirectory(output);
        async Task Save(string suffix)
        {
            var image = await window.Dispatcher.InvokeAsync(() =>
            {
                capture.UpdateLayout();
                capture.Measure(new Size(width, height)); capture.Arrange(new Rect(0, 0, width, height));
                if (capture.ActualWidth != width || capture.ActualHeight != height) throw new InvalidOperationException("Synthetic layout was constrained by the runner desktop.");
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(capture); return bitmap;
            }, DispatcherPriority.Render);
            if (diagnostics.Errors != 0) throw new InvalidOperationException("Preview binding errors: " + diagnostics.Errors);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = System.IO.File.Create(System.IO.Path.Combine(output, $"{(Phase6WorkflowPreview.Modes.Contains(mode)?"flujo":"inicio")}-{mode}{suffix}-{width}x{height}.png"));
            encoder.Save(file);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { preview = mode, view = suffix, synthetic = true, offscreenLayout = true, bindingErrors = diagnostics.Errors, width = image.PixelWidth, height = image.PixelHeight }));
        }
        await Save("");
        if(mode=="review"&&compact)
        {
            var tools=(ScrollViewer)window.FindName("ToolsScroll");
            if(tools.ScrollableHeight<=0)throw new InvalidOperationException("Compact tools must have a constrained scrolling viewport.");
            var lastTool=Descendants(tools).OfType<Button>().Single(b=>b.Content as string=="RoDo");
            lastTool.BringIntoView();
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            if(tools.VerticalOffset<=0)throw new InvalidOperationException("The last compact tool could not be brought into view.");
            await Save("-herramientas");
        }
        if(mode is "choice" or "exception" or "approved" or "history")
        {
            // Preserve an additional native view of the lower detail, including the real action controls.
            var detail=Descendants(capture).OfType<ScrollViewer>().Single(v=>v.Name=="ReviewDetailsScroll");
            detail.ScrollToBottom();
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            await Save("-detalle");
        }
        if(mode.StartsWith("activity"))
        {
            var decisionTarget=Descendants(capture).OfType<FrameworkElement>().Single(v=>v.Name==(mode=="activity-decisions"?"DecisionsPaging":"DecisionsHeading"));
            decisionTarget.BringIntoView();
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            await Save("-decisiones");
            var eventsTarget=Descendants(capture).OfType<FrameworkElement>().Single(v=>v.Name=="EventsPaging");
            eventsTarget.BringIntoView();
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            await Save("-actividad");
        }
        if(mode=="exception")await CaptureComparison(window,shell.Pipeline.Work!,diagnostics,output,width,height);
    }
    private static async Task CaptureComparison(MainWindow owner,WorkflowViewModel model,BindingDiagnostics diagnostics,string output,int width,int height)
    {
        var preview=model.Comparison!;
        var original=System.Security.Cryptography.SHA256.HashData(preview.OriginalImage);
        var candidate=System.Security.Cryptography.SHA256.HashData(preview.CandidateImage);
        // The product content keeps its Window namescope and logical parent, while an
        // unshown Window avoids all native desktop clipping during this offscreen capture.
        var window=new ComparisonWindow(model);
        var content=(UIElement)window.Content;
        var capture=new Grid{Background=window.Background,Width=width,Height=height};
        window.Content=capture;capture.Children.Add(content);
        try
        {
            foreach(var zoom in new[]{1d,2d})
            {
                var bitmap=await window.Dispatcher.InvokeAsync(()=>
                {
                    // Flush ordinary Window layout first. Never let a later UpdateLayout reapply
                    // the runner's physical client bounds after this fixed offscreen arrangement.
                    capture.UpdateLayout();
                    capture.Measure(new Size(width,height));capture.Arrange(new Rect(0,0,width,height));
                    window.PrepareCapture(zoom);
                    capture.Measure(new Size(width,height));capture.Arrange(new Rect(0,0,width,height));
                    var clip=VisualTreeHelper.GetClip(capture);
                    if(clip is not null&&!clip.Bounds.Contains(new Rect(0,0,width,height)))throw new InvalidOperationException("Comparison capture was clipped by the physical desktop.");
                    var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(capture);
                    var guides=Descendants(capture).OfType<Controls.ComparisonBorderGuides>().Single();
                    if(guides.VisibleGuides!=2)throw new InvalidOperationException("Expected two visible margin guides.");
                    var scroll=(ScrollViewer)window.FindName("ImageScroll");
                    if(zoom==2&&(scroll.ScrollableHeight<=0||scroll.ScrollableWidth<=0))throw new InvalidOperationException("Zoom must scroll on both axes.");
                    return image;
                },DispatcherPriority.Render);
                if(diagnostics.Errors!=0)throw new InvalidOperationException("Comparison binding errors: "+diagnostics.Errors);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=System.IO.File.Create(System.IO.Path.Combine(output,$"comparacion-{(zoom==1?"ampliada":"zoom-200")}-{width}x{height}.png"));encoder.Save(file);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{comparison=true,synthetic=true,offscreenLayout=true,zoom,width,height,bindingErrors=diagnostics.Errors,marginGuides=2}));
            }
            if(!original.SequenceEqual(System.Security.Cryptography.SHA256.HashData(preview.OriginalImage))||!candidate.SequenceEqual(System.Security.Cryptography.SHA256.HashData(preview.CandidateImage)))
                throw new InvalidOperationException("Read-only comparison changed its buffers.");
        }
        finally{window.Close();}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
    private sealed class PreviewSource(string mode, IReadOnlyList<UniverseContext> contexts,Phase6WorkflowPreview workflow) : IAutomationOverviewClient
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
                Phase6WorkflowPreview.Modes.Contains(mode)?workflow.ReadOverview(universe,policy):new(universe, DateTimeOffset.UtcNow, counts, review, policy),
                new(universe, 1, mode == "paused", false, new(universe, c.Storage, new(new string('a', 64)), policy.Reference, auth.AuthorizationId, auth.Actor, false), "synthetic", [])));
        }
        public Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation) =>
            throw new InvalidOperationException("Capture-only preview cannot send commands.");
    }
    private sealed class ReadOnlyNormalization : IImageNormalizationUiService
    {
        public Task<ImageNormalizationProposal> PrepareAsync(UniverseContext c, InboxPackageCandidate item,string? profile,ImageNormalizationPolicy policy,CancellationToken ct)=>throw new InvalidOperationException("Read-only preview.");
        public Task<ImageNormalizationResult> DecideAsync(UniverseContext c,ImageNormalizationProposal p,ImageNormalizationAuthorization a,CancellationToken ct)=>throw new InvalidOperationException("Read-only preview.");
        public Task<ImageNormalizationRecoverySnapshot> ReadAsync(UniverseContext c,CancellationToken ct)=>Task.FromResult(new ImageNormalizationRecoverySnapshot([],[],[]));
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
        nameof(IProfessionalUiService.PipelineAsync) => Task.FromResult(new PipelineSnapshot(((UniverseContext)args![0]!).Id, [], [], [])),
        nameof(IProfessionalUiService.ReceiptsAsync) => Task.FromResult<IReadOnlyList<GitOperationReceipt>>([]),
        _ => throw new InvalidOperationException("Capture-only preview has no manual writer.")
    };
}
