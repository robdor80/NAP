using System.Diagnostics;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationProcessRecoveryTests
{
    internal static async Task Until(AutomationHostProcessFixture f, Func<Task<bool>> predicate, int seconds = 120)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            if (await predicate()) return;
            var snapshot = await f.SnapshotAsync();
            Assert.DoesNotContain(snapshot.Universes, s => s.State == AutomationHostState.Blocked);
            Assert.True(timer.Elapsed.TotalSeconds < seconds, System.Text.Json.JsonSerializer.Serialize(snapshot));
            await Task.Delay(50);
        }
    }

    private static async Task<bool> Reached(AutomationHostProcessFixture f, QueueState state)
    {
        var payload = await f.ReadAsync(AutomationOperation.QueuePage, f.Files.A.Context.Id.Value, new AutomationPageRequest());
        var current = AutomationProtocol.Read<QueueItem[]>(payload).Single();
        if (current.State is QueueState.Completed or QueueState.NeedsReview or QueueState.Rejected)
            Assert.True(current.State == state, payload.GetRawText());
        return current.State == state;
    }

    private static async Task<QueueItem> Interrupt(AutomationHostProcessFixture f, string boundary, bool automatic = true, int requests = 10)
    {
        var policy = AutomationHostTests.Fixture.Enable(f.Files.A, requests); f.Files.A.Zip();
        await f.StartAsync(boundary);
        await f.Client.CallAsync(AutomationOperation.Activate, f.Files.A.Context.Id.Value, new AutomationActivation(policy.Reference, automatic));
        var line = await f.Process!.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(120));
        Assert.EndsWith(":" + boundary, line);
        var item = Assert.Single(f.Files.A.Queue.List());
        Assert.Equal(1, item.AttemptCount);
        return item;
    }

    [Theory]
    [InlineData("audit-request-intent")]
    [InlineData("audit-response-received")]
    public async Task UnboundRemoteOutcomeSurvivesRestartWithoutRefundOrUnauthorizedSecondCall(string boundary)
    {
        await using var f = new AutomationHostProcessFixture();
        var item = await Interrupt(f, boundary, requests: 1);
        await f.Client.CallAsync(AutomationOperation.Pause, f.Files.A.Context.Id.Value, new { });
        await f.KillAsync();
        var original = f.Files.A.Service.State.Get(item.Id)!;
        Assert.Equal(DurableAuditState.RequestIntent, original.AuditState); Assert.Single(original.Calls);
        await f.StartAsync();
        await f.Client.CallAsync(AutomationOperation.Resume, f.Files.A.Context.Id.Value, new { });
        await Until(f, () => Reached(f, QueueState.NeedsReview)); await f.StopAsync();
        var result = f.Files.A.Service.State.Get(item.Id)!;
        Assert.Equal(original.Calls.Single().RequestId, Assert.Single(result.Calls).RequestId);
        Assert.Equal(PreparationCode.AuditBudgetExhausted, result.Code); Assert.Null(result.ExpectedAuditReceipt);
        Assert.Empty(Directory.EnumerateFiles(f.Files.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("snapshot-recorded", true)]
    [InlineData("extraction-recorded", true)]
    [InlineData("original-preserved", true)]
    [InlineData("audit-response-bound", true)]
    [InlineData("claimed", true)]
    [InlineData("after:archive-index", true)]
    [InlineData("after:production-file:portrait_example_001.webp", true)]
    [InlineData("after:catalog-register", true)]
    [InlineData("after:journal-Completed", true)]
    [InlineData("queue-completed", true)]
    [InlineData("effect:archive-file:portrait_example_001_brief.txt", false)]
    [InlineData("effect:production-file:portrait_example_001.webp", true)]
    [InlineData("effect:production-publish:portrait_example_001.webp", false)]
    public async Task KillAndRestartSameSidecarsPreservesIdentityAndNeverAdoptsAmbiguousFinal(string boundary, bool recoverable)
    {
        await using var f = new AutomationHostProcessFixture();
        var item = await Interrupt(f, boundary);
        await f.KillAsync();
        var attempt = f.Files.A.Queue.GetAttempt(item.ActiveAttempt!);
        var source = File.ReadAllBytes(item.SourcePath);
        var preparation = f.Files.A.Service.State.Get(item.Id);
        var originalRoot = preparation?.Originals is { } originals ? Path.Combine(f.Files.A.Roots.StateRoot, originals.RelativeDirectory) : null;
        var originalInventory = originalRoot is null ? null : DurableExecutionTests.Inventory(originalRoot);
        var existing = Directory.EnumerateFiles(f.Files.A.Roots.ArchiveRoot, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(f.Files.A.Roots.ProductionRoot, "*", SearchOption.AllDirectories))
            .ToDictionary(p => p, p => (Hash: new Sha256Hasher().Compute(p).Hex, Stamp: File.GetLastWriteTimeUtc(p)));
        await f.StartAsync();
        await Until(f, () => Reached(f, recoverable ? QueueState.Completed : QueueState.NeedsReview));
        await f.StopAsync();
        var final = Assert.Single(f.Files.A.Queue.List());
        Assert.Equal(item.Id, final.Id); Assert.Equal(item.ActiveAttempt, final.ActiveAttempt); Assert.Equal(1, final.AttemptCount);
        Assert.Equal(attempt.JobId, f.Files.A.Queue.GetAttempt(final.ActiveAttempt!).JobId);
        Assert.Equal(source, File.ReadAllBytes(item.SourcePath));
        if (originalRoot is not null) Assert.Equal(originalInventory, DurableExecutionTests.Inventory(originalRoot));
        foreach (var (path, prior) in existing)
        { Assert.Equal(prior.Hash, new Sha256Hasher().Compute(path).Hex); Assert.Equal(prior.Stamp, File.GetLastWriteTimeUtc(path)); }
        var record = f.Files.A.Service.State.Get(item.Id)!;
        Assert.Single(record.Calls); // a bound response is reused; no new provider dispatch on recovery
        if (preparation?.ExpectedAuditReceipt is { } receipt)
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(receipt), System.Text.Json.JsonSerializer.Serialize(record.ExpectedAuditReceipt));
        if (recoverable) Assert.Equal(JobState.Completed, new JobStateStore(f.Files.A.Context).Load(attempt.JobId).State);
        else Assert.False(new DurableExecutionService(f.Files.A.Context, f.Files.A.Queue).State.Get(item.Id)!.Completed);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("cancel")]
    [InlineData("session")]
    [InlineData("revoke")]
    public async Task DurableInhibitionsAndRestartAuthorizationCannotBeBypassed(string mode)
    {
        await using var f = new AutomationHostProcessFixture();
        var item = await Interrupt(f, "original-preserved", mode != "session");
        if (mode is "pause" or "cancel")
            await f.Client.CallAsync(mode == "pause" ? AutomationOperation.Pause : AutomationOperation.CancelItem,
                f.Files.A.Context.Id.Value, new AutomationItemRequest(item.Id.Value));
        await f.KillAsync();
        if (mode == "revoke")
        {
            using var owner = f.Files.A.Queue.AcquireOwner(); var policy = f.Files.A.Queue.CurrentPolicy();
            f.Files.A.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Revoked);
        }
        var before = DurableExecutionTests.Inventory(f.Files.A.Roots.ArchiveRoot, f.Files.A.Roots.ProductionRoot);
        await f.StartAsync();
        // Query through the process and let several scheduler cycles run before checking inhibition.
        for (var n = 0; n < 8; n++) { await f.SnapshotAsync(); await Task.Delay(50); }
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Files.A.Roots.ArchiveRoot, f.Files.A.Roots.ProductionRoot));
        Assert.Empty(f.Files.A.Service.State.Get(item.Id)!.Calls);
        if (mode == "revoke")
        { Assert.Contains((await f.SnapshotAsync()).Universes, s => s.State == AutomationHostState.Blocked); return; }
        if (mode == "session")
        {
            var policy = f.Files.A.Queue.CurrentPolicy();
            await f.Client.CallAsync(AutomationOperation.Activate, f.Files.A.Context.Id.Value, new AutomationActivation(policy.Reference, false));
        }
        else await f.Client.CallAsync(mode == "pause" ? AutomationOperation.Resume : AutomationOperation.ResumeItem,
            f.Files.A.Context.Id.Value, new AutomationItemRequest(item.Id.Value));
        await Until(f, () => Reached(f, QueueState.Completed));
        await f.StopAsync();
        Assert.Equal(1, f.Files.A.Queue.Get(item.Id).AttemptCount); Assert.Single(f.Files.A.Service.State.Get(item.Id)!.Calls);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("receipt")]
    [InlineData("paused")]
    public async Task CompletedStartupVerificationReportsCorruptionWithoutRepairOrNewAudit(string corruption)
    {
        await using var f = new AutomationHostProcessFixture(); var item = await Interrupt(f, "queue-completed");
        if (corruption == "paused") await f.Client.CallAsync(AutomationOperation.Pause, f.Files.A.Context.Id.Value, new { });
        await f.KillAsync();
        var record = f.Files.A.Service.State.Get(item.Id)!;
        var production = Directory.EnumerateFiles(f.Files.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Single();
        var receipt = Path.Combine(f.Files.A.Roots.StateRoot, record.AuditResponse!.RelativeDirectory, "report.json");
        if (corruption == "receipt") File.AppendAllText(receipt, " "); else File.Delete(production);
        var preserved = DurableExecutionTests.Inventory(f.Files.A.Roots.ArchiveRoot, f.Files.A.Roots.ProductionRoot);
        var receivedReceipt = File.ReadAllBytes(receipt);
        await f.StartAsync();
        if (corruption == "paused")
        {
            for (var n = 0; n < 5; n++) { await f.SnapshotAsync(); await Task.Delay(50); }
            var current = AutomationProtocol.Read<QueueItem[]>(await f.Client.CallAsync(AutomationOperation.QueuePage, f.Files.A.Context.Id.Value, new AutomationPageRequest())).Single();
            Assert.Equal(QueueIncident.None, current.Incident);
            await f.Client.CallAsync(AutomationOperation.Resume, f.Files.A.Context.Id.Value, new { });
        }
        await Until(f, async () => AutomationProtocol.Read<QueueItem[]>(await f.ReadAsync(AutomationOperation.QueuePage,
            f.Files.A.Context.Id.Value, new AutomationPageRequest())).Single().Incident == QueueIncident.InvalidEvidence);
        await f.StopAsync();
        var final = f.Files.A.Queue.Get(item.Id); Assert.Equal(QueueState.NeedsReview, final.State); Assert.Equal(1, final.AttemptCount);
        var job = f.Files.A.Queue.GetAttempt(final.ActiveAttempt!).JobId;
        Assert.Equal(JobState.Completed, new JobStateStore(f.Files.A.Context).Load(job).State);
        Assert.Contains(f.Files.A.Queue.Events(item.Id), e => e.Code == QueueEventCode.CompletedInconsistent);
        Assert.Equal(preserved, DurableExecutionTests.Inventory(f.Files.A.Roots.ArchiveRoot, f.Files.A.Roots.ProductionRoot));
        Assert.Equal(receivedReceipt, File.ReadAllBytes(receipt)); Assert.Single(f.Files.A.Service.State.Get(item.Id)!.Calls);
    }
}
