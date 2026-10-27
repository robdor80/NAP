using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class GitVisualLabF81Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NAP-F81-Lab-" + Guid.NewGuid().ToString("N"));
    private string App => Path.Combine(_root, "app");
    private string[] Args => ["--f81-git-visual-lab=" + GitVisualLabContract.Consent, "--f81-root=" + _root];
    public GitVisualLabF81Tests()
    {
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(_root, "f81-isolation.txt"), GitVisualLabContract.Consent);
    }
    private GitVisualLabContract Contract() => GitVisualLabContract.Validate(Args, App);
    public void Dispose() => Directory.Delete(_root, true); // Only this test's newly generated synthetic directory.

    [Fact]
    public void ExplicitActivationRequiresDedicatedLocationAndExactConsent()
    {
        Assert.Equal(_root, Contract().Root);
        foreach (var invalid in new[] { Array.Empty<string>(), new[] { Args[0] }, new[] { Args[0], Args[1], "--settings=production.json" },
            new[] { "--f81-git-visual-lab=true", Args[1] }, new[] { Args[0], Args[0] }, new[] { Args[0], "--f81-root=relative" },
            new[] { Args[0], "--f81-root=" + Path.GetTempPath() }, new[] { Args[0], "--f81-root=\\\\server\\share" } })
            Assert.Throws<InvalidDataException>(() => GitVisualLabContract.Validate(invalid, App));
        Assert.Throws<InvalidDataException>(() => GitVisualLabContract.Validate(Args, Path.GetTempPath()));
    }
    [Fact]
    public void MissingOrInvalidContractAndUnexpectedDataFailClosed()
    {
        var marker = Path.Combine(_root, "f81-isolation.txt");
        File.Delete(marker);
        Assert.Throws<InvalidDataException>(() => Contract());
        File.WriteAllText(marker, "production");
        Assert.Throws<InvalidDataException>(() => Contract());
        File.WriteAllText(marker, GitVisualLabContract.Consent);
        Directory.CreateDirectory(Path.Combine(_root, "projects"));
        Assert.Throws<InvalidDataException>(() => Contract());
    }
    [Fact]
    public void RepositoryAncestorsAndReparsePointsAreRejected()
    {
        File.WriteAllText(Path.Combine(_root, ".git"), "gitdir: synthetic");
        Assert.Throws<InvalidDataException>(() => Contract());
        File.Delete(Path.Combine(_root, ".git"));
        // Linux Cloud can create this link without changing OS permissions or policies.
        if (!OperatingSystem.IsWindows())
        {
            var evidence = Path.Combine(_root, "evidence");
            Directory.CreateSymbolicLink(evidence, App);
            Assert.Throws<InvalidDataException>(() => Contract());
            Directory.Delete(evidence);
        }
    }
    [Fact]
    public async Task EveryScenarioUsesRealViewModelAndLeavesFilesystemUntouched()
    {
        var service = new GitVisualLabService(Contract());
        var model = new GitVisualLabViewModel(service);
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray();
        foreach (var scenario in model.Scenarios)
        {
            model.SelectedScenario = scenario;
            Assert.IsType<GitViewModel>(model.Git);
            Assert.False(model.Git.HostAccess!.Available);
            Assert.False(model.Git.HostAccess.MutationsAllowed);
            foreach (var command in model.OperationalCommands) { Assert.False(command.CanExecute(null)); await command.ExecuteAsync(); }
            await model.Refresh.ExecuteAsync();
            if (scenario == GitVisualLabScenario.Error) { Assert.Equal(UiPageState.Stopped, model.Git.State); Assert.Empty(model.Git.Receipts); continue; }
            if (scenario is GitVisualLabScenario.Disabled or GitVisualLabScenario.Empty) { Assert.Empty(model.Git.Intents); Assert.NotNull(model.Git.EmptyMessage); continue; }
            Assert.Equal(24, model.Git.Batches.Count);
            Assert.Equal(24, model.Git.Intents.Count);
            Assert.Equal(24, model.Git.Receipts.Count);
            Assert.Equal(Enum.GetValues<GitOutboxState>().Order(), model.Git.Intents.Select(i => i.State).Distinct().Order());
            Assert.Equal(Enum.GetValues<GitReceiptState>().Order(), model.Git.Receipts.Select(i => i.State).Distinct().Order());
            model.Git.SelectedBatch = model.Git.Batches[0];
            Assert.True(model.ViewOutboxReceipt.CanExecute(null));
            await model.ViewOutboxReceipt.ExecuteAsync();
            Assert.Equal(model.Git.SelectedBatch.OperationId, model.Git.SelectedReceipt!.OperationId.Value);
            model.Git.SelectedIntent = model.Git.Intents[1];
            Assert.Null(model.Git.SelectedBatch);
            Assert.NotNull(model.Git.SelectedOutbox!.Reason);
            Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(model.Git.Receipts[0]));
            Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(model.Git.Repository!));
        }
        Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order().ToArray());
        Assert.Equal(GitVisualLabContract.Consent, File.ReadAllText(Path.Combine(_root, "f81-isolation.txt")));
    }
    [Fact]
    public void FixturesRetainOfficialModesAndSeparateGrants()
    {
        var service = new GitVisualLabService(Contract());
        var modes = new[] { GitVisualLabScenario.Disabled, GitVisualLabScenario.Manual, GitVisualLabScenario.AutoCommitBatch, GitVisualLabScenario.AutoCommitAndPushBatch };
        Assert.Equal(Enum.GetValues<GitPublicationMode>(), modes.Select(s => service.Read(s).Configuration.Mode));
        Assert.False(service.Read(GitVisualLabScenario.AutoCommitBatch).Configuration.AllowPush);
        var revoked = service.Read(GitVisualLabScenario.Revoked);
        Assert.True(revoked.Configuration.Revoked);
        Assert.False(revoked.Configuration.AllowCommit);
        Assert.Equal(2, revoked.Outbox.History.Count);
        Assert.NotEqual(revoked.Outbox.History[0].AuthorizationId, revoked.Configuration.AuthorizationId);
        Assert.Equal(revoked.Outbox.History[1].AuthorizationId, revoked.Configuration.AuthorizationId);
        Assert.All(revoked.Outbox.Rows, r => Assert.Equal(1, r.Revision)); // Old authorization remains historical.
        foreach (var scenario in new[] { GitVisualLabScenario.Manual, GitVisualLabScenario.AutoCommitBatch, GitVisualLabScenario.Revoked })
        {
            var snapshot = service.Read(scenario);
            Assert.Equal(2, snapshot.Configuration.Revision);
            Assert.Equal(GitPublicationMode.AutoCommitAndPushBatch, snapshot.Outbox.History[0].Mode);
            Assert.Equal(snapshot.Configuration.Mode, snapshot.Outbox.History[1].Mode);
            Assert.All(snapshot.Outbox.Rows, row => {
                Assert.Equal(snapshot.Outbox.History[0].AuthorizationId, row.AuthorizationId);
                Assert.NotEqual(snapshot.Configuration.AuthorizationId, row.AuthorizationId);
            });
        }
        Assert.Throws<InvalidDataException>(() => service.Read((GitVisualLabScenario)999));
    }
    [Fact]
    public async Task AdapterDeniesDirectMutationsAndForeignContextWithoutFallback()
    {
        var service = new GitVisualLabService(Contract());
        var c = service.Context;
        Assert.Throws<InvalidOperationException>(() => GitVisualLabService.DenyOperationalAction());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProposeGitAuthorizationAsync(c, new(GitPublicationMode.Manual, "actor", ["assets/"], 1, true, true), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveGitAuthorizationAsync(c, null!, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RevokeGitAuthorizationAsync(c, 1, "actor", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetryGitOutboxAsync(c, new("synthetic", 1, "grant", true), default));
        Assert.False((await service.GitHostAccessAsync(c, default)).MutationsAllowed);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadGitOutboxAsync(new(c.Profile, c.Storage), default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ReadGitConfigurationAsync(c, cancelled.Token));
        Assert.Throws<ArgumentNullException>(() => new GitVisualLabService(null!));
        Assert.Throws<ArgumentNullException>(() => new GitVisualLabViewModel(null!));
    }
    private static string Repository([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../.."));
    [Fact]
    public void SyntheticCompositionHasNoOperationalDependenciesOrCoordinators()
    {
        var forbidden = new[] { typeof(ProfessionalUiService), typeof(AutomationHostClient), typeof(RemoteDesktopService),
            typeof(System.Net.Http.HttpClient), typeof(System.Diagnostics.Process), typeof(Mutex), typeof(System.IO.Pipes.NamedPipeClientStream) };
        foreach (var type in new[] { typeof(GitVisualLabService), typeof(GitVisualLabViewModel), typeof(GitVisualLabContract) })
        {
            var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.DoesNotContain(fields, f => forbidden.Any(t => t.IsAssignableFrom(f.FieldType)));
        }
        // Source guard complements the real cross-build. It is not a Windows runtime attestation.
        var repo = Repository();
        var startup = File.ReadAllText(Path.Combine(repo, "src/NAP.App/App.xaml.cs"));
        var labBranch = startup.Split("#if NAP_F81_LAB")[1].Split("#elif NAP_ACCEPTANCE_ONLY")[0];
        Assert.Contains("Phase81GitLab.Start(this, e.Args);", labBranch);
        Assert.Contains("return;", labBranch);
        Assert.DoesNotContain("new ProfessionalUiService", labBranch);
        var composition = File.ReadAllText(Path.Combine(repo, "src/NAP.App/Phase81GitLab.cs"));
        Assert.DoesNotContain("new MainWindow", composition);
        Assert.DoesNotContain("new ShellViewModel", composition);
        Assert.True(composition.IndexOf("GitVisualLabContract.Validate", StringComparison.Ordinal) < composition.IndexOf("new GitVisualLabService", StringComparison.Ordinal));
        Assert.Contains("new ContentControl { Content = model.Git,", composition);
        var view = File.ReadAllText(Path.Combine(repo, "src/NAP.App/Views/GitView.xaml"));
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(view, @"Binding DataContext\.(\w+)"))
            Assert.NotNull(typeof(GitVisualLabViewModel).GetProperty(match.Groups[1].Value));
    }
}
