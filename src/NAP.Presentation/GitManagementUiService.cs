using System.Runtime.CompilerServices;
using NAP.Core;

namespace NAP.Presentation;

public sealed partial class ProfessionalUiService
{
    private readonly ConditionalWeakTable<GitAuthorizationReview, GitAuthorizationProposal> _gitReviews = new();
    private GitPublicationPolicyStore Policy(UniverseContext c) => gitPolicy?.Invoke(c) ?? new(c);
    private GitPublicationManagementService Management(UniverseContext c) => new(c, Policy(c), _gitInspector, _git);
    public Task<GitConfigurationView> ReadGitConfigurationAsync(UniverseContext c, CancellationToken ct) => Run(c, ct,
        () => GitUiStoppedException.Guard(GitUiFailure.ConfigurationIntegrity, () => GitConfigurationView.From(c, Management(c).ReadConfiguration())));
    public Task<GitAuthorizationReview> ProposeGitAuthorizationAsync(UniverseContext c, GitAuthorizationInput input, CancellationToken ct) => Run(c, ct, () => GitUiStoppedException.Guard(GitUiFailure.AuthorizationInput, () =>
    {
        var p = Management(c).Propose(input.Mode, input.Actor, input.Prefixes, input.MaximumJobs, input.AllowCommit, input.AllowPush);
        var view = new GitAuthorizationReview(GitConfigurationView.From(c, p.ReadPolicy()), input.Actor, p.Author, p.Committer);
        _gitReviews.Add(view, p); return view;
    }));
    public async Task SaveGitAuthorizationAsync(UniverseContext c, GitAuthorizationReview review, CancellationToken ct) => await Run(c, ct, () => GitUiStoppedException.Guard(GitUiFailure.AuthorizationSave, () =>
    {
        if (review.Configuration.UniverseId != c.Id || !_gitReviews.TryGetValue(review, out var native))
            throw new InvalidDataException("Git review is not an owned proposal in this universe.");
        _gitReviews.Remove(review); // A failed/stale decision must be reviewed anew, never silently refreshed.
        Management(c).Save(native); return true;
    })).ConfigureAwait(false);
    public async Task RevokeGitAuthorizationAsync(UniverseContext c, long revision, string actor, CancellationToken ct) =>
        await Run(c, ct, () => GitUiStoppedException.Guard(GitUiFailure.Revocation, () => { Management(c).Revoke(revision, actor); return true; })).ConfigureAwait(false);
    public Task<GitOutboxView> ReadGitOutboxAsync(UniverseContext c, CancellationToken ct) => Run(c, ct, () => GitUiStoppedException.Guard(GitUiFailure.OutboxIntegrity, () =>
    {
        var state = Management(c).ReadOutbox(); return new GitOutboxView(c.Id, GitOutboxRow.From(state), state.PolicyHistory);
    }));
    public Task<GitHostAccess> GitHostAccessAsync(UniverseContext c, CancellationToken ct) => dispatcher is HostedUniverseOperationDispatcher hosted
        ? hosted.GitAccessAsync(c, ct) : Task.FromResult(new GitHostAccess(false, "Sin owner de AutomationHost: reintentos Outbox bloqueados. El host actual solo admite laboratorio."));
    public Task<string> RetryGitOutboxAsync(UniverseContext c, GitRetryRequest request, CancellationToken ct)
    {
        if (dispatcher is not HostedUniverseOperationDispatcher hosted)
            throw new InvalidOperationException("A legitimate host owner is required; no local fallback.");
        return hosted.RunOwnedAsync(c, scope =>
        {
            if (request.Id.Length != 64 || request.Id.Any(ch => !char.IsAsciiHexDigit(ch))) throw new InvalidDataException("Invalid Outbox identity.");
            var p = Policy(c).Load() ?? throw new InvalidDataException("Git authorization absent.");
            if (p.PolicyRevision != request.Revision || p.AuthorizationId != request.AuthorizationId) throw new InvalidDataException("Git retry review expired.");
            var service = new GitBatchPublicationService(c, scope.Queue, Policy(c), _git);
            return request.Publication ? service.RetryPublication(scope.Owner, request.Id) : service.RetryBlocked(scope.Owner, request.Id);
        }, ct);
    }
}
