using System.Reflection;
using NAP.Core;
using Xunit;
using static NAP.Tests.GitProductionFixture;

namespace NAP.Tests;

public sealed class GitProductionBoundaryTests
{
    [Theory] [InlineData("inspect")] [InlineData("prepare")] [InlineData("commit")] [InlineData("push")]
    public void EveryOperationUsesExistingProductionMutex(string operation)
    {
        using var f = new GitProductionFixture(); var result = f.Produce(); var plan = f.Plan(result);
        var commit = operation == "push" ? f.Service.Commit(f.Context, plan) : null;
        using var lease = BackupTestSupport.Lock(f.Context, "Production");
        Stop(() =>
        {
            switch (operation)
            {
                case "inspect": f.Inspector.Inspect(f.Context); break;
                case "prepare": f.Plan(result); break;
                case "commit": f.Service.Commit(f.Context, plan); break;
                case "push": f.Service.Push(f.Context, commit!); break;
            }
        }, NapIssueCodes.GitBusy);
    }
    [Theory] [InlineData("writer")] [InlineData("snapshot")] [InlineData("bundle")] [InlineData("commit")] [InlineData("push")]
    public void ActiveCommitSerializesWritersBackupsOtherCommitsAndPush(string competing)
    {
        using var f = new GitProductionFixture(); var productionPlan = f.Production.Plan(); var result = f.Production.Execute(productionPlan); var plan = f.Plan(result);
        var observed = false;
        f.Observe(stage =>
        {
            if (stage != "prepared") return; observed = true;
            // Another thread proves non-reentrancy and nonblocking behavior rather than relying on Windows Mutex recursion.
            Task.Run(() =>
            {
                switch (competing)
                {
                    case "writer": Assert.Throws<IOException>(() => f.Production.Execute(productionPlan)); break;
                    case "snapshot": BackupTestSupport.Stop(() => new RepositorySnapshotService().Create(f.Context), NapIssueCodes.BackupBusy); break;
                    case "bundle": BackupTestSupport.Stop(() => new GitBundleService().Create(f.Context), NapIssueCodes.BackupBusy); break;
                    case "commit": Stop(() => f.Service.Commit(f.Context, plan), NapIssueCodes.GitBusy); break;
                    case "push": Stop(() => f.Service.Push(f.Context, Assert.Single(f.Service.ListOperations(f.Context))), NapIssueCodes.GitBusy); break;
                }
            }).GetAwaiter().GetResult();
        });
        var committed = f.Service.Commit(f.Context, plan); Assert.True(observed); Assert.Equal(committed.CommitSha, f.Git("rev-parse", "HEAD"));
    }
    [Theory] [InlineData("reset")] [InlineData("clean")] [InlineData("checkout")] [InlineData("switch")] [InlineData("merge")]
    [InlineData("rebase")] [InlineData("pull")] [InlineData("fetch")] [InlineData("branch")]
    [InlineData("tag")] [InlineData("remote")]
    public void ArbitraryVerbsAreStructurallyAbsentFromRunnerApi(string prohibited)
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitProductionProcess")!;
        var available = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(m => !m.IsPrivate).ToArray();
        Assert.DoesNotContain(available, m => m.Name.Equals(prohibited, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(available, m => m.GetParameters().Any(p => p.Name is "command" or "args" or "arguments"));
        Assert.True(type.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Instance)!.IsPrivate);
        var pushes = Assert.Single(available, m => m.Name == "Push"); Assert.Equal(new[] { typeof(UniverseContext), typeof(string), typeof(string) }, pushes.GetParameters().Select(p => p.ParameterType));
        var config = Assert.Single(available, m => m.Name == "Config"); Assert.Single(config.GetParameters());
        Assert.DoesNotContain(typeof(GitProductionService).GetMethods().Where(m => m.Name is "Commit" or "Push").SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(string));
    }
    [Fact]
    public void BackupRunnerStillHasNoNetworkOrOperationalWrites()
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitBackupProcess")!;
        var names = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.IsPrivate).Select(m => m.Name).ToArray();
        Assert.DoesNotContain("Push", names); Assert.DoesNotContain("Add", names); Assert.DoesNotContain("Commit", names); Assert.DoesNotContain("RemoteTip", names);
    }
    [Fact]
    public void ProcessTimeoutIsBoundedAndNeverLeaksCommandOutput()
    {
        using var f = new GitProductionFixture(); BackupTestSupport.Set(BackupTestSupport.Get(f.Inspector, "Git"), "Timeout", TimeSpan.Zero);
        var before = ArchiveTestFixture.Snapshot(f.Root); Stop(() => f.Inspector.Inspect(f.Context), NapIssueCodes.GitTimeout); ArchiveTestFixture.AssertSnapshot(before, f.Root);
    }
    [Fact]
    public void BoundedBinaryOutputStopsInsteadOfTruncatingOrLeaking()
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitProductionProcess")!;
        using var stream = new MemoryStream(new byte[8 * 1024 * 1024 + 1]);
        var task = (Task<byte[]>)type.GetMethod("ReadBounded", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [stream, CancellationToken.None])!;
        Stop(() => task.GetAwaiter().GetResult(), NapIssueCodes.GitOutputLimit);
    }
    [Theory] [InlineData("g_00000000000000000000000000000000")] [InlineData("G_12345678901234567890123456789012")]
    [InlineData("g_ABCDEFABCDEFABCDEFABCDEFABCDEFABCD")] [InlineData("../../outside")]
    public void OperationIdentityCannotBeAnArbitraryPath(string value) => Assert.Throws<ArgumentException>(() => new GitOperationId(value));
}
