using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveMultiUniverseTests
{
    [Fact]
    public void SameAssetIdIsIndependentAcrossTwoExplicitUniverses()
    {
        using var first = new ArchiveTestFixture();
        using var second = new ArchiveTestFixture(ArchiveTestFixture.Generic(), first.Package.AssetKey.AssetId);
        Assert.True(UniverseStorageIsolationValidator.Validate([first.Context.Storage, second.Context.Storage]).IsClean);
        var firstPlan = first.Plan();
        var secondPlan = second.Plan();
        var firstBefore = ArchiveTestFixture.Snapshot(first.Root);
        second.Execute(secondPlan);
        ArchiveTestFixture.AssertSnapshot(firstBefore, first.Root);
        var secondAfter = ArchiveTestFixture.Snapshot(second.Root);
        first.Execute(firstPlan);
        ArchiveTestFixture.AssertSnapshot(secondAfter, second.Root);
        Assert.Equal(first.Package.AssetKey.AssetId, second.Package.AssetKey.AssetId);
        Assert.NotEqual(firstPlan.ArchiveRoot, secondPlan.ArchiveRoot);
        Assert.Equal(first.Context.Id, first.Store.Load().UniverseId);
        Assert.Equal(second.Context.Id, second.Store.Load().UniverseId);
        Assert.Single(first.Store.Load().Entries);
        Assert.Single(second.Store.Load().Entries);
        Assert.True(first.Plan().Issues.IsClean);
        Assert.True(second.Plan().Issues.IsClean);
    }

    [Fact]
    public void PlannerExecutorAndIndexCannotCrossContexts()
    {
        using var first = new ArchiveTestFixture();
        using var second = new ArchiveTestFixture(ArchiveTestFixture.Generic());
        var firstPlan = first.Plan();
        var firstBefore = ArchiveTestFixture.Snapshot(first.Root);
        var secondBefore = ArchiveTestFixture.Snapshot(second.Root);
        Assert.Throws<ArgumentException>(() => new ArchiveMasterPlanner(second.Context).Plan(first.Package, first.Processing));
        Assert.Throws<ArgumentException>(() => new ArchiveMasterExecutor(second.Context).Execute(firstPlan, ArchiveTestFixture.Pass));
        ArchiveTestFixture.AssertSnapshot(firstBefore, first.Root);
        ArchiveTestFixture.AssertSnapshot(secondBefore, second.Root);
        second.WriteIndex(ArchiveTestFixture.ValidIndex("nimroel"));
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => second.Store.Load()), NapIssueCodes.ArchiveIndexInvalid);
    }

    [Fact]
    public void SameUniverseButDifferentAuthorizedRootCannotExecuteAnotherPlan()
    {
        using var first = new ArchiveTestFixture();
        using var second = new ArchiveTestFixture();
        var before = ArchiveTestFixture.Snapshot(second.Root);
        Assert.Throws<ArgumentException>(() => new ArchiveMasterExecutor(second.Context).Execute(first.Plan(), ArchiveTestFixture.Pass));
        Assert.Throws<ArgumentException>(() => new ArchiveMasterPlanner(second.Context).Plan(first.Package, first.Processing));
        ArchiveTestFixture.AssertSnapshot(before, second.Root);
    }
}
