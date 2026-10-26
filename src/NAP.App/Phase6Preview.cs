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
    private static readonly string[] HistoricalModes = ["historical", "historical-children", "historical-elder", "historical-ambiguous", "historical-duplicate", "historical-unknown", "historical-empty", "historical-context", "historical-export"];
    internal static ShellViewModel Create(string mode, bool interactiveAcceptance = false)
    {
        if (mode is not ("active" or "paused" or "review" or "offline" or "local" or "catalog" or "settings") && !Phase6WorkflowPreview.Modes.Contains(mode) && !Phase6SettingsPreview.Modes.Contains(mode) && !HistoricalModes.Contains(mode)) throw new ArgumentException("Unknown preview mode.");
        // An inert absolute namespace, never created or probed; independent of user environment/configuration.
        var root = System.IO.Path.Combine(System.IO.Path.GetPathRoot(AppContext.BaseDirectory)!, "NAP-SYNTHETIC-NOT-A-WORKSPACE");
        UniverseContext Context(string id, string label) => new(new UniverseProfile(new(id), label),
            new(new(id), System.IO.Path.Combine(root, id, "workspace"), System.IO.Path.Combine(root, id, "production"), System.IO.Path.Combine(root, id, "archive")));
        var contexts = new[] { Context("preview", "Universo de muestra"), Context("other_preview", "Segundo universo") };
        var explorer = new ExplorerViewModel(contexts.Select(c => c.Profile), new(System.IO.Path.Combine(root, "unused-settings.json")), new PreviewExplorer(), new NoFolders(), hostConfigurations: contexts.Select(c => c.Storage).ToArray());
        var confirmation = new NoConfirmation();
        var workflow=new Phase6WorkflowPreview(mode, interactiveAcceptance);
        var automation = interactiveAcceptance || mode is "local" or "catalog" or "settings" || Phase6SettingsPreview.Modes.Contains(mode) || HistoricalModes.Contains(mode) ? null : new AutomationControlViewModel(() => explorer.Context, confirmation, new PreviewSource(mode, contexts,workflow));
        var service = DispatchProxy.Create<IProfessionalUiService, Phase6ReadOnlyPreviewService>();
        return new(explorer, service, confirmation, automation: automation, normalization: new ReadOnlyNormalization(), workflowClient: workflow, diagnosticClient: mode=="diagnostics-offline"?null:new Phase6SettingsPreview(mode), licenses: Phase6SettingsPreview.Licenses(mode), syntheticReadOnly: true) { ReviewExpanded = mode == "review", ToolsExpanded = mode == "review" };
    }
    internal static async Task CaptureAsync(MainWindow window, BindingDiagnostics diagnostics, string mode, string output)
    {
        // This is a capture, never acceptance on the responsible person's MSI.
        var shell = (ShellViewModel)window.DataContext;
        if(mode is "catalog" or "settings") { shell.NavigateTo(mode == "catalog" ? "Catálogo" : "Ajustes"); await shell.LastRefresh; }
        window.VerifyTrayRoundTrip();
        if(HistoricalModes.Contains(mode))
        {
            shell.NavigateTo("Ajustes"); await shell.LastRefresh;
            shell.Settings.Section = "Histórico sintético";
            var vm = shell.Settings.Historical;
            if(mode is "historical-children" or "historical-elder")
            {
                vm.Group = vm.Groups.Single(g => g.Value == (mode == "historical-children" ? "children" : "elder"));
                vm.Selected = mode == "historical-elder" ? vm.Rows.First(r => r.ProposedPath is not null) : vm.Rows[0];
            }
            if(mode == "historical-ambiguous") vm.Selected = vm.Rows.Single(r => r.Reference.EndsWith("004"));
            if(mode == "historical-duplicate") vm.Selected = vm.Rows[0];
            if(mode == "historical-unknown") vm.Selected = vm.Rows.Single(r => r.Status == HistoricalSimulationStatus.Unverifiable);
            if(vm.Selected?.Issues.Count > 0) vm.SelectedIssue = vm.Selected.Issues.Last();
            if(mode == "historical-empty") { vm.Group = vm.Groups.Single(g => g.Value == "children"); vm.Status = vm.Statuses.Single(s => s.Value == HistoricalSimulationStatus.Unverifiable); if(vm.Rows.Count != 0) throw new InvalidOperationException("Expected empty historical filter."); }
            if(mode == "historical-context")
            {
                vm.Selected = vm.Rows[0]; vm.Search = "001";
                shell.SelectedUniverse = shell.Profiles.Last(); await shell.Explorer.UniverseChangeTask; await shell.LastRefresh;
                if(vm.Selected is not null || vm.Rows.Count != 39 || vm.Search != "") throw new InvalidOperationException("Historical context retained stale data.");
            }
        }
        if(Phase6SettingsPreview.Modes.Contains(mode))
        {
            shell.NavigateTo("Ajustes");await shell.LastRefresh;
            shell.Settings.Section=mode.StartsWith("licenses")?"Licencias":"Diagnóstico";
            if(mode.StartsWith("diagnostics"))
            {
                var vm=shell.Settings.Diagnostics!;
                if(mode=="diagnostics-filtered")
                { vm.From=new(2026,10,22);vm.Until=new(2026,10,22);vm.Severity=vm.Severities.Single(s=>s.Value==DiagnosticSeverity.Warning);vm.Category=vm.Categories.Single(c=>c.Value==QueueStage.Normalization); }
                await vm.Refresh.ExecuteAsync();
                if(mode is "diagnostics" or "diagnostics-export" && vm.Rows.Count!=4)throw new InvalidOperationException("Missing diagnostic fixture.");
                if(mode=="diagnostics-filtered"&&vm.Rows.Count!=1)throw new InvalidOperationException("Diagnostic filters differ.");
            }
        }
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
        if ((!Phase6WorkflowPreview.Modes.Contains(mode)&&!Phase6SettingsPreview.Modes.Contains(mode)&&!HistoricalModes.Contains(mode)&& mode is not ("catalog" or "settings") && shell.CurrentPage != shell.Dashboard) || shell.Navigation.Count != 5 || shell.Tools.Count != 5)
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
        string? previousImageHash = null;
        async Task Save(string suffix)
        {
            var image = await window.Dispatcher.InvokeAsync(() =>
            {
                capture.UpdateLayout();
                capture.Measure(new Size(width, height)); capture.Arrange(new Rect(0, 0, width, height));
                if (capture.ActualWidth != width || capture.ActualHeight != height) throw new InvalidOperationException("Synthetic layout was constrained by the runner desktop.");
                foreach(var picker in Descendants(capture).OfType<DatePicker>().Where(p=>p.IsVisible&&p.SelectedDate is not null))
                {
                    var editor=(System.Windows.Controls.Primitives.DatePickerTextBox)picker.Template.FindName("PART_TextBox",picker);
                    var text=new FormattedText(editor.Text,System.Globalization.CultureInfo.GetCultureInfo("es-ES"),FlowDirection.LeftToRight,
                        new Typeface(editor.FontFamily,editor.FontStyle,editor.FontWeight,editor.FontStretch),editor.FontSize,editor.Foreground,VisualTreeHelper.GetDpi(editor).PixelsPerDip);
                    if(text.WidthIncludingTrailingWhitespace>editor.ActualWidth-editor.Padding.Left-editor.Padding.Right-2)
                        throw new InvalidOperationException("Date field clips its recorded date.");
                }
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(capture); return bitmap;
            }, DispatcherPriority.Render);
            if (diagnostics.Errors != 0) throw new InvalidOperationException("Preview binding errors: " + diagnostics.Errors);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            var pixels = new byte[width * height * 4]; image.CopyPixels(pixels, width * 4, 0);
            var pixelHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
            if (pixelHash == previousImageHash) { Console.WriteLine($"omitted_unchanged_view:{mode}{suffix}"); return; }
            previousImageHash = pixelHash;
            var path = System.IO.Path.Combine(output, $"{(HistoricalModes.Contains(mode)?"historico":Phase6SettingsPreview.Modes.Contains(mode)?"ajustes":Phase6WorkflowPreview.Modes.Contains(mode)?"flujo":"inicio")}-{mode}{suffix}-{width}x{height}.png");
            using (var file = new System.IO.FileStream(path, System.IO.FileMode.CreateNew)) encoder.Save(file);
            var work = shell.CurrentPage == shell.History ? shell.History.Work : shell.Pipeline.Work;
            var state = new { mode, view = suffix, width, height, page = shell.CurrentPage.Name, section = shell.Settings.Section,
                selectedUniverse = shell.UniverseLabel, historicalRows = shell.Settings.Historical.Rows.Count,
                historicalViewport = Descendants(capture).OfType<ListBox>().FirstOrDefault(v => v.Name == "HistoricalInventory")?.ActualHeight,
                historicalSelection = shell.Settings.Historical.Selected?.Reference, issue = shell.Settings.Historical.SelectedIssue?.Code,
                workflowSelection = work?.Name, canApprove = work?.CanApprove, canContinue = work?.CanContinue,
                events = work?.EventPageLabel, decisions = work?.DecisionPageLabel,
                diagnosticRows = shell.Settings.Diagnostics?.Rows.Count, license = shell.Settings.Licenses?.Selected?.Reminder,
                trayRoundTrip = true, bindingErrors = diagnostics.Errors,
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))) };
            System.IO.File.WriteAllText(path + ".json", System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { preview = mode, view = suffix, synthetic = true, offscreenLayout = true, bindingErrors = diagnostics.Errors, width = image.PixelWidth, height = image.PixelHeight }));
        }
        if(mode=="diagnostics-export")
        {
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            var copyButton=Descendants(capture).OfType<Button>().Single(v=>v.Name=="CopyReportButton");
            copyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(!Clipboard.ContainsText()||Clipboard.GetText()!=shell.Settings.Diagnostics!.Report)throw new InvalidOperationException("Native diagnostic clipboard differs.");
            var expander=Descendants(capture).OfType<Expander>().Single(v=>v.Name=="ReportPreview");
            expander.IsExpanded=true;expander.BringIntoView();
            await window.Dispatcher.InvokeAsync(()=>capture.UpdateLayout(),DispatcherPriority.Render);
            DiagnosticReportWriter.Export(System.IO.Path.GetFullPath(System.IO.Path.Combine(output,$"diagnostic-report-{width}x{height}.txt")),shell.Settings.Diagnostics!.Report,[],System.IO.Path.Combine(System.IO.Path.GetTempPath(),"nap-preview-unused-admin"));
            if(System.IO.File.ReadAllText(System.IO.Path.Combine(output,$"diagnostic-report-{width}x{height}.txt"))!=shell.Settings.Diagnostics!.Report)throw new InvalidOperationException("Exported diagnostic bytes differ.");
            Console.WriteLine("diagnostic_native_clipboard_and_new_text_export_verified");
        }
        if(mode == "historical-export")
        {
            await window.Dispatcher.InvokeAsync(() => capture.UpdateLayout(), DispatcherPriority.Render);
            var reportPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(output, $"historical-simulation-{width}x{height}.txt"));
            DiagnosticReportWriter.Export(reportPath, shell.Settings.Historical.Report, shell.Explorer.Configurations, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nap-preview-unused-admin"));
            if(System.IO.File.ReadAllText(reportPath) != HistoricalSimulation.Report()) throw new InvalidOperationException("Historical export differs.");
            Descendants(capture).OfType<TextBlock>().Single(v => v.Name == "HistoricalTransferNotice").Text = "Simulación exportada · sin homologación para producción";
        }
        if(HistoricalModes.Contains(mode))
        {
            await window.Dispatcher.InvokeAsync(() => capture.UpdateLayout(), DispatcherPriority.Render);
            var inventory = Descendants(capture).OfType<ListBox>().Single(v => v.Name == "HistoricalInventory");
            var expectedGroups = shell.Settings.Historical.Rows.Select(r => r.HistoricalGroup).Distinct().Count();
            if (inventory.ActualHeight < (compact ? 180 : 350)) throw new InvalidOperationException($"Historical viewport too small: {inventory.ActualHeight}");
            var inventoryBounds = inventory.TransformToAncestor(capture).TransformBounds(new Rect(inventory.RenderSize));
            if (inventoryBounds.Bottom > height || inventoryBounds.Top < 0) throw new InvalidOperationException("Historical inventory is outside the captured viewport.");
            if (shell.Settings.Historical.HasSelection) inventory.ScrollIntoView(shell.Settings.Historical.Selected);
            if(inventory.Items.Count != shell.Settings.Historical.Rows.Count || inventory.Items.Groups?.Count != expectedGroups)
                throw new InvalidOperationException("Historical visual grouping differs from reference.");
        }
        await Save("");
        if(HistoricalModes.Contains(mode) && shell.Settings.Historical.HasSelection)
        {
            var detail = Descendants(capture).OfType<ScrollViewer>().Single(v => v.Name == "HistoricalScroll");
            foreach (var (name, suffix) in new[] { ("HistoricalRoute", "-ruta-historica"), ("ProposedRoute", "-ruta-propuesta") })
            {
                var route = Descendants(capture).OfType<TextBlock>().Single(v => v.Name == name);
                route.BringIntoView();
                await window.Dispatcher.InvokeAsync(() => capture.UpdateLayout(), DispatcherPriority.Render);
                var bounds = route.TransformToAncestor(detail).TransformBounds(new Rect(route.RenderSize));
                if (bounds.Top < -1 || bounds.Bottom > detail.ViewportHeight + 1) throw new InvalidOperationException("The named route is clipped.");
                await Save(suffix);
            }
            if(shell.Settings.Historical.SelectedIssue is not null)
            {
                detail.ScrollToBottom();
                await window.Dispatcher.InvokeAsync(() => capture.UpdateLayout(), DispatcherPriority.Render);
                var proposal = Descendants(capture).OfType<TextBlock>().Single(v => v.Name == "HistoricalProposal");
                var rect = proposal.TransformToAncestor(detail).TransformBounds(new Rect(proposal.RenderSize));
                if (rect.Top < -1 || rect.Bottom > detail.ViewportHeight + 1 || string.IsNullOrWhiteSpace(proposal.Text))
                    throw new InvalidOperationException("The named proposal is not fully visible.");
                // A second view is evidence only when scrolling really changes the viewport.
                if (detail.VerticalOffset > 0) await Save("-incidencia");
            }
        }
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
                await window.Dispatcher.InvokeAsync(()=>
                {
                    capture.Measure(new Size(width,height));capture.Arrange(new Rect(0,0,width,height));capture.UpdateLayout();
                    window.PrepareCapture(zoom);capture.UpdateLayout();
                },DispatcherPriority.Render);
                // Hidden content still needs the normal binding/ScrollChanged dispatcher cycle.
                await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                var bitmap=await window.Dispatcher.InvokeAsync(()=>
                {
                    capture.UpdateLayout();
                    capture.Measure(new Size(width,height));capture.Arrange(new Rect(0,0,width,height));capture.UpdateLayout();
                    var clip=VisualTreeHelper.GetClip(capture);
                    if(clip is not null&&!clip.Bounds.Contains(new Rect(0,0,width,height)))throw new InvalidOperationException("Comparison capture was clipped by the physical desktop.");
                    var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(capture);
                    var guides=Descendants(capture).OfType<Controls.ComparisonBorderGuides>().Single();
                    if(guides.VisibleGuides!=2)throw new InvalidOperationException("Expected two visible margin guides.");
                    var scroll=(ScrollViewer)window.FindName("ImageScroll");
                    if(zoom==2&&(scroll.ScrollableHeight<=0||scroll.ScrollableWidth<=0))throw new InvalidOperationException($"Zoom must scroll on both axes: {scroll.ExtentWidth} × {scroll.ExtentHeight} / {scroll.ViewportWidth} × {scroll.ViewportHeight}.");
                    return image;
                },DispatcherPriority.Render);
                if(diagnostics.Errors!=0)throw new InvalidOperationException("Comparison binding errors: "+diagnostics.Errors);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path=System.IO.Path.Combine(output,$"comparacion-{(zoom==1?"ampliada":"zoom-200")}-{width}x{height}.png");
                using (var file=new System.IO.FileStream(path,System.IO.FileMode.CreateNew)) encoder.Save(file);
                System.IO.File.WriteAllText(path+".json",System.Text.Json.JsonSerializer.Serialize(new { mode="exception",view=zoom==1?"comparacion-ampliada":"comparacion-zoom-200",width,height,zoom,marginGuides=2,bindingErrors=diagnostics.Errors,
                    originalSha256=Convert.ToHexString(original),candidateSha256=Convert.ToHexString(candidate),sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))) },new System.Text.Json.JsonSerializerOptions { WriteIndented=true }));
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
        nameof(IProfessionalUiService.PlanningAsync) => Task.FromResult(new CatalogPlanningReadModel(((UniverseContext)args![0]!).Id, [], [])),
        nameof(IProfessionalUiService.BackupsAsync) => Task.FromResult(new BackupHistory([], new NapIssueReport([]))),
        _ => throw new InvalidOperationException("Capture-only preview has no manual writer.")
    };
}
