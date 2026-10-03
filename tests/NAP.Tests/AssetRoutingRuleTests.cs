using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AssetRoutingRuleTests
{
    [Fact]
    public void SegmentFactoriesProduceExactlyTheThreeSupportedVariants()
    {
        Assert.Equal(new[] { "Literal", "Classification", "AssetId" }, Enum.GetNames<AssetRouteSegmentKind>());
        var literal = AssetRouteSegment.Literal("portraits");
        Assert.Equal(AssetRouteSegmentKind.Literal, literal.Kind);
        Assert.Equal("portraits", literal.Value);
        var classification = AssetRouteSegment.Classification("culture");
        Assert.Equal(AssetRouteSegmentKind.Classification, classification.Kind);
        Assert.Equal("culture", classification.Value);
        var assetId = AssetRouteSegment.AssetId();
        Assert.Equal(AssetRouteSegmentKind.AssetId, assetId.Kind);
        Assert.Null(assetId.Value);
        Assert.True(typeof(AssetRouteSegment).IsSealed);
        Assert.Empty(typeof(AssetRouteSegment).GetConstructors());
        Assert.All(typeof(AssetRouteSegment).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Portraits")]
    [InlineData(" culture ")]
    [InlineData("two words")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../assets")]
    [InlineData("C:\\assets")]
    [InlineData("\\\\server\\share")]
    [InlineData("%HOME%")]
    [InlineData("${culture}")]
    [InlineData("{asset_id}")]
    [InlineData("a__b")]
    [InlineData("a_")]
    [InlineData("é")]
    [InlineData("name\n")]
    public void LiteralAndClassificationRejectUnsafeIdentifiersWithoutCorrection(string? value)
    {
        Assert.Throws<ArgumentException>(() => AssetRouteSegment.Literal(value!));
        Assert.Throws<ArgumentException>(() => AssetRouteSegment.Classification(value!));
    }

    [Fact]
    public void SegmentLengthMatchesExistingMachineIdentifierLimitAndValuesAreExact()
    {
        var value = "a" + new string('b', 63);
        Assert.Same(value, AssetRouteSegment.Literal(value).Value);
        Assert.Same(value, AssetRouteSegment.Classification(value).Value);
        Assert.Throws<ArgumentException>(() => AssetRouteSegment.Literal(value + "b"));
        Assert.Throws<ArgumentException>(() => AssetRouteSegment.Classification(value + "b"));
    }

    [Fact]
    public void NullEmptyAndNullItemsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new AssetRoutingRule(null!));
        Assert.Throws<ArgumentException>(() => new AssetRoutingRule([]));
        Assert.Throws<ArgumentException>(() => new AssetRoutingRule([AssetRouteSegment.AssetId(), null!]));
    }

    [Fact]
    public void RulePreservesOrderTakesDefensiveSnapshotAndAllowsRepeatedSegments()
    {
        var first = AssetRouteSegment.Literal("portraits");
        var last = AssetRouteSegment.AssetId();
        var source = new List<AssetRouteSegment> { first, first, last, last, AssetRouteSegment.Classification("culture") };
        var rule = new AssetRoutingRule(source);
        source.Clear();
        Assert.Equal(new[] { AssetRouteSegmentKind.Literal, AssetRouteSegmentKind.Literal,
            AssetRouteSegmentKind.AssetId, AssetRouteSegmentKind.AssetId, AssetRouteSegmentKind.Classification }, rule.Segments.Select(segment => segment.Kind));
        Assert.Same(first, rule.Segments[0]);
        Assert.Same(last, rule.Segments[3]);
        var collection = Assert.IsAssignableFrom<IList<AssetRouteSegment>>(rule.Segments);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection[0] = last);
        Assert.Single(new AssetRoutingRule([first]).Segments); // No classification or asset ID requirement.
        Assert.True(typeof(AssetRoutingRule).IsSealed);
    }

    [Fact]
    public void HistoricalConstructorsKeepRoutingNullAndSnapshots()
    {
        var historical = new UniverseAssetRule("portrait", "portrait_npc", [], []);
        var file = new AssetPackageFileRule("master", "", ".png", true);
        AssetPackageFileRule[] files = [file];
        var v2 = new UniverseAssetRule("portrait", "portrait_npc", [], [], files);
        files[0] = new AssetPackageFileRule("info", "_info", ".md", false);
        Assert.Null(historical.Routing);
        Assert.Empty(historical.PackageFiles);
        Assert.Null(v2.Routing);
        Assert.Same(file, Assert.Single(v2.PackageFiles));
    }

    [Fact]
    public void NewConstructorKeepsExactRoutingAndDefensivePackageAndClassificationSnapshots()
    {
        var file = new AssetPackageFileRule("master", "", ".png", true, "png_master");
        var files = new List<AssetPackageFileRule> { file };
        var allowed = new List<string> { "culture", "optional" };
        var required = new List<string> { "culture" };
        var routing = new AssetRoutingRule([AssetRouteSegment.Classification("culture"), AssetRouteSegment.AssetId()]);
        var rule = new UniverseAssetRule("portrait", "portrait_npc", allowed, required, files, routing);
        files.Clear(); allowed.Clear(); required.Clear();
        Assert.Same(routing, rule.Routing);
        Assert.Same(file, Assert.Single(rule.PackageFiles));
        Assert.Equal(new[] { "culture", "optional" }, rule.AllowedClassification);
        Assert.Equal(new[] { "culture" }, rule.RequiredClassification);
        Assert.Throws<NotSupportedException>(() => ((IList<AssetPackageFileRule>)rule.PackageFiles).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)rule.RequiredClassification).Clear());
    }

    [Theory]
    [InlineData("optional")]
    [InlineData("unregistered")]
    [InlineData("different")]
    public void RouteClassificationMustBeRequiredRatherThanMerelyAllowed(string dimension)
    {
        var routing = new AssetRoutingRule([AssetRouteSegment.Classification(dimension)]);
        var exception = Assert.Throws<ArgumentException>(() => new UniverseAssetRule("type", "profile",
            ["culture", "optional"], ["culture"], [], routing));
        Assert.Equal("routing", exception.ParamName);
    }

    [Fact]
    public void LiteralAndAssetIdNeedNoClassificationAndNewConstructorCanKeepRoutingNull()
    {
        var routing = new AssetRoutingRule([AssetRouteSegment.Literal("assets"), AssetRouteSegment.AssetId(), AssetRouteSegment.AssetId()]);
        Assert.Same(routing, new UniverseAssetRule("type", "profile", [], [], [], routing).Routing);
        Assert.Null(new UniverseAssetRule("type", "profile", [], [], [], null).Routing);
    }

    [Fact]
    public void RoutingModelsArePureAndDoNotAcceptRuntimeBoundariesOrResolvePaths()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "nap-routing-pure-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(missingRoot));
        var routing = new AssetRoutingRule([AssetRouteSegment.Literal("assets"), AssetRouteSegment.AssetId()]);
        _ = new UniverseAssetRule("type", "profile", [], [], [], routing);
        Assert.False(Directory.Exists(missingRoot));
        Type[] forbidden = [typeof(ValidatedAssetPackage), typeof(ValidatedProductionRepository), typeof(ProductionRepositorySnapshot), typeof(Stream)];
        foreach (var type in new[] { typeof(AssetRouteSegment), typeof(AssetRoutingRule) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(method.Name, new[] { "ResolvePath", "ResolveDirectory", "BuildPath", "GetDestination" });
                Assert.All(method.GetParameters(), parameter => Assert.DoesNotContain(parameter.ParameterType, forbidden));
            }
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
    }
}
