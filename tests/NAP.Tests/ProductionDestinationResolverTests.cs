using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionDestinationResolverTests
{
    private const string DefaultAssetId = "portrait_example_001";
    private static string TestRoot => Path.Combine(Path.GetTempPath(), "nap-destination-tests", "production");
    private readonly ProductionDestinationResolver _resolver = new();

    [Fact]
    public void NullInputsAreRejectedAndPublicApiOnlyAcceptsValidatedPackageAndRepository()
    {
        Assert.Equal("package", Assert.Throws<ArgumentNullException>(() => _resolver.Resolve(null!, Repository())).ParamName);
        Assert.Equal("repository", Assert.Throws<ArgumentNullException>(() => _resolver.Resolve(Package(), null!)).ParamName);
        Assert.True(typeof(ProductionDestinationResolver).IsSealed);
        var method = Assert.Single(typeof(ProductionDestinationResolver).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Resolve", method.Name);
        Assert.Equal(typeof(ProductionAssetDestination), method.ReturnType);
        Assert.Equal(new[] { typeof(ValidatedAssetPackage), typeof(ValidatedProductionRepository) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void DifferentUniverseIsRejectedBeforeResolvingEvenWithoutRouting()
    {
        var package = Package(rule: Rule(null));
        Assert.Equal("repository", Assert.Throws<ArgumentException>(() =>
            _resolver.Resolve(package, Repository(universe: "other_universe"))).ParamName);
    }

    [Fact]
    public void EqualUniverseValuesDoNotRequireTheSameInstanceAndIdentityIsPreserved()
    {
        var package = Package();
        var repository = Repository();
        Assert.NotSame(package.AssetKey.UniverseId, repository.UniverseId);
        Assert.Equal(package.AssetKey.UniverseId, repository.UniverseId);
        var destination = _resolver.Resolve(package, repository);
        Assert.Same(package.AssetKey, destination.AssetKey);
        Assert.Same(repository.RootPath, destination.RootPath);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HistoricalProfilesWithoutRoutingCannotResolve(int version)
    {
        var profile = UniverseProfileLoader.Load(version == 1
            ? UniverseProfileLoaderTests.HistoricalConfigPath : UniverseProfileLoaderTests.HistoricalV2ConfigPath);
        var rule = Assert.Single(profile.AssetRules);
        Assert.Null(rule.Routing);
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(
            Package(rule: rule, universe: profile.Id.Value), Repository(universe: profile.Id.Value)));
    }

    [Theory]
    [InlineData("portrait_treskal_farmer_male_002", "farmer", "male")]
    [InlineData("portrait_treskal_farmer_boy_002", "farmer", "male")]
    [InlineData("portrait_treskal_boy_001", "village_child", "male")]
    [InlineData("portrait_treskal_elder_male_001", "village_elder", "male")]
    public void RealNimroelPolicyUsesClassificationWithoutAssetIdInferenceOrLegacyCasing(string assetId, string role, string sex)
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        var package = Package(Assert.Single(profile.AssetRules), NimroelClassification(role, sex), assetId, profile.Id.Value);
        var destination = _resolver.Resolve(package, Repository(universe: profile.Id.Value));
        Assert.Equal($"portraits/norgard/treskal/{role}/{sex}/{assetId}", destination.RelativeDirectory);
        Assert.DoesNotContain("children/boy", destination.RelativeDirectory);
        Assert.DoesNotContain("/elder/", destination.RelativeDirectory);
        Assert.DoesNotContain("Norgard", destination.RelativeDirectory);
        Assert.DoesNotContain("Treskal", destination.RelativeDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(TestRoot, "portraits", "norgard", "treskal", role, sex, assetId)),
            destination.FullDirectoryPath);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OptionalRealmAndRegionDoNotAffectNimroelRouting(bool realm, bool region)
    {
        var rule = Assert.Single(UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath).AssetRules);
        var classification = NimroelClassification();
        var baseline = _resolver.Resolve(Package(rule, classification), Repository());
        if (realm) classification.Add("realm", "example_realm");
        if (region) classification.Add("region", "example_region");
        var result = _resolver.Resolve(Package(rule, classification), Repository());
        Assert.Equal(baseline.RelativeDirectory, result.RelativeDirectory);
        Assert.Equal(baseline.FullDirectoryPath, result.FullDirectoryPath);
    }

    [Fact]
    public void ClassificationDictionaryOrderDoesNotChangeRouteOrder()
    {
        var rule = Rule(new AssetRoutingRule([AssetRouteSegment.Classification("culture"), AssetRouteSegment.Classification("role")]));
        var first = new Dictionary<string, string> { ["role"] = "farmer", ["culture"] = "norgard" };
        var second = new Dictionary<string, string> { ["culture"] = "norgard", ["role"] = "farmer" };
        Assert.Equal("norgard/farmer", _resolver.Resolve(Package(rule, first), Repository()).RelativeDirectory);
        Assert.Equal("norgard/farmer", _resolver.Resolve(Package(rule, second), Repository()).RelativeDirectory);
    }

    [Fact]
    public void RouteOrderAndRepetitionsArePreservedIncludingClassificationAndAssetId()
    {
        var route = new AssetRoutingRule([AssetRouteSegment.Literal("assets"), AssetRouteSegment.Classification("culture"),
            AssetRouteSegment.Classification("culture"), AssetRouteSegment.AssetId(), AssetRouteSegment.AssetId()]);
        var classification = new Dictionary<string, string> { ["culture"] = "example_culture" };
        Assert.Equal($"assets/example_culture/example_culture/{DefaultAssetId}/{DefaultAssetId}",
            _resolver.Resolve(Package(Rule(route), classification), Repository()).RelativeDirectory);
        var reversed = Rule(new AssetRoutingRule(route.Segments.Reverse()));
        Assert.Equal($"{DefaultAssetId}/{DefaultAssetId}/example_culture/example_culture/assets",
            _resolver.Resolve(Package(reversed, classification), Repository()).RelativeDirectory);
    }

    [Fact]
    public void LiteralOnlyAndAssetIdOnlyAndClassificationOnlyRoutesAreSupported()
    {
        Assert.Equal("assets", _resolver.Resolve(Package(Rule(new AssetRoutingRule([AssetRouteSegment.Literal("assets")]))), Repository()).RelativeDirectory);
        Assert.Equal(DefaultAssetId, _resolver.Resolve(Package(Rule(new AssetRoutingRule([AssetRouteSegment.AssetId()]))), Repository()).RelativeDirectory);
        var rule = Rule(new AssetRoutingRule([AssetRouteSegment.Classification("culture")]));
        Assert.Equal("example_culture", _resolver.Resolve(Package(rule, new() { ["culture"] = "example_culture" }), Repository()).RelativeDirectory);
    }

    [Fact]
    public void MissingClassificationInDeliberatelyIncoherentInternalPackageFailsClosed()
    {
        var rule = Rule(new AssetRoutingRule([AssetRouteSegment.Classification("culture")]));
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(Package(rule), Repository()));
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(Package(rule, new() { ["Culture"] = "example" }), Repository()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Norgard")]
    [InlineData(" example")]
    [InlineData("example ")]
    [InlineData("..")]
    [InlineData("../production_evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:\\escape")]
    [InlineData("/escape")]
    [InlineData("nórgard")]
    [InlineData(null)]
    public void UnsafeClassificationInInternalPackageIsRejectedWithoutCorrection(string? value)
    {
        var rule = Rule(new AssetRoutingRule([AssetRouteSegment.Classification("culture")]));
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(Package(rule, new() { ["culture"] = value! }), Repository()));
    }

    [Fact]
    public void UnsafeLiteralInDeliberatelyCorruptedInternalRoutingFailsClosed()
    {
        var segment = AssetRouteSegment.Literal("assets");
        typeof(AssetRouteSegment).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(segment, "../production_evil");
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(Package(Rule(new AssetRoutingRule([segment]))), Repository()));
    }

    [Fact]
    public void UnsafeAssetIdCannotBeConstructedThroughIdentityBoundaryAndResolverAlsoDefendsIt()
    {
        // The public UniverseAssetKey constructor prevents an unsafe asset ID. Reflection below
        // deliberately breaks that boundary to exercise the resolver's independent safety check.
        Assert.Throws<ArgumentException>(() => new UniverseAssetKey(new UniverseId("test_universe"), "../escape"));
        var package = Package();
        typeof(UniverseAssetKey).GetField("<AssetId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(package.AssetKey, "../escape");
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(package, Repository()));
    }

    public static IEnumerable<object[]> ReservedDeviceNames()
    {
        foreach (var name in new[] { "con", "prn", "aux", "nul" }
            .Concat(Enumerable.Range(1, 9).Select(number => "com" + number))
            .Concat(Enumerable.Range(1, 9).Select(number => "lpt" + number)))
        {
            yield return [name, false];
            yield return [name, true];
        }
    }

    [Theory]
    [MemberData(nameof(ReservedDeviceNames))]
    public void ExactWindowsDeviceNamesAreRejectedInLiteralAndClassificationOnEveryPlatform(string name, bool classification)
    {
        var segment = classification ? AssetRouteSegment.Classification("culture") : AssetRouteSegment.Literal(name);
        var package = Package(Rule(new AssetRoutingRule([segment])), new() { ["culture"] = name });
        Assert.Throws<InvalidOperationException>(() => _resolver.Resolve(package, Repository()));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("com10")]
    [InlineData("lpt10")]
    [InlineData("con_artist")]
    [InlineData("auxiliary")]
    public void SimilarNonReservedNamesAreAllowed(string name)
    {
        var literal = Rule(new AssetRoutingRule([AssetRouteSegment.Literal(name)]));
        var classification = Rule(new AssetRoutingRule([AssetRouteSegment.Classification("culture")]));
        Assert.Equal(name, _resolver.Resolve(Package(literal), Repository()).RelativeDirectory);
        Assert.Equal(name, _resolver.Resolve(Package(classification, new() { ["culture"] = name }), Repository()).RelativeDirectory);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("production/")]
    [InlineData("production with spaces")]
    [InlineData("producción 日本語")]
    public void RelativePathIsPortableAndFullPathIsNormalizedNativeAndContained(string rootName)
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-destination-tests", rootName.Replace('/', Path.DirectorySeparatorChar));
        var repository = Repository(root);
        var package = Package();
        var result = _resolver.Resolve(package, repository);
        Assert.Equal($"assets/{DefaultAssetId}", result.RelativeDirectory);
        Assert.False(result.RelativeDirectory.StartsWith('/'));
        Assert.False(result.RelativeDirectory.EndsWith('/'));
        Assert.DoesNotContain('\\', result.RelativeDirectory);
        Assert.DoesNotContain(repository.RootPath, result.RelativeDirectory);
        Assert.DoesNotContain(package.PackageRoot, result.RelativeDirectory);
        Assert.DoesNotContain(package.ManifestPath, result.RelativeDirectory);
        Assert.True(Path.IsPathFullyQualified(result.FullDirectoryPath));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "assets", DefaultAssetId)), result.FullDirectoryPath);
        Assert.True(IsStrictlyWithinRoot(root, result.FullDirectoryPath));
        Assert.Same(repository.RootPath, result.RootPath);
    }

    [Fact]
    public void ContainmentRejectsEqualRootSiblingPrefixAndTraversalAndUsesPlatformComparison()
    {
        var parent = Path.Combine(Path.GetTempPath(), "nap-destination-tests");
        var root = Path.Combine(parent, "prod");
        Assert.False(IsStrictlyWithinRoot(root, root));
        Assert.False(IsStrictlyWithinRoot(root + Path.DirectorySeparatorChar, root));
        Assert.False(IsStrictlyWithinRoot(root, Path.Combine(parent, "production_evil", "asset")));
        Assert.False(IsStrictlyWithinRoot(root, Path.Combine(root, "..", "production_evil", "asset")));
        Assert.True(IsStrictlyWithinRoot(root, Path.Combine(root, "unused", "..", "asset")));
        Assert.Equal(OperatingSystem.IsWindows(), IsStrictlyWithinRoot(root, Path.Combine(root.ToUpperInvariant(), "asset")));
        var volumeRoot = Path.GetPathRoot(root)!;
        Assert.False(IsStrictlyWithinRoot(volumeRoot, volumeRoot));
        Assert.True(IsStrictlyWithinRoot(volumeRoot, root));
    }

    [Fact]
    public void ResolverUsesRetainedRuleAndManifestSnapshotRatherThanLaterChanges()
    {
        var dimensions = new Dictionary<string, string> { ["culture"] = "original" };
        var route = new List<AssetRouteSegment> { AssetRouteSegment.Classification("culture") };
        var package = Package(Rule(new AssetRoutingRule(route)), dimensions);
        dimensions["culture"] = "changed";
        route[0] = AssetRouteSegment.Literal("changed");
        package.Manifest.Classification["culture"] = "changed_again";
        var newPackage = Package(Rule(new AssetRoutingRule(route)), dimensions);
        Assert.Equal("original", _resolver.Resolve(package, Repository()).RelativeDirectory);
        Assert.Equal("changed", _resolver.Resolve(newPackage, Repository()).RelativeDirectory);
    }

    [Fact]
    public void PackageRootManifestPathAndFilesByRoleDoNotAffectRouting()
    {
        var first = Package();
        var other = Construct<ValidatedAssetPackage>(first.AssetKey, Path.Combine(TestRoot, "other_package"),
            Path.Combine(TestRoot, "other_manifest.json"), first.Manifest, first.AssetRule,
            new Dictionary<string, string> { ["arbitrary_role"] = "opaque unrelated file" });
        var before = _resolver.Resolve(first, Repository());
        var after = _resolver.Resolve(other, Repository());
        Assert.Equal(before.RelativeDirectory, after.RelativeDirectory);
        Assert.Equal(before.FullDirectoryPath, after.FullDirectoryPath);
    }

    [Fact]
    public void AssetIdSegmentUsesAssetKeyEvenInAnIncoherentInternalManifestSnapshot()
    {
        var original = Package();
        var manifest = original.Manifest with { AssetId = "portrait_different_002" };
        var package = Construct<ValidatedAssetPackage>(original.AssetKey, original.PackageRoot,
            original.ManifestPath, manifest, original.AssetRule, original.FilesByRole);
        Assert.Equal($"assets/{DefaultAssetId}", _resolver.Resolve(package, Repository()).RelativeDirectory);
    }

    [Fact]
    public void NonexistentRootAndParentsAreNotCreatedAndRepeatedResolutionsAreDeterministic()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-destination-absent-" + Guid.NewGuid().ToString("N"), "missing", "production");
        var repository = Repository(root);
        var package = Package();
        Assert.False(Directory.Exists(root));
        var first = _resolver.Resolve(package, repository);
        var second = _resolver.Resolve(package, repository);
        Assert.Equal(first.AssetKey, second.AssetKey);
        Assert.Equal(first.RootPath, second.RootPath);
        Assert.Equal(first.RelativeDirectory, second.RelativeDirectory);
        Assert.Equal(first.FullDirectoryPath, second.FullDirectoryPath);
        Assert.False(Directory.Exists(first.FullDirectoryPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(root))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDirectoryOrBlockingFileDoesNotAffectResolutionAndContentsStayUntouched(bool blockingFile)
    {
        // A physical fixture is needed only to prove that existing content and a file where
        // the destination's parent should be do not participate in resolution.
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var parent = Path.Combine(fixture.ProductionRoot, "assets");
        if (blockingFile) File.WriteAllText(parent, "blocking sentinel");
        else
        {
            Directory.CreateDirectory(Path.Combine(parent, DefaultAssetId));
            File.WriteAllText(Path.Combine(parent, DefaultAssetId, "sentinel.txt"), "existing asset");
        }
        var before = fixture.Snapshot();
        var repository = Construct<ValidatedProductionRepository>(fixture.Context(universe: "test_universe"));
        var result = _resolver.Resolve(Package(), repository);
        Assert.Equal(Path.GetFullPath(Path.Combine(parent, DefaultAssetId)), result.FullDirectoryPath);
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void PublicValidationBoundariesFeedResolverWithoutProductionWrites()
    {
        using var fixture = new PackageSemanticTestFixture();
        fixture.Manifest = fixture.Manifest with { Classification = NimroelClassification() };
        fixture.WriteManifest();
        var package = fixture.ValidateReadOnly().Package!;
        Directory.CreateDirectory(fixture.Context.Storage.ProductionRoot);
        var repository = new ProductionRepositoryValidator().Validate(fixture.Context).Repository!;
        fixture.AssertReadOnly(() =>
        {
            var result = _resolver.Resolve(package, repository);
            Assert.Equal($"portraits/norgard/treskal/farmer/male/{package.AssetKey.AssetId}", result.RelativeDirectory);
            Assert.False(Directory.Exists(result.FullDirectoryPath));
        });
    }

    [Fact]
    public void DestinationIsSealedWithOnlyTheFourGetOnlyPropertiesAndInternalConstructor()
    {
        var type = typeof(ProductionAssetDestination);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.True(Assert.Single(type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)).IsAssembly);
        var properties = type.GetProperties();
        Assert.Equal(new[] { "AssetKey", "FullDirectoryPath", "RelativeDirectory", "RootPath" }, properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.Equal(typeof(UniverseAssetKey), type.GetProperty("AssetKey")!.PropertyType);
        Assert.All(properties.Where(property => property.Name != "AssetKey"), property => Assert.Equal(typeof(string), property.PropertyType));
    }

    private static UniverseAssetRule Rule(AssetRoutingRule? route) => new("portrait", "portrait_npc",
        route?.Segments.Where(segment => segment.Kind == AssetRouteSegmentKind.Classification).Select(segment => segment.Value!).Distinct() ?? [],
        route?.Segments.Where(segment => segment.Kind == AssetRouteSegmentKind.Classification).Select(segment => segment.Value!).Distinct() ?? [], [], route);

    private static ValidatedAssetPackage Package(UniverseAssetRule? rule = null, Dictionary<string, string>? classification = null,
        string assetId = DefaultAssetId, string universe = "test_universe")
    {
        rule ??= Rule(new AssetRoutingRule([AssetRouteSegment.Literal("assets"), AssetRouteSegment.AssetId()]));
        var manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = universe, AssetId = assetId, AssetType = rule.AssetType,
            ProductionProfile = rule.ProductionProfile, Classification = classification ?? []
        };
        return Construct<ValidatedAssetPackage>(new UniverseAssetKey(new UniverseId(universe), assetId),
            Path.Combine(Path.GetTempPath(), "nap-destination-tests", "package"),
            Path.Combine(Path.GetTempPath(), "nap-destination-tests", "package", assetId + "_manifest.json"), manifest, rule,
            new Dictionary<string, string>());
    }

    private static ValidatedProductionRepository Repository(string? root = null, string universe = "test_universe")
    {
        var profile = new UniverseProfile(new UniverseId(universe), "Test universe");
        var storage = new UniverseStorageConfig(new UniverseId(universe), Path.Combine(TestRoot, "workspace"),
            root ?? TestRoot, Path.Combine(TestRoot, "archive"));
        return Construct<ValidatedProductionRepository>(new UniverseContext(profile, storage));
    }

    private static Dictionary<string, string> NimroelClassification(string role = "farmer", string sex = "male") =>
        new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = role, ["sex"] = sex };

    // Existing tests access internal construction by reflection; no friend assembly or public
    // constructors are needed to forge deliberately incoherent validation snapshots here.
    private static T Construct<T>(params object[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));

    private static bool IsStrictlyWithinRoot(string root, string candidate) =>
        Assert.IsType<bool>(typeof(ProductionDestinationResolver)
            .GetMethod("IsStrictlyWithinRoot", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root, candidate]));
}
