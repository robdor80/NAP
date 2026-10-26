namespace NAP.Core;

/// <summary>Existing UI confirmations remain separate. Each call validates the new explicit local Git grant.</summary>
public sealed class GitPublicationManualService
{
    private readonly GitProductionService _git;
    private readonly GitProductionInspector _inspector;
    private readonly GitPublicationPolicyStore? _policy;
    public GitPublicationManualService(GitProductionService? git = null, GitProductionInspector? inspector = null, GitPublicationPolicyStore? policy = null)
    { _git = git ?? new(); _inspector = inspector ?? new(); _policy = policy; }
    private T Authorized<T>(UniverseContext c, IEnumerable<string> paths, bool push, Func<GitPublicationPolicyStore, GitPublicationPolicyV1, T> action)
    {
        using var owner = AutomationManualAccess.Enter(c);
        var store = _policy ?? new GitPublicationPolicyStore(c); using var lease = store.Lock();
        var p = store.LoadLocked() ?? throw new InvalidDataException("Git is Disabled until explicitly authorized.");
        using (var production = GitProductionSafety.Acquire(c)) store.Require(p, _inspector.InspectLocked(c), _git.Git, paths, push);
        return action(store, p);
    }
    public GitCommitPlan PrepareCommit(UniverseContext c, IReadOnlyList<ProductionAssetResult> results, string message) =>
        Authorized(c, results.SelectMany(r => r.FilesVerified.Select(f => r.RelativeDirectory + "/" + f.FileName)), false,
            (_, p) => { var plan = _git.PrepareCommit(c, results, message); plan.PublicationPolicyHash = AutomationJson.Hash(p).Hex; return plan; });
    public GitCommitResult Commit(UniverseContext c, GitCommitPlan plan) => Authorized(c, plan.Selection.OwnedFiles.Select(f => f.RelativePath), false,
        (store, p) =>
        {
            if (plan.PublicationPolicyHash != AutomationJson.Hash(p).Hex) throw new InvalidDataException("Manual plan authorization changed; review a new plan.");
            return _git.CommitBound(c, plan, GitOperationId.New(), () => store.Require(p, _inspector.InspectLocked(c), _git.Git, plan.Selection.OwnedFiles.Select(f => f.RelativePath)));
        });
    public GitCommitResult RetryCommit(UniverseContext c, GitOperationId id)
    {
        using var owner = AutomationManualAccess.Enter(c);
        var receipt = _git.ReadReceipt(c, id);
        return Authorized(c, receipt.Plan.Selection.OwnedFiles.Select(f => f.RelativePath), false,
            (store, p) => _git.RetryCommitBound(c, id, () => store.Require(p, _inspector.InspectLocked(c), _git.Git, receipt.Plan.Selection.OwnedFiles.Select(f => f.RelativePath))));
    }
    public GitOperationReceipt Recover(UniverseContext c, GitOperationId id)
    {
        using var owner = AutomationManualAccess.Enter(c);
        var receipt = _git.ReadReceipt(c, id);
        return Authorized(c, receipt.Plan.Selection.OwnedFiles.Select(f => f.RelativePath), false, (_, _) => _git.Recover(c, id));
    }
    public GitPushResult Push(UniverseContext c, GitOperationId id)
    {
        using var owner = AutomationManualAccess.Enter(c);
        var receipt = _git.ReadReceipt(c, id);
        return Authorized(c, receipt.Plan.Selection.OwnedFiles.Select(f => f.RelativePath), true, (_, _) => _git.Push(c, id));
    }
    public GitRepositoryStatus Inspect(UniverseContext c) => Authorized(c, [], false, (_, _) => _inspector.Inspect(c));
}
