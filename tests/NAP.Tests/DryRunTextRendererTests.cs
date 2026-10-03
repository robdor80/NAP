using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class DryRunTextRendererTests
{
    private const string AssetId = "portrait_example_001";
    private readonly DryRunTextRenderer _renderer = new();

    [Fact]
    public void RendererIsSealedStatelessAndHasOnlyTheExactRenderApi()
    {
        var type = typeof(DryRunTextRenderer);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Render", method.Name);
        Assert.Equal(typeof(string), method.ReturnType);
        Assert.Equal(typeof(ProcessingPlan), Assert.Single(method.GetParameters()).ParameterType);
        Assert.NotNull(type.GetConstructor(Type.EmptyTypes));
        Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => _renderer.Render(null!)).ParamName);
    }

    [Fact]
    public void RepresentativeReportMatchesTheEntireCanonicalDocumentExactly()
    {
        var plan = Plan(new() { ["sex"] = "male", ["culture"] = "example" },
            new() { ["prompt"] = @"C:\nap\package\custom_prompt.md", ["master"] = @"C:\nap\package\source.png" });
        var expected = """
            NAP DRY RUN
            ProcessingPlan v1 - read-only preview

            ASSET
              universe_id: test_universe
              asset_id: portrait_example_001
              asset_type: portrait
              production_profile: portrait_npc

            CLASSIFICATION
              culture: example
              sex: male

            PACKAGE
              package_root: "C:\\nap\\package"
              manifest_path: "C:\\nap\\package\\portrait_example_001_manifest.json"

            INPUT FILES
              master: "C:\\nap\\package\\source.png"
              prompt: "C:\\nap\\package\\custom_prompt.md"

            PRODUCTION DESTINATION
              production_root: "D:\\prod"
              relative_directory: assets/portrait_example_001
              full_directory_path: "D:\\prod\\assets\\portrait_example_001"

            OPERATIONS
              (not defined in ProcessingPlan v1)

            SAFETY
              This renderer performs no filesystem I/O.
              No operation is executed or authorized by this dry run.
            """.Replace("\r\n", "\n"); // Source checkout line endings must not affect the expected report.
        Assert.Equal(expected, _renderer.Render(plan));
    }

    [Fact]
    public void CanonicalLineEndingsHaveNoBomCarriageReturnFinalNewlineOrTrailingWhitespace()
    {
        var report = _renderer.Render(Plan());
        Assert.DoesNotContain('\r', report);
        Assert.False(report.StartsWith('\uFEFF'));
        Assert.False(report.EndsWith('\n'));
        Assert.All(report.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
        Assert.Equal(1, report.Split('\n').Count(line => line == "OPERATIONS"));
        Assert.Equal(1, report.Split('\n').Count(line => line == "SAFETY"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void EmptyCollectionsKeepTheirSectionsWithNone(bool emptyClassification, bool emptyFiles)
    {
        var plan = Plan(emptyClassification ? [] : new() { ["culture"] = "example" },
            emptyFiles ? [] : new() { ["input"] = "source" });
        var report = _renderer.Render(plan);
        Assert.Contains(emptyClassification ? "CLASSIFICATION\n  (none)\n\nPACKAGE" : "CLASSIFICATION\n  culture: example\n\nPACKAGE", report);
        Assert.Contains(emptyFiles ? "INPUT FILES\n  (none)\n\nPRODUCTION DESTINATION" : "INPUT FILES\n  input: \"source\"\n\nPRODUCTION DESTINATION", report);
    }

    [Fact]
    public void ClassificationAndInputFilesUseOrdinalOrderIndependentlyOfInsertionOrder()
    {
        var classification = new Dictionary<string, string>
        {
            ["sex"] = "male", ["region"] = "example_region", ["role"] = "farmer",
            ["realm"] = "example_realm", ["location"] = "treskal", ["culture"] = "norgard"
        };
        var files = new Dictionary<string, string> { ["z_role"] = "last source", ["aa"] = "second source", ["a_b"] = "first source" };
        var first = Plan(classification, files);
        var other = Plan(classification.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value),
            files.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value));
        var report = _renderer.Render(first);
        Assert.Equal(report, _renderer.Render(other));
        Assert.Contains("CLASSIFICATION\n  culture: norgard\n  location: treskal\n  realm: example_realm\n  region: example_region\n  role: farmer\n  sex: male\n", report);
        Assert.Contains("INPUT FILES\n  a_b: \"first source\"\n  aa: \"second source\"\n  z_role: \"last source\"\n", report);
        Assert.Equal(report, _renderer.Render(first));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void CurrentCultureAndUiCultureDoNotAffectOrderingOrHexEscapes(string name)
    {
        var plan = Plan(new() { ["aa"] = "second", ["a_b"] = "first" },
            new() { ["aa"] = "second\u001A", ["a_b"] = "first" });
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var expected = _renderer.Render(plan);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            Assert.Equal(expected, _renderer.Render(plan));
            Assert.Contains("CLASSIFICATION\n  a_b: first\n  aa: second\n", expected);
            Assert.Contains("INPUT FILES\n  a_b: \"first\"\n  aa: \"second\\u001A\"\n", expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    public static IEnumerable<object[]> EscapedPathCases()
    {
        (string Raw, string Escaped)[] samples =
        [
            ("root\\child", "root\\\\child"), ("root\"child", "root\\\"child"),
            ("root\rchild", "root\\rchild"), ("root\nchild", "root\\nchild"),
            ("root\tchild", "root\\tchild"), ("root\u0001child", "root\\u0001child"),
            ("root\u001Achild", "root\\u001Achild"), ("root\u0085child", "root\\u0085child"),
            ("producción 日本語 😀", "producción 日本語 😀")
        ];
        foreach (var slot in new[] { "package_root", "manifest_path", "source_role", "production_root", "full_directory_path" })
            foreach (var sample in samples) yield return [slot, sample.Raw, sample.Escaped];
    }

    [Theory]
    [MemberData(nameof(EscapedPathCases))]
    public void EveryPathSlotUsesSingleLineQuotingAndPreservesNormalUnicode(string slot, string raw, string escaped)
    {
        var plan = Plan(new() { ["culture"] = "example" },
            new() { ["source_role"] = slot == "source_role" ? raw : "source" },
            packageRoot: slot == "package_root" ? raw : @"C:\nap\package",
            manifestPath: slot == "manifest_path" ? raw : @"C:\nap\package\manifest.json",
            productionRoot: slot == "production_root" ? raw : @"D:\prod",
            fullPath: slot == "full_directory_path" ? raw : @"D:\prod\asset");
        var report = _renderer.Render(plan);
        Assert.Contains($"  {slot}: \"{escaped}\"", report.Split('\n'));
        Assert.DoesNotContain('\r', report);
        Assert.Equal(30, report.Split('\n').Length);
        Assert.False(report.EndsWith('\n'));
        Assert.All(report.Where(char.IsControl), character => Assert.Equal('\n', character));
    }

    [Fact]
    public void AllOtherControlCharactersUseFourUppercaseHexDigits()
    {
        var controls = Enumerable.Range(0, 0xA0).Select(value => (char)value)
            .Where(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
        foreach (var control in controls)
        {
            var report = _renderer.Render(Plan(packageRoot: "before" + control + "after"));
            var expected = "\\u" + ((int)control).ToString("X4", CultureInfo.InvariantCulture);
            Assert.Contains($"  package_root: \"before{expected}after\"", report);
            Assert.DoesNotContain(control, report);
        }
    }

    [Theory]
    [InlineData("source\nOPERATIONS\n  would write everything")]
    [InlineData("source\r\nSAFETY\n  PASS")]
    [InlineData("OPERATIONS\nCLASSIFICATION\nASSET")]
    public void PathTextCannotInjectASectionOrAuthorizeAnOperation(string source)
    {
        var report = _renderer.Render(Plan(files: new() { ["input"] = source }));
        var lines = report.Split('\n');
        foreach (var section in new[] { "ASSET", "CLASSIFICATION", "PACKAGE", "INPUT FILES", "PRODUCTION DESTINATION", "OPERATIONS", "SAFETY" })
            Assert.Equal(1, lines.Count(line => line == section));
        Assert.DoesNotContain("  would write everything", lines);
        Assert.DoesNotContain("  PASS", lines);
        Assert.Contains("OPERATIONS\n  (not defined in ProcessingPlan v1)\n\nSAFETY", report);
        Assert.Equal(30, lines.Length);
    }

    [Fact]
    public void DestinationStringsAreShownExactlyWithoutNormalizationOrSeparatorChanges()
    {
        var plan = Plan(productionRoot: @"D:\Prod\unused\..\", fullPath: "native/full/path//kept",
            relativeDirectory: "assets/example_culture/portrait_example_001");
        var report = _renderer.Render(plan);
        Assert.Contains("  production_root: \"D:\\\\Prod\\\\unused\\\\..\\\\\"", report);
        Assert.Contains("  relative_directory: assets/example_culture/portrait_example_001\n", report);
        Assert.Contains("  full_directory_path: \"native/full/path//kept\"", report);
        Assert.DoesNotContain("  relative_directory: \"", report);
    }

    [Fact]
    public void InputsAreOnlyTheFrozenMappingsWithManifestSeparateAndNoInventedOutputs()
    {
        var report = _renderer.Render(Plan(files: new() { ["custom_role"] = "opaque source filename" }));
        Assert.Contains("INPUT FILES\n  custom_role: \"opaque source filename\"\n\nPRODUCTION DESTINATION", report);
        Assert.DoesNotContain("  manifest:", report);
        Assert.DoesNotContain(".webp", report);
        Assert.DoesNotContain("optional_missing", report);
    }

    [Fact]
    public void OperationsAndSafetyAreExactAndNoExecutionOrValidationClaimsAreAdded()
    {
        var report = _renderer.Render(Plan());
        Assert.EndsWith("OPERATIONS\n  (not defined in ProcessingPlan v1)\n\nSAFETY\n  This renderer performs no filesystem I/O.\n  No operation is executed or authorized by this dry run.", report);
        foreach (var unsupported in new[] { "would copy", "would convert", "would create", "would write", "would archive", "PASS", "FAIL",
            "JobId", "Timestamp", "CreatedAt", "filesystem is unchanged", "TeraBox", "SHA-256" })
            Assert.DoesNotContain(unsupported, report);
    }

    [Theory]
    [InlineData("portrait_treskal_farmer_male_002", "farmer", "male")]
    [InlineData("portrait_treskal_farmer_boy_002", "farmer", "male")]
    [InlineData("portrait_treskal_boy_001", "village_child", "male")]
    [InlineData("portrait_treskal_elder_male_001", "village_elder", "male")]
    public void RealNimroelPipelineRendersMetadataAndResolvedPolicyWithoutLegacyInference(string assetId, string role, string sex)
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        var rule = Assert.Single(profile.AssetRules);
        var root = Path.Combine(Path.GetTempPath(), "nap-renderer-tests", "production");
        var packageRoot = Path.Combine(Path.GetTempPath(), "nap-renderer-tests", "package");
        var classification = new Dictionary<string, string>
        {
            ["culture"] = "norgard", ["location"] = "treskal", ["role"] = role, ["sex"] = sex,
            ["realm"] = "example_realm", ["region"] = "example_region"
        };
        var manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = profile.Id.Value, AssetId = assetId,
            AssetType = "portrait", ProductionProfile = "portrait_npc", Classification = classification
        };
        var files = rule.PackageFiles.ToDictionary(file => file.Role, file => Path.Combine(packageRoot, file.ResolveFileName(assetId)), StringComparer.Ordinal);
        var package = Construct<ValidatedAssetPackage>(new UniverseAssetKey(profile.Id, assetId), packageRoot,
            Path.Combine(packageRoot, assetId + "_manifest.json"), manifest, rule, files);
        var repository = Construct<ValidatedProductionRepository>(new UniverseContext(profile,
            new UniverseStorageConfig(profile.Id, Path.Combine(root, "workspace"), root, Path.Combine(root, "archive"))));
        var destination = new ProductionDestinationResolver().Resolve(package, repository);
        var plan = new ProcessingPlanBuilder().Build(package, repository, destination);
        var report = _renderer.Render(plan);
        Assert.Contains($"ASSET\n  universe_id: nimroel\n  asset_id: {assetId}\n  asset_type: portrait\n  production_profile: portrait_npc\n", report);
        Assert.Contains($"  relative_directory: portraits/norgard/treskal/{role}/{sex}/{assetId}\n", report);
        Assert.Contains($"CLASSIFICATION\n  culture: norgard\n  location: treskal\n  realm: example_realm\n  region: example_region\n  role: {role}\n  sex: {sex}\n", report);
        foreach (var legacy in new[] { "Norgard", "Treskal", "children/boy", "/elder/male" }) Assert.DoesNotContain(legacy, report);
        Assert.Equal(files.Count, InputLines(report).Length);
        Assert.DoesNotContain("  manifest:", report);
        Assert.DoesNotContain(".webp", report);
    }

    [Fact]
    public void InexistentPathsRenderWithoutCreatingInputsRootOrDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-renderer-absent-" + Guid.NewGuid().ToString("N"));
        var packageRoot = Path.Combine(root, "package");
        var productionRoot = Path.Combine(root, "production");
        var fullPath = Path.Combine(productionRoot, "assets", AssetId);
        var plan = Plan(packageRoot: packageRoot, manifestPath: Path.Combine(packageRoot, "manifest.json"), productionRoot: productionRoot, fullPath: fullPath);
        Assert.False(Directory.Exists(root));
        var report = _renderer.Render(plan);
        Assert.StartsWith("NAP DRY RUN\n", report);
        Assert.Equal(report, _renderer.Render(plan));
        Assert.False(Directory.Exists(packageRoot));
        Assert.False(Directory.Exists(productionRoot));
        Assert.False(Directory.Exists(fullPath));
        Assert.False(Directory.Exists(root));
    }

    private static string[] InputLines(string report) => report.Split("INPUT FILES\n", StringSplitOptions.None)[1]
        .Split("\n\nPRODUCTION DESTINATION", StringSplitOptions.None)[0].Split('\n');

    private static ProcessingPlan Plan(Dictionary<string, string>? classification = null, Dictionary<string, string>? files = null,
        string packageRoot = @"C:\nap\package", string manifestPath = @"C:\nap\package\portrait_example_001_manifest.json",
        string productionRoot = @"D:\prod", string fullPath = @"D:\prod\assets\portrait_example_001", string relativeDirectory = "assets/portrait_example_001")
    {
        var key = new UniverseAssetKey(new UniverseId("test_universe"), AssetId);
        var destination = Construct<ProductionAssetDestination>(key, productionRoot, relativeDirectory, fullPath);
        return Construct<ProcessingPlan>(key, "portrait", "portrait_npc", classification ?? new() { ["culture"] = "example" },
            packageRoot, manifestPath, files ?? [], destination);
    }

    private static T Construct<T>(params object[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));
}
