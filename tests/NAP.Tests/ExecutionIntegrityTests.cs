using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ExecutionIntegrityTests
{
    [Theory]
    [InlineData("receipt")] [InlineData("audit-control")] [InlineData("original")] [InlineData("original-zip")]
    [InlineData("candidate")] [InlineData("candidate-zip")] [InlineData("profile")]
    [InlineData("policy-version")] [InlineData("reservation")]
    public void ChangedEvidenceNeverWritesProductiveRoots(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var p = DurableExecutionTests.Enable(f, owner, true); var a = f.Admit(owner, f.Zip(width: 39));
        f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var r = f.Service.State.Get(a.ItemId!)!; var context = f.Context;
        string Resolve(PreparationBundle b, string relative) => Path.Combine(f.Roots.StateRoot, b.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar), relative);
        if (kind == "receipt") File.AppendAllText(Resolve(r.AuditResponse!, "report.json"), " ");
        if (kind == "audit-control") File.AppendAllText(Resolve(r.AuditResponse!, "control.json"), " ");
        if (kind == "original") File.AppendAllText(Resolve(r.Originals!, "package/portrait_example_001/portrait_example_001_brief.txt"), "changed");
        if (kind == "original-zip") File.AppendAllText(Resolve(r.Originals!, "received.zip"), "changed");
        if (kind == "candidate") File.AppendAllText(Resolve(r.Candidate!, "package/portrait_example_001/portrait_example_001.png"), "changed");
        if (kind == "candidate-zip") File.AppendAllText(Resolve(r.Candidate!, "candidate.zip"), "changed");
        if (kind == "profile")
        {
            var snapshot = f.ExceptionalRule(80, 100);
            context = new(UniverseProfileLoader.Load(new MemoryStream(Encoding.UTF8.GetBytes(snapshot.ProfileJson))), f.Roots);
        }
        if (kind == "policy-version") f.Queue.SavePolicy(owner, p.Definition with { Version = 2 });
        if (kind == "reservation")
        {
            f.Queue.Transition(owner, a.ItemId!, f.Queue.Get(a.ItemId!).Revision, QueueState.NeedsReview, QueueIncident.InvalidEvidence);
            f.Queue.ReleaseReservation(owner, a.ItemId!, f.Queue.Get(a.ItemId!).Revision);
            f.Queue.Transition(owner, a.ItemId!, f.Queue.Get(a.ItemId!).Revision, QueueState.Queued);
        }
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot);
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(context, f.Queue).Execute(owner, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot));
        Assert.False(File.Exists(f.Roots.CatalogPath)); Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(a.JobId!).State);
    }

    [Theory] [InlineData("archive")] [InlineData("production")]
    public void ForeignIdenticalFilesAreNeverAdopted(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var record = f.Service.State.Get(a.ItemId!)!;
        var target = Path.Combine(kind == "archive" ? f.Roots.ArchiveRoot : f.Roots.ProductionRoot, "assets", "portrait_example_001");
        Directory.CreateDirectory(target);
        var source = Path.Combine(f.Roots.StateRoot, record.Originals!.RelativeDirectory, "package", "portrait_example_001");
        foreach (var path in Directory.EnumerateFiles(source)) File.Copy(path, Path.Combine(target, Path.GetFileName(path)));
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot);
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot));
        Assert.False(File.Exists(f.Roots.CatalogPath));
    }

    [Theory] [InlineData(AutomationPolicyState.Paused)] [InlineData(AutomationPolicyState.Revoked)]
    public void RevocationAtPublishBoundaryStopsBeforeRename(AutomationPolicyState state)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var p = DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var service = new DurableExecutionService(f.Context, f.Queue); bool hit = false;
        DurableExecutionTests.Hook(service, step =>
        {
            if (step != "before:archive-publish:portrait_example_001_brief.txt") return;
            hit = true; f.Queue.SetPolicyState(owner, p.Reference, p.Revision, state);
        });
        Assert.Equal(DurableExecutionCode.NeedsReview, service.Execute(owner, a.ItemId!).Code); Assert.True(hit);
        Assert.False(File.Exists(Path.Combine(f.Roots.ArchiveRoot, "assets", "portrait_example_001", "portrait_example_001_brief.txt")));
        Assert.Empty(Directory.EnumerateFiles(f.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories)); Assert.False(File.Exists(f.Roots.CatalogPath));
        Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State);
    }

    [Theory] [InlineData("queue")] [InlineData("preparation")] [InlineData("execution")] [InlineData("history")] [InlineData("sidecar")]
    public void SharedCorruptionBlocksWithoutChangingProductiveRoots(string kind)
    {
        using var f = new PreparationTestFixture(); InboxAdmissionResult a; string executionPath;
        using (var owner = f.Queue.AcquireOwner())
        {
            DurableExecutionTests.Enable(f, owner); a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
            var service = new DurableExecutionService(f.Context, f.Queue); executionPath = service.State.DatabasePath;
            DurableExecutionTests.Hook(service, s => { if (s == "execution-intent") throw new PreparationTestFixture.SimulatedCrash(); });
            Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => service.Execute(owner, a.ItemId!));
        }
        var path = kind switch { "queue" => f.Queue.DatabasePath, "preparation" => f.Service.State.DatabasePath, _ => executionPath };
        if (kind == "history")
        {
            using var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"); db.Open(); using var command = db.CreateCommand();
            command.CommandText = "UPDATE history SET hash='broken'"; command.ExecuteNonQuery();
        }
        else if (kind == "sidecar") File.WriteAllText(path + "-journal", "ambiguous");
        else File.WriteAllText(path, "corrupt synthetic database");
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot);
        Assert.Throws<AutomationException>(() =>
        {
            var queue = new AutomationQueueStore(f.Roots); using var owner = queue.AcquireOwner();
            new DurableExecutionService(f.Context, queue).Execute(owner, a.ItemId!);
        });
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot)); Assert.False(File.Exists(f.Roots.CatalogPath));
    }

    [Fact]
    public void SameAssetIdInTwoUniversesIsIsolated()
    {
        using var first = new PreparationTestFixture("first"); using var second = new PreparationTestFixture("second");
        foreach (var f in new[] { first, second })
        {
            using var owner = f.Queue.AcquireOwner(); DurableExecutionTests.Enable(f, owner); var a = f.Admit(owner);
            f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
            var other = f == first ? second : first;
            var before = DurableExecutionTests.Inventory(other.Roots.ArchiveRoot, other.Roots.ProductionRoot, other.Roots.InboxRoot, other.Roots.CatalogPath);
            Assert.Equal(DurableExecutionCode.Completed, new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!).Code);
            Assert.Equal(before, DurableExecutionTests.Inventory(other.Roots.ArchiveRoot, other.Roots.ProductionRoot, other.Roots.InboxRoot, other.Roots.CatalogPath));
            Assert.Equal(f.Context.Id, new AssetCatalog(f.Context).Query().Single().AssetKey.UniverseId);
        }
    }

    [Theory] [InlineData("archive")] [InlineData("production")]
    public void LinkedSharedRootBlocksWithoutTouchingItsTarget(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        var root = kind == "archive" ? f.Roots.ArchiveRoot : f.Roots.ProductionRoot;
        var target = Path.Combine(f.Root, "synthetic-link-target"); Directory.Move(root, target);
        ArchiveTestFixture.Junction(root, target);
        try
        {
            var before = DurableExecutionTests.Inventory(target, f.Roots.InboxRoot);
            Assert.Equal(AutomationError.UnsafePath, Assert.Throws<AutomationException>(() =>
                new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!)).Code);
            Assert.Equal(before, DurableExecutionTests.Inventory(target, f.Roots.InboxRoot)); Assert.False(File.Exists(f.Roots.CatalogPath));
        }
        finally { Directory.Delete(root); } // Unlink only; never recurse through a junction during fixture disposal.
    }

    [Theory] [InlineData("catalog")] [InlineData("index")]
    public void CorruptSharedIndexOrCatalogBlocksBeforeAnyPublication(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); DurableExecutionTests.Enable(f, owner);
        var a = f.Admit(owner); f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient());
        if (kind == "catalog") { new AssetCatalog(f.Context).Initialize(); File.WriteAllText(f.Roots.CatalogPath, "synthetic corrupt catalog"); }
        else
        {
            var index = new ArchiveMasterIndexStore(f.Context).IndexPath; Directory.CreateDirectory(Path.GetDirectoryName(index)!);
            File.WriteAllText(index, "synthetic corrupt index");
        }
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath, f.Roots.InboxRoot);
        Assert.Throws<AutomationException>(() => new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!));
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.CatalogPath, f.Roots.InboxRoot));
    }

    [Fact]
    public void ConversionCannotExceedTheAuthorizedMemoryBudget()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var prior = f.Policy(owner, operations: StoredAutomationPolicy.ExecutionOperations);
        var policy = f.Queue.SavePolicy(owner, prior.Definition with { Version = 2,
            Resources = prior.Definition.Resources with { MaxMemoryBytes = 20L * 1024 * 1024 } });
        policy = f.Queue.RecordAuthorization(owner, policy.Reference, policy.Revision,
            new("synthetic_small_budget", f.Context.Id, policy.Reference, "synthetic_responsible", DateTimeOffset.UtcNow));
        var grant = f.Service.State.Grant(prior.Reference)!;
        f.Service.State.RegisterGrant(owner, policy, grant with { GrantId = "synthetic_small_budget_terms", Policy = policy.Reference,
            PolicyAuthorizationId = policy.Authorization!.AuthorizationId });
        f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled);
        var a = f.Admit(owner); Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, new DurableExecutionTests.AuditClient()).Code);
        var before = DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot);
        Assert.Equal(DurableExecutionCode.NeedsReview, new DurableExecutionService(f.Context, f.Queue).Execute(owner, a.ItemId!).Code);
        Assert.Equal(before, DurableExecutionTests.Inventory(f.Roots.ArchiveRoot, f.Roots.ProductionRoot, f.Roots.InboxRoot)); Assert.False(File.Exists(f.Roots.CatalogPath));
    }
}
