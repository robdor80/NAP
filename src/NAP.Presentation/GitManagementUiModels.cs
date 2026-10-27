using NAP.Core;

namespace NAP.Presentation;

public sealed record GitAuthorizationInput(GitPublicationMode Mode, string Actor, IReadOnlyList<string> Prefixes,
    int MaximumJobs, bool AllowCommit, bool AllowPush);
public sealed record GitConfigurationView(UniverseId UniverseId, GitPublicationMode Mode, long Revision,
    string? AuthorizationId, bool AllowCommit, bool AllowPush, bool Revoked, string? RepositoryBinding,
    string? Branch, string? Upstream, string? RemoteName, string? IdentityFingerprint,
    string? RemoteFingerprint, string? ConfigFingerprint, IReadOnlyList<string> Prefixes, int MaximumJobs)
{
    public static GitConfigurationView From(UniverseContext c, GitPublicationPolicyV1? p) => new(c.Id,
        p?.Mode ?? GitPublicationMode.Disabled, p?.PolicyRevision ?? 0, p?.AuthorizationId, p?.AllowCommit ?? false,
        p?.AllowPush ?? false, p?.Revoked ?? false, p?.RepositoryBinding, p?.LocalBranch, p?.UpstreamRef,
        p?.RemoteName, p?.GitIdentityFingerprint, p?.RemoteFingerprint, p?.ConfigFingerprint,
        Array.AsReadOnly(p?.AllowedRelativePrefixes.ToArray() ?? []), p?.MaxJobsPerBatch ?? 5);
    public string Summary => Revision == 0 ? "Git desactivado; no existe autorización" :
        $"{Mode} · revisión {Revision} · commit: {AllowCommit} · push: {AllowPush}" + (Revoked ? " · REVOCADA" : "");
}
public sealed record GitAuthorizationReview(GitConfigurationView Configuration, string Actor, string Author, string Committer)
{
    public IReadOnlyList<string> Details => [Configuration.Summary, "Actor local: " + Actor,
        "Autor Git: " + Author, "Committer Git: " + Committer, "Binding: " + Configuration.RepositoryBinding,
        "Rama: " + Configuration.Branch, "Upstream: " + Configuration.Upstream, "Remoto: " + Configuration.RemoteName,
        "Identidad: " + Configuration.IdentityFingerprint, "Fingerprint remoto: " + Configuration.RemoteFingerprint,
        "Fingerprint config: " + Configuration.ConfigFingerprint, "AuthorizationId: " + Configuration.AuthorizationId,
        "Prefijos: " + string.Join(", ", Configuration.Prefixes), "Máximo Jobs: " + Configuration.MaximumJobs];
}
public sealed record GitRetryRequest(string Id, long Revision, string AuthorizationId, bool Publication);
public sealed record GitHostAccess(bool Available, string? Reason, bool MutationsAllowed = true);
public sealed record GitOutboxRow(string Id, bool IsBatch, GitOutboxState State, int Jobs,
    long Revision, string AuthorizationId, string? JobId, string? ItemId, string? AttemptId, string? BatchId,
    string? OperationId, string? PlanHash, string? CommitSha, string? Diagnostic)
{
    private GitConfigurationView? _displayConfiguration;
    [System.Text.Json.Serialization.JsonIgnore]
    public GitOutboxGuidance Guidance => GitOutboxGuidance.For(State, Diagnostic, _displayConfiguration, Revision, AuthorizationId);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Reason => Guidance.Reason;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Intervention => Guidance.InterventionLabel;
    public GitOutboxRow WithDisplayConfiguration(GitConfigurationView? configuration)
    { var row = this with { }; row._displayConfiguration = configuration; return row; }
    public string HumanState => State switch { GitOutboxState.Pending => "Pendiente de commit",
        GitOutboxState.Committed => "Commit local; remoto pendiente", GitOutboxState.Pushed => "Publicación acreditada según receipt",
        _ => "Revisión necesaria" };
    public string Summary => $"{HumanState} · {Jobs} Job(s) · revisión {Revision}";
    public string NextStep => Guidance.RecommendedAction;
    public static IReadOnlyList<GitOutboxRow> From(GitOutboxSnapshot s)
    {
        static string? Code(string? code) => code is null ? null : code.Length <= 100 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "git_review_required";
        var rows = new List<GitOutboxRow>();
        foreach (var b in s.Batches)
        {
            var i = s.Intents.Single(i => i.Id == b.IntentIds[0]);
            rows.Add(new(b.Id, true, b.State, b.IntentIds.Count, i.PolicyRevision, i.AuthorizationId,
                null, null, null, b.Id, b.OperationId.Value, b.PlanHash, b.CommitSha, Code(b.Diagnostic)));
        }
        foreach (var i in s.Intents)
        {
            var b = s.Batches.SingleOrDefault(b => b.Id == i.BatchId);
            rows.Add(new(i.Id, false, i.State, 1, i.PolicyRevision, i.AuthorizationId, i.JobId.Value,
                i.ItemId.Value, i.AttemptId.Value, i.BatchId, b?.OperationId.Value, b?.PlanHash, b?.CommitSha, Code(i.Diagnostic)));
        }
        return rows.AsReadOnly();
    }
}
public sealed record GitOutboxView(UniverseId UniverseId, IReadOnlyList<GitOutboxRow> Rows,
    IReadOnlyList<GitPolicyRevision> History);

public interface IGitManagementUiService
{
    Task<GitConfigurationView> ReadGitConfigurationAsync(UniverseContext c, CancellationToken ct);
    Task<GitAuthorizationReview> ProposeGitAuthorizationAsync(UniverseContext c, GitAuthorizationInput input, CancellationToken ct);
    Task SaveGitAuthorizationAsync(UniverseContext c, GitAuthorizationReview review, CancellationToken ct);
    Task RevokeGitAuthorizationAsync(UniverseContext c, long revision, string actor, CancellationToken ct);
    Task<GitOutboxView> ReadGitOutboxAsync(UniverseContext c, CancellationToken ct);
    Task<GitHostAccess> GitHostAccessAsync(UniverseContext c, CancellationToken ct);
    Task<string> RetryGitOutboxAsync(UniverseContext c, GitRetryRequest request, CancellationToken ct);
}
