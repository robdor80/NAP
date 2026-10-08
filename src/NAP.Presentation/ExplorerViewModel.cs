using System.Collections.ObjectModel;
using System.Text;
using NAP.Core;

namespace NAP.Presentation;

public interface IFolderPicker { string? Pick(string title, string current); }
public sealed record NavigationItem(string Name, bool Enabled);
public sealed record DocumentView(string Role, string Text);
public sealed record DistributionView(string Dimension, string Value, long Count, double Percent, string? ScalarType)
{ public string ScalarTypeLabel => ScalarType is null ? "" : " (" + ScalarType + ")"; }
public sealed record StorageRootStatus(string Text, UiTone Tone);
public sealed class FacetViewModel(string dimension, bool trait, IEnumerable<CatalogDistribution> values) : ObservableObject
{
    private CatalogDistribution? _selected;
    public string Dimension { get; } = dimension;
    public bool IsTrait { get; } = trait;
    public IReadOnlyList<CatalogDistribution?> Choices { get; } = Array.AsReadOnly(new CatalogDistribution?[] { null }.Concat(values).ToArray());
    public CatalogDistribution? Selected { get => _selected; set => Set(ref _selected, value); }
}

/// <summary>Explicit UI scope. All I/O goes through adapters; results guarded by cancellation/generation.</summary>
public sealed class ExplorerViewModel : ObservableObject, IDisposable
{
    private readonly LocalUniverseSettingsStore _settings;
    private readonly IExplorerService _service;
    private readonly IFolderPicker _folders;
    private readonly IUniverseProfileStore? _profileStore;
    private IReadOnlyList<UniverseProfile> _profiles;
    private UniverseStorageConfig? _validatedRoots;
    private bool _rootValidationStopped;
    private IReadOnlyList<UniverseStorageConfig> _configurations = [];
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _detailLoad;
    private UniverseProfile? _selectedUniverse;
    private UniverseContext? _context;
    private ExplorerState _state = ExplorerState.Empty;
    private ExplorerState _detailState = ExplorerState.Empty;
    private ExplorerError? _error;
    private ExplorerError? _detailError;
    private string _section = "Ajustes";
    private string _workspace = "", _production = "", _archive = "";
    private string? _assetId, _assetType, _productionProfile;
    private PagedAssetCollection? _assets;
    private AssetTileViewModel? _selectedAsset;
    private AssetTileViewModel? _preview;
    private CatalogAssetSnapshot? _detail;
    private CatalogStatistics? _statistics;
    private CatalogPlanningReadModel? _planning;
    private bool _settingsReadable = true;
    private long _generation;
    public ExplorerViewModel(IEnumerable<UniverseProfile> profiles, LocalUniverseSettingsStore settings, IExplorerService service, IFolderPicker folders, IUniverseProfileStore? profileStore = null)
    {
        _profiles = Array.AsReadOnly(profiles.ToArray());
        if (Profiles.Select(p => p.Id).Distinct().Count() != Profiles.Count) throw new ArgumentException("Duplicate profiles.", nameof(profiles));
        _settings = settings; _service = service; _folders = folders; _profileStore = profileStore;
        ApplyFilters = new(RefreshAsync, Fail); ResetFilters = new(ClearAsync, Fail);
        SaveSettings = new(SaveAsync, Fail);
        Navigate = new(p => { Section = (string)p!; }, p => Navigation.Any(n => n.Name == p as string && n.Enabled));
        Browse = new(p =>
        {
            var field = p as string; var current = field switch { "Workspace" => WorkspaceRoot, "Production" => ProductionRoot, _ => ArchiveRoot };
            var picked = _folders.Pick(field switch { "Workspace" => "Seleccionar Workspace existente", "Production" => "Seleccionar Producción existente", _ => "Seleccionar Archivo de maestros existente" }, current);
            if (picked is null) return;
            if (field == "Workspace") WorkspaceRoot = picked; else if (field == "Production") ProductionRoot = picked; else ArchiveRoot = picked;
        });
    }
    public IReadOnlyList<UniverseProfile> Profiles => _profiles;
    public IReadOnlyList<UniverseStorageConfig> Configurations => _configurations;
    public void InstallProfiles(IEnumerable<UniverseProfile> profiles)
    {
        var validated = new UniverseRegistry(profiles).Profiles;
        _profiles = Array.AsReadOnly(validated.Select(p => Profiles.FirstOrDefault(old => old.Id == p.Id) ?? p).ToArray());
        Notify(nameof(Profiles));
    }
    public IReadOnlyList<NavigationItem> Navigation { get; } = Array.AsReadOnly(new[]
    { new NavigationItem("Inicio", false), new("Producción", false), new("Catálogo", true), new("Objetivos", false), new("Estadísticas", true),
        new("Backups", false), new("Historial", false), new("RoDo", false), new("Ajustes", true) });
    public UniverseProfile? SelectedUniverse
    {
        get => _selectedUniverse;
        set { if (Set(ref _selectedUniverse, value)) UniverseChangeTask = SwitchAsync(); }
    }
    public Task UniverseChangeTask { get; private set; } = Task.CompletedTask;
    public UniverseContext? Context => _context;
    public string Section { get => _section; private set => Set(ref _section, value); }
    public string WorkspaceRoot { get => _workspace; set { if (Set(ref _workspace, value)) InvalidateRootStatus(); } }
    public string ProductionRoot { get => _production; set { if (Set(ref _production, value)) InvalidateRootStatus(); } }
    public string ArchiveRoot { get => _archive; set { if (Set(ref _archive, value)) InvalidateRootStatus(); } }
    public StorageRootStatus WorkspaceStatus => RootStatus(WorkspaceRoot);
    public StorageRootStatus ProductionStatus => RootStatus(ProductionRoot);
    public StorageRootStatus ArchiveStatus => RootStatus(ArchiveRoot);
    public string SettingsLocation => _settings.SettingsPath;
    public bool SettingsReadable => _settingsReadable;
    public string? AssetId { get => _assetId; set => Set(ref _assetId, value); }
    public string? AssetType { get => _assetType; set => Set(ref _assetType, value); }
    public string? ProductionProfile { get => _productionProfile; set => Set(ref _productionProfile, value); }
    public ObservableCollection<string> AssetTypes { get; } = [];
    public ObservableCollection<string> ProductionProfiles { get; } = [];
    public ObservableCollection<FacetViewModel> Facets { get; } = [];
    public ObservableCollection<DistributionView> Distributions { get; } = [];
    public ObservableCollection<DocumentView> Documents { get; } = [];
    public ExplorerState State { get => _state; private set { Set(ref _state, value); NotifyCatalogText(); } }
    public ExplorerState DetailState { get => _detailState; private set { Set(ref _detailState, value); NotifyCatalogText(); } }
    public ExplorerError? Error { get => _error; private set => Set(ref _error, value); }
    public ExplorerError? DetailError { get => _detailError; private set => Set(ref _detailError, value); }
    public string StatusText => State switch { ExplorerState.Loading => "Cargando catálogo…", ExplorerState.Empty => "Sin assets. Configura las raíces o ajusta los filtros.", ExplorerState.Error => "Lectura detenida. Consulta el diagnóstico.", _ => $"{Assets?.Count ?? 0:N0} assets · solo lectura" };
    public string CatalogStatus => Context is null ? "Estado: sin catálogo disponible" : State switch { ExplorerState.Loading => "Estado: consultando catálogo", ExplorerState.Error => "Estado: lectura detenida", ExplorerState.Empty => Statistics is null ? "Estado: sin catálogo disponible" : "Estado: catálogo sin assets para estos filtros", _ => "Estado: catálogo disponible" };
    private string UnavailableText => Error?.Message ?? (Context is null ? "Configura las raíces del universo en Ajustes." : State == ExplorerState.Loading ? "Consultando el catálogo…" : "No hay una lectura del catálogo disponible.");
    public string DetailTitle => Context is null ? "Catálogo no disponible" : DetailState switch { ExplorerState.Loading => "Consultando ficha", ExplorerState.Error => "Ficha no disponible", _ => Detail is null ? "Sin selección" : "Ficha del asset" };
    public string DetailMessage => Context is null ? UnavailableText : DetailState == ExplorerState.Loading ? "Consultando la ficha del asset…" : DetailState == ExplorerState.Error ? DetailError?.Message ?? "No se puede consultar la ficha." : "Selecciona un asset del catálogo para consultar su ficha.";
    public string DetailStatusText => DetailState == ExplorerState.Ready ? "Ficha disponible" : "";
    public string StatisticsAssets => Statistics?.TotalAssets.ToString("N0") ?? "—";
    public string StatisticsTypes => Statistics?.AssetTypes.Count.ToString("N0") ?? "—";
    public string StatisticsProfiles => Statistics?.ProductionProfiles.Count.ToString("N0") ?? "—";
    public string? DistributionEmptyMessage => Statistics is null ? UnavailableText : Distributions.Count == 0 ? "No hay datos de distribución para los filtros activos." : null;
    public string? CoverageEmptyMessage => Planning is null ? UnavailableText : Planning.Objectives.Count == 0 ? "No hay objetivos definidos para este universo." : null;
    public string? CatalogEmptyMessage => Assets is null ? UnavailableText : Assets.Count == 0 ? "No hay assets para los filtros activos." : null;
    public PagedAssetCollection? Assets { get => _assets; private set { if (_assets == value) return; _assets?.Dispose(); _assets = value; Notify(); Notify(nameof(StatusText)); } }
    public AssetTileViewModel? SelectedAsset { get => _selectedAsset; set { if (Set(ref _selectedAsset, value)) _ = SelectAsync(value?.Asset); } }
    public AssetTileViewModel? Preview { get => _preview; private set { _preview?.Dispose(); _preview = value; Notify(); } }
    public CatalogAssetSnapshot? Detail { get => _detail; private set { Set(ref _detail, value); NotifyCatalogText(); } }
    public CatalogStatistics? Statistics { get => _statistics; private set { Set(ref _statistics, value); NotifyCatalogText(); } }
    public CatalogPlanningReadModel? Planning { get => _planning; private set { Set(ref _planning, value); NotifyCatalogText(); } }
    public AsyncCommand ApplyFilters { get; }
    public AsyncCommand ResetFilters { get; }
    public AsyncCommand SaveSettings { get; }
    public RelayCommand Navigate { get; }
    public RelayCommand Browse { get; }
    public async Task InitializeAsync()
    {
        try { _configurations = await Task.Run(_settings.Load); }
        catch (Exception ex) { _settingsReadable = false; Notify(nameof(SettingsReadable)); NotifyRoots(); Fail(ex); return; }
        if (Profiles.Count > 0) { _selectedUniverse = Profiles[0]; Notify(nameof(SelectedUniverse)); await SwitchAsync(); }
    }
    public async Task SwitchAsync()
    {
        CancelScope(); _context = null; Notify(nameof(Context)); ResetValues();
        var profile = SelectedUniverse; var roots = _configurations.SingleOrDefault(c => c.UniverseId == profile?.Id);
        WorkspaceRoot = roots?.WorkspaceRoot ?? ""; ProductionRoot = roots?.ProductionRoot ?? ""; ArchiveRoot = roots?.ArchiveRoot ?? "";
        if (profile is null || roots is null) { State = ExplorerState.Empty; Section = "Ajustes"; return; }
        try
        {
            await Task.Run(() => { _profileStore?.ValidateStorage(_configurations); LocalUniverseSettingsStore.ValidateStructure(_configurations); LocalUniverseSettingsStore.ValidateAvailable(roots); });
            if (SelectedUniverse != profile) return;
            _validatedRoots = roots; _rootValidationStopped = false; NotifyRoots();
            _context = new(profile, roots); Notify(nameof(Context)); Section = "Catálogo"; await RefreshAsync(loadFacets: true);
        }
        catch (Exception ex) { if (SelectedUniverse == profile) { _rootValidationStopped = true; NotifyRoots(); Fail(ex); Section = "Ajustes"; } }
    }
    public CatalogFilter CurrentFilter() => new(Null(AssetId), Null(AssetType), Null(ProductionProfile),
        Facets.Where(f => !f.IsTrait && f.Selected is not null).ToDictionary(f => f.Dimension, f => f.Selected!.Value, StringComparer.Ordinal),
        Facets.Where(f => f.IsTrait && f.Selected is not null).Select(f => new CatalogVisualTrait(f.Dimension, f.Selected!.Value, f.Selected!.TraitType!.Value)));
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public Task RefreshAsync() => RefreshAsync(false);
    public void CancelRead()
    {
        ++_generation; _load?.Cancel(); _detailLoad?.Cancel();
        if (State == ExplorerState.Loading) State = ExplorerState.Empty;
        if (DetailState == ExplorerState.Loading) DetailState = ExplorerState.Empty;
    }
    private async Task RefreshAsync(bool loadFacets)
    {
        _load?.Cancel(); _load?.Dispose(); var source = _load = new(); var generation = ++_generation; var context = _context;
        _detailLoad?.Cancel(); Detail = null; Preview = null; Documents.Clear(); DetailError = null; DetailState = ExplorerState.Empty;
        Assets = null; SelectedAsset = null; Statistics = null; Planning = null; Distributions.Clear(); Error = null;
        if (context is null) { State = ExplorerState.Empty; return; }
        State = ExplorerState.Loading;
        try
        {
            if (loadFacets) await _service.BeginSessionAsync(context, source.Token);
            var filter = CurrentFilter();
            var page = await _service.PageAsync(context, filter, 0, PagedAssetCollection.PageSize, source.Token);
            var statistics = await _service.StatisticsAsync(context, filter, source.Token);
            var planning = await _service.PlanningAsync(context, source.Token);
            CatalogStatistics? facets = null;
            if (loadFacets) facets = await _service.StatisticsAsync(context, new(), source.Token);
            if (source.IsCancellationRequested || generation != _generation) return;
            if (facets is not null) SetFacets(facets);
            Assets = new(_service, context, filter, page, Fail); Statistics = statistics; Planning = planning;
            foreach (var group in new[] { statistics.AssetTypes, statistics.ProductionProfiles, statistics.Classifications, statistics.Traits })
                foreach (var d in group) Distributions.Add(new(d.Dimension, d.Value, d.Count, statistics.TotalAssets == 0 ? 0 : 100d * d.Count / statistics.TotalAssets, d.TraitType?.ToString()));
            State = page.TotalCount == 0 ? ExplorerState.Empty : ExplorerState.Ready;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == _generation && !source.IsCancellationRequested) Fail(ex); }
    }
    private void SetFacets(CatalogStatistics facts)
    {
        AssetTypes.Clear(); ProductionProfiles.Clear(); Facets.Clear(); AssetTypes.Add(""); ProductionProfiles.Add("");
        foreach (var d in facts.AssetTypes) AssetTypes.Add(d.Value);
        foreach (var d in facts.ProductionProfiles) ProductionProfiles.Add(d.Value);
        foreach (var group in facts.Classifications.GroupBy(d => d.Dimension)) Facets.Add(new(group.Key, false, group));
        foreach (var group in facts.Traits.GroupBy(d => d.Dimension)) Facets.Add(new(group.Key, true, group));
    }
    public async Task SelectAsync(CatalogAssetSummary? asset)
    {
        _detailLoad?.Cancel(); _detailLoad?.Dispose(); var source = _detailLoad = new(); var context = _context;
        Detail = null; Documents.Clear(); Preview = null; DetailError = null;
        if (asset is null || context is null) { DetailState = ExplorerState.Empty; return; }
        DetailState = ExplorerState.Loading;
        try
        {
            var detail = await _service.DetailAsync(context, asset.AssetKey.AssetId, source.Token);
            if (source.IsCancellationRequested || context != _context) return;
            if (detail is null) throw new InvalidDataException("Selected asset is no longer cataloged.");
            Detail = detail; Preview = new(_service, context); Preview.Populate(asset);
            foreach (var document in detail.Documents) Documents.Add(new(document.Role, Encoding.UTF8.GetString(document.ToArray())));
            DetailState = ExplorerState.Ready;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!source.IsCancellationRequested) { DetailError = ExplorerError.From(ex); DetailState = ExplorerState.Error; } }
    }
    private async Task SaveAsync()
    {
        if (!_settingsReadable || SelectedUniverse is null) throw new InvalidOperationException("Settings are unreadable or no profile is selected.");
        var storage = new UniverseStorageConfig(SelectedUniverse.Id, WorkspaceRoot, ProductionRoot, ArchiveRoot);
        var rows = _configurations.Where(c => c.UniverseId != storage.UniverseId).Append(storage).ToArray();
        try { await Task.Run(() => { _profileStore?.ValidateStorage(rows); _settings.Save(rows, storage.UniverseId); }); }
        catch { _rootValidationStopped = true; NotifyRoots(); throw; }
        _configurations = Array.AsReadOnly(rows); await SwitchAsync();
    }
    private Task ClearAsync() { ResetValues(); return RefreshAsync(); }
    private void ResetValues() { AssetId = null; AssetType = null; ProductionProfile = null; foreach (var f in Facets) f.Selected = null; }
    private void Fail(Exception ex)
    {
        _load?.Cancel(); _detailLoad?.Cancel(); Assets = null; Preview = null;
        Detail = null; Documents.Clear(); Statistics = null; Planning = null; Distributions.Clear();
        Error = ExplorerError.From(ex); State = ExplorerState.Error;
    }
    private void CancelScope()
    {
        _validatedRoots = null; _rootValidationStopped = false; NotifyRoots();
        ++_generation; _load?.Cancel(); _detailLoad?.Cancel(); Assets = null; SelectedAsset = null;
        Detail = null; Preview = null; Documents.Clear(); Facets.Clear(); AssetTypes.Clear(); ProductionProfiles.Clear();
        DetailError = null; DetailState = ExplorerState.Empty;
        Statistics = null; Planning = null; Distributions.Clear(); Error = null;
    }
    private void InvalidateRootStatus() { _validatedRoots = null; _rootValidationStopped = false; NotifyRoots(); }
    private StorageRootStatus RootStatus(string path)
    {
        if (!SettingsReadable) return new("STOP · configuración local ilegible", UiTone.Error);
        if (string.IsNullOrWhiteSpace(path)) return new("⚠ no configurada", UiTone.Warning);
        if (_rootValidationStopped) return new("STOP · validación de raíces detenida", UiTone.Error);
        return _validatedRoots is not null && _validatedRoots.WorkspaceRoot == WorkspaceRoot && _validatedRoots.ProductionRoot == ProductionRoot && _validatedRoots.ArchiveRoot == ArchiveRoot
            ? new("✓ válida · comprobada", UiTone.Success) : new("⚠ pendiente de validación", UiTone.Warning);
    }
    private void NotifyRoots() { Notify(nameof(WorkspaceStatus)); Notify(nameof(ProductionStatus)); Notify(nameof(ArchiveStatus)); }
    private void NotifyCatalogText()
    {
        foreach (var property in new[] { nameof(StatusText), nameof(CatalogStatus), nameof(DetailTitle), nameof(DetailMessage), nameof(DetailStatusText), nameof(StatisticsAssets), nameof(StatisticsTypes), nameof(StatisticsProfiles), nameof(DistributionEmptyMessage), nameof(CoverageEmptyMessage), nameof(CatalogEmptyMessage) }) Notify(property);
    }
    public void Dispose() { CancelScope(); _load?.Dispose(); _detailLoad?.Dispose(); }
}
