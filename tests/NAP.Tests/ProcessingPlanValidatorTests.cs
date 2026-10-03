using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProcessingPlanValidatorTests
{
    private const string AssetId = "portrait_example_001";
    private const string Destination = "assets/example_culture/portrait_example_001";
    private const string NimroelDestination = "portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040";
    private static string Root => Path.Combine(Path.GetTempPath(), "nap-plan-validation-tests", "production");
    private readonly ProcessingPlanValidator _validator = new();

    [Fact]
    public void ValidatorIsSealedStatelessWithOnlyTheExactValidateApi()
    {
        var type = typeof(ProcessingPlanValidator);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Validate", method.Name);
        Assert.Equal(typeof(NapIssueReport), method.ReturnType);
        Assert.Equal(new[] { typeof(ProcessingPlan), typeof(ProductionRepositorySnapshot) }, method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void NullInputsUseTheCorrectParameterNames()
    {
        Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, Snapshot())).ParamName);
        Assert.Equal("snapshot", Assert.Throws<ArgumentNullException>(() => _validator.Validate(Plan(), null!)).ParamName);
    }

    [Fact]
    public void DifferentUniverseIsRejectedAndEqualValuesDoNotRequireReferenceEquality()
    {
        var plan = Plan();
        Assert.Equal("snapshot", Assert.Throws<ArgumentException>(() => _validator.Validate(plan, Snapshot(universe: "other_universe"))).ParamName);
        var snapshot = Snapshot();
        Assert.NotSame(plan.AssetKey.UniverseId, snapshot.UniverseId);
        Assert.Equal(plan.AssetKey.UniverseId, snapshot.UniverseId);
        AssertClean(_validator.Validate(plan, snapshot));
    }

    [Theory]
    [InlineData("other_production")]
    [InlineData("production_evil")]
    public void DifferentRootsIncludingPrefixSiblingsAreRejected(string name)
    {
        var root = Path.Combine(Path.GetDirectoryName(Root)!, name);
        Assert.Equal("snapshot", Assert.Throws<ArgumentException>(() => _validator.Validate(Plan(), Snapshot(root: root))).ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EquivalentLexicalRootsAndTrailingSeparatorsAreAccepted(bool rawPlanRoot)
    {
        var raw = Path.Combine(Root, "unused", "..") + Path.DirectorySeparatorChar;
        var plan = Plan(root: rawPlanRoot ? raw : Root);
        var snapshot = Snapshot();
        if (!rawPlanRoot) SetRoot(snapshot, raw);
        AssertClean(_validator.Validate(plan, snapshot));
    }

    [Fact]
    public void RootCasingRespectsThePlatformComparison()
    {
        var snapshot = Snapshot();
        SetRoot(snapshot, Root.ToUpperInvariant());
        if (OperatingSystem.IsWindows()) AssertClean(_validator.Validate(Plan(), snapshot));
        else Assert.Equal("snapshot", Assert.Throws<ArgumentException>(() => _validator.Validate(Plan(), snapshot)).ParamName);
    }

    [Fact]
    public void VolumeRootIsNotTurnedIntoAParentlessRelativePath()
    {
        var root = Path.GetPathRoot(Root)!;
        AssertClean(_validator.Validate(Plan(root: root), Snapshot(root: root)));
    }

    [Theory]
    [InlineData(ProductionRepositoryEntryKind.File)]
    [InlineData(ProductionRepositoryEntryKind.Directory)]
    public void ExactDuplicateSnapshotPathsAreRejectedBeforeInterpretingBlockers(ProductionRepositoryEntryKind kind)
    {
        var snapshot = Snapshot([Entry("assets", ProductionRepositoryEntryKind.File), Entry("unrelated", kind), Entry("unrelated", kind)]);
        Assert.Equal("snapshot", Assert.Throws<ArgumentException>(() => _validator.Validate(Plan(), snapshot)).ParamName);
    }

    [Fact]
    public void CaseOnlyDuplicatePathsUsePlatformIdentityWithoutChangingTheSnapshotContract()
    {
        var snapshot = Snapshot([Entry("unrelated", ProductionRepositoryEntryKind.Directory), Entry("Unrelated", ProductionRepositoryEntryKind.File)]);
        Assert.Equal(2, snapshot.Entries.Count); // 3.2/3.4 keep their original ordinal contract.
        if (OperatingSystem.IsWindows())
            Assert.Equal("snapshot", Assert.Throws<ArgumentException>(() => _validator.Validate(Plan(), snapshot)).ParamName);
        else AssertClean(_validator.Validate(Plan(), snapshot));
    }

    [Fact]
    public void EmptySnapshotAndUnrelatedEntriesAllowMissingDirectoriesWithoutAnyIssues()
    {
        AssertClean(_validator.Validate(Plan(), Snapshot()));
        AssertClean(_validator.Validate(Plan(), Snapshot([Entry("other_assets", ProductionRepositoryEntryKind.File)])));
    }

    [Fact]
    public void ExactDirectoryAncestorsAreAllowedAndPrefixLookalikesAreUnrelated()
    {
        var snapshot = Snapshot([Entry("assets", ProductionRepositoryEntryKind.Directory), Entry("assets/example_culture", ProductionRepositoryEntryKind.Directory),
            Entry(Destination + "_other", ProductionRepositoryEntryKind.Directory), Entry("assets/example_culture_other", ProductionRepositoryEntryKind.File)]);
        AssertClean(_validator.Validate(Plan(), snapshot));
    }

    [Theory]
    [InlineData("assets")]
    [InlineData("assets/example_culture")]
    [InlineData(Destination)]
    public void FileAtAnyRequiredPrefixStopsWithExactObservedMetadata(string prefix)
    {
        var entries = Ancestors(prefix).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToList();
        const string observedFullPath = "observed full path preserved verbatim";
        var blocker = Entry(prefix, ProductionRepositoryEntryKind.File, observedFullPath);
        entries.Add(blocker);
        AssertStop(_validator.Validate(Plan(), Snapshot(entries)), "plan_destination_blocked",
            "A file blocks a required production directory path.", observedFullPath, prefix);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDestinationDirectoryStopsRegardlessOfObservedChildren(bool withChildren)
    {
        var entries = Prefixes(Destination).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToList();
        var destinationEntry = entries[^1];
        if (withChildren) entries.Add(Entry(Destination + "/opaque_content.png", ProductionRepositoryEntryKind.File));
        AssertStop(_validator.Validate(Plan(), Snapshot(entries)), "plan_destination_exists",
            "The planned production destination already exists.", destinationEntry.FullPath, Destination);
    }

    [Theory]
    [InlineData("Assets", "assets", ProductionRepositoryEntryKind.Directory)]
    [InlineData("assets/Example_culture", "assets/example_culture", ProductionRepositoryEntryKind.Directory)]
    [InlineData("assets/Example_culture", "assets/example_culture", ProductionRepositoryEntryKind.File)]
    [InlineData("assets/example_culture/Portrait_example_001", Destination, ProductionRepositoryEntryKind.Directory)]
    [InlineData("assets/example_culture/Portrait_example_001", Destination, ProductionRepositoryEntryKind.File)]
    public void EquivalentPathWithDifferentCasingStopsInWindowsBeforeKindInterpretation(string observed, string expected, ProductionRepositoryEntryKind kind)
    {
        var entries = Ancestors(observed).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToList();
        var conflict = Entry(observed, kind);
        entries.Add(conflict);
        var report = _validator.Validate(Plan(), Snapshot(entries));
        if (OperatingSystem.IsWindows())
            AssertStop(report, "plan_destination_casing_conflict",
                "An existing production path differs from the canonical destination only by casing.", conflict.FullPath,
                $"expected: {expected}; observed: {observed}");
        else AssertClean(report);
    }

    [Fact]
    public void FirstCasingConflictWinsAndValidationDoesNotCascadeToLowerPaths()
    {
        var entries = new[] { Entry("Assets", ProductionRepositoryEntryKind.Directory),
            Entry("Assets/Example_culture", ProductionRepositoryEntryKind.Directory),
            Entry("Assets/Example_culture/Portrait_example_001", ProductionRepositoryEntryKind.File) };
        var report = _validator.Validate(Plan(), Snapshot(entries));
        if (OperatingSystem.IsWindows())
            AssertStop(report, NapIssueCodes.PlanDestinationCasingConflict,
                "An existing production path differs from the canonical destination only by casing.", entries[0].FullPath,
                "expected: assets; observed: Assets");
        else AssertClean(report);
    }

    [Fact]
    public void FirstFileBlockerWinsWithoutCascadingEvenForAnInconsistentSyntheticSnapshot()
    {
        var first = Entry("assets", ProductionRepositoryEntryKind.File);
        var entries = new[] { Entry(Destination, ProductionRepositoryEntryKind.Directory), first,
            Entry("assets/example_culture", ProductionRepositoryEntryKind.File) };
        AssertStop(_validator.Validate(Plan(), Snapshot(entries)), NapIssueCodes.PlanDestinationBlocked,
            "A file blocks a required production directory path.", first.FullPath, "assets");
    }

    [Fact]
    public void DescendantsAloneDoNotInventAnExistingDestinationOrRepairASyntheticSnapshot()
    {
        AssertClean(_validator.Validate(Plan(), Snapshot([Entry(Destination + "/child.txt", ProductionRepositoryEntryKind.File)])));
    }

    [Theory]
    [InlineData("assets")]
    [InlineData("portrait_example_001")]
    public void SingleSegmentRoutesAreCheckedAsTheCompleteDestination(string relative)
    {
        var plan = Plan(relative: relative);
        AssertClean(_validator.Validate(plan, Snapshot()));
        var entry = Entry(relative, ProductionRepositoryEntryKind.Directory);
        AssertStop(_validator.Validate(plan, Snapshot([entry])), NapIssueCodes.PlanDestinationExists,
            "The planned production destination already exists.", entry.FullPath, relative);
    }

    [Fact]
    public void RepeatedSegmentsAreWalkedInOrderAsDistinctRequiredPrefixes()
    {
        const string relative = "assets/example_culture/example_culture/portrait_example_001/portrait_example_001";
        var prefix = "assets/example_culture/example_culture/portrait_example_001";
        var blocker = Entry(prefix, ProductionRepositoryEntryKind.File);
        var entries = Ancestors(prefix).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).Append(blocker);
        AssertStop(_validator.Validate(Plan(relative: relative), Snapshot(entries)), NapIssueCodes.PlanDestinationBlocked,
            "A file blocks a required production directory path.", blocker.FullPath, prefix);
    }

    [Fact]
    public void ThreeNewCodesAreExactAndAllCentralCodesRemainUniqueNamingIdentifiers()
    {
        Assert.Equal("plan_destination_casing_conflict", NapIssueCodes.PlanDestinationCasingConflict);
        Assert.Equal("plan_destination_blocked", NapIssueCodes.PlanDestinationBlocked);
        Assert.Equal("plan_destination_exists", NapIssueCodes.PlanDestinationExists);
        var codes = typeof(NapIssueCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => Assert.IsType<string>(field.GetRawConstantValue())).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(AssetNamingRules.IsValidMachineIdentifier(code)));
    }

    [Theory]
    [InlineData("portraits/Norgard", "portraits/norgard")]
    [InlineData("portraits/norgard/Treskal", "portraits/norgard/treskal")]
    public void RealNimroelHistoricalCasingDoesNotAdaptTheCanonicalPlan(string observed, string expected)
    {
        var plan = NimroelPlan();
        var entries = Ancestors(observed).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToList();
        var conflict = Entry(observed, ProductionRepositoryEntryKind.Directory);
        entries.Add(conflict);
        var historicalLocation = observed == "portraits/Norgard" ? observed + "/Treskal" : observed;
        if (historicalLocation != observed) entries.Add(Entry(historicalLocation, ProductionRepositoryEntryKind.Directory));
        entries.Add(Entry(historicalLocation + "/farmer", ProductionRepositoryEntryKind.Directory));
        var report = _validator.Validate(plan, Snapshot(entries, universe: "nimroel"));
        if (OperatingSystem.IsWindows())
            AssertStop(report, NapIssueCodes.PlanDestinationCasingConflict,
                "An existing production path differs from the canonical destination only by casing.", conflict.FullPath,
                $"expected: {expected}; observed: {observed}");
        else AssertClean(report);
        Assert.Equal(NimroelDestination, plan.ProductionDestination.RelativeDirectory);
        Assert.Equal(observed, conflict.RelativePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealNimroelCanonicalAncestorsAreCleanUntilTheAssetDirectoryExists(bool assetExists)
    {
        var plan = NimroelPlan();
        var paths = assetExists ? Prefixes(NimroelDestination) : Ancestors(NimroelDestination);
        var entries = paths.Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToArray();
        var report = _validator.Validate(plan, Snapshot(entries, universe: "nimroel"));
        if (assetExists)
            AssertStop(report, NapIssueCodes.PlanDestinationExists,
                "The planned production destination already exists.", entries[^1].FullPath, NimroelDestination);
        else AssertClean(report);
    }

    [Fact]
    public void MissingSourcesAndPhysicalRootsDoNotAffectMaterializedSnapshotValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-validation-absent-" + Guid.NewGuid().ToString("N"));
        var plan = Plan(root: Path.Combine(root, "production"));
        var snapshot = Snapshot(root: plan.ProductionDestination.RootPath);
        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(plan.PackageRoot));
        Assert.False(File.Exists(plan.ManifestPath));
        Assert.All(plan.FilesByRole.Values, file => Assert.False(File.Exists(file)));
        AssertClean(_validator.Validate(plan, snapshot));
        var entry = Entry(Destination, ProductionRepositoryEntryKind.Directory, "observed snapshot path without a physical file");
        AssertStop(_validator.Validate(plan, Snapshot([entry], root: snapshot.RootPath)), NapIssueCodes.PlanDestinationExists,
            "The planned production destination already exists.", entry.FullPath, Destination);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void LogicalInputsProduceDeterministicReportsIndependentOfInsertionOrderAndCulture(string cultureName)
    {
        var plan = Plan();
        var entries = Prefixes(Destination).Select(path => Entry(path, ProductionRepositoryEntryKind.Directory)).ToArray();
        var expected = _validator.Validate(plan, Snapshot(entries));
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(expected.Issues, _validator.Validate(plan, Snapshot(entries.Reverse())).Issues);
            Assert.Equal(expected.Issues, _validator.Validate(plan, Snapshot(entries)).Issues);
            Assert.Equal(Destination, plan.ProductionDestination.RelativeDirectory);
            Assert.Equal(entries.Length, Snapshot(entries).Entries.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static void AssertClean(NapIssueReport report)
    {
        Assert.Empty(report.Issues);
        Assert.True(report.IsClean);
        Assert.False(report.HasErrors);
        Assert.False(report.ShouldStop);
        Assert.True(report.CanContinue);
    }

    private static void AssertStop(NapIssueReport report, string code, string message, string subject, string detail)
    {
        var issue = Assert.Single(report.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(message, issue.Message);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.Equal(subject, issue.SubjectPath);
        Assert.Equal(detail, issue.Detail);
        Assert.False(report.IsClean);
        Assert.True(report.HasErrors);
        Assert.True(report.ShouldStop);
        Assert.False(report.CanContinue);
    }

    private static string[] Prefixes(string relative)
    {
        var segments = relative.Split('/');
        return Enumerable.Range(1, segments.Length).Select(count => string.Join("/", segments.Take(count))).ToArray();
    }

    private static IEnumerable<string> Ancestors(string relative) => Prefixes(relative).SkipLast(1);

    private static ProcessingPlan Plan(string universe = "test_universe", string relative = Destination, string? root = null)
    {
        var key = new UniverseAssetKey(new UniverseId(universe), AssetId);
        root ??= Root;
        var destination = Construct<ProductionAssetDestination>(key, root, relative, Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var inputs = Path.Combine(root, "missing_inputs");
        return Construct<ProcessingPlan>(key, "portrait", "portrait_npc", new Dictionary<string, string>(), inputs,
            Path.Combine(inputs, "manifest.json"), new Dictionary<string, string> { ["master"] = Path.Combine(inputs, "missing.png") }, destination);
    }

    private static ProcessingPlan NimroelPlan()
    {
        const string assetId = "portrait_treskal_farmer_male_040";
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        var rule = Assert.Single(profile.AssetRules);
        var inputs = Path.Combine(Root, "missing_inputs");
        var manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = "nimroel", AssetId = assetId, AssetType = "portrait", ProductionProfile = "portrait_npc",
            Classification = new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male" }
        };
        var package = Construct<ValidatedAssetPackage>(new UniverseAssetKey(profile.Id, assetId), inputs,
            Path.Combine(inputs, assetId + "_manifest.json"), manifest, rule, new Dictionary<string, string>());
        var repository = Repository(Root, "nimroel");
        var destination = new ProductionDestinationResolver().Resolve(package, repository);
        return new ProcessingPlanBuilder().Build(package, repository, destination);
    }

    private static ProductionRepositorySnapshot Snapshot(IEnumerable<ProductionRepositoryEntry>? entries = null,
        string? root = null, string universe = "test_universe") =>
        Construct<ProductionRepositorySnapshot>(Repository(root ?? Root, universe), entries ?? []);

    private static ValidatedProductionRepository Repository(string root, string universe)
    {
        var id = new UniverseId(universe);
        var context = new UniverseContext(new UniverseProfile(id, "Synthetic structure test"), new UniverseStorageConfig(id, root, root, root));
        return Construct<ValidatedProductionRepository>(context);
    }

    private static ProductionRepositoryEntry Entry(string relative, ProductionRepositoryEntryKind kind, string? fullPath = null) =>
        Construct<ProductionRepositoryEntry>(relative, fullPath ?? Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)), kind);

    private static void SetRoot(ProductionRepositorySnapshot snapshot, string root) =>
        typeof(ProductionRepositorySnapshot).GetField("<RootPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(snapshot, root);

    private static T Construct<T>(params object[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));
}
