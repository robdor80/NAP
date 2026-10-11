using System.Reflection;
using Microsoft.Data.Sqlite;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class PreparationIntegrityTests
{
    [Fact]
    public void ExistingValidCatalogIsByteExactAcrossPreparationParkingAndReopening()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var catalog = new AssetCatalog(f.Context); catalog.Initialize(); catalog.CheckIntegrity();
        var bytes = File.ReadAllBytes(f.Roots.CatalogPath); f.FreezeForeign();
        var good = f.Admit(owner); Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, good.ItemId!).Code);
        var bad = f.Admit(owner, f.Zip("bad.zip", asset: "portrait_other_001", invalidPng: true));
        Assert.Equal(PreparationCode.InvalidPackage, f.Service.Prepare(owner, bad.ItemId!).Code);
        Assert.Equal(PreparationCode.Planned, new DurablePreparationService(f.Context, f.Queue).Prepare(owner, good.ItemId!).Code);
        Assert.Equal(bytes, File.ReadAllBytes(f.Roots.CatalogPath)); catalog.CheckIntegrity(); f.AssertUntouched();
    }
    private static ExceptionalPreparationPreview Approve(PreparationTestFixture f, AutomationQueueOwner owner, QueueItemId item,
        ProfileNormalizationPolicy? parameters = null)
    {
        var preview = f.Service.ProposeException(owner, item, f.ExceptionalRule(40, 50), parameters ?? f.Normalization());
        var decision = f.Queue.Decisions(item).Single();
        f.Queue.RecordDecision(owner, f.Queue.Get(item).Revision, decision with { Version = 2, State = ExceptionalDecisionState.Approved,
            Actor = "synthetic_human", Utc = DateTimeOffset.UtcNow }); return preview;
    }
    [Fact]
    public void HumanApprovedCanvasAtExactThreePercentPreservesEveryOriginalPixelAndByte()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var zip = f.Zip(width: 412, height: 500);
        var originalZip = File.ReadAllBytes(zip); var admitted = f.Admit(owner, zip); f.Service.Prepare(owner, admitted.ItemId!);
        var preview = Approve(f, owner, admitted.ItemId!); Assert.Equal(3m, preview.Geometry.AddedPercent);
        var result = f.Service.ResumeException(owner, admitted.ItemId!, preview.DecisionId); Assert.Equal(PreparationCode.Planned, result.Code);
        var record = f.Service.State.Get(result.ItemId)!; var master = "portrait_example_001.png";
        var source = Path.Combine(f.Roots.StateRoot, record.Originals!.RelativeDirectory, "package", "portrait_example_001", master);
        var candidate = Path.Combine(f.Roots.StateRoot, record.Candidate!.RelativeDirectory, "package", "portrait_example_001", master);
        Assert.Equal(PreparationTestFixture.Png(412, 500, 1), File.ReadAllBytes(source)); Assert.Equal(originalZip, File.ReadAllBytes(zip));
        using var input = Image.Load<Rgba32>(source); using var derived = Image.Load<Rgba32>(candidate);
        Assert.Equal(412, derived.Width); Assert.Equal(515, derived.Height);
        for (var y = 0; y < input.Height; y++) for (var x = 0; x < input.Width; x++)
            Assert.Equal(input[x, y], derived[x + preview.Geometry.Left, y + preview.Geometry.Top]);
        Assert.Equal(PreparationCode.Planned, new DurablePreparationService(f.Context, f.Queue).Prepare(owner, result.ItemId).Code);
        Assert.Equal(NormalizationAuthorityKind.HumanDecision, record.Authority!.Kind); f.AssertUntouched();
    }
    [Theory] [InlineData(299, 300)] [InlineData(300, 299)]
    public void AreaAndAxisLimitsAreIndependentAndCannotRoundUp(int area, int axis)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner, f.Zip(width: 412, height: 500));
        f.Service.Prepare(owner, a.ItemId!);
        var error = Assert.Throws<ImageNormalizationException>(() => Approve(f, owner, a.ItemId!, f.Normalization(area, axis)));
        Assert.Equal("normalization_growth_limit", error.Code); Assert.Null(f.Service.State.Get(a.ItemId!)!.Candidate);
        Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State); f.AssertUntouched();
    }
    [Theory] [InlineData("input")] [InlineData("canvas")] [InlineData("file")] [InlineData("memory")] [InlineData("entries")]
    public void IndependentResourceLimitsRejectPreviewWithoutDerivative(string boundary)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner, f.Zip(width: 39));
        f.Service.Prepare(owner, a.ItemId!); var p = f.Normalization();
        p = boundary switch { "input" => p with { Limits = p.Limits with { MaxInputPixels = 1000 } },
            "canvas" => p with { Limits = p.Limits with { MaxCanvasPixels = 1999 } },
            "file" => p with { Limits = p.Limits with { MaxFileBytes = 1 } },
            "entries" => p with { Limits = p.Limits with { MaxPackageEntries = 2 } }, _ => p with { MaxMemoryBytes = 1 } };
        Assert.Throws<ImageNormalizationException>(() => Approve(f, owner, a.ItemId!, p));
        Assert.Null(f.Service.State.Get(a.ItemId!)!.Candidate); Assert.Empty(f.Queue.Decisions(a.ItemId!)); f.AssertUntouched();
    }
    [Theory] [InlineData("candidate-intent", true)] [InlineData("candidate-temporary", false)]
    [InlineData("candidate-before-publish", false)] [InlineData("candidate-published", true)] [InlineData("candidate-ready", true)]
    public void HumanDerivationCrashRequiresBoundIntentionAndNeverAdoptsTemporary(string stage, bool recoverable)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner, f.Zip(width: 39));
        f.Service.Prepare(owner, a.ItemId!); var preview = Approve(f, owner, a.ItemId!);
        PreparationTestFixture.Hook(f.Service, s => { if (s == stage) throw new PreparationTestFixture.SimulatedCrash(); });
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Service.ResumeException(owner, a.ItemId!, preview.DecisionId));
        var result = new DurablePreparationService(f.Context, f.Queue).Prepare(owner, a.ItemId!);
        Assert.Equal(recoverable ? PreparationCode.Planned : PreparationCode.EvidenceChanged, result.Code);
        Assert.Equal(a.JobId, result.JobId); Assert.Equal(1, f.Queue.Get(a.ItemId!).AttemptCount); f.AssertUntouched();
    }
    [Fact]
    public void RevocationAfterPlanningInvalidatesHumanAuthorityButPreservesHistoricalLineage()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner, f.Zip(width: 39));
        f.Service.Prepare(owner, a.ItemId!); var preview = Approve(f, owner, a.ItemId!);
        Assert.Equal(PreparationCode.Planned, f.Service.ResumeException(owner, a.ItemId!, preview.DecisionId).Code);
        f.Service.RevokeException(owner, a.ItemId!, preview.DecisionId, "synthetic_human");
        Assert.Equal(PreparationCode.PolicyInactive, new DurablePreparationService(f.Context, f.Queue).Prepare(owner, a.ItemId!).Code);
        Assert.Equal(2, f.Queue.Lineage(a.ItemId!).Count); f.AssertUntouched();
    }
    [Theory] [InlineData("original")] [InlineData("candidate")] [InlineData("unknown")]
    public void ModifiedOrUnknownPreservedArtifactsNeverBecomeValidatedEvidence(string kind)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner, f.Zip(width: 39));
        f.Service.Prepare(owner, a.ItemId!); var preview = Approve(f, owner, a.ItemId!); f.Service.ResumeException(owner, a.ItemId!, preview.DecisionId);
        var record = f.Service.State.Get(a.ItemId!)!;
        var directory = Path.Combine(f.Roots.StateRoot, kind == "candidate" ? record.Candidate!.RelativeDirectory : record.Originals!.RelativeDirectory);
        var path = kind == "unknown" ? Path.Combine(directory, "unowned.txt") : Path.Combine(directory, "package", "portrait_example_001", "portrait_example_001_manifest.json");
        File.AppendAllText(path, "tamper");
        Assert.Equal(PreparationCode.EvidenceChanged, new DurablePreparationService(f.Context, f.Queue).Prepare(owner, a.ItemId!).Code); f.AssertUntouched();
    }
    [Fact]
    public void LedgerRollbackAndCompareAndSwapRetainExactHistory()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner); f.Service.Prepare(owner, a.ItemId!);
        var record = f.Service.State.Get(a.ItemId!)!;
        typeof(PreparationStateStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Service.State,
            (Action<string>)(_ => throw new PreparationTestFixture.SimulatedCrash()));
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Service.State.Save(owner, record with { Code = PreparationCode.Interrupted }, record.Revision));
        var reopened = new PreparationStateStore(f.Roots); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(record), System.Text.Json.JsonSerializer.Serialize(reopened.Get(a.ItemId!)));
        Assert.Equal(AutomationError.RevisionConflict, Assert.Throws<AutomationException>(() => reopened.Save(owner, record, record.Revision - 1)).Code);
        Assert.Equal(AutomationError.InvalidContract, Assert.Throws<AutomationException>(() => reopened.Save(owner, record with { Checkpoint = PreparationCheckpoint.Intent }, record.Revision)).Code);
        f.AssertUntouched();
    }
    [Fact]
    public void RejectedInitialIntentionIsParkedWithoutInventingRecordRevisionAndOtherItemsContinue()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner);
        var hook = typeof(PreparationStateStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hook.SetValue(f.Service.State, (Action<string>)(_ => throw new AutomationException(AutomationError.InvalidContract, "Synthetic rejected intention")));
        Assert.Equal(PreparationCode.InvalidPackage, f.Service.Prepare(owner, a.ItemId!).Code);
        Assert.Null(f.Service.State.Get(a.ItemId!)); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State);
        Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(a.JobId!).State);
        hook.SetValue(f.Service.State, null);
        var b = f.Admit(owner, f.Zip("next.zip", asset: "portrait_next_001", seed: 2));
        Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, b.ItemId!).Code); f.AssertUntouched();
    }
    [Theory] [InlineData("indexed")] [InlineData("history")] [InlineData("enum")] [InlineData("sidecar")]
    public void SharedLedgerTamperingBlocksScopeInsteadOfParkingIndividualItem(string corruption)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner); f.Service.Prepare(owner, a.ItemId!);
        if (corruption == "sidecar") File.WriteAllText(f.Service.State.DatabasePath + "-journal", "unknown");
        else
        {
            using var db = new SqliteConnection("Data Source=" + f.Service.State.DatabasePath + ";Pooling=False"); db.Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = corruption switch { "indexed" => "UPDATE workflows SET job='job_foreign'",
                "history" => "DELETE FROM history WHERE revision=0", _ => "UPDATE workflows SET payload=replace(payload,'Planned','unknown_state')" }; cmd.ExecuteNonQuery();
        }
        Assert.Equal(AutomationError.CorruptStore, Assert.Throws<AutomationException>(() => new DurablePreparationService(f.Context, f.Queue).Prepare(owner, a.ItemId!)).Code);
        f.AssertUntouched();
    }
}
