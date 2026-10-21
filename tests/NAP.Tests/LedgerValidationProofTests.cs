using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class LedgerValidationProofTests
{
    [Theory]
    [InlineData(false, "index")] [InlineData(true, "index")]
    [InlineData(false, "history")] [InlineData(true, "history")]
    [InlineData(false, "domain")] [InlineData(true, "domain")]
    public void WarmProofRejectsChangedBytesEvenWithIdenticalLengthTimestampAndSelfConsistentAnchors(bool execution, string corruption)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        DurableExecutionTests.Enable(f, owner); var item = f.Admit(owner);
        var execute = new DurableExecutionService(f.Context, f.Queue);
        if (execution)
        {
            Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, item.ItemId!, new DurableExecutionTests.AuditClient()).Code);
            Assert.Equal(DurableExecutionCode.Completed, execute.Execute(owner, item.ItemId!).Code);
        }
        else Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, item.ItemId!).Code);
        var database = execution ? execute.State.DatabasePath : f.Service.State.DatabasePath;
        void Read() { if (execution) _ = execute.State.Get(item.ItemId!); else _ = f.Service.State.Get(item.ItemId!); }
        void ReadFreshStore()
        {
            if (execution) _ = new ExecutionStateStore(f.Roots).Get(item.ItemId!);
            else _ = new PreparationStateStore(f.Roots).Get(item.ItemId!);
        }
        // Reserve free pages before warming the proof so SQL cell relocation during the
        // later fault injection cannot grow the file and accidentally make size a signal.
        using (var db = Open(database)) Execute(db,
            "CREATE TABLE fault_reserve(bytes BLOB); INSERT INTO fault_reserve VALUES(zeroblob(262144)); DROP TABLE fault_reserve;");
        Read(); Read(); // exercise both a validated ledger and its unchanged-byte fast path
        ReadFreshStore(); ReadFreshStore(); // complete-byte proofs shared by separate store instances
        var originalLength = new FileInfo(database).Length; var originalStamp = File.GetLastWriteTimeUtc(database);
        var outside = DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot);
        using (var db = Open(database))
        {
            if (corruption == "index") Execute(db, "UPDATE workflows SET job=$j WHERE item=$i", ("$j", JobId.Create().Value), ("$i", item.ItemId!.Value));
            else if (corruption == "history") Execute(db,
                "UPDATE history SET previous_hash=$h WHERE item=$i AND revision=(SELECT max(revision) FROM history WHERE item=$i)",
                ("$h", new string('0', 64)), ("$i", item.ItemId!.Value));
            else
            {
                // Recompute the anchors: rejection must include domain validation, not only a stale checksum.
                var json = execution
                    ? JsonSerializer.Serialize(execute.State.Get(item.ItemId!)! with { Receipts = execute.State.Get(item.ItemId!)!.Receipts.Where(r => r != "catalog-verified").ToArray() }, AutomationProtocol.Json)
                    : JsonSerializer.Serialize(f.Service.State.Get(item.ItemId!)! with { AuditState = DurableAuditState.Pass }, AutomationProtocol.Json);
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
                Execute(db, "UPDATE workflows SET payload=$p,hash=$h WHERE item=$i", ("$p", json), ("$h", digest), ("$i", item.ItemId!.Value));
                Execute(db, "UPDATE history SET payload=$p,hash=$h WHERE item=$i AND revision=(SELECT max(revision) FROM history WHERE item=$i)",
                    ("$p", json), ("$h", digest), ("$i", item.ItemId!.Value));
            }
        }
        Assert.Equal(originalLength, new FileInfo(database).Length);
        File.SetLastWriteTimeUtc(database, originalStamp); Assert.Equal(originalStamp, File.GetLastWriteTimeUtc(database));
        Assert.Throws<AutomationException>(Read); Assert.Throws<AutomationException>(Read);
        Assert.Throws<AutomationException>(ReadFreshStore); Assert.Throws<AutomationException>(ReadFreshStore);
        Assert.Equal(outside, DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot));
    }

    [Fact]
    public void FreshQueueStoreRejectsChangedIndexedFieldsAfterWarmingTheSharedByteProof()
    {
        using var f = new PreparationTestFixture();
        QueueItemId id;
        using (var owner = f.Queue.AcquireOwner()) id = f.Admit(owner).ItemId!;
        _ = f.Queue.List(); _ = new AutomationQueueStore(f.Roots).List();
        var length = new FileInfo(f.Queue.DatabasePath).Length; var stamp = File.GetLastWriteTimeUtc(f.Queue.DatabasePath);
        var outside = DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot);
        using (var db = Open(f.Queue.DatabasePath)) Execute(db, "UPDATE items SET revision=revision+1 WHERE id=$i", ("$i", id.Value));
        Assert.Equal(length, new FileInfo(f.Queue.DatabasePath).Length); File.SetLastWriteTimeUtc(f.Queue.DatabasePath, stamp);
        Assert.Throws<AutomationException>(() => new AutomationQueueStore(f.Roots).Get(id));
        Assert.Throws<AutomationException>(() => f.Queue.Get(id));
        Assert.Equal(outside, DurableExecutionTests.Inventory(f.Roots.InboxRoot, f.Roots.ArchiveRoot, f.Roots.ProductionRoot));
    }

    [Fact]
    public void AValidatedWorkflowCannotBeReboundToAnotherUniverseThroughMetadata()
    {
        using var a = new PreparationTestFixture("alpha_universe"); using var b = new PreparationTestFixture("beta_universe");
        QueueItemId item;
        using (var owner = a.Queue.AcquireOwner())
        { a.Policy(owner, authorizePreparation: true); item = a.Admit(owner).ItemId!; Assert.Equal(PreparationCode.Planned, a.Service.Prepare(owner, item).Code); }
        _ = a.Service.State.Get(item);
        using (var owner = b.Queue.AcquireOwner()) b.Policy(owner, authorizePreparation: true);
        File.Copy(a.Service.State.DatabasePath, b.Service.State.DatabasePath, overwrite: true);
        using (var db = Open(b.Service.State.DatabasePath)) Execute(db, "UPDATE metadata SET roots=$r WHERE id=1", ("$r", JsonSerializer.Serialize(b.Roots, AutomationProtocol.Json)));
        Assert.Throws<AutomationException>(() => b.Service.State.Get(item));
        Assert.Equal(item, a.Service.State.Get(item)!.ItemId);
    }
    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); db.Open(); return db;
    }
    private static void Execute(SqliteConnection db, string sql, params (string Name, object Value)[] parameters)
    { using var cmd = db.CreateCommand(); cmd.CommandText = sql; foreach (var parameter in parameters) cmd.Parameters.AddWithValue(parameter.Name, parameter.Value); cmd.ExecuteNonQuery(); }
}
