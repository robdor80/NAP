using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProcessingPlanTests
{
    private const string AssetId = "portrait_example_001";
    private static string Root => Path.Combine(Path.GetTempPath(), "nap-plan-tests", "production");
    private readonly ProcessingPlanBuilder _builder = new();

    [Fact]
    public void PlanIsSealedWithInternalConstructorAndExactlyEightGetOnlyProperties()
    {
        var type = typeof(ProcessingPlan);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.True(Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).IsAssembly);
        var expected = new Dictionary<string, Type>
        {
            ["AssetKey"] = typeof(UniverseAssetKey), ["AssetType"] = typeof(string),
            ["ProductionProfile"] = typeof(string), ["Classification"] = typeof(IReadOnlyDictionary<string, string>),
            ["PackageRoot"] = typeof(string), ["ManifestPath"] = typeof(string),
            ["FilesByRole"] = typeof(IReadOnlyDictionary<string, string>), ["ProductionDestination"] = typeof(ProductionAssetDestination)
        };
        var properties = type.GetProperties();
        Assert.Equal(expected.Keys.OrderBy(name => name, StringComparer.Ordinal), properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(properties, property =>
        {
            Assert.Null(property.SetMethod);
            Assert.Equal(expected[property.Name], property.PropertyType);
        });
        // The exact property contract excludes operations, status, approval, timestamps, Guid and JobId.
        Assert.All(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic), field => Assert.True(field.IsInitOnly));
    }

    [Fact]
    public void BuilderIsSealedAndStatelessWithOnlyTheExactPublicBuildApi()
    {
        var type = typeof(ProcessingPlanBuilder);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Build", method.Name);
        Assert.Equal(typeof(ProcessingPlan), method.ReturnType);
        Assert.Equal(new[] { typeof(ValidatedAssetPackage), typeof(ValidatedProductionRepository), typeof(ProductionAssetDestination) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void NullInputsUseCorrectParameterNames()
    {
        var package = Package();
        var repository = Repository();
        var destination = Destination(package.AssetKey);
        Assert.Equal("package", Assert.Throws<ArgumentNullException>(() => _builder.Build(null!, repository, destination)).ParamName);
        Assert.Equal("repository", Assert.Throws<ArgumentNullException>(() => _builder.Build(package, null!, destination)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentNullException>(() => _builder.Build(package, repository, null!)).ParamName);
    }

    [Fact]
    public void CrossUniversePackageAndRepositoryAreRejected()
    {
        var package = Package();
        Assert.Equal("repository", Assert.Throws<ArgumentException>(() =>
            _builder.Build(package, Repository(universe: "other_universe"), Destination(package.AssetKey))).ParamName);
    }

    [Theory]
    [InlineData("other_universe", AssetId)]
    [InlineData("test_universe", "portrait_other_002")]
    [InlineData("other_universe", "portrait_other_002")]
    public void DestinationMustMatchTheCompleteAssetKey(string universe, string assetId)
    {
        var destination = Destination(new UniverseAssetKey(new UniverseId(universe), assetId));
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => _builder.Build(Package(), Repository(), destination)).ParamName);
    }

    [Fact]
    public void EqualUniverseAndAssetKeyValuesAreAcceptedWithoutReferenceEquality()
    {
        var package = Package();
        var repository = Repository();
        var destination = Destination(new UniverseAssetKey(new UniverseId("test_universe"), AssetId));
        Assert.NotSame(package.AssetKey.UniverseId, repository.UniverseId);
        Assert.NotSame(package.AssetKey, destination.AssetKey);
        var plan = _builder.Build(package, repository, destination);
        Assert.Same(package.AssetKey, plan.AssetKey);
        Assert.Same(destination, plan.ProductionDestination);
    }

    [Theory]
    [InlineData("other_production")]
    [InlineData("production_evil")]
    public void DifferentDestinationRootIncludingPrefixSiblingIsRejected(string name)
    {
        var package = Package();
        var otherRoot = Path.Combine(Path.GetDirectoryName(Root)!, name);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() =>
            _builder.Build(package, Repository(), Destination(package.AssetKey, otherRoot))).ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentTrailingSeparatorAndLexicallyNormalizedRootsAreAccepted(bool repositoryTrailing)
    {
        var package = Package();
        var repository = Repository(repositoryTrailing ? Root + Path.DirectorySeparatorChar : Root);
        var destinationRoot = Path.Combine(Root, "unused", "..") + Path.DirectorySeparatorChar;
        var destination = Destination(package.AssetKey, destinationRoot);
        var plan = _builder.Build(package, repository, destination);
        Assert.Same(destination, plan.ProductionDestination);
        Assert.Equal(destinationRoot, plan.ProductionDestination.RootPath); // Equivalence does not rewrite the authoritative destination.
    }

    [Fact]
    public void RootCasingComparisonRespectsThePlatform()
    {
        var package = Package();
        var destination = Destination(package.AssetKey, Root.ToUpperInvariant());
        if (OperatingSystem.IsWindows()) Assert.Same(destination, _builder.Build(package, Repository(), destination).ProductionDestination);
        else Assert.Equal("destination", Assert.Throws<ArgumentException>(() => _builder.Build(package, Repository(), destination)).ParamName);
    }

    [Fact]
    public void FilesystemVolumeRootRetainsItsMeaningWhenTrimmingSeparators()
    {
        var package = Package();
        var root = Path.GetPathRoot(Root)!;
        var destination = Destination(package.AssetKey, root);
        Assert.Same(destination, _builder.Build(package, Repository(root), destination).ProductionDestination);
    }

    [Fact]
    public void AllFactsComeFromPackageAndDestinationWithoutReRoutingOrRebuildingFilenames()
    {
        var classification = new Dictionary<string, string> { ["culture"] = "example", ["realm"] = "optional_realm", ["region"] = "optional_region" };
        var files = new Dictionary<string, string> { ["custom_role"] = "opaque source path", ["prompt"] = "unusual prompt filename" };
        var package = Package(classification: classification, files: files, assetType: "custom_type", productionProfile: "custom_profile");
        Assert.Null(package.AssetRule.Routing);
        var destination = Destination(package.AssetKey); // Arbitrary previously calculated boundary, not reconstructed from a rule.
        var plan = _builder.Build(package, Repository(), destination);
        Assert.Same(package.AssetKey, plan.AssetKey);
        Assert.Equal("custom_type", plan.AssetType);
        Assert.Equal("custom_profile", plan.ProductionProfile);
        Assert.Equal(package.PackageRoot, plan.PackageRoot);
        Assert.Equal(package.ManifestPath, plan.ManifestPath);
        Assert.Equal(classification, plan.Classification);
        Assert.Equal(files, plan.FilesByRole);
        Assert.Same(destination, plan.ProductionDestination);
        Assert.Equal(destination.RelativeDirectory, plan.ProductionDestination.RelativeDirectory);
        Assert.Equal(destination.FullDirectoryPath, plan.ProductionDestination.FullDirectoryPath);
        Assert.DoesNotContain("manifest", plan.FilesByRole.Keys);
        Assert.DoesNotContain(package.ManifestPath, plan.FilesByRole.Values);
        Assert.DoesNotContain("optional_companion", plan.FilesByRole.Keys);
        Assert.DoesNotContain(plan.FilesByRole.Values, path => path.EndsWith(".webp", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstructorDefensivelyCopiesBothCollectionsUsingOrdinalIdentity()
    {
        var classification = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["culture"] = "original" };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["prompt"] = "original path" };
        var package = Package();
        var plan = Construct<ProcessingPlan>(package.AssetKey, "portrait", "portrait_npc", classification,
            package.PackageRoot, package.ManifestPath, files, Destination(package.AssetKey));
        classification["culture"] = "changed";
        classification["new_dimension"] = "new_value";
        files.Clear();
        Assert.Equal("original", plan.Classification["culture"]);
        Assert.Single(plan.Classification);
        Assert.False(plan.Classification.ContainsKey("Culture"));
        Assert.Equal("original path", plan.FilesByRole["prompt"]);
        Assert.Single(plan.FilesByRole);
        Assert.False(plan.FilesByRole.ContainsKey("Prompt"));
        Assert.NotSame(classification, plan.Classification);
        Assert.NotSame(files, plan.FilesByRole);
    }

    [Fact]
    public void CallerManifestCopiesAndOriginalInputsCannotChangeTheBuiltPlan()
    {
        var classification = new Dictionary<string, string> { ["culture"] = "original" };
        var files = new Dictionary<string, string> { ["prompt"] = "original path" };
        var package = Package(classification: classification, files: files);
        var plan = _builder.Build(package, Repository(), Destination(package.AssetKey));
        classification.Clear();
        files.Clear();
        var manifest = package.Manifest;
        manifest.Classification["culture"] = "changed";
        manifest.Classification["region"] = "extra";
        Assert.Equal("original", plan.Classification["culture"]);
        Assert.Single(plan.Classification);
        Assert.Equal("original path", plan.FilesByRole["prompt"]);
        Assert.NotSame(package.FilesByRole, plan.FilesByRole);
    }

    [Fact]
    public void BothExposedCollectionsRejectMutationIncludingDictionaryAndCollectionInterfaces()
    {
        var package = Package(classification: new() { ["culture"] = "original" }, files: new() { ["prompt"] = "original path" });
        var plan = _builder.Build(package, Repository(), Destination(package.AssetKey));
        foreach (var collection in new[] { plan.Classification, plan.FilesByRole })
        {
            var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(collection);
            Assert.True(dictionary.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => dictionary.Add("new", "new"));
            Assert.Throws<NotSupportedException>(() => dictionary.Clear());
            Assert.Throws<NotSupportedException>(() => dictionary[collection.Keys.Single()] = "changed");
            var pairs = Assert.IsAssignableFrom<ICollection<KeyValuePair<string, string>>>(collection);
            Assert.Throws<NotSupportedException>(() => pairs.Remove(collection.Single()));
        }
    }

    [Fact]
    public void InsertionOrderDoesNotChangeLogicalFactsAndRepeatedBuildsAreDeterministic()
    {
        var first = Package(classification: new() { ["culture"] = "example", ["realm"] = "optional" }, files: new() { ["prompt"] = "a", ["info"] = "b" });
        var other = Package(classification: new() { ["realm"] = "optional", ["culture"] = "example" }, files: new() { ["info"] = "b", ["prompt"] = "a" });
        var repository = Repository();
        var destination = Destination(first.AssetKey);
        var before = _builder.Build(first, repository, destination);
        AssertLogicalEqual(before, _builder.Build(first, repository, destination));
        AssertLogicalEqual(before, _builder.Build(other, repository, destination));
    }

    [Fact]
    public void EmptyPresentFilesStayEmptyDespiteOptionalRulesAndManifestIsSeparate()
    {
        var package = Package();
        var plan = _builder.Build(package, Repository(), Destination(package.AssetKey));
        Assert.Empty(plan.FilesByRole);
        Assert.Equal(package.ManifestPath, plan.ManifestPath);
        Assert.Single(package.AssetRule.PackageFiles);
    }

    [Fact]
    public void RealNimroelPolicyAndDestinationAreRetainedWithAllMetadataAndPresentRoles()
    {
        const string assetId = "portrait_treskal_farmer_male_002";
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        var rule = Assert.Single(profile.AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc");
        var classification = new Dictionary<string, string>
        {
            ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male",
            ["realm"] = "example_realm", ["region"] = "example_region"
        };
        var files = rule.PackageFiles.ToDictionary(file => file.Role, file => Path.Combine(Root, "inputs", file.ResolveFileName(assetId)), StringComparer.Ordinal);
        var package = Package(profile.Id.Value, assetId, classification, files, rule: rule);
        var repository = Repository(universe: profile.Id.Value);
        var destination = new ProductionDestinationResolver().Resolve(package, repository);
        var plan = _builder.Build(package, repository, destination);
        Assert.Equal($"portraits/norgard/treskal/farmer/male/{assetId}", plan.ProductionDestination.RelativeDirectory);
        Assert.Same(destination, plan.ProductionDestination);
        Assert.Equal(new UniverseAssetKey(new UniverseId("nimroel"), assetId), plan.AssetKey);
        Assert.Equal("portrait", plan.AssetType);
        Assert.Equal("portrait_npc", plan.ProductionProfile);
        Assert.Equal(classification, plan.Classification);
        Assert.Equal(files, plan.FilesByRole);
        Assert.Equal(package.PackageRoot, plan.PackageRoot);
        Assert.Equal(package.ManifestPath, plan.ManifestPath);
    }

    [Fact]
    public void InexistentInputsAndDestinationsAreAcceptedWithoutCreatingAnything()
    {
        var absentRoot = Path.Combine(Path.GetTempPath(), "nap-plan-absent-" + Guid.NewGuid().ToString("N"));
        var package = Package(packageRoot: Path.Combine(absentRoot, "inputs"));
        var repository = Repository(Path.Combine(absentRoot, "production"));
        var destination = Destination(package.AssetKey, repository.RootPath);
        Assert.False(Directory.Exists(absentRoot));
        var plan = _builder.Build(package, repository, destination);
        Assert.Same(destination, plan.ProductionDestination);
        Assert.False(Directory.Exists(package.PackageRoot));
        Assert.False(Directory.Exists(repository.RootPath));
        Assert.False(Directory.Exists(destination.FullDirectoryPath));
        Assert.False(Directory.Exists(absentRoot));
    }

    [Fact]
    public void ExistingFileAtDestinationDoesNotTriggerOperationalValidation()
    {
        // Use the already-built assembly; no physical fixture or file creation is needed.
        var existingFile = Path.Combine(AppContext.BaseDirectory, "NAP.Core.dll");
        Assert.True(File.Exists(existingFile));
        var package = Package();
        var repository = Repository(AppContext.BaseDirectory);
        var destination = Construct<ProductionAssetDestination>(package.AssetKey, repository.RootPath,
            "previously_calculated", existingFile);
        Assert.Same(destination, _builder.Build(package, repository, destination).ProductionDestination);
        Assert.True(File.Exists(existingFile));
    }

    private static void AssertLogicalEqual(ProcessingPlan first, ProcessingPlan second)
    {
        Assert.Equal(first.AssetKey, second.AssetKey);
        Assert.Equal(first.AssetType, second.AssetType);
        Assert.Equal(first.ProductionProfile, second.ProductionProfile);
        Assert.Equal(first.Classification.OrderBy(pair => pair.Key, StringComparer.Ordinal), second.Classification.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(first.FilesByRole.OrderBy(pair => pair.Key, StringComparer.Ordinal), second.FilesByRole.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(first.PackageRoot, second.PackageRoot);
        Assert.Equal(first.ManifestPath, second.ManifestPath);
        Assert.Same(first.ProductionDestination, second.ProductionDestination);
    }

    private static ValidatedAssetPackage Package(string universe = "test_universe", string assetId = AssetId,
        Dictionary<string, string>? classification = null, Dictionary<string, string>? files = null,
        string assetType = "portrait", string productionProfile = "portrait_npc", UniverseAssetRule? rule = null, string? packageRoot = null)
    {
        var manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = universe, AssetId = assetId, AssetType = assetType,
            ProductionProfile = productionProfile, Classification = classification ?? []
        };
        rule ??= new UniverseAssetRule("portrait", "portrait_npc", [], [],
            [new AssetPackageFileRule("optional_companion", "_companion", ".md", false, null)]);
        var inputs = packageRoot ?? Path.Combine(Path.GetDirectoryName(Root)!, "inputs");
        return Construct<ValidatedAssetPackage>(new UniverseAssetKey(new UniverseId(universe), assetId), inputs,
            Path.Combine(inputs, assetId + "_manifest.json"), manifest, rule, files ?? []);
    }

    private static ValidatedProductionRepository Repository(string? root = null, string universe = "test_universe")
    {
        var profile = new UniverseProfile(new UniverseId(universe), "Test universe");
        var storage = new UniverseStorageConfig(new UniverseId(universe), Path.Combine(Root, "workspace"), root ?? Root, Path.Combine(Root, "archive"));
        return Construct<ValidatedProductionRepository>(new UniverseContext(profile, storage));
    }

    private static ProductionAssetDestination Destination(UniverseAssetKey key, string? root = null) =>
        Construct<ProductionAssetDestination>(key, root ?? Root, "previously_calculated/directory",
            Path.Combine(Root, "authoritative_directory"));

    // Follow existing tests' reflection access to internal boundaries; do not expose constructors.
    private static T Construct<T>(params object[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));
}
