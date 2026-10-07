using System.Collections.Concurrent;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class ExplorerViewModelTests
{
    private sealed class Picker : IFolderPicker { public string? Next { get; set; } public string? Pick(string title, string current) => Next; }
    private sealed class FakeService(UniverseContext context) : IExplorerService
    {
        public int Total { get; set; } = 1;
        public int ThumbnailCalls { get; private set; }
        public ConcurrentQueue<(UniverseId Universe, CatalogFilter Filter, int Offset)> Requests { get; } = new();
        public Func<CancellationToken, Task>? Pause { get; set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CatalogAssetSummary Summary(int n, UniverseContext? c = null) => new(new((c ?? context).Id, $"emblem_example_n{n + 1}_001"), "emblem", "painted_icon", $"icons/emblem_example_n{n + 1}_001/emblem_example_n{n + 1}_001.webp", new(new string('a', 64)), 100);
        public async Task<CatalogPage> PageAsync(UniverseContext c, CatalogFilter filter, int offset, int limit, CancellationToken ct)
        {
            Requests.Enqueue((c.Id, filter, offset)); Entered.TrySetResult();
            try { if (Pause is not null) await Pause(ct); ct.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            if (Failure is not null) throw Failure;
            return new(Total, offset, Enumerable.Range(offset, Math.Min(limit, Math.Max(0, Total - offset))).Select(i => Summary(i, c)).ToArray());
        }
        public Task<CatalogAssetSnapshot?> DetailAsync(UniverseContext c, string id, CancellationToken ct) => Task.FromResult<CatalogAssetSnapshot?>(null);
        public Task<CatalogStatistics> StatisticsAsync(UniverseContext c, CatalogFilter f, CancellationToken ct) => Task.FromResult(new CatalogStatistics(Total,
            [new("asset_type", "emblem", Total)], [new("production_profile", "painted_icon", Total)],
            [new("material", "steel", Total), new("planet", "gamma", Total)], [new("/engine/fuel", "blue", Total, CatalogTraitType.String), new("/enabled", "true", Total, CatalogTraitType.Boolean)]));
        public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => Task.FromResult(new CatalogPlanningReadModel(c.Id, [], []));
        public async Task<ThumbnailResult> ThumbnailAsync(UniverseContext c, CatalogAssetSummary a, CancellationToken ct)
        { ThumbnailCalls++; if (Pause is not null) await Pause(ct); ct.ThrowIfCancellationRequested(); return new("temporary-preview.png", 8, 10, false); }
    }
    private static (ExplorerViewModel Vm, LocalUniverseSettingsStore Settings) Create(CatalogTestFixture f, IExplorerService service, Picker? picker = null, IEnumerable<UniverseProfile>? profiles = null)
    {
        var store = new LocalUniverseSettingsStore(Path.Combine(f.Root, "local-settings.json")); store.Save([f.Context.Storage]);
        return (new(profiles ?? [f.Context.Profile], store, service, picker ?? new()), store);
    }
    [Fact] public async Task MissingConfigurationNavigatesToSettingsWithoutInventingRoots()
    {
        using var f = new CatalogTestFixture(); using var vm = new ExplorerViewModel([f.Context.Profile], new(Path.Combine(f.Root, "absent.json")), new FakeService(f.Context), new Picker());
        await vm.InitializeAsync(); Assert.Null(vm.Context); Assert.Equal("Ajustes", vm.Section); Assert.Equal("", vm.WorkspaceRoot); Assert.Equal(ExplorerState.Empty, vm.State);
    }
    [Fact] public async Task LocalSettingsBuildContextAndReadyState()
    { using var f = new CatalogTestFixture(); var (vm, _) = Create(f, new FakeService(f.Context)); using (vm) { await vm.InitializeAsync(); Assert.Equal(f.Context, vm.Context); Assert.Equal(ExplorerState.Ready, vm.State); Assert.Equal("Catálogo", vm.Section); Assert.Single(vm.Assets!.Cast<AssetTileViewModel>()); Assert.Empty(vm.Planning!.Objectives); Assert.Null(vm.Error); } }
    [Fact] public async Task EmptyStateAndNotificationsAreConsistent()
    { using var f = new CatalogTestFixture(); var (vm, _) = Create(f, new FakeService(f.Context) { Total = 0 }); using (vm) { var names = new List<string?>(); vm.PropertyChanged += (_, e) => names.Add(e.PropertyName); await vm.InitializeAsync(); Assert.Equal(ExplorerState.Empty, vm.State); Assert.Contains(nameof(vm.State), names); Assert.Contains(nameof(vm.Context), names); Assert.Contains(nameof(vm.StatusText), names); } }
    [Fact] public async Task ErrorsStopTheGridAndDoNotExposeExceptionTextPaths()
    { using var f = new CatalogTestFixture(); var (vm, _) = Create(f, new FakeService(f.Context) { Failure = new IOException("sensitive path /secret/root") }); using (vm) { await vm.InitializeAsync(); Assert.Equal(ExplorerState.Error, vm.State); Assert.Null(vm.Assets); Assert.NotNull(vm.Error); Assert.DoesNotContain("secret", vm.Error.Message); Assert.DoesNotContain("secret", vm.Error.Code); } }
    [Fact] public async Task CorruptSettingsAreVisibleAndCannotBeSavedOver()
    { using var f = new CatalogTestFixture(); var (vm, store) = Create(f, new FakeService(f.Context)); using (vm) { File.WriteAllText(store.SettingsPath, "broken"); await vm.InitializeAsync(); Assert.False(vm.SettingsReadable); Assert.Equal(ExplorerState.Error, vm.State); await vm.SaveSettings.ExecuteAsync(); Assert.Equal("broken", File.ReadAllText(store.SettingsPath)); } }
    [Fact] public async Task NavigationOnlyEnablesCurrentScope()
    { using var f = new CatalogTestFixture(); var (vm, _) = Create(f, new FakeService(f.Context)); using (vm) { await vm.InitializeAsync(); Assert.False(vm.Navigate.CanExecute("RoDo")); vm.Navigate.Execute("RoDo"); Assert.Equal("Catálogo", vm.Section); vm.Navigate.Execute("Estadísticas"); Assert.Equal("Estadísticas", vm.Section); vm.Navigate.Execute("Ajustes"); Assert.Equal("Ajustes", vm.Section); Assert.Equal(3, vm.Navigation.Count(n => n.Enabled)); } }
    [Fact] public async Task GenericFacetsCombineExactAndFiltersAndReset()
    {
        using var f = new CatalogTestFixture(); var service = new FakeService(f.Context); var (vm, _) = Create(f, service); using (vm)
        {
            await vm.InitializeAsync(); vm.AssetId = "emblem_example_00001"; vm.AssetType = "emblem"; vm.ProductionProfile = "painted_icon";
            foreach (var facet in vm.Facets) facet.Selected = facet.Choices.Last();
            await vm.ApplyFilters.ExecuteAsync(); var last = service.Requests.Last(); Assert.Equal(vm.AssetId, last.Filter.AssetId); Assert.Equal("steel", last.Filter.Classification["material"]); Assert.Equal("gamma", last.Filter.Classification["planet"]); Assert.Equal(2, last.Filter.Traits.Count); Assert.Contains(last.Filter.Traits, t => t.Key == "/enabled" && t.Type == CatalogTraitType.Boolean);
            await vm.ResetFilters.ExecuteAsync(); var clear = service.Requests.Last().Filter; Assert.Null(clear.AssetId); Assert.Null(clear.AssetType); Assert.Null(clear.ProductionProfile); Assert.Empty(clear.Classification); Assert.Empty(clear.Traits);
        }
    }
    [Fact] public async Task UniverseSwitchChangesExplicitContextAndClearsPriorData()
    {
        using var f = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); var service = new FakeService(f.Context); var (vm, store) = Create(f, service, profiles: [f.Context.Profile, b.Context.Profile]);
        store.Save([f.Context.Storage, b.Context.Storage]); using (vm)
        {
            await vm.InitializeAsync(); var old = vm.Assets; vm.AssetId = "old"; vm.SelectedUniverse = b.Context.Profile; await vm.UniverseChangeTask;
            Assert.Equal(b.Context, vm.Context); Assert.Equal(b.Context.Storage.ProductionRoot, vm.ProductionRoot); Assert.Null(vm.AssetId); Assert.Null(vm.Detail); Assert.NotSame(old, vm.Assets); Assert.Equal(b.Context.Id, service.Requests.Last().Universe);
        }
    }
    [Fact] public async Task LoadingAndCancellationOnSwitchIgnoreLateOldResults()
    {
        using var f = new CatalogTestFixture(); var service = new FakeService(f.Context) { Pause = ct => Task.Delay(Timeout.Infinite, ct) }; var (vm, _) = Create(f, service); using (vm)
        {
            var initialization = vm.InitializeAsync(); await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(ExplorerState.Loading, vm.State);
            vm.SelectedUniverse = null; await vm.UniverseChangeTask; await initialization; await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Null(vm.Context); Assert.Null(vm.Assets); Assert.Equal(ExplorerState.Empty, vm.State);
        }
    }
    [Fact] public async Task CommandRejectsConcurrentExecutionAndRecoversAfterError()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var runs = 0; Exception? error = null;
        var command = new AsyncCommand(async () => { runs++; await gate.Task; throw new IOException("failure"); }, e => error = e);
        var task = command.ExecuteAsync(); Assert.False(command.CanExecute(null)); await command.ExecuteAsync(); Assert.Equal(1, runs); gate.SetResult(); await task; Assert.True(command.CanExecute(null)); Assert.IsType<IOException>(error);
    }
    [Fact] public async Task SaveAndBrowseUseOnlyExplicitFolders()
    {
        using var f = new CatalogTestFixture(); var picker = new Picker { Next = f.Context.Storage.WorkspaceRoot }; var (vm, store) = Create(f, new FakeService(f.Context), picker); using (vm)
        { await vm.InitializeAsync(); vm.WorkspaceRoot = ""; vm.Browse.Execute("Workspace"); Assert.Equal(picker.Next, vm.WorkspaceRoot); await vm.SaveSettings.ExecuteAsync(); Assert.Equal(f.Context.Storage, store.Load().Single()); Assert.Equal(ExplorerState.Ready, vm.State); }
    }
    [Fact] public async Task DetailUsesRealDocumentsAndClearsOnSelectionRemoval()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (vm, _) = Create(f, new ExplorerService()); using (vm)
        {
            await vm.InitializeAsync(); await vm.SelectAsync(new CatalogExplorerReader(f.Context).Page().Items.Single()); Assert.Equal(ExplorerState.Ready, vm.DetailState); Assert.NotNull(vm.Detail); Assert.NotEmpty(vm.Documents); Assert.NotNull(vm.Preview);
            Assert.Equal(vm.Detail.Documents.Select(d => d.Role), vm.Documents.Select(d => d.Role)); await vm.SelectAsync(null); Assert.Null(vm.Detail); Assert.Null(vm.Preview); Assert.Empty(vm.Documents); Assert.Equal(ExplorerState.Empty, vm.DetailState);
        }
    }
    [Fact] public async Task AbsentDetailNeverCreatesFakeAssetFacts()
    { using var f = new CatalogTestFixture(); var service = new FakeService(f.Context); var (vm, _) = Create(f, service); using (vm) { await vm.InitializeAsync(); await vm.SelectAsync(service.Summary(0)); Assert.Equal(ExplorerState.Error, vm.DetailState); Assert.NotNull(vm.DetailError); Assert.Null(vm.Detail); Assert.Empty(vm.Documents); } }
    [Fact] public async Task FilterRefreshClearsDetailEvenWhenSelectedThroughTheServiceEntryPoint()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var (vm, _) = Create(f, new ExplorerService()); using (vm)
        {
            await vm.InitializeAsync(); await vm.SelectAsync(new CatalogExplorerReader(f.Context).Page().Items.Single()); Assert.NotNull(vm.Detail);
            await vm.ApplyFilters.ExecuteAsync(); Assert.Null(vm.Detail); Assert.Null(vm.Preview); Assert.Empty(vm.Documents); Assert.Equal(ExplorerState.Empty, vm.DetailState);
        }
    }
    [Fact] public async Task TileThumbnailIsLazyAndCancelledWhenRecycled()
    {
        using var f = new CatalogTestFixture(); var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var service = new FakeService(f.Context) { Pause = async ct => { gate.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } }; using var tile = new AssetTileViewModel(service, f.Context); tile.Populate(service.Summary(0)); Assert.Equal(0, service.ThumbnailCalls);
        var task = tile.ActivateAsync(); await gate.Task; tile.Deactivate(); await task; Assert.Equal(1, service.ThumbnailCalls); Assert.Null(tile.CachePath); Assert.Null(tile.Error);
    }
    [Fact] public async Task TileCanBeReactivatedAfterRecycling()
    { using var f = new CatalogTestFixture(); var service = new FakeService(f.Context); using var tile = new AssetTileViewModel(service, f.Context); tile.Populate(service.Summary(0)); await tile.ActivateAsync(); Assert.NotNull(tile.CachePath); tile.Deactivate(); Assert.Null(tile.CachePath); await tile.ActivateAsync(); Assert.NotNull(tile.CachePath); Assert.Equal(2, service.ThumbnailCalls); }
    [Fact] public void VirtualListOfThirtyThousandKeepsOnlyFourPagesAndNoThumbnails()
    {
        using var f = new CatalogTestFixture(); var service = new FakeService(f.Context) { Total = 30_000 };
        var first = new CatalogPage(30_000, 0, Enumerable.Range(0, 60).Select(i => service.Summary(i)).ToArray()); using var list = new PagedAssetCollection(service, f.Context, new(), first, _ => { });
        Assert.Equal(30_000, list.Count); Assert.Equal(1, list.CachedPages);
        foreach (var index in new[] { 200, 1500, 7000, 15_000, 29_999 }) Assert.IsType<AssetTileViewModel>(list[index]);
        Assert.Equal(4, list.CachedPages); Assert.Equal(0, service.ThumbnailCalls); Assert.Equal(5, service.Requests.Count); Assert.Throws<ArgumentOutOfRangeException>(() => list[-1]); Assert.Throws<ArgumentOutOfRangeException>(() => list[30_000]);
    }
    [Fact] public async Task VirtualPageErrorsAreReportedAndNotSilenced()
    { using var f = new CatalogTestFixture(); var service = new FakeService(f.Context) { Total = 1000, Failure = new IOException("missing") }; Exception? reported = null; using var list = new PagedAssetCollection(service, f.Context, new(), new(1000, 0, []), e => reported = e); _ = list[150]; await service.Entered.Task; Assert.IsType<IOException>(reported); Assert.NotNull(((AssetTileViewModel)list[150]!).Error); }
    [Fact] public async Task EvictedPendingPagesAreCancelledInsteadOfAccumulatingWork()
    {
        using var f = new CatalogTestFixture(); var service = new FakeService(f.Context) { Total = 30000, Pause = ct => Task.Delay(Timeout.Infinite, ct) };
        using var list = new PagedAssetCollection(service, f.Context, new(), new(30000, 0, []), _ => Assert.Fail("Cancellation is not a page error."));
        foreach (var index in new[] { 120, 180, 240, 300, 360 }) _ = list[index];
        await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(PagedAssetCollection.PageCapacity, list.CachedPages);
    }
    [Fact] public async Task RecycledVisualCanDetachAfterTileHasAlreadyBeenDisposed()
    { using var f = new CatalogTestFixture(); var s = new FakeService(f.Context); var tile = new AssetTileViewModel(s, f.Context); tile.Populate(s.Summary(0)); await tile.ActivateAsync(); tile.Dispose(); tile.Deactivate(); tile.Dispose(); await tile.ActivateAsync(); Assert.Null(tile.CachePath); Assert.Equal(1, s.ThumbnailCalls); }
}
