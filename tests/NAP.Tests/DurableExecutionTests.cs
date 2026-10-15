using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class DurableExecutionTests
{
    internal sealed class AuditClient(AiAuditDecision decision = AiAuditDecision.Pass) : IIdentifiedAiAuditClient
    {
        public string ProviderId => "synthetic_provider";
        public string ModelId => "synthetic_model";
        public int Calls { get; private set; }
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new AiAuditReport(decision, "Synthetic audit", [])); }
    }
    internal static StoredAutomationPolicy Enable(PreparationTestFixture f, AutomationQueueOwner owner, bool normalize = false)
    {
        var p = f.Policy(owner, normalize ? f.Normalization() : null,
            operations: StoredAutomationPolicy.ExecutionOperations | (normalize ? AutomationOperations.Normalize : AutomationOperations.None));
        return f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Enabled);
    }
    internal static void Hook(DurableExecutionService service, Action<string> action) => typeof(DurableExecutionService)
        .GetProperty("Checkpoint", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, action);

    [Theory] [InlineData(false)] [InlineData(true)]
    public void OneSyntheticZipCompletesSameJobAndVerifiesCatalogOnRepeat(bool normalize)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); Enable(f, owner, normalize);
        var zip = f.Zip(width: normalize ? 39 : 40); var received = File.ReadAllBytes(zip); var a = f.Admit(owner, zip); var client = new AuditClient();
        Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
        var execution = new DurableExecutionService(f.Context, f.Queue);
        var checkpoints = new List<string>(); Hook(execution, checkpoints.Add);
        var result = execution.Execute(owner, a.ItemId!);
        Assert.True(result.Code == DurableExecutionCode.Completed, string.Join("\n", checkpoints)); Assert.Equal(a.JobId, result.JobId);
        Assert.Equal(JobState.Completed, new JobStateStore(f.Context).Load(a.JobId!).State);
        Assert.Equal(QueueState.Completed, f.Queue.Get(a.ItemId!).State);
        var asset = new AssetCatalog(f.Context).Get("portrait_example_001"); Assert.NotNull(asset); Assert.Equal("PASS", asset.AuditState);
        var before = Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath);
        var revision = execution.State.Get(a.ItemId!)!.Revision;
        Assert.Equal(DurableExecutionCode.Completed, new DurableExecutionService(f.Context, new(f.Roots)).Execute(owner, a.ItemId!).Code);
        Assert.Equal(revision, execution.State.Get(a.ItemId!)!.Revision);
        Assert.Equal(before, Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath));
        Assert.Equal(received, File.ReadAllBytes(zip)); Assert.Equal(1, client.Calls);
        Assert.Equal("unrelated synthetic sentinel", File.ReadAllText(Path.Combine(f.Roots.ArchiveRoot, "foreign.txt")));
        Assert.Equal("unrelated synthetic sentinel", File.ReadAllText(Path.Combine(f.Roots.ProductionRoot, "foreign.txt")));
        Assert.Equal(normalize, f.Queue.Lineage(a.ItemId!).Any());
    }

    [Theory] [InlineData(AutomationPolicyState.Disabled)] [InlineData(AutomationPolicyState.PreparationOnly)]
    [InlineData(AutomationPolicyState.Paused)] [InlineData(AutomationPolicyState.Revoked)]
    public void InactiveProductiveGateNeverWrites(AutomationPolicyState state)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var p = state == AutomationPolicyState.PreparationOnly ? f.Policy(owner, authorizePreparation: true) : Enable(f, owner);
        var a = f.Admit(owner); var client = new AuditClient(); Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
        if (state != AutomationPolicyState.PreparationOnly) f.Queue.SetPolicyState(owner, p.Reference, p.Revision, state);
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!).Code);
        Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(a.JobId!).State); f.AssertUntouched();
    }

    [Theory] [InlineData(AiAuditDecision.Warning)] [InlineData(AiAuditDecision.Fail)]
    public void NonPassNeverExecutes(AiAuditDecision decision)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var p = Enable(f, owner); var a = f.Admit(owner);
        f.Service.Audit(owner, a.ItemId!, new AuditClient(decision));
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!).Code);
        f.Queue.SetPolicyState(owner, p.Reference, p.Revision, AutomationPolicyState.Disabled); f.AssertUntouched();
    }
    internal static string Inventory(params string[] roots) => string.Join("\n", roots.SelectMany(r => Directory.Exists(r) ?
        Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories) : File.Exists(r) ? new[] { r } : [])
        .Order(StringComparer.Ordinal).Select(p => p + ":" + new Sha256Hasher().Compute(p).Hex));
}
