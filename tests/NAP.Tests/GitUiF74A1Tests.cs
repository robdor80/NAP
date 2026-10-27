using System.Text.Json;
using NAP.Core;
using NAP.Presentation;
using Xunit;
using Xunit.Abstractions;

namespace NAP.Tests;

/// <summary>Exactly two pure presentation checks; no filesystem, Git process, ZIP or network.</summary>
public sealed class GitUiF74A1Tests(ITestOutputHelper output)
{
    private static GitOutboxRow Row(GitOutboxState state, string? diagnostic) => new(
        new string('a', 64), true, state, 1, 1, "authorization_original", null, null, null,
        new string('b', 64), "g_" + new string('c', 32), new string('d', 64), new string('e', 40), diagnostic);
    private static GitConfigurationView Policy(bool revoked = false, long revision = 1, bool commit = true, bool push = true) =>
        new(new("synthetic"), GitPublicationMode.AutoCommitAndPushBatch, revision,
            revision == 1 ? "authorization_original" : "authorization_new", commit, push, revoked,
            new string('f', 64), "main", "origin/main", "origin", null, null, null, ["assets"], 1);

    [Fact]
    public void KnownUnknownAndHistoricalDiagnosticsGiveSpecificSafeGuidanceWithoutChangingEvidence()
    {
        var cases = new (string Code, string Reason, string Action, GitOutboxIntervention Intervention)[]
        {
            ("git_policy_changed", "sustituida o revocada", "no se reactiva", GitOutboxIntervention.ConfigurationReview),
            (NapIssueCodes.GitIdentityMissing, "identidad Git", "user.name", GitOutboxIntervention.ConfigurationReview),
            (NapIssueCodes.GitPlanStale, "configuración, identidad, refs", "No regeneres", GitOutboxIntervention.ConfigurationReview),
            (NapIssueCodes.GitRemoteInvalid, "remoto o upstream", "fingerprints", GitOutboxIntervention.ConfigurationReview),
            (NapIssueCodes.GitRemoteChanged, "divergencia", "compara SHA", GitOutboxIntervention.ConfigurationReview),
            (NapIssueCodes.GitReceiptInvalid, "RequireNoPending", "recibo anterior", GitOutboxIntervention.AdministrativeReview),
            ("native_evidence_unavailable", "evidencia nativa", "ownership", GitOutboxIntervention.AdministrativeReview),
            (NapIssueCodes.GitRollbackFailed, "integridad", "conserva todas las evidencias", GitOutboxIntervention.AdministrativeReview),
            (NapIssueCodes.GitBusy, "ocupado", "Running", GitOutboxIntervention.ExplicitReview),
            (NapIssueCodes.GitRemoteUnavailable, "remoto", "autorización original", GitOutboxIntervention.ExplicitReview),
            ("git_push_retry_3", "agotó", "cuarto push automático", GitOutboxIntervention.ExplicitReview),
            ("git_push_exhausted_verify_3", "comprobación remota", "vía local nativa", GitOutboxIntervention.ExplicitReview)
        };
        foreach (var item in cases)
        {
            var original = Row(GitOutboxState.Blocked, item.Code); var before = JsonSerializer.Serialize(original);
            var view = original.WithDisplayConfiguration(null);
            Assert.Contains(item.Reason, view.Reason); Assert.Contains(item.Action, view.NextStep);
            Assert.Equal(item.Intervention, view.Guidance.Intervention);
            Assert.Equal(before, JsonSerializer.Serialize(original)); Assert.Equal(item.Code, view.Diagnostic);
            Assert.Equal(original.State, view.State); Assert.Equal(original.Id, view.Id);
            Assert.Equal(original.AuthorizationId, view.AuthorizationId); Assert.Equal(original.OperationId, view.OperationId);
            Assert.Equal(original.PlanHash, view.PlanHash); Assert.Equal(original.CommitSha, view.CommitSha);
            Assert.DoesNotContain("reset", view.NextStep); Assert.DoesNotContain("force", view.NextStep);
            Assert.DoesNotContain("pull", view.NextStep); Assert.DoesNotContain("borrar", view.NextStep);
        }
        var revoked = Row(GitOutboxState.Blocked, "git_policy_changed").WithDisplayConfiguration(Policy(revoked: true));
        var historical = Row(GitOutboxState.Blocked, "git_policy_changed").WithDisplayConfiguration(Policy(revision: 2));
        var missing = Row(GitOutboxState.Blocked, "git_publication_blocked").WithDisplayConfiguration(Policy(commit: false));
        Assert.Contains("revocada", revoked.Reason); Assert.Contains("revisión anterior", historical.Reason); Assert.Contains("No hay permiso", missing.Reason);
        var commitOnly = Row(GitOutboxState.Committed, null).WithDisplayConfiguration(Policy(push: false));
        Assert.Contains("no concede push", commitOnly.Reason);
        var exhausted = Row(GitOutboxState.Committed, "git_push_retry_3").WithDisplayConfiguration(Policy(push: false));
        Assert.Contains("agotó", exhausted.Reason); Assert.Equal(GitOutboxState.Committed, exhausted.State);
        var verifying = Row(GitOutboxState.Committed, "git_push_exhausted_verify_1");
        Assert.Contains("comprobación remota reservada", verifying.Reason);
        foreach (var unknown in new string?[] { "git_future_code", "git_push_retry_4", "https://secret:password@host/private", "raw stderr\nprivate", null })
        {
            var row = Row(GitOutboxState.Blocked, unknown).WithDisplayConfiguration(Policy(revoked: true));
            Assert.Equal(GitOutboxIntervention.AdministrativeReview, row.Guidance.Intervention);
            Assert.Contains("revisión administrativa", row.NextStep);
            Assert.DoesNotContain("password", row.Reason + row.NextStep); Assert.DoesNotContain("stderr", row.Reason + row.NextStep);
        }
        Assert.Contains("histórica", Row(GitOutboxState.Pushed, null).Reason);
        Assert.Contains("no garantiza el tip remoto actual", Row(GitOutboxState.Pushed, null).NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Guidance", JsonSerializer.Serialize(revoked));
        output.WriteLine("12 representative code/cause/action checks; policy revocation/history/permissions, Committed exhausted budgets, unknown/hostile codes and Pushed historical fact. Evidence and authorization fields preserved; ZIP=0, Git=0, filesystem=0.");
    }

    [Fact]
    public void RepositoryIdentityShowsOnlyFolderAndCurrentUniverseWithoutAncestorsOrIpcData()
    {
        var alpha = GitRepositoryIdentity.Label(new("alpha"), "/private/credential-store/SharedRepo/");
        var beta = GitRepositoryIdentity.Label(new("beta"), @"C:\Users\private-name\credential-store\SharedRepo\");
        Assert.Equal("Repositorio: SharedRepo · universo alpha", alpha);
        Assert.Equal("Repositorio: SharedRepo · universo beta", beta);
        Assert.DoesNotContain("private", alpha + beta); Assert.DoesNotContain("credential", alpha + beta);
        Assert.DoesNotContain("C:", beta); Assert.DoesNotContain("Users", beta);
        Assert.Equal("Repositorio de producción aún no configurado", GitRepositoryIdentity.Label(null, null));
        foreach (var root in new[] { "/", @"C:\", "/parent/private@token", "/parent/folder\nsecret", "/parent/" + new string('x', 81) })
            Assert.Equal("Repositorio: carpeta de producción · universo alpha", GitRepositoryIdentity.Label(new("alpha"), root));
        Assert.Equal("Repositorio: Producción local · universo alpha", GitRepositoryIdentity.Label(new("alpha"), "/private/Producción local"));
        // The label is projected locally from the selected context; no extra root/name enters the existing wire DTO.
        var wire = JsonSerializer.Serialize(Policy());
        Assert.DoesNotContain("SharedRepo", wire); Assert.DoesNotContain("ProductionRoot", wire);
        output.WriteLine("Local labels for Unix/Windows roots and universe switches; ancestors, drive and unsafe leaf names omitted, missing context explicit. No policy/IPC field added; ZIP=0, Git=0, filesystem=0.");
    }
}
