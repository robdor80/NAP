using NAP.Core;

namespace NAP.Presentation;

public interface IProfessionalUiService
{
    bool AuditAvailable { get; }
    Task<DashboardSnapshot> DashboardAsync(UniverseContext c, CancellationToken ct);
    Task<PipelineSnapshot> PipelineAsync(UniverseContext c, CancellationToken ct);
    Task<PreparedPipelineAsset> PrepareAsync(UniverseContext c, InboxPackageCandidate candidate, CancellationToken ct);
    Task<PreparedPipelineAsset> PrepareNormalizedAsync(UniverseContext c, ImageNormalizationResult candidate, CancellationToken ct);
    Task<AiAuditReport> AuditAsync(UniverseContext c, PreparedPipelineAsset prepared, CancellationToken ct);
    Task<ProductionAssetResult> ExecuteAsync(UniverseContext c, PreparedPipelineAsset prepared, AiAuditReport audit, CancellationToken ct);
    Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct);
    Task SaveObjectiveAsync(UniverseContext c, CatalogObjective objective, CancellationToken ct);
    Task<BackupHistory> BackupsAsync(UniverseContext c, CancellationToken ct);
    Task<BackupHistoryEntry> BackupAsync(UniverseContext c, BackupKind kind, CancellationToken ct);
    Task<DatabaseRestorePlan> PlanRestoreAsync(UniverseContext c, BackupId id, CancellationToken ct);
    Task<DatabaseRestoreResult> RestoreAsync(UniverseContext c, DatabaseRestorePlan plan, CancellationToken ct);
    Task<BackupRetentionPlan> PlanRetentionAsync(UniverseContext c, BackupRetentionPolicy policy, CancellationToken ct);
    Task<BackupRetentionResult> RetainAsync(UniverseContext c, BackupRetentionPlan plan, CancellationToken ct);
    Task<GitRepositoryStatus> InspectGitAsync(UniverseContext c, CancellationToken ct);
    Task<IReadOnlyList<GitOperationReceipt>> ReceiptsAsync(UniverseContext c, CancellationToken ct);
    Task<GitCommitPlan> PlanCommitAsync(UniverseContext c, IReadOnlyList<ProductionAssetResult> results, string subject, CancellationToken ct);
    Task<GitCommitResult> CommitAsync(UniverseContext c, GitCommitPlan plan, CancellationToken ct);
    Task<GitOperationReceipt> RecoverAsync(UniverseContext c, GitOperationId id, CancellationToken ct);
    Task<GitCommitResult> RetryCommitAsync(UniverseContext c, GitOperationId id, CancellationToken ct);
    Task<GitPushResult> PushAsync(UniverseContext c, GitOperationId id, CancellationToken ct);
}

/// <summary>Typed Core adapters. Heavy work runs away from the UI; no repair, implicit initialization or inferred evidence.</summary>
public sealed class ProfessionalUiService(IAiAuditClient? auditor = null, IUniverseOperationDispatcher? dispatcher = null,
    GitProductionService? gitService = null, GitProductionInspector? gitInspector = null) : IProfessionalUiService
{
    private readonly GitProductionService _git = gitService ?? new();
    private readonly GitProductionInspector _gitInspector = gitInspector ?? new();
    public bool AuditAvailable => auditor is not null;
    public const long MaxInputPixels = PackagePreparationService.MaxInputPixels;
    private Task<T> Run<T>(UniverseContext c, CancellationToken ct, Func<T> operation)
        => (dispatcher ?? LocalUniverseOperationDispatcher.Instance).RunAsync(c, operation, ct);
    private static PipelineSnapshot Pipeline(UniverseContext c)
    {
        var recovery = new JobRecoveryScanner(c).Scan();
        var inbox = WorkspacePath(c, "inbox");
        var candidates = Directory.Exists(inbox) ? new InboxPackageDetector().Detect(inbox) : [];
        return new(c.Id, candidates, recovery.RecoverableJobs.Concat(recovery.CompletedJobs).Concat(recovery.FailedJobs)
            .OrderBy(j => j.JobId.Value, StringComparer.Ordinal).Select(j => new PipelineJob(j)).ToArray(),
            recovery.Issues.Issues.Select(i => new UiNotice(i.Message, i.Code, i.StopsProcessing ? UiTone.Error : UiTone.Warning)).ToArray());
    }
    public Task<PipelineSnapshot> PipelineAsync(UniverseContext c, CancellationToken ct) => Run(c, ct, () => Pipeline(c));
    public Task<DashboardSnapshot> DashboardAsync(UniverseContext c, CancellationToken ct) => Run<DashboardSnapshot>(c, ct, () =>
    {
        CatalogStatistics? statistics = null; CatalogPlanningReadModel? planning = null; PipelineSnapshot? pipeline = null;
        IReadOnlyList<BackupHistoryEntry>? backups = null; var notices = new List<UiNotice>();
        void Read(Action read) { ct.ThrowIfCancellationRequested(); try { read(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { notices.Add(UiNotice.From(e)); } }
        Read(() => { var catalog = new CatalogExplorerReader(c); statistics = catalog.Statistics(cancellation: ct); planning = catalog.Planning(ct); });
        Read(() => { pipeline = Pipeline(c); notices.AddRange(pipeline.Notices); });
        Read(() => { var history = new BackupHistoryReader().Read(c); backups = history.Entries; notices.AddRange(history.Issues.Issues.Select(i => new UiNotice(i.Message, i.Code, i.StopsProcessing ? UiTone.Error : UiTone.Warning))); });
        return new(c.Id, statistics, planning, pipeline, backups, notices.AsReadOnly());
    });
    public Task<CatalogPlanningReadModel> PlanningAsync(UniverseContext c, CancellationToken ct) => Run(c, ct, () => new CatalogExplorerReader(c).Planning(ct));
    public async Task SaveObjectiveAsync(UniverseContext c, CatalogObjective objective, CancellationToken ct) =>
        await Run(c, ct, () => { if (objective.UniverseId != c.Id) throw new InvalidOperationException("Objective scope mismatch."); new AssetCatalog(c).SaveObjective(objective); return 0; }).ConfigureAwait(false);
    public Task<BackupHistory> BackupsAsync(UniverseContext c, CancellationToken ct) => Run(c, ct, () => new BackupHistoryReader().Read(c));
    public Task<BackupHistoryEntry> BackupAsync(UniverseContext c, BackupKind kind, CancellationToken ct) => Run(c, ct, () => kind switch
    { BackupKind.Database => new DatabaseBackupService().Create(c), BackupKind.RepositorySnapshot => new RepositorySnapshotService().Create(c), BackupKind.GitBundle => new GitBundleService().Create(c), _ => throw new ArgumentOutOfRangeException(nameof(kind)) });
    public Task<DatabaseRestorePlan> PlanRestoreAsync(UniverseContext c, BackupId id, CancellationToken ct) => Run(c, ct, () => new DatabaseRestoreService().PlanRestore(c, id));
    public Task<DatabaseRestoreResult> RestoreAsync(UniverseContext c, DatabaseRestorePlan plan, CancellationToken ct) => Run(c, ct, () => new DatabaseRestoreService().Execute(c, plan));
    public Task<BackupRetentionPlan> PlanRetentionAsync(UniverseContext c, BackupRetentionPolicy policy, CancellationToken ct) => Run(c, ct, () => new BackupRetentionService().Plan(c, policy));
    public Task<BackupRetentionResult> RetainAsync(UniverseContext c, BackupRetentionPlan plan, CancellationToken ct) => Run(c, ct, () => new BackupRetentionService().Execute(c, plan));
    public Task<GitRepositoryStatus> InspectGitAsync(UniverseContext c, CancellationToken ct) => Run(c, ct, () => _gitInspector.Inspect(c));
    public Task<IReadOnlyList<GitOperationReceipt>> ReceiptsAsync(UniverseContext c, CancellationToken ct) => Run<IReadOnlyList<GitOperationReceipt>>(c, ct, () =>
    { return _git.ListOperations(c).Select(id => _git.ReadReceipt(c, id)).ToArray(); });
    public Task<GitCommitPlan> PlanCommitAsync(UniverseContext c, IReadOnlyList<ProductionAssetResult> results, string subject, CancellationToken ct) => Run(c, ct, () => _git.PrepareCommit(c, results, subject));
    public Task<GitCommitResult> CommitAsync(UniverseContext c, GitCommitPlan plan, CancellationToken ct) => Run(c, ct, () => _git.Commit(c, plan));
    public Task<GitOperationReceipt> RecoverAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Run(c, ct, () => _git.Recover(c, id));
    public Task<GitCommitResult> RetryCommitAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Run(c, ct, () => _git.RetryCommit(c, id));
    public Task<GitPushResult> PushAsync(UniverseContext c, GitOperationId id, CancellationToken ct) => Run(c, ct, () => _git.Push(c, id));

    public Task<PreparedPipelineAsset> PrepareAsync(UniverseContext c, InboxPackageCandidate candidate, CancellationToken ct) => Run<PreparedPipelineAsset>(c, ct, () =>
    {
        var inbox = WorkspacePath(c, "inbox"); var source = Path.GetFullPath(candidate.FullPath);
        if (!string.Equals(Path.GetDirectoryName(source), inbox, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(source), candidate.FileName, StringComparison.Ordinal) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Candidate must be an ordinary file in the active Inbox.");
        return Prepare(c, source, WorkspacePath(c, "staging"), WorkspacePath(c, "packages"), ct);
    });
    public Task<PreparedPipelineAsset> PrepareNormalizedAsync(UniverseContext c, ImageNormalizationResult candidate, CancellationToken ct) => Run(c, ct, () =>
    {
        var verified = new ImageNormalizationService(c).ReadReceipt(candidate.Receipt.OperationId);
        if (verified.Receipt.Decision != "Approved" || !verified.Receipt.IsPackage || verified.CandidatePath is null)
            throw new InvalidOperationException("Only a durably approved, verified ZIP candidate may enter preparation.");
        var attempt = Guid.NewGuid().ToString("N");
        return Prepare(c, verified.CandidatePath, Path.Combine(WorkspacePath(c, "staging"), "normalization", verified.Receipt.OperationId, attempt),
            Path.Combine(WorkspacePath(c, "packages"), "normalization", verified.Receipt.OperationId, attempt), ct);
    });
    private static PreparedPipelineAsset Prepare(UniverseContext c, string source, string stage, string extraction, CancellationToken ct)
    {
        WorkspacePath(c, "state");
        var staged = new InboxPackageStager().StageAsync(new(Path.GetFileName(source), source), stage, ct).GetAwaiter().GetResult();
        if (staged.Status != InboxPackageStagingStatus.Staged) throw new InvalidDataException("ZIP staging is not ready or collides; original left untouched.");
        var job = JobId.Create(); var store = new JobStateStore(c); store.Create(job); store.Transition(job, JobState.Staged);
        var extracted = new StagedPackageExtractor().ExtractAsync(staged.FinalStagedPath, extraction, ct).GetAwaiter().GetResult();
        if (extracted.Status != StagedPackageExtractionStatus.Extracted) throw new InvalidDataException("ZIP extraction rejected or collided.");
        try
        {
            var result = new PackagePreparationService().PrepareManual(c, job, extracted.FinalPath!);
            return new(c, job, result.Package, result.Processing, result.Archive, result.Validation);
        }
        catch (PackagePreparationStoppedException stopped) { throw new UiStoppedException(stopped.Issues); }
    }
    public async Task<AiAuditReport> AuditAsync(UniverseContext c, PreparedPipelineAsset prepared, CancellationToken ct)
    {
        RequireScope(c, prepared); if (auditor is null) throw new InvalidOperationException("The existing Phase 7 auditor is not configured.");
        var request = new AiAuditRequestBuilder().Build(prepared.Processing, prepared.Validation);
        return await auditor.AuditAsync(request, ct).ConfigureAwait(false);
    }
    public Task<ProductionAssetResult> ExecuteAsync(UniverseContext c, PreparedPipelineAsset prepared, AiAuditReport audit, CancellationToken ct) => Run(c, ct, () =>
    { RequireScope(c, prepared); if (!audit.Passed) throw new InvalidOperationException("Only a real PASS can execute."); return new AssetExecutionCoordinator(c).Execute(prepared.JobId, prepared.Package, prepared.Processing, prepared.Archive, audit, MaxInputPixels).Production; });
    private static void RequireScope(UniverseContext c, PreparedPipelineAsset prepared)
    { if (c != prepared.Context || prepared.IsRemoteProjection) throw new InvalidOperationException("Prepared evidence belongs to another context or is only a remote read projection."); }
    private static string WorkspacePath(UniverseContext c, string folder)
    {
        var path = Path.Combine(c.Storage.WorkspaceRoot, folder);
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Workspace ancestors must not be links.");
        if (File.Exists(path)) throw new InvalidDataException("Workspace child must be a directory.");
        return path;
    }
}
