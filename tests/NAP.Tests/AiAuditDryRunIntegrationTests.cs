using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AiAuditDryRunIntegrationTests
{
    [Fact]
    public async Task RealValidatedNimroelPlanCleanNapValidationAndFakePassPreserveEveryRootAndCheckpoint()
    {
        using var fixture = new PackageSemanticTestFixture();
        PrepareRoots(fixture.Context);
        var before = Tree(fixture.Root);
        var (plan, validation) = ValidatedPlan(fixture);
        Assert.True(validation.IsClean);
        var dryRun = new DryRunTextRenderer().Render(plan);
        Assert.Contains("OPERATIONS\n  (not defined in ProcessingPlan v1)", dryRun);
        var client = new FakeClient(new AiAuditReport(AiAuditDecision.Pass, "Coherent facts.", []));
        var request = new AiAuditRequestBuilder().Build(plan, validation);
        var report = await client.AuditAsync(request);
        Assert.True(report.Passed); Assert.False(report.RequiresReview); Assert.False(report.ShouldStop);
        Assert.Equal(1, client.Calls);
        Assert.Same(request, client.LastRequest);
        Assert.Equal(plan.AssetKey, request.AssetKey);
        Assert.Equal(plan.Classification, request.Classification);
        Assert.Equal(plan.FilesByRole.Keys.OrderBy(k => k, StringComparer.Ordinal), request.InputRoles);
        Assert.Equal(plan.ProductionDestination.RelativeDirectory, request.DestinationRelativeDirectory);
        var payload = new AiAuditRequestJsonRenderer().Render(request);
        Assert.DoesNotContain("production_root", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("package_root", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifest_path", payload, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(plan.ProductionDestination.FullDirectoryPath));
        AssertTree(fixture.Root, before);
    }

    [Fact]
    public async Task ValidPlanWithContinueWarningSendsOnlySafeFactAndRequiresReviewWithoutMutation()
    {
        using var fixture = new PackageSemanticTestFixture();
        PrepareRoots(fixture.Context);
        var before = Tree(fixture.Root);
        var (plan, clean) = ValidatedPlan(fixture);
        Assert.True(clean.IsClean);
        var validation = new NapIssueReport([new NapIssue("review_required", NapIssueSeverity.Warning, NapIssueDisposition.Continue,
            "SECRET_MESSAGE", "SECRET_SUBJECT", "SECRET_DETAIL")]);
        var client = new FakeClient(new AiAuditReport(AiAuditDecision.Warning, "Review required.", [AiAuditTestData.Finding()]));
        var request = new AiAuditRequestBuilder().Build(plan, validation);
        var fact = Assert.Single(request.ValidationIssues);
        Assert.Equal("review_required", fact.Code); Assert.Equal(NapIssueDisposition.Continue, fact.Disposition);
        var report = await client.AuditAsync(request);
        Assert.True(report.RequiresReview); Assert.False(report.Passed); Assert.False(report.ShouldStop);
        AiAuditTestData.AssertNoPrivateFacts(new AiAuditRequestJsonRenderer().Render(request));
        Assert.Equal(1, client.Calls);
        AssertTree(fixture.Root, before);
    }

    [Fact]
    public async Task RealDeterministicDestinationStopNeverCallsEvenAFakeClientReturningPass()
    {
        using var fixture = new PackageSemanticTestFixture();
        PrepareRoots(fixture.Context);
        var (plan, clean) = ValidatedPlan(fixture);
        Assert.True(clean.IsClean);
        Directory.CreateDirectory(plan.ProductionDestination.FullDirectoryPath); // Arrange a deterministic blocker.
        var repository = new ProductionRepositoryValidator().Validate(fixture.Context).Repository!;
        var snapshot = new ProductionRepositoryScanner().Scan(repository).Snapshot!;
        var validation = new ProcessingPlanValidator().Validate(plan, snapshot);
        Assert.True(validation.ShouldStop);
        var before = Tree(fixture.Root);
        var client = new FakeClient(new AiAuditReport(AiAuditDecision.Pass, "Must not be consulted.", []));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var request = new AiAuditRequestBuilder().Build(plan, validation);
            await client.AuditAsync(request);
        });
        Assert.Equal("A ProcessingPlan with blocking NAP validation issues cannot be sent to AI audit.", exception.Message);
        Assert.Equal(0, client.Calls); Assert.Null(client.LastRequest);
        AssertTree(fixture.Root, before);
    }

    private static (ProcessingPlan Plan, NapIssueReport Validation) ValidatedPlan(PackageSemanticTestFixture fixture)
    {
        var packageResult = new PackageSemanticValidator().Validate(fixture.PackageRoot, fixture.Context);
        Assert.True(packageResult.IsValid);
        var boundary = new ProductionRepositoryValidator().Validate(fixture.Context);
        Assert.True(boundary.IsValid);
        var destination = new ProductionDestinationResolver().Resolve(packageResult.Package!, boundary.Repository!);
        var plan = new ProcessingPlanBuilder().Build(packageResult.Package!, boundary.Repository!, destination);
        var scan = new ProductionRepositoryScanner().Scan(boundary.Repository!);
        Assert.False(scan.Issues.ShouldStop);
        return (plan, new ProcessingPlanValidator().Validate(plan, scan.Snapshot!));
    }

    private static void PrepareRoots(UniverseContext context)
    {
        foreach (var path in new[] { context.Storage.InboxRoot, context.Storage.StagingRoot, context.Storage.CacheRoot,
            context.Storage.StateRoot, context.Storage.ProductionRoot, context.Storage.ArchiveRoot })
        {
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "sentinel.bin"), [0, 255, 42]);
        }
        // Existing durable checkpoint fixture; no JobStateStore or lifecycle operation is invoked.
        File.WriteAllText(Path.Combine(context.Storage.StateRoot, "job_00112233445566778899aabbccddeeff.json"),
            "{\"schema_version\":1,\"job_id\":\"job_00112233445566778899aabbccddeeff\",\"universe_id\":\"nimroel\",\"state\":\"PLANNED\"}");
    }

    private static Dictionary<string, (byte[]? Bytes, DateTime Write)> Tree(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => path, path => (Directory.Exists(path) ? null : File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private static void AssertTree(string root, Dictionary<string, (byte[]? Bytes, DateTime Write)> before)
    {
        var after = Tree(root);
        Assert.Equal(before.Keys.OrderBy(p => p, StringComparer.Ordinal), after.Keys.OrderBy(p => p, StringComparer.Ordinal));
        foreach (var (path, item) in before)
        {
            Assert.Equal(item.Bytes, after[path].Bytes);
            Assert.Equal(item.Write, after[path].Write);
        }
    }

    private sealed class FakeClient(AiAuditReport report) : IAiAuditClient
    {
        public int Calls { get; private set; }
        public AiAuditRequest? LastRequest { get; private set; }
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; LastRequest = request;
            return Task.FromResult(report);
        }
    }
}
