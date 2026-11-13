using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class CatalogDiversityTests
{
    private static readonly UniverseId Nimroel = new("nimroel");

    [Fact]
    public void FiftySixBlacksmithsAndTwoCarpentersProduceRelativeOpportunityWithoutInventedQuota()
    {
        var assets = Enumerable.Repeat("blacksmith", 56)
            .Concat(Enumerable.Repeat("fisher", 45))
            .Concat(Enumerable.Repeat("farmer", 54))
            .Concat(Enumerable.Repeat("carpenter", 2))
            .Select(role => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["asset_type"] = "portrait",
                ["production_profile"] = "portrait_npc",
                ["classification:role"] = role
            }).ToArray();
        var snapshot = CatalogDiversityReader.Aggregate(Nimroel, assets);
        Assert.Equal(157, snapshot.TotalAssets);
        Assert.Equal(56, snapshot.Counts.Single(c => c.Dimension == "classification:role" && c.Value == "blacksmith").Count);
        var carpenter = snapshot.Counts.Single(c => c.Dimension == "classification:role" && c.Value == "carpenter");
        Assert.Equal(2, carpenter.Count);
        Assert.Equal(157, carpenter.ClassifiedAssets);
        Assert.Equal(45, snapshot.Counts.Single(c => c.Value == "fisher").Count);
        Assert.Equal(54, snapshot.Counts.Single(c => c.Value == "farmer").Count);
        var opportunity = Assert.Single(snapshot.Opportunities.Where(o => o.Dimension == "classification:role" && o.Value == "carpenter"));
        Assert.Equal(56, opportunity.ComparedCount);
        Assert.Equal("blacksmith", opportunity.ComparedTo);
        Assert.DoesNotContain("excedente", opportunity.Explanation, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(snapshot.ToJson());
        Assert.Equal(157, json.RootElement.GetProperty("total_verified_assets").GetInt64());
        Assert.False(snapshot.ToJson().Contains("asset_id", StringComparison.Ordinal));
    }

    [Fact]
    public void ActualCrossTabsDoNotInventSexOrAgeByMultiplyingMarginals()
    {
        var assets = new IReadOnlyDictionary<string, string>[]
        {
            new Dictionary<string, string> { ["classification:role"] = "carpenter", ["classification:sex"] = "female", ["visual:/age_group"] = "elder" },
            new Dictionary<string, string> { ["classification:role"] = "carpenter", ["classification:sex"] = "male", ["visual:/age_group"] = "young" },
            new Dictionary<string, string> { ["classification:role"] = "blacksmith", ["classification:sex"] = "male", ["visual:/age_group"] = "young" },
            new Dictionary<string, string> { ["classification:role"] = "blacksmith", ["classification:sex"] = "female", ["visual:/age_group"] = "young" }
        };
        var report = CatalogDiversityReader.Aggregate(Nimroel, assets);
        Assert.Contains(report.Crosses, c => c.FirstDimension == "classification:role" &&
            c.FirstValue == "carpenter" && c.SecondDimension == "classification:sex" &&
            c.SecondValue == "female" && c.Count == 1);
        Assert.DoesNotContain(report.Crosses, c => c.FirstDimension == "classification:role" &&
            c.FirstValue == "carpenter" && c.SecondDimension == "visual:/age_group" && c.SecondValue == "young" && c.Count > 1);
        Assert.Single(report.Crosses, c => c.FirstDimension == "classification:role" && c.FirstValue == "carpenter" &&
            c.SecondDimension == "visual:/age_group" && c.SecondValue == "elder" && c.Count == 1);
    }

    [Fact]
    public void MissingDemographicsAreUnknownNotZeroAndSourcesStayUnmodified()
    {
        var facts = new Dictionary<string, string> { ["classification:role"] = "farmer" };
        var report = CatalogDiversityReader.Aggregate(Nimroel, [facts]);
        Assert.Equal(1, report.TotalAssets);
        Assert.DoesNotContain(report.Counts, r => r.Dimension.Contains("sex", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Counts, r => r.Dimension.Contains("age", StringComparison.Ordinal));
        Assert.Empty(report.Opportunities);
        Assert.Equal("farmer", facts["classification:role"]);
        Assert.Contains("Unrecorded categories", report.ToJson());
    }

    [Fact]
    public void EmptyAndBoundedCatalogsCannotBeMistakenForFullCoverage()
    {
        var empty = CatalogDiversityReader.Aggregate(Nimroel, Array.Empty<IReadOnlyDictionary<string, string>>());
        Assert.Equal(0, empty.TotalAssets);
        Assert.Empty(empty.Counts);
        Assert.Empty(empty.Crosses);
        Assert.Throws<InvalidOperationException>(() => CatalogDiversityReader.Aggregate(Nimroel,
            Enumerable.Repeat<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(), CatalogDiversityReader.MaxAssets + 1)));
    }

    [Fact]
    public void RealSqliteCatalogReadUsesOnlyVerifiedMetadataAndNeverWrites()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Publish("portrait_treskal_farmer_male_001", "{\"age_group\":\"adult\",\"eye_color\":\"brown\"}", automatic: true);
        f.Publish("portrait_treskal_farmer_male_002", "{\"age_group\":\"elder\"}", automatic: true);
        var before = ArchiveTestFixture.Snapshot(f.Root);
        var report = new CatalogDiversityReader(f.Context).Read();
        Assert.Equal(f.Context.Id, report.UniverseId);
        Assert.Equal(2, report.TotalAssets);
        Assert.Contains(report.Counts, c => c.Dimension == "classification:role" && c.Value == "farmer" && c.Count == 2);
        Assert.Contains(report.Counts, c => c.Dimension == "visual:/age_group" && c.Value == "adult" && c.Count == 1);
        Assert.Contains(report.Counts, c => c.Dimension == "visual:/age_group" && c.Value == "elder" && c.Count == 1);
        Assert.Contains(report.Crosses, c => c.FirstDimension == "classification:role" && c.FirstValue == "farmer" &&
            c.SecondDimension == "visual:/age_group" && c.SecondValue == "adult" && c.Count == 1);
        Assert.DoesNotContain("eye_color", report.Crosses.Select(c => c.FirstDimension + c.SecondDimension));
        f.Catalog.CheckIntegrity();
        ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
}
