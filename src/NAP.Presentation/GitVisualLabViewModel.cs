using NAP.Core;

namespace NAP.Presentation;

/// <summary>Window binding adapter for the unchanged production GitView and GitViewModel.</summary>
public sealed class GitVisualLabViewModel : ObservableObject
{
    private readonly GitVisualLabService _service;
    private GitVisualLabScenario _selected;
    private string _notice = GitVisualLabService.Denial;
    public GitViewModel Git { get; } = new();
    public string UniverseLabel => "F8.1 · Universo ficticio · Sin producción";
    public string GitRepositoryLabel => "FICTICIO · Ningún repositorio abierto";
    public bool CanChangeContext => true; // In-memory provisional editor remains testable; save always denied.
    public IReadOnlyList<GitVisualLabScenario> Scenarios { get; } = Enum.GetValues<GitVisualLabScenario>();
    public GitVisualLabScenario SelectedScenario { get => _selected; set { if (Set(ref _selected, value)) Load(); } }
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public UiAsyncCommand Refresh { get; }
    public UiAsyncCommand ViewOutboxReceipt { get; }
    public UiAsyncCommand InspectGit { get; }
    public UiAsyncCommand ProposeGitAuthorization { get; }
    public UiAsyncCommand SaveGitAuthorization { get; }
    public UiAsyncCommand RevokeGitAuthorization { get; }
    public UiAsyncCommand CommitGit { get; }
    public UiAsyncCommand RecoverGit { get; }
    public UiAsyncCommand RetryGit { get; }
    public UiAsyncCommand PushGit { get; }
    public UiAsyncCommand RetryBlockedGit { get; }
    public UiAsyncCommand RetryPublicationGit { get; }
    public IReadOnlyList<UiAsyncCommand> OperationalCommands { get; }
    public GitVisualLabViewModel(GitVisualLabService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        UiAsyncCommand Denied() => new(() => { GitVisualLabService.DenyOperationalAction(); return Task.CompletedTask; },
            () => false, _ => throw new InvalidOperationException("Unexpected operation in laboratory."), () => GitVisualLabService.Denial);
        InspectGit = Denied(); ProposeGitAuthorization = Denied(); SaveGitAuthorization = Denied(); RevokeGitAuthorization = Denied();
        CommitGit = Denied(); RecoverGit = Denied(); RetryGit = Denied(); PushGit = Denied(); RetryBlockedGit = Denied(); RetryPublicationGit = Denied();
        OperationalCommands = [InspectGit, ProposeGitAuthorization, SaveGitAuthorization, RevokeGitAuthorization, CommitGit,
            RecoverGit, RetryGit, PushGit, RetryBlockedGit, RetryPublicationGit];
        Refresh = new(() => { Load(); return Task.CompletedTask; }, () => true, _ => Notice = "F8.1 · lectura detenida");
        ViewOutboxReceipt = new(() => { Git.SelectedReceipt = Git.Receipts.First(r => r.OperationId.Value == Git.SelectedOutbox!.OperationId); return Task.CompletedTask; },
            () => Git.SelectedOutbox?.OperationId is { } id && Git.Receipts.Any(r => r.OperationId.Value == id), _ => Notice = "F8.1 · selección detenida");
        Git.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Git.SelectedOutbox)) ViewOutboxReceipt.Refresh(); };
        Load();
    }
    private void Load()
    {
        // Clear all previous scope/evidence before reading; errors cannot retain actionable stale data.
        Git.Reset();
        try
        {
            var snapshot = _service.Read(SelectedScenario);
            Git.ApplyConfiguration(snapshot.Configuration); Git.ApplyOutbox(snapshot.Outbox);
            Git.Repository = snapshot.Repository;
            foreach (var receipt in snapshot.Receipts) Git.Receipts.Add(receipt);
            Git.State = Git.Receipts.Count == 0 ? UiPageState.Empty : UiPageState.Ready;
            Notice = GitVisualLabService.Denial;
        }
        catch (InvalidDataException)
        {
            Git.State = UiPageState.Stopped;
            Git.Notice = new("F8.1 · Error sintético de integridad; vista detenida, sin fallback.", "git_ui_outbox_integrity", UiTone.Error);
            Notice = Git.Notice.Message;
        }
        Git.HostAccess = new(false, GitVisualLabService.Denial, false);
        ViewOutboxReceipt.Refresh();
    }
}
