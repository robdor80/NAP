using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationAdmissionStageTests
{
    [Fact]
    public async Task ProbeDoesNotOccupyOwnerAndBatchAdmissionKeepsSameJobOnReplay()
    {
        using var f = new PreparationTestFixture(); var path = f.Zip();
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var service = new InboxAdmissionService(f.Context, f.Queue, PreparationTestFixture.Fast with { SampleInterval = TimeSpan.FromMilliseconds(100) });
        var pending = service.ProbeAsync(path);
        Assert.False(pending.IsCompleted);
        var batch = await runtime.InvokeAsync(s => s.Queue.CreateBatch(s.Owner));
        var ready = await pending;
        var first = await runtime.InvokeAsync(s => service.AdmitReady(s.Owner, ready, batch.Id));
        Assert.Equal(InboxAdmissionCode.Admitted, first.Code);
        var second = await runtime.InvokeAsync(s => service.AdmitReady(s.Owner, ready, batch.Id));
        Assert.Equal(first.JobId, second.JobId);
        var summary = await runtime.InvokeAsync(s => s.Queue.BatchSummary(batch.Id));
        Assert.Equal(1, summary.UniqueItems); Assert.Equal(1, summary.Observations);
        await runtime.InvokeAsync(s => s.Queue.CloseBatch(s.Owner, batch.Id));
        var recreated = await runtime.InvokeAsync(s => s.Queue.CreateBatch(s.Owner, batch.Id)); Assert.NotNull(recreated.ClosedUtc);
    }

    [Fact]
    public async Task ChangedSourceInvalidatesReadinessAndDoesNotAllocateJob()
    {
        using var f = new PreparationTestFixture(); var path = f.Zip();
        var service = new InboxAdmissionService(f.Context, f.Queue, PreparationTestFixture.Fast);
        var ready = await service.ProbeAsync(path); File.AppendAllText(path, "changed");
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var result = await runtime.InvokeAsync(s => service.AdmitReady(s.Owner, ready));
        Assert.Equal(InboxAdmissionCode.WaitingStable, result.Code);
        Assert.Empty(await runtime.InvokeAsync(s => s.Queue.List()));
    }
}
