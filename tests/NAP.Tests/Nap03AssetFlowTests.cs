using System.Security.Cryptography;
using NAP.AI;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class Nap03AssetFlowTests
{
    [Fact]
    public void GeminiSelectionIsFailClosedAndNeverReturnsSyntheticPass()
    {
        using var http = new HttpClient();
        Assert.Null(HostGeminiAuditor.Create(http, null, null));
        Assert.Null(HostGeminiAuditor.Create(http, "", ""));
        Assert.Throws<InvalidDataException>(() => HostGeminiAuditor.Create(http, "private", null));
        Assert.Throws<InvalidDataException>(() => HostGeminiAuditor.Create(http, null, "gemini-model"));
        Assert.Throws<InvalidDataException>(() => HostGeminiAuditor.Create(http, "private", "model", true));
        var client = HostGeminiAuditor.Create(http, "private", "gemini-model");
        Assert.NotNull(client);
        Assert.Equal("gemini-interactions-v1", ((IIdentifiedAiAuditClient)client).ProviderId);
    }

    [Fact]
    public async Task InactiveUniverseCannotForceAnInboxScan()
    {
        using var fixture = new AutomationHostTests.Fixture();
        await using var host = new AutomationHost(fixture.Control, [fixture.A.Context], fixture.Options);
        await host.Started;
        var stopped = Assert.Throws<AutomationException>(() => host.RequestPendingScan(fixture.A.Context.Id));
        Assert.Equal(AutomationError.Disabled, stopped.Code);
    }

    [Fact]
    public async Task MixedUniversesScanArchiveConvertAndCatalogWithoutRewritingOrReaudit()
    {
        using var fixture = new AutomationHostTests.Fixture();
        var alpha = AutomationHostTests.Fixture.Enable(fixture.A);
        var beta = AutomationHostTests.Fixture.Enable(fixture.B);
        var aZip = fixture.A.Zip("alpha_inbox.zip");
        var bZip = fixture.B.Zip("beta_inbox.zip");
        var aHash = SHA256.HashData(File.ReadAllBytes(aZip));
        var bHash = SHA256.HashData(File.ReadAllBytes(bZip));
        var audit = new DurableExecutionTests.AuditClient();
        await using var host = new AutomationHost(fixture.Control, [fixture.A.Context, fixture.B.Context], fixture.Options, audit);
        await host.Started;
        await host.ActivateAsync(fixture.A.Context.Id, alpha.Reference, false);
        await host.ActivateAsync(fixture.B.Context.Id, beta.Reference, false);
        Assert.True(host.RequestPendingScan(fixture.A.Context.Id));
        Assert.True(host.RequestPendingScan(fixture.B.Context.Id));
        await AutomationHostTests.Until(() =>
            fixture.A.Queue.List().Any(i => i.State == QueueState.Completed) &&
            fixture.B.Queue.List().Any(i => i.State == QueueState.Completed), host);

        Assert.Equal(2, audit.Calls);
        foreach (var f in new[] { fixture.A, fixture.B })
        {
            Assert.NotNull(new AssetCatalog(f.Context).Get("portrait_example_001"));
            Assert.Contains(Directory.EnumerateFiles(f.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories), _ => true);
            Assert.Contains(Directory.EnumerateFiles(f.Roots.ArchiveRoot, "*.png", SearchOption.AllDirectories), _ => true);
            var summary = Assert.Single(f.Queue.BrowseWorkflowBatches().Where(b => b.Items > 0));
            Assert.Equal(1, summary.Summary?.Completed);
            Assert.Equal(0, summary.Summary?.Pending);
        }
        var beforeA = DurableExecutionTests.Inventory(fixture.A.Roots.ArchiveRoot, fixture.A.Roots.ProductionRoot, fixture.A.Roots.CatalogPath);
        var beforeB = DurableExecutionTests.Inventory(fixture.B.Roots.ArchiveRoot, fixture.B.Roots.ProductionRoot, fixture.B.Roots.CatalogPath);
        Assert.True(host.RequestPendingScan(fixture.A.Context.Id));
        Assert.True(host.RequestPendingScan(fixture.B.Context.Id));
        await AutomationHostTests.Until(() => host.Snapshot().Universes.All(u => u.Turns > 10), host);
        Assert.Equal(2, audit.Calls);
        Assert.Equal(beforeA, DurableExecutionTests.Inventory(fixture.A.Roots.ArchiveRoot, fixture.A.Roots.ProductionRoot, fixture.A.Roots.CatalogPath));
        Assert.Equal(beforeB, DurableExecutionTests.Inventory(fixture.B.Roots.ArchiveRoot, fixture.B.Roots.ProductionRoot, fixture.B.Roots.CatalogPath));
        Assert.Equal(aHash, SHA256.HashData(File.ReadAllBytes(aZip)));
        Assert.Equal(bHash, SHA256.HashData(File.ReadAllBytes(bZip)));
    }

    [Fact]
    public async Task NoGeminiAndNoSyntheticAuditCannotPublishAnAsset()
    {
        using var fixture = new AutomationHostTests.Fixture();
        var policy = AutomationHostTests.Fixture.Enable(fixture.A);
        fixture.A.Zip();
        await using var host = new AutomationHost(fixture.Control, [fixture.A.Context], fixture.Options);
        await host.Started;
        await host.ActivateAsync(fixture.A.Context.Id, policy.Reference, false);
        await AutomationHostTests.Until(() => host.Snapshot().Universes.Any(s => s.Stage == "audit-unconfigured"), host);
        Assert.Empty(Directory.EnumerateFiles(fixture.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories));
        Assert.False(File.Exists(fixture.A.Roots.CatalogPath));
    }
}
