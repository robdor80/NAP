using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogExplorerTests
{
    [Fact] public void PagingIsDeterministicAndDoesNotLoadDocuments()
    {
        using var f = new CatalogTestFixture(); f.Publish("emblem_example_003", automatic: true); f.Publish("emblem_example_001", automatic: true); f.Publish("emblem_example_002", automatic: true);
        var reader = new CatalogExplorerReader(f.Context); var one = reader.Page(offset: 0, limit: 2); var two = reader.Page(offset: 2, limit: 2);
        Assert.Equal(3, one.TotalCount); Assert.Equal(new[] { "emblem_example_001", "emblem_example_002" }, one.Items.Select(a => a.AssetKey.AssetId)); Assert.Single(two.Items); Assert.Empty(reader.Page(offset: 3).Items);
        // Broken BLOB is not inspected by an index page, but full selected detail MUST STOP.
        using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE documents SET content=$bytes WHERE asset_id='emblem_example_003' AND role='prompt'", ("$bytes", new byte[] { 1 }));
        Assert.Equal(3, reader.Page().Items.Count); Assert.NotNull(reader.Detail("emblem_example_001"));
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => reader.Detail("emblem_example_003")), NapIssueCodes.CatalogIntegrityFailed);
    }
    [Theory] [InlineData(-1, 60)] [InlineData(0, 0)] [InlineData(0, 241)]
    public void PageBoundsRejected(int offset, int limit)
    { using var f = new CatalogTestFixture(); Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogExplorerReader(f.Context).Page(offset: offset, limit: limit)); }
    [Fact] public void CombinedGenericFacetsMatchExistingContractsAndStatistics()
    {
        using var f = new CatalogTestFixture(genericClassification: true);
        f.Publish("emblem_example_001", "{\"engine\":{\"fuel\":\"blue\"},\"enabled\":true,\"power\":2}", automatic: true, classification: new() { ["material"] = "steel", ["planet"] = "gamma" });
        f.Publish("emblem_example_002", "{\"engine\":{\"fuel\":\"red\"},\"enabled\":false,\"power\":2}", automatic: true, classification: new() { ["material"] = "steel", ["planet"] = "beta" });
        var filter = new CatalogFilter(assetType: "emblem", productionProfile: "painted_icon", classification: new Dictionary<string, string> { ["material"] = "steel", ["planet"] = "gamma" }, traits: [new("/engine/fuel", "blue", CatalogTraitType.String), new("/enabled", "true", CatalogTraitType.Boolean)]);
        var reader = new CatalogExplorerReader(f.Context); Assert.Single(reader.Page(filter).Items); Assert.Equal(f.Catalog.Query(filter).Single().AssetKey, reader.Page(filter).Items.Single().AssetKey);
        var expected = f.Catalog.Statistics(filter); var actual = reader.Statistics(filter); Assert.Equal(expected.TotalAssets, actual.TotalAssets); Assert.Equal(expected.Classifications, actual.Classifications); Assert.Equal(expected.Traits, actual.Traits); Assert.Equal(expected.AssetTypes, actual.AssetTypes); Assert.Equal(expected.ProductionProfiles, actual.ProductionProfiles);
        Assert.Contains(reader.Statistics().Classifications, d => d.Dimension == "planet" && d.Value == "beta"); Assert.Contains(reader.Statistics().Traits, d => d.Dimension == "/power" && d.TraitType == CatalogTraitType.Number);
    }
    [Fact] public void InjectionValuesAreOnlyExactBoundValues()
    { using var f = new CatalogTestFixture(); f.Publish(automatic: true); var r = new CatalogExplorerReader(f.Context); Assert.Empty(r.Page(new(assetId: "' OR 1=1 --")).Items); Assert.Equal(0, r.Statistics(new(traits: [new("/eye_color", "' OR 1=1 --", CatalogTraitType.String)])).TotalAssets); Assert.Single(f.Catalog.Query()); }
    [Fact] public async Task AsyncServiceDetailDocumentsAndCoverageAreRealAndReadOnly()
    {
        using var f = new CatalogTestFixture(); f.Publish(automatic: true); f.Catalog.SaveObjective(new(f.Context.Id, "goal", "Two icons", new(assetType: "emblem"), 2)); f.Catalog.SaveCampaign(new(f.Context.Id, "campaign", "Campaign", ["goal"]));
        var sources = f.Sources(); var database = File.ReadAllBytes(f.Catalog.CatalogPath); var service = new ExplorerService();
        var page = await service.PageAsync(f.Context, new(), 0, 60, default); var detail = await service.DetailAsync(f.Context, page.Items.Single().AssetKey.AssetId, default);
        Assert.NotNull(detail); Assert.Equal("PASS", detail.AuditState); Assert.Equal(f.Catalog.Get(detail.AssetKey.AssetId)!.Documents.Select(d => d.ToArray()), detail.Documents.Select(d => d.ToArray()));
        var planning = await service.PlanningAsync(f.Context, default); Assert.Equal(1, planning.Objectives.Single().Coverage.ActualCount); Assert.Equal(1, planning.Objectives.Single().Coverage.Remaining); Assert.Single(planning.Campaigns);
        Assert.Equal(database, File.ReadAllBytes(f.Catalog.CatalogPath)); f.AssertSources(sources);
    }
    [Fact] public async Task EmptyAndAbsentDataAreNeverInvented()
    { using var f = new CatalogTestFixture(); f.Catalog.Initialize(); var service = new ExplorerService(); Assert.Empty((await service.PageAsync(f.Context, new(), 0, 60, default)).Items); Assert.Null(await service.DetailAsync(f.Context, "absent", default)); Assert.Equal(0, (await service.StatisticsAsync(f.Context, new(), default)).TotalAssets); Assert.Empty((await service.PlanningAsync(f.Context, default)).Objectives); }
    [Fact] public async Task SameAssetIdInDifferentUniversesStaysIsolated()
    { using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); a.Publish(automatic: true); b.Publish(automatic: true); var s = new ExplorerService(); var ap = await s.PageAsync(a.Context, new(), 0, 60, default); var bp = await s.PageAsync(b.Context, new(), 0, 60, default); Assert.NotEqual(ap.Items.Single().AssetKey, bp.Items.Single().AssetKey); }
    [Fact] public async Task MissingCatalogReportsStopAndDoesNotCreateIt()
    { using var f = new CatalogTestFixture(); var error = await Assert.ThrowsAsync<CatalogException>(() => new ExplorerService().PageAsync(f.Context, new(), 0, 60, default)); Assert.Equal(NapIssueCodes.CatalogMissing, ExplorerError.From(error).Code); Assert.False(File.Exists(f.Catalog.CatalogPath)); }
    [Fact] public async Task PreCancelledCatalogLoadDoesNotCreateState()
    { using var f = new CatalogTestFixture(); using var c = new CancellationTokenSource(); c.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ExplorerService().PageAsync(f.Context, new(), 0, 60, c.Token)); Assert.False(File.Exists(f.Catalog.CatalogPath)); }
    [Fact] public void TamperedProjectionFingerprintStops()
    { using var f = new CatalogTestFixture(); f.Publish(automatic: true); using (var c = f.Raw()) CatalogTestFixture.Execute(c, "UPDATE files SET sha256=$h WHERE kind='generated_webp'", ("$h", new string('a', 64))); CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new CatalogExplorerReader(f.Context).Page()), NapIssueCodes.CatalogIntegrityFailed); }
    [Fact] public void ReplacedUniverseCatalogStops()
    { using var a = new CatalogTestFixture(universe: "alpha"); using var b = new CatalogTestFixture(universe: "beta"); a.Catalog.Initialize(); b.Catalog.Initialize(); File.Copy(b.Catalog.CatalogPath, a.Catalog.CatalogPath, true); CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => new CatalogExplorerReader(a.Context).Page()), NapIssueCodes.CatalogWrongUniverse); }
    [Fact] public void SchemaRemainsExactlyVersionOne()
    { using var f = new CatalogTestFixture(); f.Catalog.Initialize(); using var c = f.Raw(); Assert.Equal(1L, CatalogTestFixture.Scalar(c, "PRAGMA user_version")); Assert.Equal(11L, CatalogTestFixture.Scalar(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='table'")); }
}
