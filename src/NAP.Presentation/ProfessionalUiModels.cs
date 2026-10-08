using NAP.Core;

namespace NAP.Presentation;

public enum UiTone { Neutral, Success, Warning, Error, Ai }
public enum UiPageState { Unconfigured, Loading, Ready, Empty, Stopped, Reserved, Cancelled }
public sealed class UiStoppedException : InvalidOperationException
{
    public UiStoppedException(NapIssueReport issues) : base("Core validation stopped.")
    { if (!issues.ShouldStop) throw new ArgumentException("A stopped operation requires STOP issues.", nameof(issues)); Issues = issues; }
    public NapIssueReport Issues { get; }
}
public sealed record UiNotice(string Message, string Code, UiTone Tone)
{
    public static UiNotice From(Exception error)
    {
        if (error is ImageNormalizationException normalization) return new(normalization.Message, normalization.Code, UiTone.Error);
        if (error is UniverseProfileStoreException profile) return new(profile.Message, profile.Code, UiTone.Error);
        var report = error switch { BackupException e => e.Issues, GitProductionException e => e.Issues,
            CatalogException e => e.Issues, ProductionStorageException e => e.Issues, ArchiveStorageException e => e.Issues, UiStoppedException e => e.Issues, _ => null };
        var code = report?.Issues.FirstOrDefault(i => i.StopsProcessing)?.Code ?? "ui_operation_unavailable";
        return new("Operación detenida. Revisa la configuración o el diagnóstico antes de reintentar.", code, UiTone.Error);
    }
}
public sealed record UiConfirmation(string Title, string Explanation, UniverseId UniverseId, IReadOnlyList<string> Details);
public interface IUserConfirmation { Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation); }
public sealed record PipelineStep(string Label, bool IsCurrent, UiTone Tone);
public sealed record PipelineJob(JobStateRecord Record)
{
    public string JobId => Record.JobId.Value;
    public string State => Record.State.ToString().ToUpperInvariant();
    public UiTone Tone => Record.State switch { JobState.Failed => UiTone.Error, JobState.Completed or JobState.Verified => UiTone.Success, _ => UiTone.Neutral };
    // A journal is one checkpoint, not an event stream. Never manufacture past step completion/timestamps.
    public IReadOnlyList<PipelineStep> Steps => Enum.GetValues<JobState>().Select(s => new PipelineStep(s.ToString().ToUpperInvariant(), s == Record.State,
        s == Record.State ? Tone : UiTone.Neutral)).ToArray();
}
public sealed record PipelineSnapshot(UniverseId UniverseId, IReadOnlyList<InboxPackageCandidate> Candidates,
    IReadOnlyList<PipelineJob> Jobs, IReadOnlyList<UiNotice> Notices);
public sealed record DashboardSnapshot(UniverseId UniverseId, CatalogStatistics? Statistics, CatalogPlanningReadModel? Planning,
    PipelineSnapshot? Pipeline, IReadOnlyList<BackupHistoryEntry>? Backups, IReadOnlyList<UiNotice> Notices);
public sealed record UiActivity(DateTimeOffset ObservedUtc, UniverseId UniverseId, string Message, UiTone Tone, string? Code = null);
public sealed record ObjectiveCard(CatalogObjectiveProgress Progress)
{
    public string Id => Progress.Objective.Id;
    public string Name => Progress.Objective.Name;
    public string Coverage => $"{Progress.Coverage.ActualCount:N0} / {Progress.Coverage.TargetCount:N0}";
    public string Remaining => Progress.Coverage.IsComplete ? "Objetivo cubierto" : $"Faltan {Progress.Coverage.Remaining:N0}";
    public double Percent => Math.Min(100, Progress.Coverage.CompletionRatio * 100);
    public UiTone Tone => Progress.Coverage.IsComplete ? UiTone.Success : UiTone.Neutral;
    public string Filter => string.Join(" · ", new[] { Progress.Objective.Filter.AssetId, Progress.Objective.Filter.AssetType, Progress.Objective.Filter.ProductionProfile }
        .Where(v => !string.IsNullOrWhiteSpace(v)).Concat(Progress.Objective.Filter.Classification.Select(p => p.Key + ": " + p.Value))
        .Concat(Progress.Objective.Filter.Traits.Select(t => t.Key + ": " + t.Value + " (" + t.Type + ")")));
}
public sealed class PreparedPipelineAsset
{
    internal PreparedPipelineAsset(UniverseContext context, JobId job, ValidatedAssetPackage package, ProcessingPlan plan, ArchiveMasterPlan archive, NapIssueReport validation)
    { Context = context; JobId = job; Package = package; Processing = plan; Archive = archive; Validation = validation; }
    public UniverseContext Context { get; }
    public JobId JobId { get; }
    public ValidatedAssetPackage Package { get; }
    public ProcessingPlan Processing { get; }
    public ArchiveMasterPlan Archive { get; }
    public NapIssueReport Validation { get; }
    public string Summary => $"{Package.AssetKey.AssetId} · {Processing.ProductionDestination.RelativeDirectory}";
}
