using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationRuntimeTests
{
    [Fact]
    public async Task CommandsUseOneOwnerThreadAndDrainBeforeReleasingOwnership()
    {
        using var f = new PreparationTestFixture();
        var runtime = new AutomationUniverseRuntime(f.Context);
        await runtime.Started;
        var threads = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => runtime.InvokeAsync(s =>
        { s.Queue.CreateBatch(s.Owner); return Environment.CurrentManagedThreadId; })));
        Assert.Single(threads.Distinct());
        Assert.Throws<AutomationException>(() => f.Queue.AcquireOwner());
        await runtime.DisposeAsync();
        using var next = f.Queue.AcquireOwner(); Assert.True(next.Epoch > 1);
    }

    [Fact]
    public async Task EscapedScopeCannotAuthorizeAnotherThreadAndItemFailureDoesNotKillRuntime()
    {
        using var f = new PreparationTestFixture();
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var escaped = await runtime.InvokeAsync(s => s);
        Assert.Throws<AutomationException>(() => escaped.Queue);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.InvokeAsync<int>(_ => throw new InvalidDataException("synthetic")));
        Assert.Equal(f.Context.Id, await runtime.InvokeAsync(s => s.Context.Id));
    }

    [Fact]
    public async Task FailedStartupCompletesQueuedCallersAndOtherUniverseContinues()
    {
        using var a = new PreparationTestFixture(); using var b = new PreparationTestFixture("other_universe");
        await using var blocker = new AutomationUniverseRuntime(a.Context); await blocker.Started;
        var bad = new AutomationUniverseRuntime(a.Context);
        await Assert.ThrowsAsync<AutomationException>(() => bad.Started);
        await Assert.ThrowsAnyAsync<Exception>(() => bad.InvokeAsync(s => s.Queue.CurrentPolicy()));
        await Assert.ThrowsAsync<AutomationException>(async () => await bad.DisposeAsync());
        await using var good = new AutomationUniverseRuntime(b.Context); await good.Started;
        Assert.Equal(b.Context.Id, await good.InvokeAsync(s => s.Context.Id));
    }
}
