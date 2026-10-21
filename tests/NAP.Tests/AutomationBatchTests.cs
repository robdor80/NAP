using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationBatchTests
{
    [Fact]
    public void CaptureIntentSurvivesReopenWithoutDuplicatingCohortOrWork()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var firstSession = "session_" + Guid.NewGuid().ToString("N"); var secondSession = "session_" + Guid.NewGuid().ToString("N");
        var store = new AutomationRuntimeStateStore(f.Context);
        var id = store.OpenCapture(owner, f.Queue, firstSession);
        var reopened = new AutomationRuntimeStateStore(f.Context);
        Assert.Equal(id, reopened.OpenCapture(owner, f.Queue, secondSession));
        var capture = Assert.Single(reopened.List(owner)); Assert.Equal(firstSession, capture.StartedSession); Assert.Equal(secondSession, capture.LastSession);
        // Simulate queue close committed, runtime close not yet written.
        f.Queue.CloseBatch(owner, id);
        var next = reopened.OpenCapture(owner, f.Queue, secondSession);
        Assert.NotEqual(id, next); Assert.True(reopened.List(owner).First().Closed);
    }

    [Fact]
    public async Task ClosedBatchSummaryReflectsFinalWorkflowAfterCaptureFinished()
    {
        using var f = new AutomationHostTests.Fixture(); var policy = AutomationHostTests.Fixture.Enable(f.A); f.A.Zip();
        await using var host = new AutomationHost(f.Control, [f.A.Context], f.Options, new DurableExecutionTests.AuditClient()); await host.Started;
        await host.ActivateAsync(f.A.Context.Id, policy.Reference, false);
        IReadOnlyList<AutomationBatchProjection> summaries = [];
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        do { await Task.Delay(50); summaries = await host.BatchesAsync(f.A.Context.Id); }
        while ((!summaries.Any(b => b.Capture.Closed && b.Summary.Completed == 1)) && DateTimeOffset.UtcNow < deadline);
        Assert.True(summaries.Any(b => b.Capture.Closed && b.Summary.UniqueItems == 1 && b.Summary.Completed == 1), System.Text.Json.JsonSerializer.Serialize(host.Snapshot()));
        var items = await host.QueryAsync(f.A.Context.Id, s => s.Queue.List()); Assert.Single(items); Assert.Equal(1, items.Single().AttemptCount);
    }
}
