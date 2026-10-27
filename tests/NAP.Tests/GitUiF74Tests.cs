using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public interface IGitUiTestService : IProfessionalUiService, IGitManagementUiService;
public sealed class GitUiF74Tests
{
    private sealed class Picker : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class Confirmation : IUserConfirmation
    {
        public bool Accept;
        public List<string> Titles { get; } = [];
        public Task<bool> ConfirmAsync(UiConfirmation c, CancellationToken ct) { Titles.Add(c.Title); return Task.FromResult(Accept); }
    }
    private static GitAuthorizationInput Input(GitPublicationMode mode = GitPublicationMode.Manual, bool commit = true, bool push = false)
        => new(mode, "Synthetic responsible", ["assets"], 2, commit, push);
    private static GitPublicationManagementService Management(GitOutboxF72Tests.Fixture f) => new(f.F.Context, f.Policy, f.Inspector, f.Git);
    private static string Inventory(GitOutboxF72Tests.Fixture f) => DurableExecutionTests.Inventory(f.F.Roots.ProductionRoot, f.F.Roots.StateRoot, f.Policy.DirectoryPath, f.Remote);
    private static ProfessionalUiService Local(GitProductionFixture f, GitPublicationPolicyStore p) =>
        new(new DurableExecutionTests.AuditClient(), gitService: f.Service, gitInspector: f.Inspector, gitPolicy: _ => p);
    private static ShellViewModel Shell(UniverseContext c, string root, IProfessionalUiService service, Confirmation confirmation, UniverseContext? second = null)
    {
        var store = new LocalUniverseSettingsStore(Path.Combine(root, "f74-ui-settings.json"));
        store.Save(second is null ? [c.Storage] : [c.Storage, second.Storage]);
        return new(new ExplorerViewModel(second is null ? [c.Profile] : [c.Profile, second.Profile], store, new ExplorerService(), new Picker()), service, confirmation);
    }

    [Theory]
    [InlineData(GitPublicationMode.Disabled, false, false)]
    [InlineData(GitPublicationMode.Manual, true, true)]
    [InlineData(GitPublicationMode.AutoCommitBatch, true, false)]
    [InlineData(GitPublicationMode.AutoCommitAndPushBatch, true, true)]
    public void FourModesRequireAReviewedExplicitSave(GitPublicationMode mode, bool commit, bool push)
    {
        using var f = new GitOutboxF72Tests.Fixture(authorize: false); var service = Management(f);
        var before = Inventory(f);
        Assert.Null(service.ReadConfiguration()); Assert.Empty(service.ReadOutbox().Intents);
        var proposed = service.Propose(mode, "Synthetic responsible", ["assets"], 2, commit, push);
        Assert.Equal(before, Inventory(f)); Assert.False(Directory.Exists(f.Policy.DirectoryPath));
        Assert.Equal("main", proposed.ReadPolicy().LocalBranch); Assert.Contains("f72@example.invalid", proposed.Author);
        Assert.Equal(64, proposed.ReadPolicy().GitIdentityFingerprint.Length);
        service.Save(proposed); var saved = f.Policy.Load()!;
        Assert.Equal(mode, saved.Mode); Assert.Equal(commit, saved.AllowCommit); Assert.Equal(push, saved.AllowPush);
        Assert.Equal(1, saved.PolicyRevision); Assert.Equal(f.BaseHead, f.RemoteHead);
        var unchanged = Inventory(f); _ = service.ReadConfiguration(); _ = service.ReadOutbox(); Assert.Equal(unchanged, Inventory(f));
    }

    [Fact]
    public void InvalidPermissionsPrefixesAndChangedReviewNeverGrantOrRewriteEvidence()
    {
        using var f = new GitOutboxF72Tests.Fixture(authorize: false); var service = Management(f); var before = Inventory(f);
        Assert.Throws<InvalidDataException>(() => service.Propose(GitPublicationMode.Manual, "actor", ["assets"], 1, false, true));
        Assert.Throws<InvalidDataException>(() => service.Propose(GitPublicationMode.AutoCommitBatch, "actor", ["assets"], 1, true, true));
        foreach (var prefixes in new[] { new[] { "." }, ["../assets"], ["/assets"], [".git"], ["assets/*"], ["assets", "assets"], [""], ["assets/../x"] })
            Assert.ThrowsAny<Exception>(() => service.Propose(GitPublicationMode.Manual, "actor", prefixes, 1, true, false));
        Assert.Throws<InvalidDataException>(() => service.Propose(GitPublicationMode.Manual, "actor", ["assets"], 21, true, false));
        Assert.Equal(before, Inventory(f));
        var proposed = service.Propose(GitPublicationMode.Manual, "actor", ["assets"], 1, true, false);
        f.Run("config", "user.email", "changed@example.invalid"); var changed = Inventory(f);
        Assert.Throws<InvalidDataException>(() => service.Save(proposed)); Assert.Equal(changed, Inventory(f)); Assert.Null(f.Policy.Load());
        f.Run("config", "user.email", "f72@example.invalid");
        var a = service.Propose(GitPublicationMode.Manual, "actor", ["assets"], 1, true, false);
        var stale = service.Propose(GitPublicationMode.Manual, "actor", ["assets"], 1, true, false);
        service.Save(a); var granted = Inventory(f);
        Assert.Throws<InvalidDataException>(() => service.Save(stale)); Assert.Equal(granted, Inventory(f));
        using var other = new GitOutboxF72Tests.Fixture(authorize: false);
        Assert.ThrowsAny<Exception>(() => Management(other).Save(stale)); Assert.Null(other.Policy.Load());
    }

    [Fact]
    public void RevocationWithoutRemoteBlocksPendingAndCorruptAnchorStopsWithoutRepair()
    {
        using var f = new GitOutboxF72Tests.Fixture(); f.Complete();
        BackupTestSupport.Set(f.Service, "Checkpoint", (Action<string>)(point => { if (point == "reconciled") throw new SyntheticCut(); }));
        Assert.Throws<SyntheticCut>(() => f.Service.RunOnce(f.Owner)); var pending = Assert.Single(f.Outbox.Read().Intents);
        var prior = f.Policy.Load()!; var offline = f.Remote + ".offline"; Directory.Move(f.Remote, offline);
        var service = Management(f); service.Revoke(prior.PolicyRevision, "Synthetic revoker");
        var revoked = f.Policy.Load()!; Assert.True(revoked.Revoked); Assert.False(revoked.AllowCommit); Assert.False(revoked.AllowPush);
        Assert.Equal(GitPublicationMode.Disabled, revoked.Mode); Assert.NotEqual(prior.AuthorizationId, revoked.AuthorizationId);
        Assert.Equal(prior.PolicyRevision + 1, revoked.PolicyRevision);
        var blocked = Assert.Single(f.Outbox.Read().Intents); Assert.Equal(pending.Id, blocked.Id); Assert.Equal(pending.AuthorizationId, blocked.AuthorizationId);
        Assert.Equal(GitOutboxState.Blocked, blocked.State); Assert.Equal("git-disabled", f.Service.RunOnce(f.Owner)); f.CheckCompleted();
        Directory.Move(offline, f.Remote); Assert.Equal(f.BaseHead, f.RemoteHead);
        var anchor = Path.Combine(f.F.Roots.StateRoot, "git-outbox-v1", "outbox.json"); File.AppendAllText(anchor, "tampered");
        var bad = Inventory(f); Assert.ThrowsAny<Exception>(() => service.ReadConfiguration());
        Assert.ThrowsAny<Exception>(() => service.Revoke(revoked.PolicyRevision, "actor")); Assert.Equal(bad, Inventory(f));
    }
    private sealed class SyntheticCut : Exception;

    [Fact]
    public void OutboxProjectionPreservesCorrelationAndRejectsReceiptDiscordanceReadOnly()
    {
        using var f = new GitOutboxF72Tests.Fixture(); f.Complete(); Assert.Equal("git-committed", f.Service.RunOnce(f.Owner));
        var service = Management(f); var before = Inventory(f); var state = service.ReadOutbox(); var rows = GitOutboxRow.From(state);
        Assert.Equal(before, Inventory(f)); var batch = Assert.Single(rows, r => r.IsBatch); var intent = Assert.Single(rows, r => !r.IsBatch);
        Assert.Equal(1, batch.Jobs); Assert.Equal(batch.BatchId, intent.BatchId); Assert.Equal(batch.OperationId, intent.OperationId);
        Assert.Equal(state.Intents[0].JobId.Value, intent.JobId); Assert.Equal(state.Intents[0].AttemptId.Value, intent.AttemptId);
        Assert.Equal("Commit local; remoto pendiente", batch.HumanState); Assert.NotNull(batch.CommitSha); Assert.NotNull(batch.PlanHash);
        // All four labels derive solely from durable state; no raw diagnostic is displayed.
        var variants = rows.Select(r => r with { State = GitOutboxState.Pushed }).ToArray();
        Assert.Contains("histórica", variants[0].NextStep);
        var receiptPath = Path.Combine(f.F.Roots.StateRoot, "git-production", batch.OperationId + ".json");
        File.Move(receiptPath, receiptPath + ".missing"); var discordant = Inventory(f);
        Assert.ThrowsAny<Exception>(() => service.ReadOutbox()); Assert.Equal(discordant, Inventory(f)); f.CheckCompleted();
    }

    [Fact]
    public async Task EditorSelectionCancellationRevocationAndOwnerReasonsAreExplicit()
    {
        using var f = new GitProductionFixture(); var p = new GitPublicationPolicyStore(f.Context, Path.Combine(f.Production.Root, "f74-policy"));
        var service = Local(f, p); var confirmation = new Confirmation(); using var shell = Shell(f.Context, f.Production.Root, service, confirmation);
        await shell.InitializeAsync(); shell.NavigateTo("Git"); await shell.LastRefresh;
        Assert.Equal(UiPageState.Ready, shell.Git.State); Assert.Contains("no existe autorización", shell.Git.ConfigurationSummary);
        Assert.False(Directory.Exists(p.DirectoryPath));
        foreach (var mode in Enum.GetValues<GitPublicationMode>()) { shell.Git.Mode = mode; Assert.Null(p.Load()); }
        shell.Git.Mode = GitPublicationMode.Manual; shell.Git.Actor = "Synthetic actor"; shell.Git.Prefixes = "assets";
        shell.Git.AllowPush = true; Assert.False(shell.Git.AllowPush); shell.Git.AllowCommit = true; shell.Git.AllowPush = true;
        await shell.ProposeGitAuthorization.ExecuteAsync(); Assert.Null(shell.Git.Review); Assert.Null(p.Load());
        confirmation.Accept = true; await shell.ProposeGitAuthorization.ExecuteAsync(); Assert.NotNull(shell.Git.Review); Assert.Null(p.Load());
        confirmation.Accept = false; await shell.SaveGitAuthorization.ExecuteAsync(); Assert.Null(p.Load());
        shell.Git.MaximumJobs = "3"; Assert.Null(shell.Git.Review); Assert.False(shell.SaveGitAuthorization.CanExecute(null));
        confirmation.Accept = true; await shell.ProposeGitAuthorization.ExecuteAsync(); await shell.SaveGitAuthorization.ExecuteAsync();
        Assert.Equal(1, p.Load()!.PolicyRevision); Assert.True(shell.Git.CanPush);
        Assert.False(shell.RetryPublicationGit.CanExecute(null)); Assert.Contains("Sin owner", shell.RetryPublicationGit.DisabledReason);
        var head = f.Git("rev-parse", "HEAD"); confirmation.Accept = false; await shell.RevokeGitAuthorization.ExecuteAsync(); Assert.False(p.Load()!.Revoked);
        var offline = f.Remote + ".offline"; Directory.Move(f.Remote, offline);
        await shell.InspectGit.ExecuteAsync(); Assert.Equal(UiPageState.Stopped, shell.Git.State);
        confirmation.Accept = true; Assert.True(shell.RevokeGitAuthorization.CanExecute(null)); await shell.RevokeGitAuthorization.ExecuteAsync();
        Assert.True(p.Load()!.Revoked); Assert.False(shell.Git.CanCommit); Assert.Equal(head, f.Git("rev-parse", "HEAD"));
        Assert.Contains("Autorizar y guardar Git", confirmation.Titles); Assert.Contains("Revocar autorización Git", confirmation.Titles);
        Directory.Move(offline, f.Remote);
    }

    [Fact]
    public async Task ManualCommitAndPushHaveIndependentCancelPathsAndNativeEvidence()
    {
        using var f = new GitProductionFixture(); var p = new GitPublicationPolicyStore(f.Context, Path.Combine(f.Production.Root, "f74-policy"));
        var management = new GitPublicationManagementService(f.Context, p, f.Inspector, f.Service);
        management.Save(management.Propose(GitPublicationMode.Manual, "actor", ["assets"], 1, true, true));
        var confirmation = new Confirmation { Accept = true }; using var shell = Shell(f.Context, f.Production.Root, Local(f, p), confirmation);
        new AssetCatalog(f.Context).Initialize();
        var zip = Path.Combine(f.Context.Storage.InboxRoot, "f74-synthetic.zip"); ZipFile.CreateFromDirectory(f.Production.Source.PackageRoot, zip);
        await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh;
        shell.Pipeline.SelectedCandidate = shell.Pipeline.Candidates.Single(); await shell.PreparePackage.ExecuteAsync(); await shell.AuditPackage.ExecuteAsync(); await shell.ExecutePackage.ExecuteAsync();
        shell.NavigateTo("Git"); await shell.LastRefresh; shell.Git.Subject = "Synthetic F74 verified session";
        var head = f.Git("rev-parse", "HEAD"); var before = ArchiveTestFixture.Snapshot(f.Context.Storage.StateRoot);
        confirmation.Accept = false; await shell.CommitGit.ExecuteAsync(); Assert.Equal(head, f.Git("rev-parse", "HEAD"));
        Assert.Equal(before.OrderBy(p => p.Key), ArchiveTestFixture.Snapshot(f.Context.Storage.StateRoot).OrderBy(p => p.Key)); Assert.Empty(shell.Git.Receipts);
        confirmation.Accept = true; await shell.CommitGit.ExecuteAsync(); var receipt = Assert.Single(shell.Git.Receipts); Assert.Equal(GitReceiptState.Committed, receipt.State);
        shell.Git.SelectedReceipt = receipt; confirmation.Accept = false; await shell.PushGit.ExecuteAsync(); Assert.Equal(head, f.RemoteGit("rev-parse", "main"));
        confirmation.Accept = true; await shell.PushGit.ExecuteAsync(); Assert.Equal(receipt.CommitSha, f.RemoteGit("rev-parse", "main"));
        Assert.Equal(GitReceiptState.Pushed, Assert.Single(shell.Git.Receipts).State); Assert.Equal(receipt.OperationId, shell.Git.SelectedReceipt!.OperationId);
        Assert.Equal(2, confirmation.Titles.Count(t => t == "Crear commit")); Assert.Equal(2, confirmation.Titles.Count(t => t == "Publicar commit"));
    }

    [Fact]
    public async Task LateGitReadCannotCrossUniversesAndResetDropsTheProposal()
    {
        using var a = new GitProductionFixture("alpha"); using var b = new GitProductionFixture("beta");
        var pa = new GitPublicationPolicyStore(a.Context, Path.Combine(a.Production.Root, "f74-policy"));
        var service = DispatchProxy.Create<IGitUiTestService, ProfessionalServiceProxy>(); var proxy = (ProfessionalServiceProxy)service;
        proxy.Inner = new ProfessionalUiService(gitPolicy: c => c.Id == a.Context.Id ? pa : new(c, Path.Combine(b.Production.Root, "f74-policy")));
        var confirmation = new Confirmation { Accept = true }; using var shell = Shell(a.Context, a.Production.Root, service, confirmation, b.Context);
        await shell.InitializeAsync(); shell.NavigateTo("Git"); await shell.LastRefresh;
        var gate = new TaskCompletionSource<GitConfigurationView>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        proxy.Overrides[nameof(IGitManagementUiService.ReadGitConfigurationAsync)] = args =>
        {
            if (((UniverseContext)args[0]!).Id == a.Context.Id) { entered.TrySetResult(); return gate.Task; }
            return ((IGitManagementUiService)proxy.Inner).ReadGitConfigurationAsync((UniverseContext)args[0]!, (CancellationToken)args[1]!);
        };
        shell.Git.Actor = "old actor"; shell.Git.SelectedReceipt = null;
        var pending = shell.RefreshCurrentAsync(); await entered.Task;
        shell.SelectedUniverse = b.Context.Profile; await shell.LastRefresh;
        gate.SetResult(GitConfigurationView.From(a.Context, null)); await pending;
        Assert.Equal(b.Context.Id, shell.Git.Configuration!.UniverseId); Assert.Equal("", shell.Git.Actor); Assert.Null(shell.Git.Review);
        Assert.Null(shell.Git.SelectedOutbox); Assert.Null(pa.Load());
    }

    [Fact]
    public async Task AuthenticatedIpcKeepsProposalOnHostDeduplicatesSaveAndRetriesWithRealOwner()
    {
        using var f = new AutomationHostTests.Fixture(); var c = f.A.Context; InitializeGit(c, f.Root);
        var git = new GitProductionService(); var inspector = new GitProductionInspector(); GitProductionFixture.Enable(git); GitProductionFixture.Enable(inspector);
        GitPublicationPolicyStore Policy(UniverseContext context) => new(context, Path.Combine(f.Root, context.Id.Value + "-git-policy"));
        var auditor = new DurableExecutionTests.AuditClient();
        await using var host = new AutomationHost(f.Control, [c, f.B.Context], f.Options, auditor); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [c, f.B.Context], Path.Combine(f.Root, "desktop-requests"), auditor, git, inspector, Policy);
        var connection = new AutomationConnection("nap-f74-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync); var client = new AutomationHostClient(connection); using var remote = new RemoteDesktopService(client, true);
        Assert.Equal(0, (await remote.ReadGitConfigurationAsync(c, default)).Revision);
        Assert.True((await remote.GitHostAccessAsync(c, default)).Available);
        var input = Input(GitPublicationMode.AutoCommitAndPushBatch, true, true);
        var view = AutomationProtocol.Read<AutomationReadResult>(await client.CallAsync(AutomationOperation.GitAuthorizationPropose, c.Id.Value, input));
        var reference = new AutomationReference(view.Reference!);
        await Assert.ThrowsAsync<AutomationException>(() => client.CallAsync(AutomationOperation.GitAuthorizationSave, f.B.Context.Id.Value, reference));
        Assert.Null(Policy(c).Load());
        await Assert.ThrowsAsync<InvalidOperationException>(() => remote.SaveGitAuthorizationAsync(c, new(GitConfigurationView.From(c, null), "forged", "forged", "forged"), default));
        var command = "command_" + Guid.NewGuid().ToString("N");
        var first = await client.CallAsync(AutomationOperation.GitAuthorizationSave, c.Id.Value, reference, requestId: command);
        var replay = await client.CallAsync(AutomationOperation.GitAuthorizationSave, c.Id.Value, reference, requestId: command);
        Assert.Equal(first.GetRawText(), replay.GetRawText()); Assert.Equal(1, Policy(c).Load()!.PolicyRevision);
        var originalRemote = Path.Combine(f.Root, "bare.git"); var offline = originalRemote + ".offline";
        var row = await host.RunManualAsync(c.Id, scope =>
        {
            var admitted = f.A.Admit(scope.Owner, f.A.Zip("f74-one.zip"));
            Assert.Equal(PreparationCode.AuditPass, f.A.Service.Audit(scope.Owner, admitted.ItemId!, auditor).Code);
            Assert.Equal(DurableExecutionCode.Completed, new DurableExecutionService(c, scope.Queue).Execute(scope.Owner, admitted.ItemId!).Code);
            var service = new GitBatchPublicationService(c, scope.Queue, Policy(c), git);
            BackupTestSupport.Set(service, "Checkpoint", (Action<string>)(point => { if (point == "before-push") Directory.Move(originalRemote, offline); }));
            Assert.Equal("git-push-retry", service.RunOnce(scope.Owner));
            return new GitPublicationManagementService(c, Policy(c), inspector, git).ReadOutbox();
        });
        Directory.Move(offline, originalRemote); var batch = row.Batches.Single(); var p = Policy(c).Load()!;
        Assert.Equal("git-pushed", await remote.RetryGitOutboxAsync(c, new(batch.Id, p.PolicyRevision, p.AuthorizationId, true), default));
        var displayed = await remote.ReadGitOutboxAsync(c, default); Assert.All(displayed.Rows, r => Assert.Equal(GitOutboxState.Pushed, r.State));
        Assert.Equal(batch.OperationId.Value, displayed.Rows[0].OperationId); Assert.Equal(batch.CommitSha, GitProductionFixture.Run(originalRemote, "rev-parse", "main"));
        var before = DurableExecutionTests.Inventory(c.Storage.ProductionRoot, c.Storage.StateRoot, Policy(c).DirectoryPath, originalRemote);
        await Assert.ThrowsAsync<AutomationException>(() => client.CallAsync(AutomationOperation.GitOutboxRetry, c.Id.Value, new { Id = batch.Id, Revision = 1, AuthorizationId = p.AuthorizationId, Publication = true, Path = "/arbitrary" }));
        Assert.Equal(before, DurableExecutionTests.Inventory(c.Storage.ProductionRoot, c.Storage.StateRoot, Policy(c).DirectoryPath, originalRemote));
        await host.QueryAsync(c.Id, scope => { Assert.All(scope.Queue.List(0, 10), i => Assert.Equal(QueueState.Completed, i.State)); return true; });
        await remote.RevokeGitAuthorizationAsync(c, p.PolicyRevision, "Synthetic revoker", default);
        var revoked = DurableExecutionTests.Inventory(c.Storage.ProductionRoot, c.Storage.StateRoot, Policy(c).DirectoryPath, originalRemote);
        await Assert.ThrowsAnyAsync<Exception>(() => remote.RetryGitOutboxAsync(c, new(batch.Id, p.PolicyRevision, p.AuthorizationId, true), default));
        Assert.Equal(revoked, DurableExecutionTests.Inventory(c.Storage.ProductionRoot, c.Storage.StateRoot, Policy(c).DirectoryPath, originalRemote));
        Assert.True(Policy(c).Load()!.Revoked);
    }
    private static void InitializeGit(UniverseContext c, string root)
    {
        var production = c.Storage.ProductionRoot; var remote = Path.Combine(root, "bare.git");
        GitProductionFixture.Run(production, "init", "--initial-branch=main");
        GitProductionFixture.Run(production, "config", "user.name", "Synthetic F74"); GitProductionFixture.Run(production, "config", "user.email", "f74@example.invalid");
        GitProductionFixture.Run(production, "add", "--", "foreign.txt"); GitProductionFixture.Run(production, "commit", "-m", "synthetic base");
        GitProductionFixture.Run(root, "init", "--bare", "--initial-branch=main", remote);
        GitProductionFixture.Run(production, "remote", "add", "origin", remote); GitProductionFixture.Run(production, "push", "-u", "origin", "main");
    }
}
