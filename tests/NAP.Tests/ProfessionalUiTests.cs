using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public class ProfessionalServiceProxy : DispatchProxy
{
    public IProfessionalUiService Inner { get; set; } = new ProfessionalUiService();
    public Dictionary<string, Func<object?[], object?>> Overrides { get; } = [];
    public ConcurrentQueue<string> Calls { get; } = new();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Enqueue(targetMethod!.Name);
        return Overrides.TryGetValue(targetMethod.Name, out var call) ? call(args!) : targetMethod.Invoke(Inner, args);
    }
}
public sealed class ProfessionalUiTests
{
    [Theory][InlineData("Producción")][InlineData("Objetivos")]
    public async Task CrossUniverseNestedRecordsStopBeforeBeingShown(string page)
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Catalog.Initialize();
        var (shell, proxy, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync();
            proxy.Overrides[nameof(IProfessionalUiService.PipelineAsync)] = _ => Task.FromResult(new PipelineSnapshot(f.Context.Id, [], [new(new(JobId.Create(), b.Context.Id, JobState.Completed))], []));
            proxy.Overrides[nameof(IProfessionalUiService.PlanningAsync)] = _ => Task.FromResult(new CatalogPlanningReadModel(f.Context.Id, [new(new(b.Context.Id, "foreign", "Foreign goal", new(), 1), new(1, 1), new(1, [], [], [], []))], []));
            shell.NavigateTo(page); await shell.LastRefresh; Assert.Equal(UiPageState.Stopped, shell.CurrentPage.State);
            Assert.Empty(shell.Pipeline.Jobs); Assert.Empty(shell.Objectives.Objectives);
        }
    }

    [Theory][InlineData(AiAuditDecision.Warning)][InlineData(AiAuditDecision.Fail)]
    public async Task WarningAndFailAuditsNeverEnablePublication(AiAuditDecision decision)
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); Zip(f);
        var report = new AiAuditReport(decision, "Needs review", [new("review", decision == AiAuditDecision.Warning ? AiAuditFindingSeverity.Warning : AiAuditFindingSeverity.Error, "Explicit concern")]);
        var (shell, _, _) = Create(f, auditor: new Auditor(report)); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single();
            await shell.PreparePackage.ExecuteAsync(); await shell.AuditPackage.ExecuteAsync(); Assert.False(shell.ExecutePackage.CanExecute(null));
            Assert.Equal(decision, shell.Pipeline.Audit!.Decision); Assert.Equal(0, new CatalogExplorerReader(f.Context).Statistics().TotalAssets);
        }
    }
    [Fact] public async Task SettingsSaveIsExplicitAndRevalidatesTheActiveScope()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var (shell, _, _) = Create(f, configured: false); using (shell)
        {
            await shell.InitializeAsync(); shell.Explorer.WorkspaceRoot = f.Context.Storage.WorkspaceRoot; shell.Explorer.ProductionRoot = f.Context.Storage.ProductionRoot; shell.Explorer.ArchiveRoot = f.Context.Storage.ArchiveRoot;
            await shell.SaveSettings.ExecuteAsync(); Assert.Equal(f.Context, shell.Context); Assert.Equal(UiPageState.Ready, shell.Settings.State);
            Assert.Equal(f.Context.Storage, new LocalUniverseSettingsStore(shell.Explorer.SettingsLocation).Load().Single()); Assert.False(shell.MinimizeToTray);
            shell.MinimizeToTray = true; Assert.True(shell.MinimizeToTray);
        }
    }
    [Fact] public async Task LateFailureFromOldUniverseCannotStopTheNewPage()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Catalog.Initialize(); b.Catalog.Initialize();
        var (shell, proxy, _) = Create(f, b); using (shell)
        {
            await shell.InitializeAsync(); var gate = Gate<DashboardSnapshot>();
            proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = args => ((UniverseContext)args[0]!).Id == f.Context.Id ? gate.Task : proxy.Inner.DashboardAsync((UniverseContext)args[0]!, (CancellationToken)args[1]!);
            var pending = shell.RefreshCurrentAsync(); shell.SelectedUniverse = b.Context.Profile; await shell.LastRefresh;
            gate.SetException(new IOException("late alpha failure")); await pending; Assert.Equal(UiPageState.Ready, shell.Dashboard.State);
            Assert.Null(shell.Dashboard.Notice); Assert.Equal(b.Context.Id, shell.Dashboard.Snapshot!.UniverseId);
        }
    }
    [Fact] public async Task UniverseSwitchOwnsOneRefreshAndLastRefreshWaitsForThatRead()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Catalog.Initialize(); b.Catalog.Initialize();
        var (shell, proxy, _) = Create(f, b); using (shell)
        {
            await shell.InitializeAsync(); var snapshot = await proxy.Inner.DashboardAsync(b.Context, default);
            var entered = Gate<bool>(); var read = Gate<DashboardSnapshot>(); var calls = 0;
            proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = args =>
            {
                if (((UniverseContext)args[0]!).Id != b.Context.Id) throw new InvalidOperationException("Unexpected old-universe refresh.");
                Interlocked.Increment(ref calls); entered.TrySetResult(true); return read.Task;
            };
            shell.SelectedUniverse = b.Context.Profile; var refresh = shell.LastRefresh;
            await shell.Explorer.UniverseChangeTask; await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            read.SetResult(snapshot); await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(refresh, shell.LastRefresh); Assert.Equal(1, calls);
            Assert.Equal(UiPageState.Ready, shell.Dashboard.State); Assert.Equal(b.Context.Id, shell.Dashboard.Snapshot!.UniverseId);
        }
    }
    [Fact] public async Task DisposeDuringPendingMutationDoesNotPublishActivityIntoDisposedPages()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var (shell, proxy, _) = Create(f);
        await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh; var gate = Gate<BackupHistoryEntry>();
        proxy.Overrides[nameof(IProfessionalUiService.BackupAsync)] = _ => gate.Task;
        var pending = shell.CreateBackup.ExecuteAsync(); Assert.True(shell.IsExecuting); shell.Dispose();
        gate.SetResult(new DatabaseBackupService().Create(f.Context)); await pending;
        Assert.Empty(shell.History.Activities); Assert.Empty(shell.Backups.Entries); Assert.False(shell.CreateBackup.CanExecute(null));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task GitRequiresRealSessionEvidenceAndIndependentCommitPushConfirmations(bool commitAccepted)
    {
        using var g = new GitProductionFixture(); var store = new LocalUniverseSettingsStore(Path.Combine(g.Production.Root, "ui-git-settings.json")); store.Save([g.Context.Storage]);
        var explorer = new ExplorerViewModel([g.Context.Profile], store, new ExplorerService(), new Picker());
        var service = DispatchProxy.Create<IProfessionalUiService, ProfessionalServiceProxy>(); var proxy = (ProfessionalServiceProxy)service;
        proxy.Inner = new ProfessionalUiService(new Auditor(ProductionTestFixture.Pass));
        // Test-only local transport is enabled by the existing temporary Git fixture, never by the app.
        proxy.Overrides[nameof(IProfessionalUiService.PlanCommitAsync)] = args => Task.Run(() => g.Service.PrepareCommit((UniverseContext)args[0]!, (IReadOnlyList<ProductionAssetResult>)args[1]!, (string)args[2]!));
        proxy.Overrides[nameof(IProfessionalUiService.CommitAsync)] = args => Task.Run(() => g.Service.Commit((UniverseContext)args[0]!, (GitCommitPlan)args[1]!));
        proxy.Overrides[nameof(IProfessionalUiService.PushAsync)] = args => Task.Run(() => g.Service.Push((UniverseContext)args[0]!, (GitOperationId)args[1]!));
        var confirm = new Confirmation { Accepted = true }; using var shell = new ShellViewModel(explorer, service, confirm);
        new AssetCatalog(g.Context).Initialize();
        var zip = Path.Combine(g.Context.Storage.InboxRoot, g.Production.Package.AssetKey.AssetId + ".zip"); ZipFile.CreateFromDirectory(g.Production.Source.PackageRoot, zip);
        await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single();
        await shell.PreparePackage.ExecuteAsync(); await shell.AuditPackage.ExecuteAsync(); await shell.ExecutePackage.ExecuteAsync();
        Assert.Equal(UiPageState.Ready, shell.Pipeline.State); shell.NavigateTo("Git"); await shell.LastRefresh; shell.Git.Subject = "Verified UI session";
        var head = g.Git("rev-parse", "HEAD"); confirm.Accepted = commitAccepted; await shell.CommitGit.ExecuteAsync();
        Assert.Equal(g.Context.Id, confirm.Plan!.UniverseId); Assert.Contains("HEAD base: " + head, confirm.Plan.Details);
        if (!commitAccepted) { Assert.Equal(head, g.Git("rev-parse", "HEAD")); Assert.DoesNotContain(nameof(IProfessionalUiService.CommitAsync), proxy.Calls); Assert.Empty(shell.Git.Receipts); return; }
        Assert.Equal(UiPageState.Ready, shell.Git.State); var receipt = Assert.Single(shell.Git.Receipts);
        Assert.Equal(GitReceiptState.Committed, receipt.State); Assert.Equal(receipt.CommitSha, g.Git("rev-parse", "HEAD")); Assert.Equal(head, g.RemoteGit("rev-parse", "refs/heads/main"));
        shell.Git.SelectedReceipt = receipt; confirm.Accepted = false; await shell.PushGit.ExecuteAsync();
        Assert.Equal(head, g.RemoteGit("rev-parse", "refs/heads/main")); Assert.DoesNotContain(nameof(IProfessionalUiService.PushAsync), proxy.Calls);
        confirm.Accepted = true; shell.Git.SelectedReceipt = receipt; await shell.PushGit.ExecuteAsync();
        Assert.Equal(receipt.CommitSha, g.RemoteGit("rev-parse", "refs/heads/main")); Assert.Equal(GitReceiptState.Pushed, shell.Git.Receipts.Single().State);
    }

    private sealed class Picker : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class Confirmation : IUserConfirmation
    {
        public bool Accepted { get; set; }
        public UiConfirmation? Plan { get; private set; }
        public Func<Task<bool>>? Pause { get; set; }
        public Task<bool> ConfirmAsync(UiConfirmation c, CancellationToken ct) { Plan = c; return Pause?.Invoke() ?? Task.FromResult(Accepted); }
    }
    private sealed class Auditor(AiAuditReport report) : IAiAuditClient
    { public int Calls { get; private set; } public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(report); } }
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static (ShellViewModel Shell, ProfessionalServiceProxy Proxy, Confirmation Confirm) Create(CatalogTestFixture f, CatalogTestFixture? second = null, bool configured = true, IAiAuditClient? auditor = null)
    {
        var store = new LocalUniverseSettingsStore(Path.Combine(f.Root, "ui-settings.json"));
        if (configured) store.Save(second is null ? [f.Context.Storage] : [f.Context.Storage, second.Context.Storage]);
        var explorer = new ExplorerViewModel(second is null ? [f.Context.Profile] : [f.Context.Profile, second.Context.Profile], store, new ExplorerService(), new Picker());
        var service = DispatchProxy.Create<IProfessionalUiService, ProfessionalServiceProxy>(); var proxy = (ProfessionalServiceProxy)service;
        proxy.Inner = new ProfessionalUiService(auditor); var confirmation = new Confirmation();
        return (new(explorer, service, confirmation), proxy, confirmation);
    }
    private static InboxPackageCandidate Zip(CatalogTestFixture f)
    {
        var file = Path.Combine(f.Context.Storage.InboxRoot, f.Production.Package.AssetKey.AssetId + ".zip");
        ZipFile.CreateFromDirectory(f.Production.Source.PackageRoot, file);
        return new(Path.GetFileName(file), file);
    }

    [Fact] public async Task UnconfiguredNavigationNeverInventsCountsOrPerformsIo()
    {
        using var f = new CatalogTestFixture(); var (shell, proxy, _) = Create(f, configured: false); using (shell)
        {
            await shell.InitializeAsync(); Assert.Equal("Ajustes", shell.CurrentPage.Name);
            Assert.Equal("—", shell.Dashboard.Assets); Assert.Equal("—", shell.Dashboard.BackupCount); Assert.Null(shell.Context);
            foreach (var nav in shell.Navigation) { shell.NavigateTo(nav.Name); await shell.LastRefresh; Assert.Single(shell.Navigation, n => n.IsActive); Assert.Equal(nav.Name, shell.CurrentPage.Name); }
            Assert.Equal(9, shell.Navigation.Count); Assert.Empty(proxy.Calls);
            Assert.False(shell.PreparePackage.CanExecute(null)); Assert.False(shell.CreateBackup.CanExecute(null)); Assert.False(shell.CommitGit.CanExecute(null)); Assert.False(shell.PushGit.CanExecute(null));
        }
    }
    [Fact] public async Task DashboardAndAllPagesReadRealScopedDataWithoutWritingSources()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "goal", "Two emblems", new(assetType: "emblem"), 2));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Real campaign", ["goal"]));
        var sources = f.Sources(); var database = File.ReadAllBytes(f.Catalog.CatalogPath);
        var (shell, proxy, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); Assert.Equal("1", shell.Dashboard.Assets); Assert.Equal("0", shell.Dashboard.InboxCount); Assert.Equal("0", shell.Dashboard.BackupCount);
            Assert.Single(shell.Dashboard.Objectives); Assert.Equal("1 / 2", shell.Dashboard.Objectives[0].Coverage);
            foreach (var page in shell.Pages) { shell.NavigateTo(page.Name); await shell.LastRefresh; Assert.NotEqual(UiPageState.Stopped, page.State); Assert.NotEqual(UiPageState.Loading, page.State); }
            Assert.Single(shell.Objectives.Campaigns); Assert.Equal(1, shell.Explorer.Statistics!.TotalAssets);
            Assert.Empty(shell.Backups.Entries); Assert.NotEmpty(shell.History.Jobs); Assert.Empty(shell.History.Activities);
            Assert.Null(shell.Git.Repository); Assert.DoesNotContain(nameof(IProfessionalUiService.InspectGitAsync), proxy.Calls);
            shell.NavigateTo("Git"); await shell.LastRefresh; Assert.DoesNotContain(shell.Navigation, n => n.IsActive);
            Assert.False(shell.CommitGit.CanExecute(null)); Assert.False(shell.RetryGit.CanExecute(null)); Assert.False(shell.PushGit.CanExecute(null));
            f.AssertSources(sources); Assert.Equal(database, File.ReadAllBytes(f.Catalog.CatalogPath));
        }
    }
    [Fact] public async Task RodoRemainsAnHonestEmptyPlaceholderWithNoAuditCalls()
    {
        using var f = new CatalogTestFixture(); var (shell, proxy, _) = Create(f, configured: false); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("RoDo"); await shell.LastRefresh;
            Assert.Equal(UiPageState.Reserved, shell.Rodo.State); Assert.False(shell.Rodo.CanSend); Assert.Empty(shell.Rodo.Conversations);
            Assert.Equal("RoDo estará disponible en la Fase 15.", shell.Rodo.Availability); Assert.Empty(proxy.Calls); Assert.False(shell.AuditAvailable);
        }
    }
    [Fact] public async Task MissingCatalogIsStoppedWithoutCreatingOrRepairingIt()
    {
        using var f = new CatalogTestFixture(); var (shell, _, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); Assert.Equal(UiPageState.Stopped, shell.Dashboard.State); Assert.Equal("—", shell.Dashboard.Assets);
            Assert.Equal("0", shell.Dashboard.BackupCount); Assert.NotEmpty(shell.Dashboard.Notices); Assert.False(File.Exists(f.Catalog.CatalogPath));
            shell.NavigateTo("Catálogo"); await shell.LastRefresh; Assert.Equal(UiPageState.Stopped, shell.Catalog.State); Assert.Null(shell.Explorer.Assets);
            Assert.False(shell.CommitGit.CanExecute(null));
        }
    }
    [Fact] public async Task SaveObjectiveUsesExactCatalogFilterAndRealCoverage()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (shell, _, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.Explorer.AssetId = "emblem_example_001"; shell.Explorer.AssetType = "emblem";
            shell.NavigateTo("Objetivos"); await shell.LastRefresh; shell.Objectives.ObjectiveId = "own"; shell.Objectives.Title = "One specific asset"; shell.Objectives.Target = "2";
            var sources = f.Sources(); await shell.SaveObjective.ExecuteAsync();
            var progress = Assert.Single(new CatalogExplorerReader(f.Context).Planning().Objectives);
            Assert.Equal("emblem_example_001", progress.Objective.Filter.AssetId); Assert.Equal(1, progress.Coverage.ActualCount);
            Assert.Equal(50, shell.Objectives.Objectives[0].Percent); Assert.Contains("emblem_example_001", shell.Objectives.Objectives[0].Filter); f.AssertSources(sources);
            shell.Objectives.Target = "0"; Assert.False(shell.SaveObjective.CanExecute(null));
        }
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)]
    public void StepperShowsOnlyTheDurableCheckpoint(int state)
    {
        var checkpoint = new PipelineJob(new(JobId.Create(), new("test"), (JobState)state));
        Assert.Equal(9, checkpoint.Steps.Count); var current = Assert.Single(checkpoint.Steps, s => s.IsCurrent);
        Assert.Equal(((JobState)state).ToString().ToUpperInvariant(), current.Label);
        Assert.All(checkpoint.Steps.Where(s => !s.IsCurrent), step => Assert.Equal(UiTone.Neutral, step.Tone));
        Assert.Equal(state == 8 ? UiTone.Error : state is 6 or 7 ? UiTone.Success : UiTone.Neutral, current.Tone);
        Assert.DoesNotContain(typeof(PipelineStep).GetProperties(), p => p.Name.Contains("Time", StringComparison.Ordinal));
    }
    [Fact] public async Task InvalidJournalStopsPreparationWithoutRepair()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var candidate = Zip(f);
        var journal = Path.Combine(f.Context.Storage.StateRoot, JobId.Create().Value + ".json"); File.WriteAllText(journal, "broken");
        var (shell, _, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = candidate;
            Assert.Equal(UiPageState.Stopped, shell.Pipeline.State); Assert.Equal(UiTone.Error, shell.Pipeline.Notice!.Tone); Assert.False(shell.PreparePackage.CanExecute(null));
            Assert.Equal("broken", File.ReadAllText(journal)); Assert.Empty(Directory.GetFiles(f.Context.Storage.StagingRoot));
        }
    }
    [Fact] public async Task RealZipPublishesOnlyAfterRealAuditAndExplicitConfirmation()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var candidate = Zip(f); var original = File.ReadAllBytes(candidate.FullPath);
        var auditor = new Auditor(ProductionTestFixture.Pass); var (shell, proxy, confirm) = Create(f, auditor: auditor); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single();
            await shell.PreparePackage.ExecuteAsync(); Assert.Equal(UiPageState.Ready, shell.Pipeline.State); Assert.NotNull(shell.Pipeline.Prepared);
            Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(shell.Pipeline.Prepared!.JobId).State);
            Assert.Null(shell.Pipeline.Audit); Assert.False(shell.ExecutePackage.CanExecute(null)); await shell.AuditPackage.ExecuteAsync(); Assert.Equal(1, auditor.Calls);
            var job = shell.Pipeline.Prepared.JobId; var sources = f.Sources(); await shell.ExecutePackage.ExecuteAsync();
            f.AssertSources(sources); Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(job).State);
            Assert.DoesNotContain(nameof(IProfessionalUiService.ExecuteAsync), proxy.Calls); Assert.Equal(f.Context.Id, confirm.Plan!.UniverseId);
            confirm.Accepted = true; await shell.ExecutePackage.ExecuteAsync();
            Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(job).State); Assert.Equal(1, new CatalogExplorerReader(f.Context).Statistics().TotalAssets);
            Assert.Null(shell.Pipeline.Prepared); Assert.Equal(original, File.ReadAllBytes(candidate.FullPath));
            shell.NavigateTo("Git"); await shell.LastRefresh; shell.Git.Subject = "Explicit session outputs"; Assert.True(shell.CommitGit.CanExecute(null));
        }
    }
    [Fact] public async Task AbsentAuditorCannotManufacturePassOrEnablePublication()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); Zip(f); var (shell, _, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single();
            await shell.PreparePackage.ExecuteAsync(); Assert.NotNull(shell.Pipeline.Prepared); Assert.False(shell.AuditPackage.CanExecute(null));
            Assert.False(shell.ExecutePackage.CanExecute(null)); Assert.Null(shell.Pipeline.Audit); Assert.Equal(0, new CatalogExplorerReader(f.Context).Statistics().TotalAssets);
        }
    }
    [Fact] public async Task PreparedEvidenceCannotBeUsedInAnotherUniverse()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        var service = new ProfessionalUiService(new Auditor(ProductionTestFixture.Pass)); var prepared = await service.PrepareAsync(f.Context, Zip(f), default);
        var sources = b.Sources();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AuditAsync(b.Context, prepared, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(b.Context, prepared, ProductionTestFixture.Pass, default)); b.AssertSources(sources);
    }
    [Fact] public async Task CandidateOutsideActiveInboxStopsBeforeStaging()
    {
        using var f = new CatalogTestFixture(); using var b = new CatalogTestFixture(); var candidate = Zip(b);
        var before = ArchiveTestFixture.Snapshot(f.Context.Storage.WorkspaceRoot); var original = File.ReadAllBytes(candidate.FullPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ProfessionalUiService().PrepareAsync(f.Context, candidate, default));
        Assert.Equal(before.OrderBy(p => p.Key), ArchiveTestFixture.Snapshot(f.Context.Storage.WorkspaceRoot).OrderBy(p => p.Key));
        Assert.Equal(original, File.ReadAllBytes(candidate.FullPath));
    }
    [Fact] public async Task BackupRestoreDeclineLeavesCatalogAndInventoryByteExact()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (shell, proxy, confirm) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh; await shell.CreateBackup.ExecuteAsync();
            var entry = Assert.Single(shell.Backups.Entries); shell.Backups.Selected = entry;
            var database = File.ReadAllBytes(f.Catalog.CatalogPath); var sources = f.Sources(); await shell.RestoreBackup.ExecuteAsync();
            Assert.Equal("Restaurar catálogo", confirm.Plan!.Title); Assert.Equal(f.Context.Id, confirm.Plan.UniverseId); Assert.Contains("SHA-256: " + entry.Sha256.Hex, confirm.Plan.Details);
            Assert.DoesNotContain(nameof(IProfessionalUiService.RestoreAsync), proxy.Calls); Assert.Equal(database, File.ReadAllBytes(f.Catalog.CatalogPath)); f.AssertSources(sources);
        }
    }
    [Fact] public async Task ConfirmedRestoreUsesFrozenCorePlanAndMandatorySafetyBackup()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "goal", "Prior goal", new(), 2)); var backup = new DatabaseBackupService().Create(f.Context);
        f.Catalog.SaveObjective(new(f.Context.Id, "goal", "Changed goal", new(), 3));
        var (shell, _, confirm) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh; shell.Backups.Selected = shell.Backups.Entries.Single(e => e.BackupId == backup.BackupId);
            var production = ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot); confirm.Accepted = true; await shell.RestoreBackup.ExecuteAsync();
            Assert.Equal(2, new CatalogExplorerReader(f.Context).Planning().Objectives.Single().Objective.TargetCount);
            Assert.Equal(2, new BackupHistoryReader().Read(f.Context).Entries.Count); Assert.Equal(UiPageState.Ready, shell.Backups.State);
            Assert.Contains(shell.History.Activities, a => a.Message == "Catálogo restaurado y verificado" && a.UniverseId == f.Context.Id);
            Assert.Equal(production.OrderBy(p => p.Key), ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot).OrderBy(p => p.Key));
        }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RetentionRequiresInventoryConfirmationAndProtectsLastBackup(bool accepted)
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true);
        for (var n = 0; n < 3; n++) new DatabaseBackupService().Create(f.Context);
        var (shell, proxy, confirm) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh; shell.Backups.Newest = "1";
            var sources = f.Sources(); confirm.Accepted = accepted; await shell.RetainBackups.ExecuteAsync();
            Assert.Equal(f.Context.Id, confirm.Plan!.UniverseId); Assert.Equal(2, confirm.Plan.Details.Count);
            Assert.Equal(accepted ? 1 : 3, new BackupHistoryReader().Read(f.Context).Entries.Count);
            Assert.Equal(accepted, proxy.Calls.Contains(nameof(IProfessionalUiService.RetainAsync))); if (!accepted) f.AssertSources(sources);
        }
    }
    [Fact] public async Task ConfirmationLocksContextAndNavigationAndRejectsConcurrentExecution()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Publish(automatic: true); b.Catalog.Initialize();
        new DatabaseBackupService().Create(f.Context); var (shell, proxy, confirm) = Create(f, b); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh; shell.Backups.Selected = shell.Backups.Entries.Single();
            var gate = Gate<bool>(); var entered = Gate<bool>(); confirm.Pause = () => { entered.TrySetResult(true); return gate.Task; };
            var task = shell.RestoreBackup.ExecuteAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(shell.IsExecuting); Assert.False(shell.CanChangeContext); shell.SelectedUniverse = b.Context.Profile; shell.NavigateTo("Inicio");
            Assert.Equal(f.Context.Id, shell.Context!.Id); Assert.Equal("Backups", shell.CurrentPage.Name);
            await shell.RestoreBackup.ExecuteAsync(); gate.SetResult(false); await task;
            Assert.False(shell.IsExecuting); Assert.DoesNotContain(nameof(IProfessionalUiService.RestoreAsync), proxy.Calls);
        }
    }
    [Fact] public async Task LateDashboardFromOldUniverseIsDiscardedAfterSwitch()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Publish(automatic: true); b.Catalog.Initialize();
        var (shell, proxy, _) = Create(f, b); using (shell)
        {
            await shell.InitializeAsync(); var gate = Gate<DashboardSnapshot>(); var old = shell.Dashboard.Snapshot!;
            proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = args => ((UniverseContext)args[0]!).Id == f.Context.Id ? gate.Task : proxy.Inner.DashboardAsync((UniverseContext)args[0]!, (CancellationToken)args[1]!);
            var pending = shell.RefreshCurrentAsync(); Assert.Equal(UiPageState.Loading, shell.Dashboard.State);
            shell.SelectedUniverse = b.Context.Profile; await shell.LastRefresh; gate.SetResult(old); await pending;
            Assert.Equal(b.Context.Id, shell.Dashboard.Snapshot!.UniverseId); Assert.Equal("0", shell.Dashboard.Assets); Assert.Empty(shell.History.Activities); Assert.Empty(shell.Objectives.Objectives);
        }
    }
    [Fact] public async Task CancellationRejectsLateReadEvenWhenAdapterIgnoresToken()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (shell, proxy, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); var gate = Gate<DashboardSnapshot>(); CancellationToken token = default; var prior = shell.Dashboard.Snapshot!;
            proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = args => { token = (CancellationToken)args[1]!; return gate.Task; };
            var pending = shell.RefreshCurrentAsync(); shell.CancelRead.Execute(null); Assert.True(token.IsCancellationRequested);
            gate.SetResult(prior with { Statistics = new(999, [], [], [], []) }); await pending;
            Assert.Equal(UiPageState.Cancelled, shell.Dashboard.State); Assert.Equal("1", shell.Dashboard.Assets);
            proxy.Overrides.Clear(); await shell.RefreshCurrentAsync(); Assert.Equal(UiPageState.Ready, shell.Dashboard.State);
        }
    }
    [Fact] public async Task DisposeCancelsReadsClearsEvidenceAndIgnoresLateResults()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (shell, proxy, _) = Create(f);
        await shell.InitializeAsync(); var gate = Gate<DashboardSnapshot>(); var prior = shell.Dashboard.Snapshot!;
        proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = _ => gate.Task;
        var pending = shell.RefreshCurrentAsync(); shell.Dispose(); shell.Dispose(); gate.SetResult(prior); await pending;
        Assert.Null(shell.Dashboard.Snapshot); Assert.False(shell.Navigate.CanExecute("Inicio")); Assert.False(shell.Refresh.CanExecute(null)); Assert.False(shell.CommitGit.CanExecute(null)); Assert.Empty(shell.History.Activities);
    }
    [Fact] public async Task CrossUniverseReadIsStopAndNeverPublishesWrongCounts()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); f.Catalog.Initialize(); b.Catalog.Initialize();
        var (shell, proxy, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync();
            proxy.Overrides[nameof(IProfessionalUiService.DashboardAsync)] = _ => Task.FromResult(new DashboardSnapshot(b.Context.Id, new(999, [], [], [], []), null, null, null, []));
            await shell.RefreshCurrentAsync(); Assert.Equal(UiPageState.Stopped, shell.Dashboard.State); Assert.Equal("0", shell.Dashboard.Assets); Assert.NotNull(shell.Dashboard.Notice);
        }
    }
    [Fact] public async Task FailedOperationIsSanitizedAndDoesNotEnableDangerousCommands()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var (shell, proxy, _) = Create(f); using (shell)
        {
            await shell.InitializeAsync(); shell.NavigateTo("Backups"); await shell.LastRefresh;
            proxy.Overrides[nameof(IProfessionalUiService.BackupAsync)] = _ => Task.FromException<BackupHistoryEntry>(new IOException("private secret path"));
            await shell.CreateBackup.ExecuteAsync(); Assert.Equal(UiPageState.Stopped, shell.Backups.State);
            Assert.DoesNotContain("secret", shell.Backups.Notice!.Message); Assert.False(shell.CreateBackup.CanExecute(null)); Assert.False(shell.RestoreBackup.CanExecute(null));
            Assert.False(shell.IsExecuting); Assert.Single(shell.History.Activities); Assert.Equal(UiTone.Error, shell.History.Activities[0].Tone);
        }
    }
    [Fact] public async Task CancelledServiceRequestDoesNotCreateBackup()
    {
        using var f = new CatalogTestFixture(); f.Catalog.Initialize(); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProfessionalUiService().BackupAsync(f.Context, BackupKind.Database, cts.Token));
        Assert.Empty(new BackupHistoryReader().Read(f.Context).Entries);
    }
}
