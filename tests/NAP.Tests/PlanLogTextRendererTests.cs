using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PlanLogTextRendererTests
{
    private const string Relative = "portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040";
    private const string AssetId = "portrait_treskal_farmer_male_040";
    private readonly PlanLogTextRenderer _renderer = new();

    [Fact]
    public void ExactPublicApiIsSealedStatelessAndHasDefaultConstructor()
    {
        var type = typeof(PlanLogTextRenderer);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Render", method.Name);
        Assert.Equal(typeof(string), method.ReturnType);
        Assert.Equal(new[] { typeof(ProcessingPlan), typeof(NapIssueReport) }, method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "plan", "validationReport" }, method.GetParameters().Select(p => p.Name));
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
    }

    [Fact]
    public void NullInputsNameTheirExactParameters()
    {
        Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => _renderer.Render(null!, new NapIssueReport([]))).ParamName);
        Assert.Equal("validationReport", Assert.Throws<ArgumentNullException>(() => _renderer.Render(Plan(), null!)).ParamName);
    }

    [Fact]
    public void CleanOutputIsExact()
    {
        Assert.Equal(Expected(true, false, true, 0, "  (none)"), _renderer.Render(Plan(), new NapIssueReport([])));
    }

    [Fact]
    public void ErrorStopOutputIsExact()
    {
        var issue = Issue(NapIssueCodes.PlanDestinationExists, NapIssueSeverity.Error, NapIssueDisposition.Stop);
        Assert.Equal(Expected(false, true, false, 1, IssueText(0, issue)), _renderer.Render(Plan(), new NapIssueReport([issue])));
    }

    [Fact]
    public void WarningContinueDoesNotAuthorizeOrInventAStatus()
    {
        var issue = Issue("synthetic_warning", NapIssueSeverity.Warning, NapIssueDisposition.Continue);
        Assert.Equal(Expected(false, false, true, 1, IssueText(0, issue)), _renderer.Render(Plan(), new NapIssueReport([issue])));
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Stop)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Continue)]
    public void FlagsComeFromReportAndDispositionIsIndependentOfSeverity(NapIssueSeverity severity, NapIssueDisposition disposition)
    {
        var issue = Issue("synthetic_issue", severity, disposition);
        var report = new NapIssueReport([issue]);
        Assert.Equal(Expected(report.IsClean, report.ShouldStop, report.CanContinue, 1, IssueText(0, issue)), _renderer.Render(Plan(), report));
    }

    [Fact]
    public void MultipleIssuesRetainOrderDuplicatesAndZeroBasedIndices()
    {
        var first = Issue("z_issue", NapIssueSeverity.Warning, NapIssueDisposition.Continue);
        var second = Issue("a_issue", NapIssueSeverity.Error, NapIssueDisposition.Stop);
        var report = new NapIssueReport([first, second, first]);
        Assert.Equal(Expected(false, true, false, 3, string.Join("\n", IssueText(0, first), IssueText(1, second), IssueText(2, first))),
            _renderer.Render(Plan(), report));
    }

    [Fact]
    public void SensitiveIssueAndPlanFieldsNeverEnterTheLogOrInjectLines()
    {
        var issue = new NapIssue("safe_code", NapIssueSeverity.Error, NapIssueDisposition.Stop,
            "SECRET_MESSAGE API_KEY_123\nnewline injection", @"C:\Users\private\secret", "SECRET_DETAIL");
        var text = _renderer.Render(Plan(), new NapIssueReport([issue]));
        Assert.Equal(Expected(false, true, false, 1, IssueText(0, issue)), text);
        foreach (var sentinel in new[] { "SECRET_MESSAGE", "API_KEY_123", "newline injection", @"C:\Users\private\secret",
            "SECRET_DETAIL", "SECRET_PACKAGE_ROOT", "SECRET_MANIFEST", "SECRET_SOURCE_FILE",
            "SECRET_PRODUCTION_ROOT", "SECRET_FULL_PATH", "SECRET_CLASSIFICATION" })
            Assert.DoesNotContain(sentinel, text);
        Assert.Contains("  relative_directory: " + Relative, text);
    }

    [Fact]
    public void OutputHasCanonicalLfWithoutBomFinalNewlineOrTrailingWhitespace()
    {
        var text = _renderer.Render(Plan(), new NapIssueReport([]));
        Assert.Contains("\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("\uFEFF", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith('\n'));
        Assert.All(text.Split('\n'), line => Assert.Equal(line.TrimEnd(), line));
    }

    [Fact]
    public void OutputOmitsFutureMetadataAndDetailedDryRunSections()
    {
        var text = _renderer.Render(Plan(), new NapIssueReport([]));
        foreach (var field in new[] { "CLASSIFICATION", "PACKAGE", "INPUT FILES", "OPERATIONS", "Timestamp",
            "CreatedAt", "LoggedAt", "JobId", "SessionId", "OperationId", "CorrelationId", "Exception", "StackTrace", "PASS", "FAIL" })
            Assert.DoesNotContain(field, text);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", text);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", text);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    public void RepeatedOutputIsCultureIndependent(string cultureName)
    {
        var plan = Plan();
        var report = new NapIssueReport(Enumerable.Repeat(Issue("safe_code", NapIssueSeverity.Warning, NapIssueDisposition.Continue), 12));
        var expected = _renderer.Render(plan, report);
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            for (var repeat = 0; repeat < 3; repeat++) Assert.Equal(expected, _renderer.Render(plan, report));
            Assert.Contains("  issue_count: 12", expected);
            Assert.Contains("  [11]", expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void RenderingNeedsNoPhysicalInputsAndCreatesNoFilesOrDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-plan-log-absent-" + Guid.NewGuid().ToString("N"));
        var plan = Plan(root);
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(plan.ManifestPath));
        Assert.All(plan.FilesByRole.Values, file => Assert.False(File.Exists(file)));
        Assert.Equal(Expected(true, false, true, 0, "  (none)"), _renderer.Render(plan, new NapIssueReport([])));
        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(plan.ManifestPath));
        Assert.All(plan.FilesByRole.Values, file => Assert.False(File.Exists(file)));
    }

    [Fact]
    public void RealNimroelCleanPipelineUsesCanonicalAncestors()
    {
        var (plan, repository) = NimroelPlan();
        var segments = Relative.Split('/');
        var entries = Enumerable.Range(1, segments.Length - 1).Select(count =>
            Entry(string.Join("/", segments.Take(count)))).ToArray();
        var snapshot = Construct<ProductionRepositorySnapshot>(repository, entries);
        var report = new ProcessingPlanValidator().Validate(plan, snapshot);
        Assert.True(report.IsClean);
        Assert.Equal(Expected(true, false, true, 0, "  (none)"), _renderer.Render(plan, report));
    }

    [Fact]
    public void RealNimroelHistoricalCasingIsReportedWithoutSensitiveDetailsOrAdaptation()
    {
        var (plan, repository) = NimroelPlan();
        var observed = Entry("portraits/Norgard");
        var snapshot = Construct<ProductionRepositorySnapshot>(repository, new[] { Entry("portraits"), observed });
        var report = new ProcessingPlanValidator().Validate(plan, snapshot);
        var text = _renderer.Render(plan, report);
        if (OperatingSystem.IsWindows())
        {
            var issue = Assert.Single(report.Issues);
            Assert.Equal(NapIssueCodes.PlanDestinationCasingConflict, issue.Code);
            Assert.Equal(NapIssueSeverity.Error, issue.Severity);
            Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
            Assert.Equal(Expected(false, true, false, 1, IssueText(0, issue)), text);
            Assert.DoesNotContain(issue.Detail!, text);
            Assert.DoesNotContain(issue.SubjectPath!, text);
        }
        else Assert.Equal(Expected(true, false, true, 0, "  (none)"), text);
        Assert.DoesNotContain(observed.FullPath, text);
        Assert.DoesNotContain("Norgard", text);
        Assert.DoesNotContain("expected:", text);
        Assert.DoesNotContain("observed:", text);
        Assert.Equal(Relative, plan.ProductionDestination.RelativeDirectory);
        Assert.Equal("portraits/Norgard", observed.RelativePath);
    }

    private static string Expected(bool clean, bool stop, bool canContinue, int count, string issues) =>
        string.Join("\n", "NAP PLAN LOG", "Planning summary v1", "", "ASSET", "  universe_id: nimroel",
            "  asset_id: " + AssetId, "  asset_type: portrait", "  production_profile: portrait_npc", "",
            "DESTINATION", "  relative_directory: " + Relative, "", "VALIDATION",
            "  is_clean: " + (clean ? "true" : "false"), "  should_stop: " + (stop ? "true" : "false"),
            "  can_continue: " + (canContinue ? "true" : "false"), "  issue_count: " + count.ToString(CultureInfo.InvariantCulture),
            "", "ISSUES", issues, "", "SAFETY", "  Absolute paths and source file paths are intentionally omitted.",
            "  This log does not authorize execution.");

    private static string IssueText(int index, NapIssue issue) =>
        string.Join("\n", "  [" + index.ToString(CultureInfo.InvariantCulture) + "]", "    code: " + issue.Code,
            "    severity: " + issue.Severity, "    disposition: " + issue.Disposition);

    private static NapIssue Issue(string code, NapIssueSeverity severity, NapIssueDisposition disposition) =>
        new(code, severity, disposition, "SECRET_MESSAGE");

    private static ProcessingPlan Plan(string? root = null)
    {
        var key = new UniverseAssetKey(new UniverseId("nimroel"), AssetId);
        var destination = Construct<ProductionAssetDestination>(key, root ?? "SECRET_PRODUCTION_ROOT", Relative,
            root is null ? "SECRET_FULL_PATH" : Path.Combine(root, "SECRET_FULL_PATH"));
        return Construct<ProcessingPlan>(key, "portrait", "portrait_npc",
            new Dictionary<string, string> { ["culture"] = "SECRET_CLASSIFICATION" },
            root is null ? "SECRET_PACKAGE_ROOT" : Path.Combine(root, "SECRET_PACKAGE_ROOT"),
            root is null ? "SECRET_MANIFEST" : Path.Combine(root, "SECRET_MANIFEST"),
            new Dictionary<string, string> { ["master"] = root is null ? "SECRET_SOURCE_FILE" : Path.Combine(root, "SECRET_SOURCE_FILE") },
            destination);
    }

    private static (ProcessingPlan, ValidatedProductionRepository) NimroelPlan()
    {
        var profile = UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        var root = Path.Combine(Path.GetTempPath(), "nap-plan-log-nimroel", "production");
        var context = new UniverseContext(profile, new UniverseStorageConfig(profile.Id, root, root, root));
        var repository = Construct<ValidatedProductionRepository>(context);
        var manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = "nimroel", AssetId = AssetId, AssetType = "portrait", ProductionProfile = "portrait_npc",
            Classification = new() { ["culture"] = "norgard", ["location"] = "treskal", ["role"] = "farmer", ["sex"] = "male" }
        };
        var package = Construct<ValidatedAssetPackage>(new UniverseAssetKey(profile.Id, AssetId), "SECRET_PACKAGE_ROOT",
            "SECRET_MANIFEST", manifest, Assert.Single(profile.AssetRules, r => r.AssetType == "portrait" && r.ProductionProfile == "portrait_npc"), new Dictionary<string, string>());
        var destination = new ProductionDestinationResolver().Resolve(package, repository);
        return (new ProcessingPlanBuilder().Build(package, repository, destination), repository);
    }

    private static ProductionRepositoryEntry Entry(string relative) =>
        Construct<ProductionRepositoryEntry>(relative, @"C:\SECRET_HISTORICAL_PATH\" + relative, ProductionRepositoryEntryKind.Directory);

    private static T Construct<T>(params object[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));
}
