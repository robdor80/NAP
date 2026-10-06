using System.Collections;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogRebuildTests
{
    [Fact]
    public void RebuildPreservesLogicalDerivedDataAndOperationalObjectivesCampaigns()
    {
        using var f = new CatalogTestFixture(); f.Publish("emblem_a_001", automatic: true); f.Publish("emblem_b_002", automatic: true);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective", "Target", new(traits: [new("/eye_color", "green-grey", CatalogTraitType.String)]), 10));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Campaign", ["objective"]));
        var before = f.Catalog.Query().Select(CatalogTestFixture.Logical).ToArray(); var operational = f.Catalog.Planning(); var sources = f.Sources();
        var result = new CatalogRebuilder(f.Context).Rebuild(); Assert.Equal(2, result.AssetCount); Assert.True(result.OperationalDataPreserved); Assert.False(result.PriorCatalogUnreadable);
        Assert.Equal(before, f.Catalog.Query().Select(CatalogTestFixture.Logical));
        Assert.All(f.Catalog.Query(), asset => Assert.Equal("PASS", asset.AuditState));
        Assert.Equal(operational.Objectives[0].Objective.Name, f.Catalog.Planning().Objectives[0].Objective.Name);
        Assert.Equal(operational.Objectives[0].Coverage, f.Catalog.Planning().Objectives[0].Coverage);
        Assert.Equal(operational.Campaigns[0].Campaign.ObjectiveIds, f.Catalog.Planning().Campaigns[0].Campaign.ObjectiveIds); f.AssertSources(sources); f.Catalog.CheckIntegrity();
        File.Delete(f.Catalog.CatalogPath); var missing = new CatalogRebuilder(f.Context).Rebuild(); Assert.False(missing.OperationalDataPreserved);
        Assert.Equal(before, f.Catalog.Query().Select(CatalogTestFixture.Logical)); Assert.Empty(f.Catalog.Planning().Objectives); Assert.Empty(f.Catalog.Planning().Campaigns);
        Assert.All(f.Catalog.Query(), asset => Assert.Null(asset.AuditState));
    }
    [Theory]
    [InlineData("random")] [InlineData("truncated")] [InlineData("header")] [InlineData("empty")]
    public void ExplicitRebuildCanReplaceUnreadableDbAndDoesNotInventOperationalData(string mutation)
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var expected = CatalogTestFixture.Logical(Assert.Single(f.Catalog.Query()));
        var valid = File.ReadAllBytes(f.Catalog.CatalogPath);
        File.WriteAllBytes(f.Catalog.CatalogPath, mutation switch { "random" => [1, 2, 3, 4], "truncated" => valid[..64], "header" => new byte[4096], _ => [] });
        Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()); var sources = f.Sources();
        var result = new CatalogRebuilder(f.Context).Rebuild(); Assert.True(result.PriorCatalogUnreadable); Assert.False(result.OperationalDataPreserved);
        Assert.Equal(expected, CatalogTestFixture.Logical(Assert.Single(f.Catalog.Query()))); Assert.Empty(f.Catalog.Planning().Objectives); f.AssertSources(sources);
    }
    [Fact]
    public void ReadableDerivedDamagePreservesOperationsButOperationalDamageRequiresReview()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); f.Catalog.SaveObjective(new(f.Context.Id, "o", "Objective", new(), 2));
        f.Catalog.SaveCampaign(new(f.Context.Id, "c", "Campaign", ["o"]));
        using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, "DROP INDEX ix_trait_selection; INSERT INTO classifications VALUES('future_world','missing','d','v')");
        Assert.Throws<CatalogException>(() => f.Catalog.CheckIntegrity()); Assert.True(new CatalogRebuilder(f.Context).Rebuild().OperationalDataPreserved);
        Assert.Single(f.Catalog.Planning().Objectives); Assert.Single(f.Catalog.Planning().Campaigns);
        using (var c = f.Raw(false)) CatalogTestFixture.Execute(c, "INSERT INTO campaign_objectives VALUES('future_world','c','missing')");
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new CatalogRebuilder(f.Context).Rebuild()), NapIssueCodes.CatalogRebuildFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void InvalidSourcesLeaveOldCatalogIntact()
    {
        using var f = new CatalogTestFixture(); var asset = f.Publish(automatic: true);
        File.WriteAllText(asset.Production.FilesVerified.Single(p => p.Kind == ProductionAssetFileKind.GeneratedWebp).DestinationPath, "broken");
        var before = ArchiveTestFixture.Snapshot(f.Root); Assert.Throws<CatalogException>(() => new CatalogRebuilder(f.Context).Rebuild());
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Single(f.Catalog.Query());
    }
    [Fact]
    public void FailureAfterTemporaryConstructionBeforePublicationPreservesOldCatalog()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); var plan = new CatalogImporter(f.Context).Plan();
        var original = File.ReadAllBytes(f.Catalog.CatalogPath); var sources = f.Sources();
        using var lease = (IDisposable)CatalogTestFixture.Invoke(null, "CatalogBoundary", "Acquire", f.Context)!;
        var revoked = new RevokingSnapshots(plan.Assets, lease);
        Assert.Throws<InvalidOperationException>(() => CatalogTestFixture.Invoke(f.Catalog, "AssetCatalog", "CreateAndPublish", revoked, lease, true, null!));
        Assert.Equal(original, File.ReadAllBytes(f.Catalog.CatalogPath)); f.AssertSources(sources); f.Catalog.CheckIntegrity();
        var temp = Assert.Single(Directory.GetFiles(f.Context.Storage.StateRoot, "AssetCatalog.*.tmp"));
        Assert.True(new FileInfo(temp).Length > 0); Assert.True(new JobRecoveryScanner(f.Context).Scan().Issues.IsClean);
    }
    [Fact]
    public void RebuildOfOneUniverseDoesNotTouchOtherUniverse()
    {
        using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta");
        a.Publish(automatic: true); b.Publish(automatic: true); var before = ArchiveTestFixture.Snapshot(b.Root);
        File.Delete(a.Catalog.CatalogPath); new CatalogRebuilder(a.Context).Rebuild(); ArchiveTestFixture.AssertSnapshot(before, b.Root);
        Assert.Equal(new UniverseId("alpha"), Assert.Single(a.Catalog.Query()).AssetKey.UniverseId);
    }
    private sealed class RevokingSnapshots(IReadOnlyList<CatalogAssetSnapshot> assets, IDisposable lease) : IReadOnlyList<CatalogAssetSnapshot>
    {
        public int Count => assets.Count;
        public CatalogAssetSnapshot this[int index] => assets[index];
        public IEnumerator<CatalogAssetSnapshot> GetEnumerator() { foreach (var asset in assets) yield return asset; lease.Dispose(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
