using System.Collections.ObjectModel;
using NAP.Core;

namespace NAP.Presentation;

public abstract class ProfessionalPage(string name, string description) : ObservableObject
{
    private UiPageState _state = UiPageState.Unconfigured;
    private UiNotice? _notice;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public UiPageState State { get => _state; internal set { Set(ref _state, value); Notify(nameof(Status)); Notify(nameof(EmptyMessage)); Notify(nameof(EmptyJobs)); Notify(nameof(EmptyReceipts)); } }
    public UiNotice? Notice { get => _notice; internal set => Set(ref _notice, value); }
    public string Status => State switch { UiPageState.Unconfigured => "Configura las raíces del universo en Ajustes.", UiPageState.Loading => "Leyendo datos del universo…",
        UiPageState.Empty => "No hay registros en esta sección.", UiPageState.Stopped => "STOP · revisión necesaria", UiPageState.Reserved => "RoDo · preparación visual", UiPageState.Cancelled => "Lectura cancelada. Actualiza para volver a leer.", _ => "Información del universo activo" };
    public virtual string? EmptyMessage => EvidenceMessage(null);
    public virtual string? EmptyJobs => null;
    public virtual string? EmptyReceipts => null;
    protected string? EvidenceMessage(string? verified) => State switch
    {
        UiPageState.Ready or UiPageState.Empty => verified,
        UiPageState.Unconfigured => "Configura las raíces del universo en Ajustes.",
        UiPageState.Loading => "Consultando los datos del universo…",
        UiPageState.Stopped => Notice?.Message ?? "Lectura detenida. Revisa el diagnóstico antes de reintentar.",
        UiPageState.Cancelled => "Lectura cancelada. Actualiza para volver a consultar.", _ => null
    };
    internal virtual void Reset() { State = UiPageState.Unconfigured; Notice = null; }
}
public sealed class DashboardViewModel() : ProfessionalPage("Inicio", "Una vista del estado real de tu universo.")
{
    private DashboardSnapshot? _snapshot;
    public DashboardSnapshot? Snapshot { get => _snapshot; internal set { Set(ref _snapshot, value); Notify(nameof(Assets)); Notify(nameof(ActiveJobs)); Notify(nameof(BackupCount)); Notify(nameof(InboxCount)); Notify(nameof(OperationalStatus)); } }
    public string Assets => Snapshot?.Statistics?.TotalAssets.ToString("N0") ?? "—";
    public string ActiveJobs => Snapshot?.Pipeline?.Jobs.Count(j => j.Record.State is not (JobState.Completed or JobState.Failed)).ToString("N0") ?? "—";
    public string BackupCount => Snapshot?.Backups?.Count.ToString("N0") ?? "—";
    public string InboxCount => Snapshot?.Pipeline?.Candidates.Count.ToString("N0") ?? "—";
    public string OperationalStatus => Snapshot is null ? "Aún no hay una lectura del universo" : Snapshot.Notices.Count > 0 ? "Hay información que requiere revisión" : "Lectura completada";
    public ObservableCollection<ObjectiveCard> Objectives { get; } = [];
    public ObservableCollection<UiNotice> Notices { get; } = [];
    internal override void Reset() { base.Reset(); Snapshot = null; Objectives.Clear(); Notices.Clear(); }
}
public sealed class PipelineViewModel() : ProfessionalPage("Producción", "Inbox, preparación explícita y checkpoints persistidos por Core.")
{
    private InboxPackageCandidate? _candidate;
    private PipelineJob? _job;
    private PreparedPipelineAsset? _prepared;
    private AiAuditReport? _audit;
    public ObservableCollection<InboxPackageCandidate> Candidates { get; } = [];
    public ObservableCollection<PipelineJob> Jobs { get; } = [];
    public InboxPackageCandidate? SelectedCandidate { get => _candidate; set => Set(ref _candidate, value); }
    public PipelineJob? SelectedJob { get => _job; set => Set(ref _job, value); }
    public PreparedPipelineAsset? Prepared { get => _prepared; internal set => Set(ref _prepared, value); }
    public AiAuditReport? Audit { get => _audit; internal set => Set(ref _audit, value); }
    internal override void Reset() { base.Reset(); Candidates.Clear(); Jobs.Clear(); SelectedCandidate = null; SelectedJob = null; Prepared = null; Audit = null; }
}
public sealed class CatalogViewModel(ExplorerViewModel explorer) : ProfessionalPage("Catálogo", "Producción verificada, filtros exactos y documentos originales.")
{ public ExplorerViewModel Explorer { get; } = explorer; }
public sealed class ObjectivesViewModel() : ProfessionalPage("Objetivos", "Cobertura medida sobre los assets catalogados; cada objetivo conserva su filtro.")
{
    public ObservableCollection<ObjectiveCard> Objectives { get; } = [];
    public ObservableCollection<CatalogCampaignProgress> Campaigns { get; } = [];
    private string _id = "", _title = "", _target = "10";
    public string ObjectiveId { get => _id; set => Set(ref _id, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Target { get => _target; set => Set(ref _target, value); }
    public override string? EmptyMessage => EvidenceMessage(Objectives.Count == 0 ? "No hay objetivos definidos para este universo." : null);
    internal override void Reset() { base.Reset(); Objectives.Clear(); Campaigns.Clear(); ObjectiveId = ""; Title = ""; Target = "10"; }
}
public sealed class StatisticsViewModel(ExplorerViewModel explorer) : ProfessionalPage("Estadísticas", "Distribuciones actuales del catálogo. Sin tendencias temporales inferidas.")
{ public ExplorerViewModel Explorer { get; } = explorer; }
public sealed class BackupsViewModel() : ProfessionalPage("Backups", "Copias locales verificadas de catálogo, repositorio y Git.")
{
    public ObservableCollection<BackupHistoryEntry> Entries { get; } = [];
    public IReadOnlyList<BackupKindChoice> Kinds { get; } = Enum.GetValues<BackupKind>().Select(k => new BackupKindChoice(k, UiTerminology.Backup(k))).ToArray();
    private BackupHistoryEntry? _selected;
    private BackupKind _kind;
    private string _newest = "3", _daily = "0", _weekly = "0", _monthly = "0";
    public BackupHistoryEntry? Selected { get => _selected; set => Set(ref _selected, value); }
    public BackupKind Kind { get => _kind; set => Set(ref _kind, value); }
    public string Newest { get => _newest; set => Set(ref _newest, value); }
    public string Daily { get => _daily; set => Set(ref _daily, value); }
    public string Weekly { get => _weekly; set => Set(ref _weekly, value); }
    public string Monthly { get => _monthly; set => Set(ref _monthly, value); }
    public override string? EmptyMessage => EvidenceMessage(Entries.Count == 0 ? "Todavía no hay copias verificadas para este universo." : null);
    internal override void Reset() { base.Reset(); Entries.Clear(); Selected = null; }
}
public sealed class HistoryViewModel : ProfessionalPage
{
    public HistoryViewModel() : base("Historial", "Checkpoints, recibos Git y actividad observada en esta sesión.")
    { Activities.CollectionChanged += (_, _) => Notify(nameof(EmptyActivity)); }
    public ObservableCollection<PipelineJob> Jobs { get; } = [];
    public ObservableCollection<GitOperationReceipt> Receipts { get; } = [];
    public ObservableCollection<UiActivity> Activities { get; } = [];
    public override string? EmptyJobs => EvidenceMessage(Jobs.Count == 0 ? "No hay jobs persistidos para este universo." : null);
    public override string? EmptyReceipts => EvidenceMessage(Receipts.Count == 0 ? "No hay operaciones Git registradas para este universo." : null);
    public string? EmptyActivity => Activities.Count == 0 ? "Todavía no hay actividad registrada en esta sesión." : null;
    internal override void Reset() { base.Reset(); Jobs.Clear(); Receipts.Clear(); Activities.Clear(); }
}
public sealed class GitViewModel() : ProfessionalPage("Git", "Estado humano y operaciones explícitas sobre el repositorio de producción.")
{
    private GitRepositoryStatus? _repository;
    private GitOperationReceipt? _receipt;
    private string _subject = "";
    public GitRepositoryStatus? Repository { get => _repository; internal set { Set(ref _repository, value); Notify(nameof(HumanStatus)); Notify(nameof(Recommendation)); Notify(nameof(RecommendationTone)); } }
    public string HumanStatus => Repository is null ? "El repositorio aún no se ha inspeccionado" : Repository.Synchronization != GitSynchronization.Synchronized ? "Local y remoto requieren revisión" : Repository.IsClean ? "Repositorio sin cambios" : "Hay cambios locales pendientes";
    public string Recommendation => Repository is null ? "Inspeccionar consulta también el remoto configurado." : !Repository.IndexIsClean ? "Revisa el staging existente fuera de NAP." : "Revisa los cambios y las evidencias antes de cualquier operación.";
    public UiTone RecommendationTone => Repository is null ? UiTone.Neutral : Repository.IndexIsClean && Repository.IsClean && Repository.Synchronization == GitSynchronization.Synchronized ? UiTone.Success : UiTone.Warning;
    public ObservableCollection<GitOperationReceipt> Receipts { get; } = [];
    public GitOperationReceipt? SelectedReceipt { get => _receipt; set => Set(ref _receipt, value); }
    public string Subject { get => _subject; set => Set(ref _subject, value); }
    public override string? EmptyMessage => EvidenceMessage(Receipts.Count == 0 ? "No hay operaciones Git registradas para este universo." : null);
    internal override void Reset() { base.Reset(); Repository = null; Receipts.Clear(); SelectedReceipt = null; Subject = ""; }
}
public sealed class SettingsViewModel(ExplorerViewModel explorer) : ProfessionalPage("Ajustes", "Raíces locales explícitas, independientes y recordadas por universo.")
{
    public ExplorerViewModel Explorer { get; } = explorer;
    private IReadOnlyList<InstalledUniverseProfile> _installed = explorer.Profiles.Select(p => new InstalledUniverseProfile(p, false)).ToArray();
    public IReadOnlyList<InstalledUniverseProfile> InstalledProfiles { get => _installed; internal set => Set(ref _installed, value); }
    public ObservableCollection<UiNotice> ProfileIssues { get; } = [];
    public string? ProfilesLocation { get; internal set; }
}
public sealed class RodoViewModel() : ProfessionalPage("RoDo", "Tu espacio para la próxima fase de IA.")
{
    public string Availability => "RoDo estará disponible en la Fase 15.";
    public string Explanation => "La interfaz está preparada, pero todavía no hay conversación ni respuestas de IA.";
    public bool CanSend => false;
    public IReadOnlyList<string> Conversations { get; } = Array.Empty<string>();
    internal override void Reset() { base.Reset(); State = UiPageState.Reserved; }
}
public sealed record BackupKindChoice(BackupKind Value, string Label);
public static class UiTerminology
{
    public static string Backup(BackupKind kind) => kind switch { BackupKind.Database => "Base de datos (SQLite)", BackupKind.GitBundle => "Git bundle", BackupKind.RepositorySnapshot => "Repositorio", _ => "Tipo de copia desconocido" };
}
public sealed class ShellNavigationItem(string name, string glyph) : ObservableObject
{
    private bool _active;
    public string Name { get; } = name;
    public string Glyph { get; } = glyph;
    public bool IsActive { get => _active; internal set => Set(ref _active, value); }
}
