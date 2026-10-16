using System.Security.Cryptography;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationDesktopTests
{
    [Fact]
    public async Task RemoteGitCommitAndPushUseNativeServerPlanAndExplicitLocalBareFixture()
    {
        using var f = new AutomationHostTests.Fixture(); var c = f.A.Context; var path = f.A.Zip("portrait_example_001.zip");
        var remote = Path.Combine(f.Root, "remote.git");
        GitProductionFixture.Run(c.Storage.ProductionRoot, "init", "--initial-branch=main");
        GitProductionFixture.Run(c.Storage.ProductionRoot, "config", "user.name", "NAP synthetic");
        GitProductionFixture.Run(c.Storage.ProductionRoot, "config", "user.email", "nap@example.invalid");
        GitProductionFixture.Run(c.Storage.ProductionRoot, "add", "--", "foreign.txt"); GitProductionFixture.Run(c.Storage.ProductionRoot, "commit", "-m", "synthetic base");
        GitProductionFixture.Run(f.Root, "init", "--bare", "--initial-branch=main", remote);
        GitProductionFixture.Run(c.Storage.ProductionRoot, "remote", "add", "origin", remote); GitProductionFixture.Run(c.Storage.ProductionRoot, "push", "-u", "origin", "main");
        var git = new GitProductionService(); var inspector = new GitProductionInspector(); GitProductionFixture.Enable(git); GitProductionFixture.Enable(inspector);
        var auditor = new DurableExecutionTests.AuditClient();
        await using var host = new AutomationHost(f.Control, [c], f.Options, auditor); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [c], Path.Combine(f.Root, "desktop-requests"), auditor, git, inspector);
        var connection = new AutomationConnection("nap-git-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync); using var proxy = new RemoteDesktopService(new(connection), true);
        var prepared = await proxy.PrepareAsync(c, new(Path.GetFileName(path), path), CancellationToken.None);
        var audit = await proxy.AuditAsync(c, prepared, CancellationToken.None); var production = await proxy.ExecuteAsync(c, prepared, audit, CancellationToken.None);
        var status = await proxy.InspectGitAsync(c, CancellationToken.None); Assert.False(status.IsClean);
        var plan = await proxy.PlanCommitAsync(c, [production], "Synthetic explicit publish", CancellationToken.None);
        Assert.NotEmpty(plan.Selection.Paths); Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(plan));
        var observer = typeof(GitProductionService).GetProperty("Observer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        observer.SetValue(git, (Action<string>)(point => { if (point == "prepared") throw new InvalidOperationException("synthetic manual interruption"); }));
        var interrupted = await Assert.ThrowsAsync<AutomationRemoteOperationException>(() => proxy.CommitAsync(c, plan, CancellationToken.None));
        Assert.Contains(interrupted.Issues.Issues, i => i.Code == NapIssueCodes.GitIoFailed);
        observer.SetValue(git, null);
        var pending = Assert.Single(await proxy.ReceiptsAsync(c, CancellationToken.None)); Assert.Equal(GitReceiptState.Prepared, pending.State);
        Assert.Equal(GitReceiptState.Prepared, (await proxy.RecoverAsync(c, pending.OperationId, CancellationToken.None)).State);
        var commit = await proxy.RetryCommitAsync(c, pending.OperationId, CancellationToken.None); Assert.NotEmpty(commit.CommitSha);
        var receipts = await proxy.ReceiptsAsync(c, CancellationToken.None); Assert.Single(receipts); Assert.Equal(commit.OperationId, receipts.Single().OperationId);
        var pushed = await proxy.PushAsync(c, commit.OperationId, CancellationToken.None); Assert.Equal(GitPushOutcome.Pushed, pushed.Outcome);
        Assert.Equal(commit.CommitSha, (await proxy.RecoverAsync(c, commit.OperationId, CancellationToken.None)).CommitSha);
        Assert.Equal("2", GitProductionFixture.Run(c.Storage.ProductionRoot, "rev-list", "--count", "HEAD"));
        await proxy.BackupAsync(c, BackupKind.RepositorySnapshot, CancellationToken.None);
        await proxy.BackupAsync(c, BackupKind.GitBundle, CancellationToken.None);
        var history = await proxy.BackupsAsync(c, CancellationToken.None);
        Assert.Contains(history.Entries, e => e.Kind == BackupKind.RepositorySnapshot); Assert.Contains(history.Entries, e => e.Kind == BackupKind.GitBundle);
    }

    [Fact]
    public async Task RemoteNormalizationKeepsPreviewAndDecisionBoundToServerProposal()
    {
        using var f = new AutomationHostTests.Fixture(); var c = f.A.Context; var path = f.A.Zip("portrait_example_001.zip", width: 39);
        await using var host = new AutomationHost(f.Control, [c], f.Options); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [c], Path.Combine(f.Root, "desktop-requests"));
        var connection = new AutomationConnection("nap-normalization-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync); using var proxy = new RemoteDesktopService(new(connection), false);
        var received = File.ReadAllBytes(path);
        var proposal = await proxy.PrepareAsync(c, new(Path.GetFileName(path), path), null,
            new ImageNormalizationPolicy { MaxAddedAreaBasisPoints = 500, MaxAxisGrowthBasisPoints = 500 }, CancellationToken.None);
        Assert.Equal(40, proposal.Preview.Geometry.CanvasWidth); Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(proposal));
        var decision = await proxy.DecideAsync(c, proposal, new(proposal.OperationId, proposal.EvidenceSha256, true), CancellationToken.None);
        Assert.Equal("Approved", decision.Receipt.Decision); Assert.Equal(received, File.ReadAllBytes(path));
        Assert.Single((await proxy.ReadAsync(c, CancellationToken.None)).Recorded);
    }

    [Fact]
    public async Task RemoteManualPipelineKeepsNativeAuthorityInHostAndPreservesBackupFunctions()
    {
        using var f = new AutomationHostTests.Fixture(); var path = f.A.Zip("portrait_example_001.zip"); var audit = new DurableExecutionTests.AuditClient();
        await using var host = new AutomationHost(f.Control, [f.A.Context], f.Options, audit); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [f.A.Context], Path.Combine(f.Root, "desktop-requests"), audit);
        var connection = new AutomationConnection("nap-desktop-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection, bridge.HandleAsync);
        using var proxy = new RemoteDesktopService(new(connection), true); var c = f.A.Context;
        await Assert.ThrowsAsync<AutomationException>(() => new ProfessionalUiService(audit).PrepareAsync(c, new(Path.GetFileName(path), path), CancellationToken.None));
        var prepared = await proxy.PrepareAsync(c, new(Path.GetFileName(path), path), CancellationToken.None);
        Assert.Contains("portrait_example_001", prepared.Summary);
        var report = await proxy.AuditAsync(c, prepared, CancellationToken.None); Assert.True(report.Passed);
        var result = await proxy.ExecuteAsync(c, prepared, report, CancellationToken.None);
        Assert.Equal("portrait_example_001", result.AssetKey.AssetId);
        Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(result));
        var page = await proxy.PageAsync(c, new(), 0, 10, CancellationToken.None); Assert.Equal(1, page.TotalCount);
        var thumbnail = await proxy.ThumbnailAsync(c, page.Items.Single(), CancellationToken.None);
        Assert.True(File.Exists(thumbnail.CachePath));
        var cached = await proxy.ThumbnailAsync(c, page.Items.Single(), CancellationToken.None);
        Assert.Equal(thumbnail.CachePath, cached.CachePath); Assert.True(cached.CacheHit);
        var detail = await proxy.DetailAsync(c, result.AssetKey.AssetId, CancellationToken.None); Assert.NotNull(detail); Assert.NotEmpty(detail.Documents);
        await proxy.SaveObjectiveAsync(c, new(c.Id, "synthetic_objective", "Synthetic target", new(), 3), CancellationToken.None);
        var planning = await proxy.PlanningAsync(c, CancellationToken.None); Assert.Single(planning.Objectives);
        var backup = await proxy.BackupAsync(c, BackupKind.Database, CancellationToken.None);
        Assert.Equal(backup.BackupId, Assert.Single((await proxy.BackupsAsync(c, CancellationToken.None)).Entries).BackupId);
        var restore = await proxy.PlanRestoreAsync(c, backup.BackupId, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => AutomationReadViews.RequireNative(restore));
        Assert.True((await proxy.RestoreAsync(c, restore, CancellationToken.None)).Restored);
        var newest = await proxy.BackupAsync(c, BackupKind.Database, CancellationToken.None);
        var retention = await proxy.PlanRetentionAsync(c, new(1), CancellationToken.None);
        Assert.NotEmpty((await proxy.RetainAsync(c, retention, CancellationToken.None)).DeletedBackupIds);
        Assert.Equal(newest.BackupId, Assert.Single((await proxy.BackupsAsync(c, CancellationToken.None)).Entries).BackupId);
        Assert.Equal(1, (await proxy.StatisticsAsync(c, new(), CancellationToken.None)).TotalAssets);
        Assert.NotNull(await proxy.DashboardAsync(c, CancellationToken.None));
        proxy.Dispose(); if (OperatingSystem.IsWindows()) Assert.False(File.Exists(thumbnail.CachePath));
        Assert.Equal(1, audit.Calls);
    }

    [Fact]
    public async Task RequestReplayDoesNotCreateSecondManualJobAndCrossUniverseReferenceIsRejected()
    {
        using var f = new AutomationHostTests.Fixture(); var path = f.A.Zip("portrait_example_001.zip");
        await using var host = new AutomationHost(f.Control, [f.A.Context, f.B.Context], f.Options); await host.Started;
        var bridge = new AutomationDesktopBridge(host, [f.A.Context, f.B.Context], Path.Combine(f.Root, "desktop-requests"));
        var id = "command_" + Guid.NewGuid().ToString("N");
        var request = new AutomationRequest(1, id, AutomationOperation.Prepare, f.A.Context.Id.Value, AutomationProtocol.Payload(new AutomationPrepare(Path.GetFileName(path))));
        var first = await bridge.HandleAsync(request, "synthetic_principal"); var second = await bridge.HandleAsync(request, "synthetic_principal");
        Assert.Equal(first.GetRawText(), second.GetRawText());
        var handle = AutomationProtocol.Read<AutomationReadResult>(first).Reference!;
        await Assert.ThrowsAsync<AutomationException>(() => bridge.HandleAsync(new(1, "command_" + Guid.NewGuid().ToString("N"), AutomationOperation.Audit,
            f.B.Context.Id.Value, AutomationProtocol.Payload(new AutomationReference(handle))), "synthetic_principal"));
        Assert.Single(Directory.EnumerateFiles(f.A.Roots.StateRoot, "job_*.json"));
    }
}
