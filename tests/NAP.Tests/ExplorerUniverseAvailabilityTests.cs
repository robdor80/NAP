using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class ExplorerUniverseAvailabilityTests
{
    private sealed class Picker : IFolderPicker { public string? Pick(string title, string current) => null; }
    private static LocalUniverseSettingsStore Settings(CatalogTestFixture a, CatalogTestFixture b)
    {
        var store = new LocalUniverseSettingsStore(Path.Combine(a.Root, "settings.json"));
        store.Save([a.Context.Storage, b.Context.Storage]); return store;
    }
    private static ExplorerViewModel View(CatalogTestFixture a, CatalogTestFixture b, LocalUniverseSettingsStore store) =>
        new([a.Context.Profile, b.Context.Profile], store, new ExplorerService(), new Picker());
    private static void Opened(ExplorerViewModel vm, CatalogTestFixture f)
    {
        Assert.Equal(f.Context, vm.Context); Assert.Equal("Catálogo", vm.Section); Assert.Null(vm.Error);
        Assert.NotNull(vm.Assets); Assert.NotNull(vm.Statistics); Assert.NotNull(vm.Planning);
        Assert.True(vm.State is ExplorerState.Ready or ExplorerState.Empty);
    }
    [Fact]
    public async Task TwoAvailableUniversesCanBothOpenTheirRealCatalogs()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Publish(automatic: true); b.Publish(automatic: true); var store = Settings(a, b);
        using var vm = View(a, b, store); await vm.InitializeAsync(); Opened(vm, a);
        Assert.Equal(a.Context.Id, Assert.IsType<AssetTileViewModel>(vm.Assets![0]).Asset!.AssetKey.UniverseId);
        vm.SelectedUniverse = b.Context.Profile; await vm.UniverseChangeTask; Opened(vm, b);
        Assert.Equal(b.Context.Id, Assert.IsType<AssetTileViewModel>(vm.Assets![0]).Asset!.AssetKey.UniverseId);
    }
    [Fact]
    public async Task OfflineSiblingDoesNotBlockGridFiltersStatisticsOrSettingsSave()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Publish(automatic: true); b.Catalog.Initialize(); var store = Settings(a, b); Directory.Delete(b.Context.Storage.ArchiveRoot);
        using var vm = View(a, b, store); await vm.InitializeAsync(); Opened(vm, a);
        Assert.Single(vm.Assets!); Assert.Equal(1, vm.Statistics!.TotalAssets);
        vm.AssetType = "emblem"; await vm.ApplyFilters.ExecuteAsync(); Opened(vm, a);
        Assert.Single(vm.Assets!); Assert.Equal(1, vm.Statistics!.TotalAssets);
        vm.AssetId = "emblem_absent_001"; await vm.ApplyFilters.ExecuteAsync(); Opened(vm, a);
        Assert.Empty(vm.Assets!); Assert.Equal(0, vm.Statistics!.TotalAssets);
        await vm.ResetFilters.ExecuteAsync(); Opened(vm, a); Assert.Single(vm.Assets!);
        vm.WorkspaceRoot = Path.Combine(a.Context.Storage.WorkspaceRoot, ".");
        await vm.SaveSettings.ExecuteAsync(); Opened(vm, a);
        Assert.Equal(a.Context.Storage, store.Load().Single(r => r.UniverseId == a.Context.Id));
        Assert.Equal(b.Context.Storage, store.Load().Single(r => r.UniverseId == b.Context.Id));
        Assert.False(Directory.Exists(b.Context.Storage.ArchiveRoot));
    }
    [Fact]
    public async Task SelectingOfflineSiblingStopsAndItCanReopenAfterAvailabilityReturns()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Catalog.Initialize(); b.Catalog.Initialize(); var store = Settings(a, b); var prior = File.ReadAllBytes(store.SettingsPath);
        Directory.Delete(b.Context.Storage.ArchiveRoot);
        using var vm = View(a, b, store); await vm.InitializeAsync(); Opened(vm, a);
        vm.SelectedUniverse = b.Context.Profile; await vm.UniverseChangeTask;
        Assert.Equal(ExplorerState.Error, vm.State); Assert.Equal("Ajustes", vm.Section);
        Assert.Null(vm.Context); Assert.Null(vm.Assets); Assert.Null(vm.Statistics); Assert.NotNull(vm.Error);
        Assert.False(Directory.Exists(b.Context.Storage.ArchiveRoot));
        // The test restores its own empty root; the application must not create it.
        Directory.CreateDirectory(b.Context.Storage.ArchiveRoot); await vm.SwitchAsync(); Opened(vm, b);
        Assert.Equal(prior, File.ReadAllBytes(store.SettingsPath));
    }
    [Fact]
    public void ChangedActiveRootsCanSaveWhileUnchangedSiblingIsOffline()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        var store = Settings(a, b); Directory.Delete(b.Context.Storage.ArchiveRoot);
        var workspace = Directory.CreateDirectory(Path.Combine(a.Root, "new-workspace")).FullName;
        var changed = new UniverseStorageConfig(a.Context.Id, workspace, a.Context.Storage.ProductionRoot, a.Context.Storage.ArchiveRoot);
        store.Save([changed, b.Context.Storage], a.Context.Id);
        Assert.Equal(changed, store.Load().Single(r => r.UniverseId == a.Context.Id));
        Assert.Equal(b.Context.Storage, store.Load().Single(r => r.UniverseId == b.Context.Id));
        Assert.False(Directory.Exists(b.Context.Storage.ArchiveRoot));
    }
    [Fact]
    public void OverlapWithOfflineSiblingStillStopsLoadAndSave()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        var store = Settings(a, b); Directory.Delete(b.Context.Storage.ArchiveRoot); var prior = File.ReadAllBytes(store.SettingsPath);
        var overlap = new UniverseStorageConfig(a.Context.Id, a.Context.Storage.WorkspaceRoot, a.Context.Storage.ProductionRoot,
            Path.Combine(b.Context.Storage.ArchiveRoot, "nested"));
        Assert.Throws<InvalidDataException>(() => store.Save([overlap, b.Context.Storage], a.Context.Id));
        Assert.Equal(prior, File.ReadAllBytes(store.SettingsPath));
        File.WriteAllText(store.SettingsPath, File.ReadAllText(store.SettingsPath).Replace(a.Context.Storage.ArchiveRoot.Replace("\\", "\\\\"),
            overlap.ArchiveRoot.Replace("\\", "\\\\"), StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => store.Load());
    }
    [Theory] [InlineData("Workspace")] [InlineData("Production")] [InlineData("Archive")]
    public async Task MissingActiveRootStopsActivationAndSaveEvenWhenConfigurationIsUnchanged(string root)
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Catalog.Initialize(); b.Catalog.Initialize(); var store = Settings(a, b); var prior = File.ReadAllBytes(store.SettingsPath);
        var path = root switch { "Workspace" => a.Context.Storage.WorkspaceRoot, "Production" => a.Context.Storage.ProductionRoot, _ => a.Context.Storage.ArchiveRoot };
        Directory.Move(path, path + "-offline");
        using var vm = View(a, b, store); await vm.InitializeAsync();
        Assert.Equal(ExplorerState.Error, vm.State); Assert.Equal("Ajustes", vm.Section); Assert.Null(vm.Context);
        await vm.SaveSettings.ExecuteAsync(); Assert.Equal(ExplorerState.Error, vm.State);
        Assert.ThrowsAny<Exception>(() => store.Save([a.Context.Storage, b.Context.Storage], a.Context.Id));
        Assert.Equal(prior, File.ReadAllBytes(store.SettingsPath)); Assert.False(Directory.Exists(path));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NewOrChangedInvalidSiblingCannotBeIntroducedBySavingActiveUniverse(bool existing)
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        var store = new LocalUniverseSettingsStore(Path.Combine(a.Root, "settings.json"));
        store.Save(existing ? [a.Context.Storage, b.Context.Storage] : [a.Context.Storage]); var prior = File.ReadAllBytes(store.SettingsPath);
        var bad = new UniverseStorageConfig(b.Context.Id, b.Context.Storage.WorkspaceRoot, b.Context.Storage.ProductionRoot, Path.Combine(b.Root, "absent"));
        Assert.Throws<ArchiveStorageException>(() => store.Save([a.Context.Storage, bad], a.Context.Id));
        Assert.Equal(prior, File.ReadAllBytes(store.SettingsPath)); Assert.False(Directory.Exists(bad.ArchiveRoot));
    }
}
