using System.Reflection;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ExecutionPolicyTests
{
    [Fact]
    public void ProductiveActivationIsExplicitDurableAndRevocable()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var p = f.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations);
        Assert.False(p.IsExecutionAuthorized);
        Assert.Throws<AutomationException>(() => f.Queue.SetPolicyState(owner, p.Reference, p.Revision - 1, AutomationPolicyState.Enabled));
        p = f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled);
        Assert.True(new AutomationQueueStore(f.Roots).CurrentPolicy().IsExecutionAuthorized);
        Assert.False(p.IsPreparationAuthorized); Assert.True(p.IsPreproductionAuthorized);
        p = f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Paused);
        Assert.False(p.IsExecutionAuthorized);
        p = f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled);
        p = f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Revoked);
        Assert.False(p.IsExecutionAuthorized);
        Assert.Throws<AutomationException>(() => f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled));
        f.AssertUntouched();
    }

    [Theory]
    [InlineData(AutomationOperations.Admit)] [InlineData(AutomationOperations.Audit)]
    [InlineData(AutomationOperations.Archive)] [InlineData(AutomationOperations.Produce)] [InlineData(AutomationOperations.Register)]
    public void EveryProductivePermissionIsRequired(AutomationOperations missing)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var p = f.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations & ~missing);
        Assert.Equal(AutomationError.PolicyMismatch, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled)).Code);
        f.AssertUntouched();
    }

    [Fact]
    public void ActivationRollbackAndNewVersionCannotInheritProductiveAuthority()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var p = f.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations);
        var hook = typeof(AutomationQueueStore).GetProperty("BeforeCommit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        hook.SetValue(f.Queue, (Action<string>)(_ => throw new PreparationTestFixture.SimulatedCrash()));
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled));
        hook.SetValue(f.Queue, null);
        Assert.Equal(JsonSerializer.Serialize(p), JsonSerializer.Serialize(f.Queue.CurrentPolicy()));
        var newer = f.Queue.SavePolicy(owner, p.Definition with { Version = 2 });
        Assert.False(newer.IsExecutionAuthorized); Assert.Null(newer.Authorization);
        Assert.Throws<AutomationException>(() => f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled));
        Assert.Throws<AutomationException>(() => f.Queue.SetPolicyState(owner, newer.Reference, newer.Revision, AutomationPolicyState.Enabled));
        f.AssertUntouched();
    }
}
