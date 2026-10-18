using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PreparationStorageDiagnosticsTests
{
    [Fact]
    public async Task AuthenticatedExternalStopRemainsSafeForRepeatedFixtureDisposal()
    {
        await using var f = new AutomationHostProcessFixture(); await f.StartAsync();
        await f.Client.CallAsync(AutomationOperation.ShutdownHost, null, new { });
        await f.StopAsync(); await f.StopAsync();
        Assert.True(f.Process!.HasExited); Assert.Equal(0, f.Process.ExitCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalStorageFailureIsReportedWithoutChangingIdentityOrAdoptingEffects(bool audit)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        f.Policy(owner, authorizePreparation: true); var admitted = f.Admit(owner);
        var attempt = f.Queue.GetAttempt(f.Queue.Get(admitted.ItemId!).ActiveAttempt!);
        var originals = DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot);
        var failure = new IOException("synthetic preservation disk failure");
        Exception? observed = null; QueueItemId? observedItem = null;
        typeof(DurablePreparationService).GetProperty("StorageFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(f.Service, (Action<QueueItemId, Exception>)((item, error) => { observedItem = item; observed = error; }));
        PreparationTestFixture.Hook(f.Service, point => { if (point == (audit ? "audit-temporary" : "original-temporary")) throw failure; });

        var result = audit ? f.Service.Audit(owner, admitted.ItemId!, new DurableExecutionTests.AuditClient()) : f.Service.Prepare(owner, admitted.ItemId!);
        Assert.Equal(PreparationCode.StorageUnavailable, result.Code);
        Assert.Same(failure, observed); Assert.Equal(admitted.ItemId, observedItem);
        Assert.Contains(nameof(OriginalStorageFailureIsReportedWithoutChangingIdentityOrAdoptingEffects), observed!.StackTrace);
        Assert.Equal(QueueIncident.StorageUnavailable, f.Queue.Get(admitted.ItemId!).Incident);
        Assert.Equal(attempt.JobId, f.Service.State.Get(admitted.ItemId!)!.JobId);
        Assert.Equal(attempt.Id, f.Queue.Get(admitted.ItemId!).ActiveAttempt);
        Assert.Equal(1, f.Queue.Get(admitted.ItemId!).AttemptCount);
        Assert.Equal(audit ? 1 : 0, f.Service.State.Get(admitted.ItemId!)!.Calls.Count);
        Assert.Equal(PreparationCode.StorageUnavailable, f.Service.Prepare(owner, admitted.ItemId!).Code);
        Assert.Equal(originals, DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot));
        Assert.Equal(audit ? JobState.Planned : JobState.Staged, new JobStateStore(f.Context).Load(attempt.JobId).State);
    }

    [Fact]
    public void ValidatedProfileCopiesRevalidateEveryScopeHashAndRuleField()
    {
        using var f = new PreparationTestFixture();
        var rule = f.Context.Profile.AssetRules.Single();
        var snapshot = EffectiveRuleSnapshot.Create(f.Context.Id, 1, rule.AssetType, rule.ProductionProfile,
            UniverseProfileSnapshotRenderer.Render(f.Context.Profile));
        var loaded = snapshot.ValidateAndGetRule(); Assert.Same(loaded, snapshot.ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { Version = 0 }).ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { UniverseId = new("other_universe") }).ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { Hash = new(new string('0', 64)) }).ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { ProfileJson = snapshot.ProfileJson + " " }).ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { AssetType = "missing" }).ValidateAndGetRule());
        Assert.Throws<AutomationException>(() => (snapshot with { ProductionProfile = "missing" }).ValidateAndGetRule());
        Assert.Same(loaded, snapshot.ValidateAndGetRule());
    }
}
