using System.Text.Json;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseProfileImportTests
{
    [Fact] public void RodoPresentationPngPreservesPinnedDimensionsTransparencyAndDecodedPixels()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var png = Path.Combine(root, "src", "NAP.App", "Assets", "RoDo", "rodo_v0_1.png");
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(png);
        Assert.Equal(1024, image.Width); Assert.Equal(1024, image.Height); var pixels = new byte[image.Width * image.Height * 4]; image.CopyPixelDataTo(pixels);
        Assert.Equal("A5DF35D85C5173DC3B7C555C4EF02A323B0034077BF58EF26A76461EA99A9954", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        var alpha = Enumerable.Range(0, image.Width * image.Height).Select(i => pixels[i * 4 + 3]).ToArray(); Assert.Equal(0, alpha.Min()); Assert.Equal(255, alpha.Max());
    }
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nap-profiles-" + Guid.NewGuid().ToString("N"))).FullName;
        internal string BuiltIn => Path.Combine(Root, "built-in");
        internal string Local => Path.Combine(Root, "local", "universes");
        internal UniverseProfileStore Store => new(BuiltIn, Local);
        internal Fixture() { Write(Path.Combine(BuiltIn, "nimroel", "profile.json"), Profile("nimroel", "Nimroel")); }
        internal static string Profile(string id, string? name = null) => JsonSerializer.Serialize(new { schema_version = 1, universe_id = id, display_name = name ?? id, classification_dimensions = Array.Empty<string>(), asset_rules = Array.Empty<object>() });
        internal string Source(string json, string name = "profile.json") { var path = Path.Combine(Root, "source", name); Write(path, json); return path; }
        internal static void Write(string path, string contents) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); }
        internal Dictionary<string, string> Snapshot() => ArchiveTestFixture.Snapshot(Root);
        public void Dispose() { Directory.Delete(Root, recursive: true); }
    }
    [Fact] public void ValidImportPreservesExactBytesAndSurvivesRestartWithDeterministicOrder()
    {
        using var f = new Fixture(); var source = f.Source(Fixture.Profile("terra", "Terra")); var builtIn = File.ReadAllBytes(Path.Combine(f.BuiltIn, "nimroel", "profile.json"));
        var result = f.Store.Import(source, []); Assert.Equal("terra", result.Id.Value); Assert.Empty(result.Snapshot.Issues);
        Assert.Equal(new[] { "nimroel", "terra" }, result.Snapshot.Installed.Select(p => p.Id));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(f.Local, "terra", "profile.json")));
        Assert.False(result.Snapshot.Installed[0].Imported); Assert.True(result.Snapshot.Installed[1].Imported);
        f.Store.Import(f.Source(Fixture.Profile("alpha")), []);
        var restarted = new UniverseProfileStore(f.BuiltIn, f.Local).Discover([]);
        Assert.Equal(new[] { "nimroel", "alpha", "terra" }, restarted.Installed.Select(p => p.Id)); Assert.Empty(restarted.Issues);
        Assert.Equal(builtIn, File.ReadAllBytes(Path.Combine(f.BuiltIn, "nimroel", "profile.json")));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(f.Local), p => Path.GetFileName(p).StartsWith(".nap-import-", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"schema_version\":99,\"universe_id\":\"terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[]}")]
    [InlineData("{\"schema_version\":1,\"universe_id\":\"../terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[]}")]
    [InlineData("{\"schema_version\":1,\"universe_id\":\"Terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[]}")]
    [InlineData("{\"schema_version\":1,\"universe_id\":\"terra\",\"universe_id\":\"other\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[]}")]
    [InlineData("{\"schema_version\":1,\"universe_id\":\"terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":[],\"workspace_root\":\"/evil\"}")]
    [InlineData("{\"schema_version\":1,\"universe_id\":\"terra\",\"display_name\":\"Terra\",\"classification_dimensions\":[],\"asset_rules\":\"wrong\"}")]
    public void InvalidProfilesStopBeforeAnyPersistentChange(string json)
    {
        using var f = new Fixture(); var source = f.Source(json); var before = f.Snapshot();
        Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])); Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key)); Assert.False(Directory.Exists(f.Local));
    }
    [Theory][InlineData(0)][InlineData(UniverseProfileStore.MaxProfileBytes + 1)]
    public void EmptyAndOversizedSourcesStopWithoutCreatingTheStore(int length)
    {
        using var f = new Fixture(); var source = f.Source(new string(' ', length));
        Assert.Equal("profile_size_invalid", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])).Code); Assert.False(Directory.Exists(f.Local));
    }
    [Fact] public void WrongFileFormatAndDirectorySourcesStopWithoutChanges()
    {
        using var f = new Fixture(); var source = f.Source(Fixture.Profile("terra"), "profile.txt"); var before = f.Snapshot();
        Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])); Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(Path.GetDirectoryName(source)!, []));
        Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key));
    }
    [Theory][InlineData("nimroel")][InlineData("terra")]
    public void DuplicateBuiltInAndImportedIdsNeverOverwrite(string id)
    {
        using var f = new Fixture(); if (id == "terra") f.Store.Import(f.Source(Fixture.Profile(id)), []);
        var source = f.Source(Fixture.Profile(id, "Replacement")); var before = f.Snapshot();
        Assert.Equal("profile_duplicate", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])).Code);
        Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key));
    }
    [Theory][InlineData("workspace")][InlineData("production")][InlineData("archive")]
    public void LocalStoreCannotOverlapAnyUniverseRoot(string root)
    {
        using var f = new Fixture(); var source = f.Source(Fixture.Profile("terra"));
        var storage = new UniverseStorageConfig(new("nimroel"), root == "workspace" ? Path.GetDirectoryName(f.Local)! : Path.Combine(f.Root, "workspace"),
            root == "production" ? f.Local : Path.Combine(f.Root, "production"), root == "archive" ? Path.Combine(f.Local, "nested") : Path.Combine(f.Root, "archive"));
        var before = f.Snapshot(); Assert.Equal("profile_storage_overlap", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [storage])).Code);
        Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void StoreInCheckoutStopsIncludingGitFileWorktrees(bool gitFile)
    {
        using var f = new Fixture(); Directory.CreateDirectory(Path.GetDirectoryName(f.Local)!);
        var marker = Path.Combine(Path.GetDirectoryName(f.Local)!, ".git"); if (gitFile) File.WriteAllText(marker, "gitdir: elsewhere"); else Directory.CreateDirectory(marker);
        var source = f.Source(Fixture.Profile("terra")); var before = f.Snapshot();
        Assert.Equal("profile_checkout", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])).Code);
        Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key));
    }
    [Theory][InlineData("source")][InlineData("root")][InlineData("installed_directory")][InlineData("installed_file")]
    public void ReparseEntriesAreRejectedWithoutFollowingTheirTargets(string variant)
    {
        using var f = new Fixture(); var source = f.Source(Fixture.Profile("terra")); var target = Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName;
        string link;
        if (variant == "source") { link = Path.Combine(f.Root, "linked-source"); ArchiveTestFixture.Junction(link, Path.GetDirectoryName(source)!); source = Path.Combine(link, "profile.json"); }
        else if (variant == "root") { Directory.CreateDirectory(Path.GetDirectoryName(f.Local)!); link = f.Local; ArchiveTestFixture.Junction(link, target); }
        else
        {
            Directory.CreateDirectory(f.Local);
            if (variant == "installed_directory") { link = Path.Combine(f.Local, "terra"); ArchiveTestFixture.Junction(link, target); }
            else { var folder = Directory.CreateDirectory(Path.Combine(f.Local, "terra")).FullName; link = Path.Combine(folder, "profile.json"); if (OperatingSystem.IsWindows()) ArchiveTestFixture.Junction(link, target); else File.CreateSymbolicLink(link, source); }
        }
        try
        {
            if (variant is "root" or "source") Assert.Equal("profile_reparse", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])).Code);
            else { var snapshot = f.Store.Discover([]); Assert.Equal("nimroel", Assert.Single(snapshot.Installed).Id); Assert.Contains(snapshot.Issues, i => i.Code == "profile_reparse"); Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])); }
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally { if (variant == "installed_file" && !OperatingSystem.IsWindows()) File.Delete(link); else Directory.Delete(link); }
    }
    [Fact] public void CorruptLocalProfileAndDuplicateCannotHideOrReplaceNimroel()
    {
        using var f = new Fixture(); Fixture.Write(Path.Combine(f.Local, "terra", "profile.json"), "{broken"); Fixture.Write(Path.Combine(f.Local, "nimroel", "profile.json"), Fixture.Profile("nimroel", "Replacement"));
        var snapshot = f.Store.Discover([]); var nimroel = Assert.Single(snapshot.Installed);
        Assert.Equal("Nimroel", nimroel.Name); Assert.False(nimroel.Imported); Assert.Equal(2, snapshot.Issues.Count); Assert.All(snapshot.Issues, issue => Assert.Equal(UiTone.Error, issue.Tone));
        Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(f.Source(Fixture.Profile("terra")), []));
    }
    [Fact] public void CanonicalDirectoryIdMustMatchProfileAndNoRecursiveDiscoveryOccurs()
    {
        using var f = new Fixture(); Fixture.Write(Path.Combine(f.Local, "terra", "profile.json"), Fixture.Profile("other"));
        Fixture.Write(Path.Combine(f.Local, "nested", "deeper", "profile.json"), Fixture.Profile("hidden"));
        var result = f.Store.Discover([]); Assert.Single(result.Installed); Assert.Equal(2, result.Issues.Count);
        Assert.DoesNotContain(result.Installed, p => p.Id is "other" or "hidden");
    }
    [Fact] public void InventoryLimitStopsImportWithoutPublication()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Local);
        for (var i = 0; i <= UniverseProfileStore.MaxEntries; i++) Directory.CreateDirectory(Path.Combine(f.Local, "u" + i));
        var source = f.Source(Fixture.Profile("terra")); Assert.Contains(f.Store.Discover([]).Issues, i => i.Code == "profile_inventory_limit");
        Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(source, [])); Assert.False(Directory.Exists(Path.Combine(f.Local, "terra")));
    }
    [Fact] public void ConcurrentImportPublishesOnlyOneCompleteProfile()
    {
        using var f = new Fixture(); var source = f.Source(Fixture.Profile("terra"));
        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();
        Parallel.For(0, 2, _ => { try { f.Store.Import(source, []); results.Add(true); } catch (UniverseProfileStoreException error) { Assert.Equal("profile_duplicate", error.Code); results.Add(false); } });
        Assert.Equal(1, results.Count(ok => ok)); Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(Path.Combine(f.Local, "terra", "profile.json"))); Assert.Empty(f.Store.Discover([]).Issues);
    }
    [Theory][InlineData("con")][InlineData("nul")][InlineData("com1")]
    public void DeviceNameIdsCannotBecomeFilesystemPaths(string id)
    { using var f = new Fixture(); Assert.Equal("profile_path_invalid", Assert.Throws<UniverseProfileStoreException>(() => f.Store.Import(f.Source(Fixture.Profile(id)), [])).Code); Assert.False(Directory.Exists(f.Local)); }
    [Fact] public void ExplicitAbsoluteSeparateRootsAreRequired()
    {
        using var f = new Fixture(); Assert.Throws<UniverseProfileStoreException>(() => new UniverseProfileStore("relative", f.Local));
        Assert.Throws<UniverseProfileStoreException>(() => new UniverseProfileStore(f.BuiltIn, Path.Combine(f.BuiltIn, "local")));
    }
    private sealed class Picker(string? source = null) : IProfileFilePicker, IFolderPicker
    { public string? Pick() => source; public string? Pick(string title, string current) => null; }
    private sealed class Confirmation : IUserConfirmation
    { public Task<bool> ConfirmAsync(UiConfirmation plan, CancellationToken cancellation) => Task.FromResult(false); }
    private static ShellViewModel Shell(Fixture f, LocalUniverseSettingsStore settings, string? source = null)
    {
        var store = f.Store; var snapshot = store.Discover([]); var picker = new Picker(source);
        var explorer = new ExplorerViewModel(snapshot.Installed.Select(p => p.Profile), settings, new ExplorerService(), picker, store);
        return new(explorer, new ProfessionalUiService(), new Confirmation(), store, picker, snapshot);
    }
    [Fact] public async Task ImportIsAvailableWithoutRootsAndSelectorUpdatesWithoutRecompiling()
    {
        using var f = new Fixture(); var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); var source = f.Source(Fixture.Profile("terra", "Terra"));
        using var shell = Shell(f, settings, source); await shell.InitializeAsync(); Assert.True(shell.HasSingleUniverse); Assert.False(shell.HasUniverseSelector);
        Assert.True(shell.ImportProfile.CanExecute(null)); await shell.ImportProfile.ExecuteAsync();
        Assert.Equal("terra", shell.SelectedUniverse!.Id.Value); Assert.Null(shell.Context); Assert.Equal("Ajustes", shell.CurrentPage.Name);
        Assert.False(shell.HasSingleUniverse); Assert.True(shell.HasUniverseSelector); Assert.Equal(2, shell.Settings.InstalledProfiles.Count);
        Assert.Empty(shell.Explorer.WorkspaceRoot); Assert.Empty(shell.Explorer.ProductionRoot); Assert.Empty(shell.Explorer.ArchiveRoot); Assert.False(File.Exists(settings.SettingsPath));
        using var restarted = Shell(f, settings); await restarted.InitializeAsync(); Assert.Equal(2, restarted.Profiles.Count); Assert.Equal("nimroel", restarted.SelectedUniverse!.Id.Value);
    }
    [Fact] public async Task ImportedUniverseRootsAreIndependentAndSwitchingClearsOtherUniverseData()
    {
        using var f = new Fixture(); using var a = new CatalogTestFixture(nimroel: true); using var b = new CatalogTestFixture(universe: "terra");
        a.Catalog.Initialize(); b.Catalog.Initialize(); a.Catalog.SaveObjective(new(a.Context.Id, "alpha_goal", "Only Nimroel", new(), 1)); b.Catalog.SaveObjective(new(b.Context.Id, "beta_goal", "Only Terra", new(), 2));
        var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); settings.Save([a.Context.Storage]); var source = f.Source(Fixture.Profile("terra"));
        var aSources = a.Sources(); var aDatabase = File.ReadAllBytes(a.Catalog.CatalogPath);
        using var shell = Shell(f, settings, source); await shell.InitializeAsync(); shell.NavigateTo("Objetivos"); await shell.LastRefresh; Assert.Equal("alpha_goal", Assert.Single(shell.Objectives.Objectives).Id);
        await shell.ImportProfile.ExecuteAsync(); Assert.Empty(shell.Objectives.Objectives);
        shell.Explorer.WorkspaceRoot = b.Context.Storage.WorkspaceRoot; shell.Explorer.ProductionRoot = b.Context.Storage.ProductionRoot; shell.Explorer.ArchiveRoot = b.Context.Storage.ArchiveRoot;
        Assert.Equal(UiTone.Warning, shell.Explorer.WorkspaceStatus.Tone); await shell.SaveSettings.ExecuteAsync(); Assert.Equal(b.Context.Id, shell.Context!.Id);
        Assert.Equal(UiTone.Success, shell.Explorer.WorkspaceStatus.Tone); shell.NavigateTo("Objetivos"); await shell.LastRefresh; Assert.Equal("beta_goal", Assert.Single(shell.Objectives.Objectives).Id);
        shell.SelectedUniverse = shell.Profiles.Single(p => p.Id == a.Context.Id); await shell.LastRefresh; Assert.Equal("alpha_goal", Assert.Single(shell.Objectives.Objectives).Id);
        Assert.Equal(2, settings.Load().Count); a.AssertSources(aSources); Assert.Equal(aDatabase, File.ReadAllBytes(a.Catalog.CatalogPath));
    }
    [Fact] public async Task ImportInvalidProfileStopsAndLeavesInstalledProfilesAndSettingsIntact()
    {
        using var f = new Fixture(); var source = f.Source("{broken"); var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); var before = f.Snapshot();
        using var shell = Shell(f, settings, source); await shell.InitializeAsync(); await shell.ImportProfile.ExecuteAsync();
        Assert.Equal(UiPageState.Stopped, shell.Settings.State); Assert.Equal("profile_invalid", shell.Settings.Notice!.Code); Assert.Single(shell.Profiles);
        Assert.Equal(before.OrderBy(p => p.Key), f.Snapshot().OrderBy(p => p.Key));
    }
    [Fact] public async Task SavingRootsOverlappingProfileStoreStopsWithoutOverwritingSettings()
    {
        using var f = new Fixture(); using var a = new CatalogTestFixture(nimroel: true); a.Catalog.Initialize();
        var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); settings.Save([a.Context.Storage]); var before = File.ReadAllBytes(settings.SettingsPath);
        using var shell = Shell(f, settings); await shell.InitializeAsync(); shell.NavigateTo("Ajustes"); await shell.LastRefresh;
        shell.Explorer.WorkspaceRoot = Path.GetDirectoryName(f.Local)!; Assert.Equal(UiTone.Warning, shell.Explorer.WorkspaceStatus.Tone);
        await shell.SaveSettings.ExecuteAsync(); Assert.Equal("profile_storage_overlap", shell.Settings.Notice!.Code); Assert.Equal(UiTone.Error, shell.Explorer.WorkspaceStatus.Tone);
        Assert.Equal(before, File.ReadAllBytes(settings.SettingsPath));
    }
    [Fact] public async Task CorruptSettingsStayVisibleAndCannotBeOverwrittenThroughImport()
    {
        using var f = new Fixture(); var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); File.WriteAllText(settings.SettingsPath, "{broken"); var before = File.ReadAllBytes(settings.SettingsPath);
        using var shell = Shell(f, settings, f.Source(Fixture.Profile("terra"))); await shell.InitializeAsync();
        Assert.Equal(UiPageState.Stopped, shell.Settings.State); Assert.NotNull(shell.Settings.Notice); Assert.False(shell.ImportProfile.CanExecute(null)); Assert.Contains("no se puede leer", shell.ImportProfile.DisabledReason);
        Assert.Equal(UiTone.Error, shell.Explorer.WorkspaceStatus.Tone); await shell.ImportProfile.ExecuteAsync(); Assert.Equal(before, File.ReadAllBytes(settings.SettingsPath)); Assert.False(Directory.Exists(f.Local));
    }
    [Fact] public async Task EmptyStatesAndDisabledReasonsDistinguishUnknownFromVerifiedEmpty()
    {
        using var f = new Fixture(); var settings = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); using var shell = Shell(f, settings); await shell.InitializeAsync();
        Assert.Contains(shell.Navigation, n => n.Name == "Producción"); Assert.DoesNotContain(shell.Navigation, n => n.Name == "Pipeline");
        foreach (var page in new[] { "Objetivos", "Backups", "Historial", "Git" })
        { shell.NavigateTo(page); await shell.LastRefresh; Assert.Contains("Configura", shell.CurrentPage.EmptyMessage); }
        Assert.Equal("Catálogo no disponible", shell.Explorer.DetailTitle); Assert.Contains("Ajustes", shell.Explorer.DetailMessage); Assert.Equal("—", shell.Explorer.StatisticsAssets);
        Assert.Equal("Configura primero las raíces del universo.", shell.CreateBackup.DisabledReason); Assert.Contains("Fase 15", shell.Rodo.Availability); Assert.False(shell.Rodo.CanSend);
        using var a = new CatalogTestFixture(nimroel: true);
        var emptyStorage = new UniverseStorageConfig(a.Context.Id, Directory.CreateDirectory(Path.Combine(a.Root, "empty-workspace")).FullName, a.Context.Storage.ProductionRoot, a.Context.Storage.ArchiveRoot);
        new AssetCatalog(new UniverseContext(a.Context.Profile, emptyStorage)).Initialize(); shell.NavigateTo("Ajustes"); await shell.LastRefresh;
        shell.Explorer.WorkspaceRoot = emptyStorage.WorkspaceRoot; shell.Explorer.ProductionRoot = emptyStorage.ProductionRoot; shell.Explorer.ArchiveRoot = emptyStorage.ArchiveRoot; await shell.SaveSettings.ExecuteAsync();
        shell.NavigateTo("Catálogo"); await shell.LastRefresh; Assert.Equal("Sin selección", shell.Explorer.DetailTitle); Assert.Equal("Selecciona un asset del catálogo para consultar su ficha.", shell.Explorer.DetailMessage);
        Assert.True(shell.Explorer.Statistics is not null, "Catalog read failed: " + shell.Explorer.Error?.Code + " / " + shell.Explorer.Error?.Message); Assert.Equal("0", shell.Explorer.StatisticsAssets); Assert.Contains("No hay datos", shell.Explorer.DistributionEmptyMessage); Assert.Contains("No hay objetivos", shell.Explorer.CoverageEmptyMessage);
        shell.NavigateTo("Objetivos"); await shell.LastRefresh; Assert.Equal("No hay objetivos definidos para este universo.", shell.Objectives.EmptyMessage); Assert.Contains("identificador", shell.SaveObjective.DisabledReason);
        shell.NavigateTo("Backups"); await shell.LastRefresh; Assert.Equal("Todavía no hay copias verificadas para este universo.", shell.Backups.EmptyMessage); Assert.Null(shell.CreateBackup.DisabledReason); Assert.Equal("Selecciona una copia verificada.", shell.RestoreBackup.DisabledReason);
        shell.NavigateTo("Historial"); await shell.LastRefresh; Assert.Equal("No hay jobs persistidos para este universo.", shell.History.EmptyJobs); Assert.Equal("No hay operaciones Git registradas para este universo.", shell.History.EmptyReceipts); Assert.Equal("Todavía no hay actividad registrada en esta sesión.", shell.History.EmptyActivity);
        shell.NavigateTo("Git"); await shell.LastRefresh; Assert.Equal("No hay operaciones Git registradas para este universo.", shell.Git.EmptyMessage); Assert.Equal("No hay una operación Git seleccionada.", shell.PushGit.DisabledReason); Assert.Contains("outputs", shell.CommitGit.DisabledReason);
        shell.NavigateTo("Producción"); await shell.LastRefresh; Assert.Contains("Prepara", shell.AuditPackage.DisabledReason); Assert.Contains("Prepara", shell.ExecutePackage.DisabledReason);
        Assert.Contains(shell.Backups.Kinds, k => k.Label == "Base de datos (SQLite)");
    }
}
