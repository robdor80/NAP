using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

// Display/metadata only. No ZIP fixtures, worker, provider or production executor.
public sealed class Phase6OverviewTests
{
    [Fact]
    public async Task AuthenticatedOverviewReadsEmptyHostWithoutCreatingCommandsOrChangingQueue()
    {
        // Existing fixture creates isolated directories and sentinels only. Never call Zip/Admit/Enable.
        using var fixture = new AutomationHostTests.Fixture();
        var contexts = new[] { fixture.A.Context, fixture.B.Context };
        await using var host = new AutomationHost(fixture.Control, contexts, fixture.Options);
        await host.Started;
        var bridge = new AutomationDesktopBridge(host, contexts, Path.Combine(fixture.Root, "desktop-requests"));
        var connection = new AutomationConnection("nap-phase6-" + Guid.NewGuid().ToString("N"),
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync);
        var client = new AutomationOverviewClient(new(connection));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await host.QueryAsync(contexts[0].Id, s => s.Queue.CurrentPolicy(), timeout.Token);
        var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.A.Queue.DatabasePath));
        var overview = await client.ReadAsync(contexts[0].Id, timeout.Token);
        Assert.Equal(contexts[0].Id, overview.Queue.UniverseId); Assert.Equal(2, overview.Host.Universes.Count);
        Assert.Equal(new AutomationQueueCounts(0, 0, 0, 0, 0), overview.Queue.Counts); Assert.Empty(overview.Queue.Review);
        Assert.False(overview.Queue.Policy.IsPreproductionAuthorized);
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fixture.A.Queue.DatabasePath)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Root, "desktop-requests")));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, "*.zip", SearchOption.AllDirectories));
        var foreign = await Assert.ThrowsAsync<AutomationException>(() => client.ReadAsync(new("unknown_universe"), timeout.Token));
        Assert.Equal(AutomationError.UniverseMismatch, foreign.Code);
    }

    [Fact]
    public async Task SelectedCountsStaySeparateFromExecutionScope()
    {
        var context = Context("alpha"); var source = new Source { Value = Snapshot(context) };
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        await vm.RefreshAsync();
        Assert.Equal("2", vm.Pending); Assert.Equal("1", vm.Processing); Assert.Equal("14", vm.Completed);
        Assert.Equal("1", vm.NeedsReview); Assert.Equal("En marcha: 2 universos", vm.ExecutionScope);
        Assert.Equal("Procesando imágenes", vm.Status); Assert.True(vm.CanPause); Assert.False(vm.CanResume);
        var row = Assert.Single(vm.Review); Assert.Equal("Retrato de muestra.zip", row.Name);
        Assert.Equal("La imagen no supera la revisión", row.Problem);
    }
    [Fact]
    public async Task LateOldUniverseReplyCannotReplaceNewSelection()
    {
        UniverseContext context = Context("alpha"); var old = context; var gate = new TaskCompletionSource<AutomationDesktopOverview>();
        var source = new Source { Read = _ => gate.Task };
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        var pending = vm.RefreshAsync(); context = Context("beta"); vm.ContextChanged();
        Assert.True(source.LastReadCancellation.IsCancellationRequested);
        Assert.Equal("—", vm.Completed); Assert.False(vm.CanPause);
        gate.SetResult(Snapshot(old)); await pending;
        Assert.Equal("—", vm.Pending); Assert.Empty(vm.Review);
        source.Read = null; source.Value = Snapshot(context) with { Queue = Snapshot(context).Queue with { Counts = new(0, 0, 3, 0, 0), Review = [] } };
        await vm.RefreshAsync(); Assert.Equal("3", vm.Completed); Assert.Equal("Todo al día", vm.Status);
    }
    [Fact]
    public async Task DisconnectClearsCountsAndNeverFallsBackOrSendsCommands()
    {
        var context = Context("alpha"); var source = new Source { Value = Snapshot(context) };
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        await vm.RefreshAsync(); source.Read = _ => throw new IOException("private/path/queue_id"); await vm.RefreshAsync();
        Assert.Equal("—", vm.Pending); Assert.Equal("—", vm.Completed); Assert.Empty(vm.Review);
        Assert.False(vm.CanPause); Assert.False(vm.CanResume); Assert.Empty(vm.ExecutionScope);
        Assert.Equal("No se pudo consultar la automatización", vm.Status);
        await vm.Pause.ExecuteAsync(); Assert.Empty(source.Sent);
    }
    [Fact]
    public async Task ForeignControlOrQueueIsNeverDisplayed()
    {
        var context = Context("alpha"); var source = new Source { Value = Snapshot(context) with { Control = Snapshot(Context("beta")).Control } };
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        await vm.RefreshAsync(); Assert.Equal("—", vm.NeedsReview); Assert.False(vm.CanPause);
        source.Value = Snapshot(Context("beta")); await vm.RefreshAsync(); Assert.Equal("—", vm.Pending);
    }
    [Fact]
    public async Task PauseWaitsForHostAndSendsOnlySelectedScopeWithoutConfirmation()
    {
        var context = Context("alpha"); var source = new Source { Value = Snapshot(context) };
        var gate = new TaskCompletionSource(); source.Send = () => gate.Task;
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        await vm.RefreshAsync(); var pausing = vm.Pause.ExecuteAsync();
        Assert.Equal("Pausando automatización…", vm.Status); Assert.Equal("—", vm.Processing); Assert.False(vm.CanResume);
        source.Value = Snapshot(context, AutomationHostState.Paused); gate.SetResult(); await pausing;
        Assert.Equal("Automatización pausada", vm.Status); Assert.True(vm.CanResume);
        Assert.Equal((AutomationOperation.Pause, context.Id), Assert.Single(source.Sent));
    }
    [Fact]
    public async Task RevokedOrMismatchedGrantNeverEnablesResume()
    {
        var context = Context("alpha"); var value = Snapshot(context, AutomationHostState.Paused);
        var source = new Source { Value = value };
        using var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        await vm.RefreshAsync(); Assert.True(vm.CanResume);
        source.Value = value with { Queue = value.Queue with { Policy = value.Queue.Policy with { State = AutomationPolicyState.Revoked } } };
        await vm.RefreshAsync(); Assert.False(vm.CanResume);
        source.Value = value with { Control = value.Control with { Grant = value.Control.Grant! with { Actor = "other" } } };
        await vm.RefreshAsync(); Assert.False(vm.CanResume); await vm.Resume.ExecuteAsync(); Assert.Empty(source.Sent);
    }
    [Fact]
    public async Task PollsNeverOverlapAndDisposedRepliesCannotPublish()
    {
        var context = Context("alpha"); var gate = new TaskCompletionSource<AutomationDesktopOverview>();
        var source = new Source { Read = _ => gate.Task };
        var vm = new AutomationControlViewModel(() => context, new NoConfirmation(), source);
        var pending = vm.RefreshAsync(); await vm.RefreshAsync(); Assert.Equal(1, source.Reads);
        vm.Dispose(); gate.SetResult(Snapshot(context)); await pending; Assert.Equal("—", vm.Completed); Assert.Empty(vm.Review);
    }
    [Theory]
    [InlineData(AutomationHostState.Paused, "Automatización pausada")]
    [InlineData(AutomationHostState.Blocked, "Automatización necesita revisión")]
    [InlineData(AutomationHostState.Degraded, "Automatización necesita revisión")]
    [InlineData(AutomationHostState.Disabled, "Automatización sin activar")]
    public void ControlStateHasPriorityOverCounts(AutomationHostState state, string label) =>
        Assert.Equal(label, AutomationControlViewModel.Describe(state, new(0, 1, 14, 0, 0)));

    private static UniverseContext Context(string id)
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-phase6-metadata", id);
        return new(new UniverseProfile(new(id), "Universo " + id), new(new(id), Path.Combine(root, "workspace"), Path.Combine(root, "production"), Path.Combine(root, "archive")));
    }
    private static AutomationDesktopOverview Snapshot(UniverseContext c, AutomationHostState state = AutomationHostState.Running)
    {
        var definition = AutomationPolicyDefinition.DisabledDefault(c.Storage) with { Operations = AutomationOperations.Admit };
        var auth = new AutomationAuthorization("synthetic", c.Id, definition.Reference, "Preview", DateTimeOffset.UtcNow);
        var policy = new StoredAutomationPolicy(definition, definition.Reference, AutomationPolicyState.PreparationOnly, auth, 1);
        var control = new AutomationHostControl(c.Id, 1, state == AutomationHostState.Paused, false,
            new(c.Id, c.Storage, new(new string('a', 64)), policy.Reference, auth.AuthorizationId, auth.Actor, false), "synthetic", []);
        var other = new UniverseId(c.Id.Value == "alpha" ? "beta" : "alpha");
        return new(new(null, "synthetic", AutomationHostState.Running,
            [new(c.Id, state, 0, 0, null, null, null), new(other, AutomationHostState.Running, 0, 0, null, null, null)], null),
            new(c.Id, DateTimeOffset.UtcNow, new(2, 1, 14, 1, 0),
                [new(QueueItemId.Create(), "Retrato de muestra.zip", QueueIncident.AuditFail, DateTimeOffset.UtcNow)], policy), control);
    }
    private sealed class NoConfirmation : IUserConfirmation
    {
        public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation) => throw new InvalidOperationException("Pause/resume must not prompt.");
    }
    private sealed class Source : IAutomationOverviewClient
    {
        public AutomationDesktopOverview Value { get; set; } = null!;
        public Func<UniverseId, Task<AutomationDesktopOverview>>? Read { get; set; }
        public Func<Task>? Send { get; set; }
        public int Reads;
        public CancellationToken LastReadCancellation;
        public List<(AutomationOperation, UniverseId?)> Sent { get; } = [];
        public Task<AutomationDesktopOverview> ReadAsync(UniverseId universe, CancellationToken cancellation) { Reads++; LastReadCancellation = cancellation; return Read?.Invoke(universe) ?? Task.FromResult(Value); }
        public Task SendAsync(AutomationOperation operation, UniverseId? universe, object payload, CancellationToken cancellation) { Sent.Add((operation, universe)); return Send?.Invoke() ?? Task.CompletedTask; }
    }
}
