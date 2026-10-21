using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationAuditStageTests
{
    private sealed class Client : IIdentifiedAiAuditClient
    {
        public string ProviderId => "synthetic_provider";
        public string ModelId => "synthetic_model";
        public TaskCompletionSource<AiAuditReport> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken token = default) => Response.Task;
    }

    [Fact]
    public async Task HttpWaitLeavesOwnerFreeAndDuplicateCompletionCannotReplacePass()
    {
        using var f = new PreparationTestFixture(); var client = new Client();
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var step = await runtime.InvokeAsync(s =>
        { f.Policy(s.Owner, authorizePreparation: true); var item = f.Admit(s.Owner); return f.Service.BeginAudit(s.Owner, item.ItemId!, client.ProviderId, client.ModelId); });
        Assert.NotNull(step.Dispatch);
        var transport = AutomationAuditTransport.SendAsync(step.Dispatch!, client);
        var batch = await runtime.InvokeAsync(s => s.Queue.CreateBatch(s.Owner)); Assert.NotNull(batch);
        client.Response.SetResult(new(AiAuditDecision.Pass, "synthetic", []));
        var outcome = await transport;
        var result = await runtime.InvokeAsync(s => f.Service.CompleteAudit(s.Owner, step.Dispatch!, outcome.Report!));
        Assert.Equal(PreparationCode.AuditPass, result.Code);
        await Assert.ThrowsAsync<AutomationException>(() => runtime.InvokeAsync(s => f.Service.CompleteAudit(s.Owner, step.Dispatch!, outcome.Report!)));
    }

    [Fact]
    public async Task TimeoutDoesNotClaimRemoteCancellationOrRestoreBudget()
    {
        using var f = new PreparationTestFixture(); var client = new Client();
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var step = await runtime.InvokeAsync(s =>
        { f.Policy(s.Owner, authorizePreparation: true); var item = f.Admit(s.Owner); return f.Service.BeginAudit(s.Owner, item.ItemId!, client.ProviderId, client.ModelId); });
        var outcome = await AutomationAuditTransport.SendAsync(step.Dispatch!, client);
        Assert.False(outcome.RemoteCompletion.IsCompleted); Assert.Equal(AiAuditErrorKind.Timeout, outcome.Failure!.Code);
        var failed = await runtime.InvokeAsync(s => f.Service.FailAudit(s.Owner, step.Dispatch!, outcome.Failure));
        Assert.Equal(PreparationCode.AuditRetryScheduled, failed.Code);
        client.Response.SetResult(new(AiAuditDecision.Pass, "too late", [])); await outcome.RemoteCompletion;
        await Assert.ThrowsAsync<AutomationException>(() => runtime.InvokeAsync(s => f.Service.CompleteAudit(s.Owner, step.Dispatch!, client.Response.Task.Result)));
        var calls = await runtime.InvokeAsync(s => f.Service.State.Get(step.Dispatch!.ItemId)!.Calls.Count); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PausedHostCanBindResponseWithoutPublishingPass()
    {
        using var f = new PreparationTestFixture(); var client = new Client();
        await using var runtime = new AutomationUniverseRuntime(f.Context); await runtime.Started;
        var step = await runtime.InvokeAsync(s =>
        { f.Policy(s.Owner, authorizePreparation: true); var item = f.Admit(s.Owner); return f.Service.BeginAudit(s.Owner, item.ItemId!, client.ProviderId, client.ModelId); });
        await runtime.InvokeAsync(s => f.Service.CompleteAudit(s.Owner, step.Dispatch!, new(AiAuditDecision.Pass, "synthetic", []), false));
        var record = await runtime.InvokeAsync(s => f.Service.State.Get(step.Dispatch!.ItemId)!);
        Assert.Equal(DurableAuditState.ResponseReady, record.AuditState);
        Assert.False(Directory.Exists(Path.Combine(f.Roots.StateRoot, record.AuditResponse!.RelativeDirectory)));
        var resumed = await runtime.InvokeAsync(s => f.Service.BeginAudit(s.Owner, step.Dispatch!.ItemId, client.ProviderId, client.ModelId));
        Assert.Null(resumed.Dispatch); Assert.Equal(PreparationCode.AuditPass, resumed.Result.Code);
    }
}
