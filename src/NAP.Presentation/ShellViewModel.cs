using System.ComponentModel;
using NAP.Core;

namespace NAP.Presentation;

/// <summary>Desktop session. Scope/generation protect every result; mutations retain their typed Core plans and explicit confirmation.</summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IProfessionalUiService _service;
    private readonly IUserConfirmation _confirmation;
    private readonly IUniverseProfileStore? _profilesStore;
    private readonly IProfileFilePicker? _profilePicker;
    private readonly List<UiAsyncCommand> _commands = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _read;
    private long _generation;
    private bool _executing, _disposed, _trayOnMinimize, _scopePending, _initializing;
    private ProfessionalPage _current;
    private readonly List<ProductionAssetResult> _verified = [];
    public ShellViewModel(ExplorerViewModel explorer, IProfessionalUiService service, IUserConfirmation confirmation,
        IUniverseProfileStore? profilesStore = null, IProfileFilePicker? profilePicker = null, UniverseProfilesSnapshot? profilesSnapshot = null)
    {
        Explorer = explorer; _service = service; _confirmation = confirmation; _profilesStore = profilesStore; _profilePicker = profilePicker;
        Catalog = new(explorer); Statistics = new(explorer); Settings = new(explorer); _current = Dashboard;
        Settings.ProfilesLocation = profilesStore?.LocalRoot;
        if (profilesSnapshot is not null) ApplyProfiles(profilesSnapshot);
        Navigation = new[] { new ShellNavigationItem("Inicio", "\uE80F"), new("Producción", "\uE895"), new("Catálogo", "\uE8F1"),
            new("Objetivos", "\uE81D"), new("Estadísticas", "\uE9D9"), new("Backups", "\uE74E"), new("Historial", "\uE81C"), new("RoDo", "\uE899"), new("Ajustes", "\uE713") };
        Navigate = new(p => NavigateTo((string)p!), p => !_disposed && !IsExecuting && (p as string == "Git" || Navigation.Any(n => n.Name == p as string)));
        Refresh = Command(RefreshCurrentAsync, () => !IsExecuting);
        SaveSettings = Command(async () =>
        {
            _read?.Cancel(); ++_generation; IsExecuting = true;
            try { await Explorer.SaveSettings.ExecuteAsync(); }
            finally { IsExecuting = false; }
            if (_disposed) return;
            _scopePending = false;
            if (Explorer.Error is { } error) { Settings.Notice = new(error.Message, error.Code, UiTone.Error); Settings.State = UiPageState.Stopped; }
            else await RefreshCurrentAsync();
        }, () => !IsExecuting && Explorer.SettingsReadable && SelectedUniverse is not null && new[] { Explorer.WorkspaceRoot, Explorer.ProductionRoot, Explorer.ArchiveRoot }.All(Path.IsPathFullyQualified));
        ImportProfile = Command(async () =>
        {
            var path = _profilePicker!.Pick(); if (path is null) return;
            _read?.Cancel(); ++_generation; IsExecuting = true;
            try
            {
                var storage = Explorer.Configurations.ToArray();
                var result = await Task.Run(() => _profilesStore!.Import(path, storage), _lifetime.Token);
                if (_disposed) return;
                ApplyProfiles(result.Snapshot); Explorer.InstallProfiles(result.Snapshot.Installed.Select(p => p.Profile));
                Explorer.SelectedUniverse = Profiles.Single(p => p.Id == result.Id); await Explorer.UniverseChangeTask;
                _scopePending = false; Activate(Settings); Settings.State = Context is null ? UiPageState.Unconfigured : UiPageState.Ready;
                Settings.Notice = new("Perfil instalado. Configura sus tres raíces existentes para utilizar el universo.", "profile_installed", UiTone.Neutral);
            }
            finally { IsExecuting = false; }
        }, () => !IsExecuting && Explorer.SettingsReadable && _profilesStore is not null && _profilePicker is not null);
        CancelRead = new(_ => { _read?.Cancel(); if (CurrentPage == Catalog || CurrentPage == Statistics) Explorer.CancelRead(); CurrentPage.State = Context is null ? UiPageState.Unconfigured : UiPageState.Cancelled; StatusChanged(); }, _ => !_disposed && !IsExecuting && CurrentPage.State == UiPageState.Loading);
        PreparePackage = Command(() => ActionAsync(async (c, ct) =>
        { var candidate = Pipeline.SelectedCandidate!; Pipeline.Prepared = null; Pipeline.Audit = null; var value = await _service.PrepareAsync(c, candidate, ct); if (!Active(c)) return; if (value.Context != c) throw new InvalidDataException("Prepared context mismatch."); Pipeline.Prepared = value; Record(c, "Paquete preparado hasta PLANNED", UiTone.Neutral); }), () => Writable(Pipeline) && Pipeline.SelectedCandidate is not null);
        AuditPackage = Command(() => ActionAsync(async (c, ct) =>
        { var audit = await _service.AuditAsync(c, Pipeline.Prepared!, ct); if (!Active(c)) return; Pipeline.Audit = audit; Record(c, "Auditoría: " + audit.Decision, audit.Passed ? UiTone.Success : audit.ShouldStop ? UiTone.Error : UiTone.Warning); }), () => Writable(Pipeline) && Pipeline.Prepared is not null && _service.AuditAvailable);
        ExecutePackage = Command(() => ActionAsync(async (c, ct) =>
        {
            var prepared = Pipeline.Prepared!; var audit = Pipeline.Audit!;
            if (!await Confirm(c, "Publicar asset", "Core verificará Archivo, producción y catálogo antes de COMPLETED.", [prepared.Summary, "Job: " + prepared.JobId.Value, "Auditoría PASS real"], ct)) return;
            var result = await _service.ExecuteAsync(c, prepared, audit, ct); if (!Active(c)) return; Scope(result.AssetKey.UniverseId, c); _verified.RemoveAll(r => r.AssetKey == result.AssetKey); _verified.Add(result);
            Pipeline.Prepared = null; Pipeline.Audit = null; Record(c, "Asset COMPLETED y físicamente verificado", UiTone.Success); await Explorer.RefreshAsync();
        }), () => Writable(Pipeline) && Pipeline.Prepared is not null && Pipeline.Audit?.Passed == true);
        SaveObjective = Command(() => ActionAsync(async (c, ct) =>
        {
            var target = long.Parse(Objectives.Target, System.Globalization.CultureInfo.InvariantCulture);
            var objective = new CatalogObjective(c.Id, Objectives.ObjectiveId.Trim(), Objectives.Title.Trim(), Explorer.CurrentFilter(), target);
            await _service.SaveObjectiveAsync(c, objective, ct); Record(c, "Objetivo guardado: " + objective.Name, UiTone.Neutral);
        }), () => Writable(Objectives) && !string.IsNullOrWhiteSpace(Objectives.ObjectiveId) && !string.IsNullOrWhiteSpace(Objectives.Title) && long.TryParse(Objectives.Target, out var target) && target > 0);
        CreateBackup = Command(() => ActionAsync(async (c, ct) => { var entry = await _service.BackupAsync(c, Backups.Kind, ct); if (!Active(c)) return; Scope(entry.UniverseId, c); Record(c, "Backup verificado: " + entry.BackupId.Value, UiTone.Success); }), () => Writable(Backups));
        RestoreBackup = Command(() => ActionAsync(async (c, ct) =>
        {
            var plan = await _service.PlanRestoreAsync(c, Backups.Selected!.BackupId, ct);
            Scope(plan.UniverseId, c);
            if (!await Confirm(c, "Restaurar catálogo", "Reemplazará el catálogo activo. Core conserva copia de seguridad o evidencia forense según su estado.",
                ["Backup: " + plan.Source.BackupId.Value, "Estado actual: " + plan.ActiveState, "Bytes: " + plan.Source.Size, "SHA-256: " + plan.Source.Sha256.Hex], ct)) return;
            await _service.RestoreAsync(c, plan, ct); if (!Active(c)) return; await Explorer.SwitchAsync(); Record(c, "Catálogo restaurado y verificado", UiTone.Success);
        }), () => Writable(Backups) && Backups.Selected?.Kind == BackupKind.Database);
        RetainBackups = Command(() => ActionAsync(async (c, ct) =>
        {
            var policy = new BackupRetentionPolicy(Parse(Backups.Newest), Parse(Backups.Daily), Parse(Backups.Weekly), Parse(Backups.Monthly));
            var plan = await _service.PlanRetentionAsync(c, policy, ct); Scope(plan.UniverseId, c); var deletes = plan.Decisions.Where(d => d.Action == BackupRetentionAction.Delete).ToArray();
            if (deletes.Length == 0) { Record(c, "La política conserva todos los backups", UiTone.Neutral); return; }
            if (!await Confirm(c, "Aplicar retención", "Se eliminarán únicamente los backups reconocidos que aparecen en este plan. Core comprobará que el inventario siga intacto.", deletes.Select(d => UiTerminology.Backup(d.Backup.Kind) + " · " + d.Backup.BackupId.Value).ToArray(), ct)) return;
            var result = await _service.RetainAsync(c, plan, ct); Record(c, $"Retención completada: {result.DeletedBackupIds.Count} backups eliminados", UiTone.Warning);
        }), () => Writable(Backups) && new[] { Backups.Newest, Backups.Daily, Backups.Weekly, Backups.Monthly }.All(v => int.TryParse(v, out var n) && n >= 0));
        InspectGit = Command(() => ActionAsync(async (c, ct) => { var repository = await _service.InspectGitAsync(c, ct); if (!Active(c)) return; Scope(repository.UniverseId, c); Git.Repository = repository; Record(c, "Estado Git inspeccionado", UiTone.Neutral); }), () => Context is not null && !IsExecuting);
        CommitGit = Command(() => ActionAsync(async (c, ct) =>
        {
            var plan = await _service.PlanCommitAsync(c, _verified.ToArray(), Git.Subject, ct);
            Scope(plan.UniverseId, c);
            if (!await Confirm(c, "Crear commit", "Solo se incluirán outputs verificados de esta sesión. El commit no hace push.", new[] { "Rama: " + plan.Status.Branch, "HEAD base: " + plan.Status.Head, "Mensaje: " + plan.Message }.Concat(plan.Selection.Paths).ToArray(), ct)) return;
            var result = await _service.CommitAsync(c, plan, ct); if (!Active(c)) return; Scope(result.UniverseId, c); _verified.Clear(); Git.Repository = null; Record(c, "Commit verificado: " + result.CommitSha, UiTone.Success);
        }), () => Writable(Git) && _verified.Count > 0 && !string.IsNullOrWhiteSpace(Git.Subject));
        RecoverGit = Command(() => ActionAsync(async (c, ct) => { var receipt = await _service.RecoverAsync(c, Git.SelectedReceipt!.OperationId, ct); Record(c, "Recibo verificado: " + receipt.State, UiTone.Neutral); }), () => Writable(Git) && Git.SelectedReceipt is not null);
        RetryGit = Command(() => ActionAsync(async (c, ct) =>
        { var receipt = Git.SelectedReceipt!; if (await Confirm(c, "Reintentar commit", "Core revalidará la operación Prepared y su index propio.", [receipt.OperationId.Value, "Rama: " + receipt.Branch, "HEAD base: " + receipt.BaseHead], ct)) { var result = await _service.RetryCommitAsync(c, receipt.OperationId, ct); Record(c, "Commit recuperado: " + result.CommitSha, UiTone.Success); } }), () => Writable(Git) && Git.SelectedReceipt?.State == GitReceiptState.Prepared);
        PushGit = Command(() => ActionAsync(async (c, ct) =>
        { var receipt = Git.SelectedReceipt!; if (await Confirm(c, "Publicar commit", "Push normal a la rama y remoto congelados. Nunca force, upstream nuevo ni todas las ramas.", [receipt.OperationId.Value, "Rama: " + receipt.Branch, "Remoto: " + receipt.RemoteName, "Commit: " + receipt.CommitSha], ct)) { var result = await _service.PushAsync(c, receipt.OperationId, ct); Record(c, "Push: " + result.Outcome, UiTone.Success); } }), () => Writable(Git) && Git.SelectedReceipt?.State == GitReceiptState.Committed);
        foreach (var page in Pages) page.PropertyChanged += PageChanged;
        Explorer.PropertyChanged += ExplorerChanged; Activate(Dashboard);
    }
    public ExplorerViewModel Explorer { get; }
    public DashboardViewModel Dashboard { get; } = new();
    public PipelineViewModel Pipeline { get; } = new();
    public CatalogViewModel Catalog { get; }
    public ObjectivesViewModel Objectives { get; } = new();
    public StatisticsViewModel Statistics { get; }
    public BackupsViewModel Backups { get; } = new();
    public HistoryViewModel History { get; } = new();
    public GitViewModel Git { get; } = new();
    public SettingsViewModel Settings { get; }
    public RodoViewModel Rodo { get; } = new();
    public IReadOnlyList<ShellNavigationItem> Navigation { get; }
    public IReadOnlyList<ProfessionalPage> Pages => [Dashboard, Pipeline, Catalog, Objectives, Statistics, Backups, History, Git, Settings, Rodo];
    public UniverseContext? Context => Explorer.Context;
    public IReadOnlyList<UniverseProfile> Profiles => Explorer.Profiles;
    public bool HasSingleUniverse => Profiles.Count == 1;
    public bool HasUniverseSelector => Profiles.Count > 1;
    public UniverseProfile? SelectedUniverse { get => Explorer.SelectedUniverse; set { if (!_disposed && !IsExecuting) { Explorer.SelectedUniverse = value; LastRefresh = RefreshAfterSwitchAsync(); } } }
    public string UniverseLabel => SelectedUniverse?.DisplayName ?? "Sin universo";
    public ProfessionalPage CurrentPage { get => _current; private set { Set(ref _current, value); StatusChanged(); } }
    public bool IsExecuting { get => _executing; private set { Set(ref _executing, value); Notify(nameof(CanChangeContext)); StatusChanged(); RefreshCommands(); } }
    public bool CanChangeContext => !_disposed && !IsExecuting;
    public bool AuditAvailable => _service.AuditAvailable;
    public bool MinimizeToTray { get => _trayOnMinimize; set => Set(ref _trayOnMinimize, value); }
    public string StatusText => IsExecuting ? "Operación explícita en curso · conserva esta sesión abierta" : CurrentPage.Status;
    public RelayCommand Navigate { get; }
    public RelayCommand CancelRead { get; }
    public UiAsyncCommand Refresh { get; }
    public UiAsyncCommand SaveSettings { get; }
    public UiAsyncCommand ImportProfile { get; }
    public UiAsyncCommand PreparePackage { get; }
    public UiAsyncCommand AuditPackage { get; }
    public UiAsyncCommand ExecutePackage { get; }
    public UiAsyncCommand SaveObjective { get; }
    public UiAsyncCommand CreateBackup { get; }
    public UiAsyncCommand RestoreBackup { get; }
    public UiAsyncCommand RetainBackups { get; }
    public UiAsyncCommand InspectGit { get; }
    public UiAsyncCommand CommitGit { get; }
    public UiAsyncCommand RecoverGit { get; }
    public UiAsyncCommand RetryGit { get; }
    public UiAsyncCommand PushGit { get; }
    public Task LastRefresh { get; private set; } = Task.CompletedTask;
    public async Task InitializeAsync()
    { _initializing = true; try { await Explorer.InitializeAsync(); } finally { _initializing = false; _scopePending = false; } if (_disposed) return; Activate(Context is null ? Settings : Dashboard); await RefreshCurrentAsync(); }
    private async Task RefreshAfterSwitchAsync() { await Explorer.UniverseChangeTask; if (!_disposed && !IsExecuting) { _scopePending = false; await RefreshCurrentAsync(); } }
    public void NavigateTo(string name)
    {
        if (!Navigate.CanExecute(name)) return; Activate(Pages.Single(p => p.Name == name)); LastRefresh = RefreshCurrentAsync();
    }
    private void Activate(ProfessionalPage page)
    { CurrentPage = page; foreach (var item in Navigation) item.IsActive = item.Name == page.Name; RefreshCommands(); }
    private UiAsyncCommand Command(Func<Task> action, Func<bool> allowed)
    { UiAsyncCommand? command = null; command = new(action, () => !_disposed && allowed(), Fail, () => DisabledReason(command!)); _commands.Add(command); return command; }
    private string DisabledReason(UiAsyncCommand command)
    {
        if (_disposed) return "Esta sesión ya está cerrada.";
        if (IsExecuting) return "Operación en curso. Espera a que termine.";
        if (command == ImportProfile && (_profilesStore is null || _profilePicker is null)) return "La instalación de perfiles no está disponible en esta sesión.";
        if (!Explorer.SettingsReadable) return "La configuración local no se puede leer. Revisa el diagnóstico; no se sobrescribirá.";
        if (command == SaveSettings) return SelectedUniverse is null ? "Selecciona primero un universo." : "Completa las tres raíces con rutas absolutas existentes.";
        if (Context is null) return Explorer.Error is null ? "Configura primero las raíces del universo." : "Las raíces configuradas no están disponibles o no son seguras. Revisa Ajustes.";
        var page = command == SaveObjective ? (ProfessionalPage)Objectives : command == CreateBackup || command == RestoreBackup || command == RetainBackups ? Backups : command == PreparePackage || command == AuditPackage || command == ExecutePackage ? Pipeline : Git;
        if (page.State == UiPageState.Loading) return "Espera a que termine la lectura del universo.";
        if (page.State == UiPageState.Stopped) return "Lectura detenida. Revisa el diagnóstico antes de reintentar.";
        if (page.State is UiPageState.Cancelled or UiPageState.Unconfigured) return "Actualiza esta sección para consultar los datos del universo.";
        if (command == SaveObjective) return "Completa identificador, nombre y un número de assets mayor que cero.";
        if (command == RestoreBackup) return Backups.Selected is null ? "Selecciona una copia verificada." : "La restauración está disponible para copias de Base de datos (SQLite).";
        if (command == RetainBackups) return "Los valores de retención deben ser números enteros mayores o iguales que cero.";
        if (command == PreparePackage) return "Selecciona un paquete ZIP del Inbox.";
        if (command == AuditPackage) return Pipeline.Prepared is null ? "Prepara primero un paquete para obtener su plan." : "Configura un auditor de producción real antes de auditar.";
        if (command == ExecutePackage) return Pipeline.Prepared is null ? "Prepara primero un paquete para obtener su plan." : Pipeline.Audit is null ? "Audita el plan y obtén PASS antes de publicar." : "La auditoría no ha dado PASS; no se puede publicar.";
        if (command == CommitGit) return _verified.Count == 0 ? "Publica primero un asset: el commit requiere outputs físicamente verificados de esta sesión." : "Escribe el mensaje del commit.";
        if (command == RecoverGit || command == RetryGit || command == PushGit) return Git.SelectedReceipt is null ? "No hay una operación Git seleccionada." : command == RetryGit ? "Solo se puede reintentar una operación Prepared." : "Selecciona una operación con commit verificado pendiente de push.";
        return "Espera a que la operación actual termine.";
    }
    private void ApplyProfiles(UniverseProfilesSnapshot snapshot)
    { Settings.InstalledProfiles = snapshot.Installed; Replace(Settings.ProfileIssues, snapshot.Issues); }
    private bool Writable(ProfessionalPage page) => Context is not null && !IsExecuting && page.State is UiPageState.Ready or UiPageState.Empty;
    private async Task ActionAsync(Func<UniverseContext, CancellationToken, Task> action)
    {
        var context = Context ?? throw new InvalidOperationException("No active context."); _read?.Cancel(); ++_generation; IsExecuting = true;
        try { await action(context, _lifetime.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception error) { if (Active(context)) Fail(error); return; }
        finally { IsExecuting = false; }
        if (!_disposed) await RefreshCurrentAsync();
    }
    private bool Active(UniverseContext context) => !_disposed && context == Context && !_lifetime.IsCancellationRequested;
    private async Task<bool> Confirm(UniverseContext c, string title, string explanation, IReadOnlyList<string> details, CancellationToken ct)
    { var accepted = await _confirmation.ConfirmAsync(new(title, explanation, c.Id, details), ct); return accepted && !_disposed && Context == c && !ct.IsCancellationRequested; }
    public async Task RefreshCurrentAsync()
    {
        if (_disposed || IsExecuting) return;
        _read?.Cancel(); _read?.Dispose(); var source = _read = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var generation = ++_generation; var context = Context; var page = CurrentPage; page.Notice = null;
        if (page == Rodo) { page.State = UiPageState.Reserved; return; }
        if (page == Settings) { page.State = !Explorer.SettingsReadable || (Context is null && Explorer.Error is not null) ? UiPageState.Stopped : Context is null ? UiPageState.Unconfigured : UiPageState.Ready; page.Notice = Explorer.Error is null ? null : new(Explorer.Error.Message, Explorer.Error.Code, UiTone.Error); return; }
        if (context is null) { page.State = Explorer.Error is null ? UiPageState.Unconfigured : UiPageState.Stopped; page.Notice = Explorer.Error is null ? null : new(Explorer.Error.Message, Explorer.Error.Code, UiTone.Error); return; }
        page.State = UiPageState.Loading;
        bool Current() => !_disposed && !source.IsCancellationRequested && generation == _generation && context == Context;
        try
        {
            if (page == Dashboard)
            {
                var value = await _service.DashboardAsync(context, source.Token); if (!Current()) return; Scope(value.UniverseId, context);
                if (value.Planning is not null) CheckPlanning(value.Planning, context);
                if (value.Pipeline is not null) CheckPipeline(value.Pipeline, context);
                foreach (var entry in value.Backups ?? []) Scope(entry.UniverseId, context);
                Dashboard.Snapshot = value; Replace(Dashboard.Objectives, (value.Planning?.Objectives ?? []).Select(o => new ObjectiveCard(o)));
                Replace(Dashboard.Notices, value.Notices); page.State = value.Notices.Any(n => n.Tone == UiTone.Error) ? UiPageState.Stopped : UiPageState.Ready;
            }
            else if (page == Pipeline || page == History)
            {
                var value = await _service.PipelineAsync(context, source.Token); if (!Current()) return; CheckPipeline(value, context);
                if (page == Pipeline) { Replace(Pipeline.Candidates, value.Candidates); Replace(Pipeline.Jobs, value.Jobs); }
                else { var receipts = await _service.ReceiptsAsync(context, source.Token); if (!Current()) return; CheckReceipts(receipts, context); Replace(History.Jobs, value.Jobs); Replace(History.Receipts, receipts); }
                page.Notice = value.Notices.FirstOrDefault(n => n.Tone == UiTone.Error) ?? value.Notices.FirstOrDefault(); page.State = value.Notices.Any(n => n.Tone == UiTone.Error) ? UiPageState.Stopped : value.Jobs.Count + value.Candidates.Count == 0 ? UiPageState.Empty : UiPageState.Ready;
            }
            else if (page == Objectives)
            { var value = await _service.PlanningAsync(context, source.Token); if (!Current()) return; CheckPlanning(value, context); Replace(Objectives.Objectives, value.Objectives.Select(o => new ObjectiveCard(o))); Replace(Objectives.Campaigns, value.Campaigns); page.State = value.Objectives.Count == 0 ? UiPageState.Empty : UiPageState.Ready; }
            else if (page == Backups)
            { var value = await _service.BackupsAsync(context, source.Token); if (!Current()) return; foreach (var entry in value.Entries) Scope(entry.UniverseId, context); Replace(Backups.Entries, value.Entries); Backups.Selected = null; var issue = value.Issues.Issues.FirstOrDefault(i => i.StopsProcessing) ?? value.Issues.Issues.FirstOrDefault(); page.Notice = issue is null ? null : new(issue.Message, issue.Code, issue.StopsProcessing ? UiTone.Error : UiTone.Warning); page.State = value.Issues.ShouldStop ? UiPageState.Stopped : value.Entries.Count == 0 ? UiPageState.Empty : UiPageState.Ready; }
            else if (page == Git)
            { var receipts = await _service.ReceiptsAsync(context, source.Token); if (!Current()) return; CheckReceipts(receipts, context); Replace(Git.Receipts, receipts); Git.SelectedReceipt = null; page.State = UiPageState.Ready; }
            else
            { await Explorer.RefreshAsync(); if (!Current()) return; page.State = Explorer.State == ExplorerState.Error ? UiPageState.Stopped : Explorer.State == ExplorerState.Empty ? UiPageState.Empty : UiPageState.Ready; if (Explorer.Error is { } error) page.Notice = new(error.Message, error.Code, UiTone.Error); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current()) Fail(error); }
        finally { if (Current()) { StatusChanged(); RefreshCommands(); } }
    }
    private void ExplorerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.PropertyName == nameof(ExplorerViewModel.Profiles)) { Notify(nameof(Profiles)); Notify(nameof(HasSingleUniverse)); Notify(nameof(HasUniverseSelector)); }
        if (e.PropertyName is nameof(ExplorerViewModel.Context) or nameof(ExplorerViewModel.SelectedUniverse))
        {
            _read?.Cancel(); ++_generation; _verified.Clear(); foreach (var page in Pages) page.Reset();
            Notify(nameof(Context)); Notify(nameof(SelectedUniverse)); Notify(nameof(UniverseLabel));
            _scopePending = true;
        }
        if (e.PropertyName == nameof(ExplorerViewModel.State) && _scopePending && !_initializing && !IsExecuting && Explorer.State != ExplorerState.Loading)
        { _scopePending = false; LastRefresh = RefreshCurrentAsync(); }
        RefreshCommands();
    }
    private void PageChanged(object? sender, PropertyChangedEventArgs e) { RefreshCommands(); if (sender == CurrentPage) StatusChanged(); }
    private void StatusChanged() => Notify(nameof(StatusText));
    private void RefreshCommands() { foreach (var command in _commands) command.Refresh(); Navigate?.Refresh(); CancelRead?.Refresh(); }
    private void Fail(Exception error)
    { if (_disposed) return; var notice = UiNotice.From(error); CurrentPage.Notice = notice; CurrentPage.State = UiPageState.Stopped; if (Context is { } c) Record(c, notice.Message, notice.Tone, notice.Code); }
    private void Record(UniverseContext c, string message, UiTone tone, string? code = null)
    { if (_disposed || c != Context) return; History.Activities.Insert(0, new(DateTimeOffset.UtcNow, c.Id, message, tone, code)); while (History.Activities.Count > 100) History.Activities.RemoveAt(History.Activities.Count - 1); }
    private static int Parse(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static void Scope(UniverseId id, UniverseContext c) { if (id != c.Id) throw new InvalidDataException("UI result universe mismatch."); }
    private static void CheckReceipts(IEnumerable<GitOperationReceipt> receipts, UniverseContext c) { foreach (var receipt in receipts) Scope(receipt.UniverseId, c); }
    private static void CheckPipeline(PipelineSnapshot value, UniverseContext c)
    { Scope(value.UniverseId, c); foreach (var job in value.Jobs) Scope(job.Record.UniverseId, c); }
    private static void CheckPlanning(CatalogPlanningReadModel value, UniverseContext c)
    {
        Scope(value.UniverseId, c); foreach (var goal in value.Objectives) Scope(goal.Objective.UniverseId, c);
        foreach (var campaign in value.Campaigns) { Scope(campaign.Campaign.UniverseId, c); foreach (var goal in campaign.Objectives) Scope(goal.Objective.UniverseId, c); }
    }
    private static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _read?.Cancel(); ++_generation; Explorer.PropertyChanged -= ExplorerChanged; foreach (var page in Pages) { page.PropertyChanged -= PageChanged; page.Reset(); } _verified.Clear(); Explorer.Dispose(); _read?.Dispose(); _lifetime.Dispose(); RefreshCommands(); }
}
