using System.Collections.ObjectModel;
using NAP.Core;

namespace NAP.Presentation;

public sealed partial class GitViewModel
{
    private GitConfigurationView? _configuration;
    private GitAuthorizationReview? _review;
    private GitHostAccess? _hostAccess;
    private GitPublicationMode _mode;
    private string _actor = "", _prefixes = "", _maximum = "5";
    private bool _allowCommit, _allowPush;
    private GitOutboxRow? _batch, _intent;
    public IReadOnlyList<GitPublicationMode> Modes { get; } = Enum.GetValues<GitPublicationMode>();
    public GitConfigurationView? Configuration { get => _configuration; private set { Set(ref _configuration, value); Notify(nameof(ConfigurationSummary)); } }
    public string ConfigurationSummary => Configuration?.Summary ?? "Configuración Git aún no consultada";
    public GitHostAccess? HostAccess { get => _hostAccess; internal set => Set(ref _hostAccess, value); }
    public GitAuthorizationReview? Review { get => _review; internal set => Set(ref _review, value); }
    public GitPublicationMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            Review = null;
            if (value == GitPublicationMode.Disabled) AllowCommit = false;
            if (!PushOptionAvailable) AllowPush = false;
            Notify(nameof(CommitOptionAvailable)); Notify(nameof(PushOptionAvailable));
        }
    }
    public string Actor { get => _actor; set { if (Set(ref _actor, value)) Review = null; } }
    public string Prefixes { get => _prefixes; set { if (Set(ref _prefixes, value)) Review = null; } }
    public string MaximumJobs { get => _maximum; set { if (Set(ref _maximum, value)) Review = null; } }
    public bool AllowCommit
    {
        get => _allowCommit;
        set { if (Set(ref _allowCommit, value && CommitOptionAvailable)) { Review = null; if (!_allowCommit) AllowPush = false; Notify(nameof(PushOptionAvailable)); } }
    }
    public bool AllowPush { get => _allowPush; set { if (Set(ref _allowPush, value && PushOptionAvailable)) Review = null; } }
    public bool CommitOptionAvailable => Mode != GitPublicationMode.Disabled;
    public bool PushOptionAvailable => AllowCommit && Mode is GitPublicationMode.Manual or GitPublicationMode.AutoCommitAndPushBatch;
    public bool CanCommit => Configuration is { AllowCommit: true, Revoked: false, Mode: not GitPublicationMode.Disabled };
    public bool CanPush => CanCommit && Configuration!.AllowPush;
    public string RequireNoPendingNotice => "AutoCommitBatch: un segundo lote puede esperar mientras exista un receipt Prepared o Committed anterior sin publicar. Es RequireNoPending; requiere resolver la operación anterior.";
    public ObservableCollection<GitOutboxRow> Batches { get; } = [];
    public ObservableCollection<GitOutboxRow> Intents { get; } = [];
    public ObservableCollection<GitPolicyRevision> PolicyHistory { get; } = [];
    public GitOutboxRow? SelectedBatch { get => _batch; set { if (Set(ref _batch, value)) { if (value is not null) SelectedIntent = null; Notify(nameof(SelectedOutbox)); } } }
    public GitOutboxRow? SelectedIntent { get => _intent; set { if (Set(ref _intent, value)) { if (value is not null) SelectedBatch = null; Notify(nameof(SelectedOutbox)); } } }
    public GitOutboxRow? SelectedOutbox => SelectedBatch ?? SelectedIntent;
    public string OutboxCounts => string.Join(" · ", Enum.GetValues<GitOutboxState>().Select(s => $"{s}: {Intents.Count(i => i.State == s)}"));
    public bool ValidEditor => !string.IsNullOrWhiteSpace(Actor) && int.TryParse(MaximumJobs, out var n) && n is >= 1 and <= 20 && !string.IsNullOrWhiteSpace(Prefixes);
    public GitAuthorizationInput Input() => new(Mode, Actor.Trim(), Array.AsReadOnly(Prefixes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray()), int.Parse(MaximumJobs), AllowCommit, AllowPush);
    internal void ApplyConfiguration(GitConfigurationView value)
    {
        var changed = Configuration is null || Configuration.UniverseId != value.UniverseId || Configuration.Revision != value.Revision;
        Configuration = value;
        if (changed)
        {
            Review = null; Mode = value.Mode; AllowCommit = value.AllowCommit; AllowPush = value.AllowPush;
            Prefixes = string.Join(Environment.NewLine, value.Prefixes); MaximumJobs = value.MaximumJobs.ToString();
        }
        Notify(nameof(CanCommit)); Notify(nameof(CanPush));
    }
    internal void ApplyOutbox(GitOutboxView value)
    {
        var selected = SelectedOutbox;
        Batches.Clear(); Intents.Clear(); PolicyHistory.Clear();
        foreach (var row in value.Rows) (row.IsBatch ? Batches : Intents).Add(row.WithDisplayConfiguration(Configuration));
        foreach (var entry in value.History) PolicyHistory.Add(entry);
        SelectedBatch = selected?.IsBatch == true ? Batches.FirstOrDefault(r => r.Id == selected.Id) : null;
        SelectedIntent = selected?.IsBatch == false ? Intents.FirstOrDefault(r => r.Id == selected.Id) : null;
        Notify(nameof(OutboxCounts));
    }
    internal void InvalidateConfiguration()
    {
        Configuration = null; Review = null; HostAccess = null; Repository = null; ClearOutbox();
        Notify(nameof(CanCommit)); Notify(nameof(CanPush));
    }
    internal void ClearOutbox()
    {
        Batches.Clear(); Intents.Clear(); PolicyHistory.Clear(); SelectedBatch = null; SelectedIntent = null;
        Notify(nameof(OutboxCounts));
    }
    private void ResetManagement()
    {
        Configuration = null; Review = null; HostAccess = null; Mode = GitPublicationMode.Disabled;
        Actor = ""; Prefixes = ""; MaximumJobs = "5"; AllowCommit = false; AllowPush = false;
        Batches.Clear(); Intents.Clear(); PolicyHistory.Clear(); SelectedBatch = null; SelectedIntent = null;
        Notify(nameof(CanCommit)); Notify(nameof(CanPush)); Notify(nameof(OutboxCounts));
    }
}
