using System.Globalization;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogQueryTests
{
    [Fact]
    public void FutureDimensionsTypesAndProfilesAreStoredFilteredAndGroupedWithoutUniverseVocabulary()
    {
        using var f = new CatalogTestFixture(genericClassification: true);
        f.Publish("emblem_a_001", automatic: true, classification: new() { ["material"] = "stone", ["planet"] = "venus" });
        f.Publish("banner_a_001", automatic: true, classification: new() { ["material"] = "cloth", ["planet"] = "venus" }, assetType: "banner", productionProfile: "painted_banner");
        Assert.Single(f.Catalog.Query(new CatalogFilter(assetType: "banner", productionProfile: "painted_banner", classification: new Dictionary<string, string> { ["material"] = "cloth", ["planet"] = "venus" })));
        var facts = f.Catalog.Statistics(); Assert.Equal(2, facts.AssetTypes.Count); Assert.Equal(2, facts.ProductionProfiles.Count);
        Assert.Contains(new CatalogDistribution("planet", "venus", 2), facts.Classifications);
        Assert.Contains(new CatalogDistribution("material", "stone", 1), facts.Classifications);
        f.Catalog.SaveObjective(new(f.Context.Id, "future", "Cloth banners", new(assetType: "banner", classification: new Dictionary<string, string> { ["material"] = "cloth" }), 3));
        Assert.Equal(2, Assert.Single(f.Catalog.Planning().Objectives).Coverage.Remaining);
    }
    [Theory]
    [InlineData("en-US")] [InlineData("tr-TR")] [InlineData("ar-SA")]
    public void StructuredAndFiltersUseExactValuesStableOrdinalOrderAndGenericTraits(string culture)
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Publish("portrait_treskal_farmer_male_003", "{\"future_trait\":\"green-grey\"}", true);
        f.Publish("portrait_treskal_farmer_male_001", "{\"future_trait\":\"brown\"}", true);
        f.Publish("portrait_treskal_farmer_male_002", "{\"future_trait\":\"green-grey\"}", true);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(new[] { "portrait_treskal_farmer_male_001", "portrait_treskal_farmer_male_002", "portrait_treskal_farmer_male_003" }, f.Catalog.Query().Select(a => a.AssetKey.AssetId));
            var filter = new CatalogFilter(assetType: "portrait", productionProfile: "portrait_npc", classification: new Dictionary<string, string>
                { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male" }, traits: [new("/future_trait", "green-grey", CatalogTraitType.String)]);
            Assert.Equal(2, f.Catalog.Query(filter).Count);
            Assert.Single(f.Catalog.Query(new CatalogFilter(assetId: "portrait_treskal_farmer_male_002", traits: filter.Traits)));
            Assert.Empty(f.Catalog.Query(new CatalogFilter(assetType: "Portrait"))); Assert.Empty(f.Catalog.Query(new CatalogFilter(productionProfile: "unknown")));
            Assert.Empty(f.Catalog.Query(new CatalogFilter(classification: new Dictionary<string, string> { ["Culture"] = "norgard" })));
            Assert.Empty(f.Catalog.Query(new CatalogFilter(traits: [new("/future_trait", "GREEN-GREY", CatalogTraitType.String)])));
            Assert.Empty(f.Catalog.Query(new CatalogFilter(assetType: "portrait", traits: [new("/future_trait", "green-grey", CatalogTraitType.Number)])));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData("' OR 1=1 --")] [InlineData("x'; DROP TABLE assets;--")]
    [InlineData("green-grey\n\"quoted\"\\value")] [InlineData("日本語 🌍")]
    public void SqlInjectionAndProblematicValuesRemainBoundData(string value)
    {
        using var f = new CatalogTestFixture(); f.Publish(identity: System.Text.Json.JsonSerializer.Serialize(new { future_trait = value }), automatic: true);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        Assert.Single(f.Catalog.Query(new CatalogFilter(traits: [new("/future_trait", value, CatalogTraitType.String)])));
        Assert.Empty(f.Catalog.Query(new CatalogFilter(assetId: value, assetType: value, productionProfile: value)));
        Assert.Empty(f.Catalog.Query(new CatalogFilter(classification: new Dictionary<string, string> { [value] = value })));
        f.Catalog.CheckIntegrity(); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void StatisticsCoverageAndDiversityReturnFactsForTheSelectedAssets()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Publish("portrait_treskal_farmer_male_001", "{\"eye_color\":\"brown\"}", true);
        f.Publish("portrait_treskal_farmer_male_002", "{\"eye_color\":\"green-grey\"}", true);
        f.Publish("portrait_treskal_farmer_male_003", "{\"eye_color\":\"brown\"}", true);
        var stats = f.Catalog.Statistics(); Assert.Equal(3, stats.TotalAssets);
        Assert.Equal(new CatalogDistribution("asset_type", "portrait", 3), Assert.Single(stats.AssetTypes));
        Assert.Equal(new CatalogDistribution("production_profile", "portrait_npc", 3), Assert.Single(stats.ProductionProfiles));
        Assert.Contains(new CatalogDistribution("location", "treskal", 3), stats.Classifications);
        Assert.Contains(new CatalogDistribution("/eye_color", "brown", 2, CatalogTraitType.String), stats.Traits);
        Assert.Contains(new CatalogDistribution("/eye_color", "green-grey", 1, CatalogTraitType.String), stats.Traits);
        var filter = new CatalogFilter(traits: [new("/eye_color", "brown", CatalogTraitType.String)]);
        var coverage = f.Catalog.Coverage(filter, 10); Assert.Equal(2, coverage.ActualCount); Assert.Equal(10, coverage.TargetCount);
        Assert.Equal(8, coverage.Remaining); Assert.Equal(.2, coverage.CompletionRatio); Assert.False(coverage.IsComplete);
        Assert.Equal(2, f.Catalog.Statistics(filter).TotalAssets);
        var complete = f.Catalog.Coverage(filter, 1); Assert.Equal(0, complete.Remaining); Assert.Equal(2, complete.CompletionRatio); Assert.True(complete.IsComplete);
        Assert.Equal(0, f.Catalog.Statistics(new CatalogFilter(assetType: "missing")).TotalAssets);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Catalog.Coverage(filter, 0));
    }
    [Fact]
    public void ObjectivesCampaignsAndRodoReadModelAreGenericPersistedAndTransactional()
    {
        using var f = new CatalogTestFixture(); f.Publish(identity: "{\"future_trait\":\"unique\"}", automatic: true);
        var filter = new CatalogFilter(assetType: "emblem", productionProfile: "painted_icon", traits: [new("/future_trait", "unique", CatalogTraitType.String)]);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective';--", "Target ' \"未来\"", filter, 10));
        f.Catalog.SaveObjective(new(f.Context.Id, "complete", "Complete", new(), 1));
        f.Catalog.SaveObjective(new(f.Context.Id, "arbitrary", "Future dimension", new(classification: new Dictionary<string, string> { ["new_dimension"] = "new_value" }), 3));
        f.Catalog.SaveCampaign(new(f.Context.Id, "campaign';--", "Campaign", ["objective';--", "complete", "arbitrary"]));
        var model = f.Catalog.Planning(); Assert.Equal(f.Context.Id, model.UniverseId); Assert.Equal(3, model.Objectives.Count);
        Assert.Equal(3, Assert.Single(model.Campaigns).Objectives.Count);
        var pending = model.Objectives.Single(o => o.Objective.Id == "objective';--"); Assert.Equal(9, pending.Coverage.Remaining);
        Assert.Contains(pending.Diversity.Traits, d => d.Dimension == "/future_trait" && d.Count == 1);
        Assert.True(model.Objectives.Single(o => o.Objective.Id == "complete").Coverage.IsComplete);
        Assert.Equal(0, model.Objectives.Single(o => o.Objective.Id == "arbitrary").Coverage.ActualCount);
        f.Catalog.SaveObjective(new(f.Context.Id, "objective';--", "Updated", new(assetType: "unknown"), 7));
        Assert.Equal(7, f.Catalog.Planning().Objectives.Single(o => o.Objective.Id == "objective';--").Coverage.Remaining);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.SaveCampaign(new(f.Context.Id, "campaign';--", "Bad update", ["missing"]))), NapIssueCodes.CatalogIntegrityFailed);
        ArchiveTestFixture.AssertSnapshot(before, f.Root); Assert.Equal(3, Assert.Single(f.Catalog.Planning().Campaigns).Campaign.ObjectiveIds.Count);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.SaveObjective(new(new UniverseId("other"), "other", "Other", new(), 1))), NapIssueCodes.CatalogWrongUniverse);
        CatalogTestFixture.Stop(Assert.Throws<CatalogException>(() => f.Catalog.SaveCampaign(new(new UniverseId("other"), "other", "Other", []))), NapIssueCodes.CatalogWrongUniverse);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CatalogObjective(f.Context.Id, "bad", "Bad", new(), 0));
    }
}
