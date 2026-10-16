using System.Reflection;
using System.Text.Json;
using NAP.Core;
using NAP.Presentation;
using Xunit;
using Xunit.Abstractions;

namespace NAP.Tests;

public sealed class AutomationAcceptanceTests(ITestOutputHelper output)
{
    [VisualExplorerWindowsFact]
    public void RetentionPinsLongPathsWithoutDependingOnTheCallerManifest()
    {
        using var f = new AutomationHostTests.Fixture();
        var root = Path.Combine(f.A.Roots.StagingRoot, "automation-admission");
        var nested = Path.Combine(root, new string('a', 120), new string('b', 80)); Directory.CreateDirectory(nested);
        var path = Path.Combine(nested, "sentinel.txt"); File.WriteAllText(path, "synthetic retained bytes"); Assert.True(path.Length > 260);
        var fits = typeof(AutomationHost).Assembly.GetType("NAP.Core.RetainedAdmissionInventory")!.GetMethod("Fits", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)fits.Invoke(null, [root, 1024L, CancellationToken.None])!);
        Assert.False((bool)fits.Invoke(null, [root, 1L, CancellationToken.None])!);
        Assert.Equal("synthetic retained bytes", File.ReadAllText(path));
    }

    [Fact]
    public async Task AuditHarvestCannotDropAdmissionDeferredByBackpressure()
    {
        using var f = new AutomationHostTests.Fixture(); StoredAutomationPolicy policy;
        using (var owner = f.A.Queue.AcquireOwner())
        {
            policy = f.A.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations, pending: 1);
            policy = f.A.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled);
        }
        for (var n = 0; n < 3; n++) f.A.Zip($"input-{n}.zip", $"portrait_sample{n}_001", seed: n + 1);
        var client = new DelayedAuditor(); var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(); int entered = 0;
        var options = f.Options with { LaboratoryCheckpoint = (_, _, point) =>
        {
            if (point == "admission-backpressure" && Volatile.Read(ref client.Calls) == 1 && Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            { blocked.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(20))); }
        } };
        await using var host = new AutomationHost(f.Control, [f.A.Context], options, client); await host.Started;
        try
        {
            await host.ActivateAsync(f.A.Context.Id, policy.Reference, false);
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
            client.Late.TrySetResult(new(AiAuditDecision.Pass, "synthetic barrier response", []));
            var http = (Task)typeof(AutomationHost).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            await http.WaitAsync(TimeSpan.FromSeconds(10)); release.Set();
            IReadOnlyList<AutomationBatchProjection> batches = [];
            var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
            do { batches = await host.BatchesAsync(f.A.Context.Id); Assert.True(DateTimeOffset.UtcNow < deadline); await Task.Delay(50); }
            while (!batches.Any(b => b.Capture.Closed));
            Assert.Equal(3, batches.Sum(b => b.Summary.UniqueItems));
        }
        finally { release.Set(); client.Late.TrySetResult(new(AiAuditDecision.Pass, "release synthetic fixture", [])); }
    }

    [Fact]
    public async Task StopPersistenceFailureRemainsDiagnosedAfterOwnerDrain()
    {
        using var f = new AutomationHostTests.Fixture(); var policy = AutomationHostTests.Fixture.Enable(f.A);
        await using var host = new AutomationHost(f.Control, [f.A.Context], f.Options); await host.Started;
        await host.ActivateAsync(f.A.Context.Id, policy.Reference, true);
        var seam = typeof(AutomationHostControlStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        seam.SetValue(host.Controls, (Action)(() => throw new IOException("synthetic persistence failure")));
        host.RequestStop(); await host.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(AutomationHostState.Stopped, host.Snapshot().State);
        Assert.StartsWith("stop_not_persisted:", host.Snapshot().Diagnostic);
        seam.SetValue(host.Controls, null);
        Assert.False(host.ReadControl(f.A.Context.Id).Stopped); // no durable-stop success is invented
        Assert.Empty(Directory.EnumerateFiles(f.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
    }

    private sealed class DelayedAuditor : IIdentifiedAiAuditClient
    {
        public string ProviderId => "synthetic_provider";
        public string ModelId => "synthetic_model";
        internal readonly TaskCompletionSource<AiAuditReport> Late = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        internal Action<int>? Observe;
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref Calls); Observe?.Invoke(call);
            if (call == 1) { Entered.TrySetResult(); return Late.Task; }
            return Task.FromResult(new AiAuditReport(AiAuditDecision.Pass, "synthetic subsequent", []));
        }
    }

    [Fact]
    public async Task BoundResponseExecutesBeforeDispatchingAnotherItemFromTheBacklog()
    {
        using var f = new AutomationHostTests.Fixture(); StoredAutomationPolicy policy;
        using (var owner = f.A.Queue.AcquireOwner())
        {
            policy = f.A.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations, requests: 64, cost: 64, timeout: 120);
            policy = f.A.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled);
        }
        for (var n = 0; n < 64; n++) f.A.Zip($"input-{n}.zip", $"portrait_sample{n}_001", seed: n + 1);
        using (var owner = f.A.Queue.AcquireOwner())
        {
            var admission = new InboxAdmissionService(f.A.Context, f.A.Queue, PreparationTestFixture.Fast);
            var checkpoints = new List<string>();
            typeof(InboxAdmissionService).GetProperty("Checkpoint", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(admission, (Action<string>)checkpoints.Add);
            foreach (var path in Directory.EnumerateFiles(f.A.Roots.InboxRoot, "*.zip").ToArray())
            {
                checkpoints.Clear(); var result = admission.Admit(owner, path);
                Assert.True(result.Code == InboxAdmissionCode.Admitted, JsonSerializer.Serialize(result) + "\n" + string.Join(",", checkpoints));
            }
        }
        var client = new DelayedAuditor(); var next = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Observe = call => { if (call == 2) next.TrySetResult("second-audit"); };
        using var release = new ManualResetEventSlim(); var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AutomationHost? observedHost = null; int barrier = 0, responseBound = 0;
        var options = f.Options with { LaboratoryCheckpoint = (_, _, point) =>
        {
            if (point == "execution-start") next.TrySetResult("execution");
            if (point == "audit-decision-bound") Interlocked.Exchange(ref responseBound, 1);
            if (point == "preparation-intent" && Volatile.Read(ref responseBound) == 1) next.TrySetResult("new-preparation");
            if (point != "workflow-idle" || Volatile.Read(ref client.Calls) != 1 || Volatile.Read(ref barrier) != 0) return;
            var slot = ((Array)typeof(AutomationHost).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(observedHost)!).GetValue(0)!;
            var offset = (int)slot.GetType().GetField("Offset", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(slot)!;
            if (offset == 0 || Interlocked.Exchange(ref barrier, 1) != 0) return;
            blocked.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(20)));
        } };
        await using var host = new AutomationHost(f.Control, [f.A.Context], options, client); observedHost = host; await host.Started;
        try
        {
            await host.ActivateAsync(f.A.Context.Id, policy.Reference, false);
            try { await AutomationHostTests.Until(() => client.Entered.Task.IsCompleted, host); }
            catch
            {
                output.WriteLine(JsonSerializer.Serialize(await host.QueryAsync(f.A.Context.Id, s => s.Queue.List(0, 5).Select(i =>
                    new { i.State, i.Incident, Preparation = new DurablePreparationService(s.Context, s.Queue).State.Get(i.Id) }).ToArray())));
                throw;
            }
            await AutomationHostTests.Until(() => blocked.Task.IsCompleted, host, 110);
            var http = (Task)typeof(AutomationHost).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            Assert.False(http.IsCompleted);
            client.Late.TrySetResult(new(AiAuditDecision.Pass, "synthetic accepted response", []));
            await http.WaitAsync(TimeSpan.FromSeconds(10)); release.Set();
            Assert.Equal("execution", await next.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally { release.Set(); client.Late.TrySetResult(new(AiAuditDecision.Pass, "release synthetic fixture", [])); }
    }

    [Fact]
    public async Task UncertainRemoteReservesGlobalHttpButNotOtherUniverseOwnerAndLatePassCannotPublish()
    {
        using var f = new AutomationHostTests.Fixture();
        var a = AutomationHostTests.Fixture.Enable(f.A); var b = AutomationHostTests.Fixture.Enable(f.B);
        f.A.Zip(); f.B.Zip(); var client = new DelayedAuditor();
        await using var host = new AutomationHost(f.Control, [f.A.Context, f.B.Context], f.Options, client); await host.Started;
        await host.ActivateAsync(f.A.Context.Id, a.Reference, true);
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await host.ActivateAsync(f.B.Context.Id, b.Reference, true);
        var item = Assert.Single(await host.QueryAsync(f.A.Context.Id, s => s.Queue.List()));
        host.Control(f.A.Context.Id, AutomationControlAction.CancelItem, item.Id);
        // Policy timeout is one second. An uncooperative provider remains unresolved afterwards.
        await Task.Delay(1500);
        await AutomationHostTests.Until(() => host.Snapshot().Universes.Single(s => s.UniverseId == f.B.Context.Id).Turns >= 5, host);
        PreparationRecord? preparation = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (preparation?.Checkpoint != PreparationCheckpoint.Planned)
        {
            preparation = await host.QueryAsync(f.B.Context.Id, s =>
            {
                var queued = s.Queue.List();
                try { return queued.Count == 1 ? new DurablePreparationService(s.Context, s.Queue).State.Get(queued[0].Id) : null; }
                catch (AutomationException error) when (error.Code == AutomationError.Missing) { return null; }
            });
            Assert.True(DateTimeOffset.UtcNow < deadline); await Task.Delay(50);
        }
        Assert.Equal(1, Volatile.Read(ref client.Calls));
        var blockedManual = host.RunManualAuditAsync(f.B.Context.Id, () => Task.FromResult(0));
        // Resume B is active, so even after reservation release a manual audit cannot gain authority.
        Assert.False(blockedManual.IsCompleted);
        client.Late.TrySetResult(new(AiAuditDecision.Pass, "late response must be ignored", []));
        await Assert.ThrowsAsync<AutomationException>(() => blockedManual);
        await AutomationHostTests.Until(() => Directory.EnumerateFiles(f.B.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Any(), host);
        Assert.Empty(Directory.EnumerateFiles(f.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
        var first = await host.QueryAsync(f.A.Context.Id, s => new DurablePreparationService(s.Context, s.Queue).State.Get(item.Id)!);
        Assert.NotEqual(DurableAuditState.Pass, first.AuditState); Assert.Single(first.Calls); Assert.Null(first.ExpectedAuditReceipt);
        Assert.True((await host.QueryAsync(f.B.Context.Id, s => s.Queue.List())).Single().AttemptCount == 1);
        // A second owner is denied while the same host owns the universe.
        var rejected = Assert.Throws<AutomationException>(() => f.B.Queue.AcquireOwner()); Assert.Equal(AutomationError.Busy, rejected.Code);
    }

    [Fact]
    public async Task BridgeAccountsHiddenNormalizationBuffersAndRejectsBeforeRetainingBeyondBudget()
    {
        using var f = new AutomationHostTests.Fixture(); var path = f.A.Zip("portrait_example_001.zip", width: 39);
        var proposal = await new ImageNormalizationService(f.A.Context,
            new() { MaxAddedAreaBasisPoints = 500, MaxAxisGrowthBasisPoints = 500 }).PreparePackageAsync(path);
        var buffers = (Dictionary<string, byte[]>)typeof(ImageNormalizationProposal).GetProperty("Files", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(proposal)!;
        buffers["synthetic-budget-buffer"] = new byte[70 * 1024 * 1024];
        await using var host = new AutomationHost(f.Control, [f.A.Context], f.Options); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [f.A.Context], Path.Combine(f.Root, "desktop-requests"));
        var view = typeof(AutomationDesktopBridge).GetMethod("View", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _ = view.Invoke(bridge, [f.A.Context, proposal, true, null]);
        var rejected = Assert.Throws<TargetInvocationException>(() => view.Invoke(bridge, [f.A.Context, proposal, true, null]));
        Assert.Equal(AutomationError.Busy, Assert.IsType<AutomationException>(rejected.InnerException).Code);
        var charged = (long)typeof(AutomationDesktopBridge).GetField("_handleBytes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridge)!;
        Assert.InRange(charged, 70L * 1024 * 1024, AutomationDesktopBridge.MaximumHandleBytes);
    }

    [VisualExplorerWindowsFact]
    public void ClientCachePinsAncestorsAndPreservesReplacedFilesDuringHandleBasedCleanup()
    {
        var type = typeof(RemoteDesktopService).Assembly.GetType("NAP.Presentation.AutomationThumbnailCache")!;
        using var cache = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        string Write(byte[] bytes) => (string)type.GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cache, [bytes])!;
        var path = Write([1, 2, 3]); var replaced = Write([4, 5, 6]); var root = Path.GetDirectoryName(path)!;
        Assert.Throws<IOException>(() => Directory.Move(root, root + "-replaced"));
        Assert.Equal(path, Write([1, 2, 3]));
        // Hold the original inode live so the filesystem cannot recycle its identity.
        using var original = new FileStream(replaced, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        File.Move(replaced, replaced + ".preserved"); File.WriteAllText(replaced, "unrelated replacement");
        cache.Dispose(); cache.Dispose();
        Assert.False(File.Exists(path)); Assert.Equal("unrelated replacement", File.ReadAllText(replaced));
        original.Dispose(); File.Delete(replaced); File.Delete(replaced + ".preserved"); Directory.Delete(root);
    }
}
