using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationSuspensionTests
{
    [Theory]
    [InlineData("after:archive-file:portrait_example_001_brief.txt")]
    [InlineData("after:archive-index")]
    [InlineData("after:production-file:portrait_example_001.webp")]
    [InlineData("after:catalog-register")]
    [InlineData("after:journal-Completed")]
    public void SuspensionResumesSameJobWithoutTurningIntoAnIncident(string boundary)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        DurableExecutionTests.Enable(f, owner); var a = f.Admit(owner);
        f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var execution = new DurableExecutionService(f.Context, f.Queue);
        var suspended = execution.Execute(owner, a.ItemId!, new((_, point) => point == boundary));
        Assert.Equal(DurableExecutionCode.Suspended, suspended.Code);
        Assert.Equal(QueueState.Running, f.Queue.Get(a.ItemId!).State);
        var published = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot);
        Assert.DoesNotContain(Directory.EnumerateFiles(f.Roots.ArchiveRoot, "*", SearchOption.AllDirectories), p => p.EndsWith(".tmp"));
        var stamps = Directory.EnumerateFiles(f.Roots.ProductionRoot, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc);
        var complete = execution.Execute(owner, a.ItemId!);
        Assert.Equal(DurableExecutionCode.Completed, complete.Code); Assert.Equal(a.JobId, complete.JobId);
        foreach (var stamp in stamps) Assert.Equal(stamp.Value, File.GetLastWriteTimeUtc(stamp.Key));
        Assert.Equal(QueueState.Completed, f.Queue.Get(a.ItemId!).State);
    }

    [Fact]
    public void PreparationSuspendsAfterPreservationAndResumesWithoutDiscardingOriginals()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        f.Service.RunControl = new((_, point) => point == "original-preserved");
        Assert.Equal(PreparationCode.Interrupted, f.Service.Prepare(owner, a.ItemId!).Code);
        Assert.Equal(QueueState.Queued, f.Queue.Get(a.ItemId!).State);
        var original = f.Service.State.Get(a.ItemId!)!.Originals!;
        var before = DurableExecutionTests.Inventory(Path.Combine(f.Roots.StateRoot, original.RelativeDirectory));
        f.Service.RunControl = null; Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(Path.Combine(f.Roots.StateRoot, original.RelativeDirectory)));
    }
}
