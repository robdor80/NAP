using System.Diagnostics;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationHostTests
{
    internal sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "nap-host-" + Guid.NewGuid().ToString("N"));
        internal readonly PreparationTestFixture A, B;
        internal string Control => Path.Combine(Root, "control");
        internal AutomationHostOptions Options => new() { LaboratoryRoot = Root,
            Admission = PreparationTestFixture.Fast with { MaxPendingCandidates = 3, RescanInterval = TimeSpan.FromHours(1) } };
        internal Fixture()
        { Directory.CreateDirectory(Control); A = new("alpha_universe", parent: Root); B = new("beta_universe", parent: Root); }
        internal static StoredAutomationPolicy Enable(PreparationTestFixture fixture, int requests = 10)
        {
            using var owner = fixture.Queue.AcquireOwner();
            var policy = fixture.Policy(owner, requests: requests, cost: requests, operations: StoredAutomationPolicy.ExecutionOperations);
            return fixture.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled);
        }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root); var temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("nap-host-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid host fixture cleanup root.");
            A.Dispose(); B.Dispose();
            foreach (var file in Directory.EnumerateFiles(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(full, true);
        }
    }

    internal static async Task Until(Func<bool> condition, AutomationHost host, int seconds = 120)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.DoesNotContain(host.Snapshot().Universes, s => s.State == AutomationHostState.Blocked);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(seconds), System.Text.Json.JsonSerializer.Serialize(host.Snapshot()));
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task StartsPausedAndRunsTwoSyntheticUniversesWithSameAssetIdentity()
    {
        using var f = new Fixture(); var a = Fixture.Enable(f.A); var b = Fixture.Enable(f.B);
        f.A.Zip(); f.B.Zip(); var auditor = new DurableExecutionTests.AuditClient();
        await using var host = new AutomationHost(f.Control, [f.A.Context, f.B.Context], f.Options, auditor); await host.Started;
        Assert.All(host.Snapshot().Universes, s => Assert.NotEqual(AutomationHostState.Running, s.State));
        await host.ActivateAsync(f.A.Context.Id, a.Reference, false); await host.ActivateAsync(f.B.Context.Id, b.Reference, false);
        await Until(() => Directory.EnumerateFiles(f.A.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Any() &&
            Directory.EnumerateFiles(f.B.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Any(), host);
        await Until(() => host.Snapshot().Universes.All(s => s.Turns > 5), host);
        Assert.Equal(2, auditor.Calls);
        Assert.Equal("unrelated synthetic sentinel", File.ReadAllText(Path.Combine(f.A.Roots.ArchiveRoot, "foreign.txt")));
        Assert.Equal("unrelated synthetic sentinel", File.ReadAllText(Path.Combine(f.B.Roots.ProductionRoot, "foreign.txt")));
    }

    [Fact]
    public async Task MissingAuthorityNeverStartsAndSecondHostCannotOwnSameDeployment()
    {
        using var f = new Fixture();
        await using var first = new AutomationHost(f.Control, [f.A.Context], f.Options); await first.Started;
        var policy = await first.QueryAsync(f.A.Context.Id, s => s.Queue.CurrentPolicy());
        var rejected = await Assert.ThrowsAsync<AutomationException>(() => first.ActivateAsync(f.A.Context.Id, policy.Reference, true));
        Assert.Equal(AutomationError.PolicyMismatch, rejected.Code);
        var other = new AutomationHost(f.Control, [f.A.Context], f.Options);
        await Assert.ThrowsAsync<IOException>(() => other.Started);
        await Assert.ThrowsAsync<IOException>(async () => await other.DisposeAsync());
    }

    [Fact]
    public void HostRejectsRootsOutsideExplicitTemporaryLaboratory()
    {
        using var f = new PreparationTestFixture();
        Assert.Throws<AutomationException>(() => new AutomationHost(Path.Combine(f.Root, "host"), [f.Context],
            new() { LaboratoryRoot = f.Root }));
    }
}
