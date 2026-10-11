using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class DurablePreparationTests
{
    [Theory] [InlineData(40, 50)] [InlineData(80, 100)] [InlineData(160, 200)]
    public void ExactRatioPreservesOriginalAndSameJobThroughPlannedWithoutNormalization(int width, int height)
    {
        using var f = new PreparationTestFixture(); var zip = f.Zip(width: width, height: height); var received = File.ReadAllBytes(zip);
        using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner, zip);
        var result = f.Service.Prepare(owner, admitted.ItemId!); Assert.Equal(PreparationCode.Planned, result.Code);
        Assert.Equal(admitted.JobId, result.JobId); Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(result.JobId).State);
        var record = f.Service.State.Get(result.ItemId)!; Assert.Null(record.Candidate); Assert.Empty(f.Queue.Lineage(result.ItemId));
        var preserved = Path.Combine(f.Roots.StateRoot, "automation", "originals", result.ItemId.Value);
        Assert.Equal(received, File.ReadAllBytes(Path.Combine(preserved, "received.zip"))); Assert.Equal(received, File.ReadAllBytes(zip));
        Assert.NotNull(record.Snapshot); Assert.NotNull(f.Queue.GetReservation(new(f.Context.Id, "portrait_example_001")));
        var reopened = new DurablePreparationService(f.Context, new AutomationQueueStore(f.Roots));
        Assert.Equal(PreparationCode.Planned, reopened.Prepare(owner, result.ItemId).Code); Assert.Equal(1, f.Queue.Get(result.ItemId).AttemptCount);
        Assert.Equal(AutomationPolicyState.Disabled, f.Queue.CurrentPolicy().State); f.AssertUntouched();
    }
    [Theory]
    [InlineData("preparation-intent", true)] [InlineData("original-temporary", false)]
    [InlineData("original-before-publish", false)] [InlineData("original-published", true)]
    [InlineData("original-preserved", true)] [InlineData("journal-validated", true)] [InlineData("journal-planned", true)]
    public void PreparationCrashOnlyResumesBoundPublicationAndJournalIntent(string stage, bool recoverable)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner);
        PreparationTestFixture.Hook(f.Service, s => { if (s == stage) throw new PreparationTestFixture.SimulatedCrash(); });
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Service.Prepare(owner, admitted.ItemId!));
        var reopened = new DurablePreparationService(f.Context, f.Queue);
        var result = reopened.Prepare(owner, admitted.ItemId!); Assert.Equal(admitted.JobId, result.JobId);
        Assert.Equal(recoverable ? PreparationCode.Planned : PreparationCode.EvidenceChanged, result.Code);
        Assert.Equal(1, f.Queue.Get(admitted.ItemId!).AttemptCount); f.AssertUntouched();
    }
    [Fact]
    public void RepackedSemanticDuplicateHasNoSecondReservationOrPlannedProductiveJob()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var a = f.Admit(owner, f.Zip("first.zip")); Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        var b = f.Admit(owner, f.Zip("repacked.zip", timestamp: 2)); Assert.NotEqual(a.ItemId, b.ItemId);
        var duplicate = f.Service.Prepare(owner, b.ItemId!); Assert.Equal(PreparationCode.Duplicate, duplicate.Code); Assert.Equal(a.ItemId, duplicate.DuplicateOf);
        Assert.Equal(QueueState.Duplicate, f.Queue.Get(b.ItemId!).State); Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(b.JobId!).State);
        Assert.Equal(a.ItemId, f.Queue.GetReservation(new(f.Context.Id, "portrait_example_001"))!.ItemId); f.AssertUntouched();
    }
    [Fact]
    public void AssetCollisionIncludesCompanionContentAndMasterDuplicateDefaultsToReview()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var a = f.Admit(owner); Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        var collision = f.Admit(owner, f.Zip("collision.zip", brief: "different"));
        Assert.Equal(PreparationCode.AssetCollision, f.Service.Prepare(owner, collision.ItemId!).Code);
        var duplicateMaster = f.Admit(owner, f.Zip("master.zip", asset: "portrait_other_001"));
        Assert.Equal(PreparationCode.MasterDuplicate, f.Service.Prepare(owner, duplicateMaster.ItemId!).Code);
        Assert.Equal(a.ItemId, f.Queue.GetReservation(new(f.Context.Id, "portrait_example_001"))!.ItemId); f.AssertUntouched();
    }
    [Fact]
    public void PolicyAuthorizationDeclarationDoesNotSilentlyEnableNormalizationOrAudit()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, f.Normalization());
        var admitted = f.Admit(owner, f.Zip(width: 39)); Assert.Equal(PreparationCode.GeometryPolicyRequired, f.Service.Prepare(owner, admitted.ItemId!).Code);
        Assert.Equal(AutomationPolicyState.Disabled, f.Queue.CurrentPolicy().State); Assert.Null(f.Service.State.Get(admitted.ItemId!)!.Candidate); f.AssertUntouched();
    }
    [Fact]
    public void ExistingProductionAndPartialArchiveAreNeverAdoptedOrOverwritten()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var a = f.Admit(owner);
        var occupied = Path.Combine(f.Roots.ProductionRoot, "assets", "portrait_example_001"); Directory.CreateDirectory(occupied); File.WriteAllText(Path.Combine(occupied, "foreign.txt"), "unowned"); f.FreezeForeign();
        Assert.Equal(PreparationCode.ExistingStorage, f.Service.Prepare(owner, a.ItemId!).Code); f.AssertUntouched();
        var b = f.Admit(owner, f.Zip("archive.zip", asset: "portrait_other_001", seed: 2));
        var target = Path.Combine(f.Roots.ArchiveRoot, "assets", "portrait_other_001"); Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "portrait_other_001.png"), PreparationTestFixture.Png(40, 50, 2)); f.FreezeForeign();
        Assert.Equal(PreparationCode.ExistingStorage, f.Service.Prepare(owner, b.ItemId!).Code); f.AssertUntouched();
    }
    [Fact]
    public void InvalidPngAndTamperedAdmissionDoNotPreventAnotherPackage()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var bad = f.Admit(owner, f.Zip("bad.zip", invalidPng: true));
        Assert.Equal(PreparationCode.InvalidPackage, f.Service.Prepare(owner, bad.ItemId!).Code);
        var tampered = f.Admit(owner, f.Zip("tampered.zip", asset: "portrait_other_001"));
        var receipt = new InboxAdmissionService(f.Context, f.Queue).GetReceipt(tampered.ItemId!);
        File.AppendAllText(Path.Combine(f.Roots.StagingRoot, receipt.ExtractionRelativePath.Replace('/', Path.DirectorySeparatorChar), "portrait_other_001_brief.txt"), "tampered");
        Assert.Equal(PreparationCode.EvidenceChanged, f.Service.Prepare(owner, tampered.ItemId!).Code);
        var good = f.Admit(owner, f.Zip("next.zip", asset: "portrait_next_001", seed: 3));
        Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, good.ItemId!).Code); f.AssertUntouched();
    }
    [Fact]
    public void SameAssetAndMasterInDifferentUniversesAreIndependent()
    {
        using var a = new PreparationTestFixture("universe_a"); using var b = new PreparationTestFixture("universe_b");
        using var ao = a.Queue.AcquireOwner(); using var bo = b.Queue.AcquireOwner();
        var aa = a.Admit(ao); var ba = b.Admit(bo);
        Assert.Equal(PreparationCode.Planned, a.Service.Prepare(ao, aa.ItemId!).Code); Assert.Equal(PreparationCode.Planned, b.Service.Prepare(bo, ba.ItemId!).Code);
        Assert.NotEqual(aa.JobId, ba.JobId); Assert.NotEqual(a.Service.State.DatabasePath, b.Service.State.DatabasePath); a.AssertUntouched(); b.AssertUntouched();
    }
    [Fact]
    public void MissingPreservationAndChangedProfileInvalidateRecoveryWithoutRecreatingJournal()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner);
        Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, admitted.ItemId!).Code);
        var changed = UniverseProfileSnapshotRenderer.Context(f.ExceptionalRule(80, 100), f.Roots);
        Assert.Equal(PreparationCode.EvidenceChanged, new DurablePreparationService(changed, f.Queue).Prepare(owner, admitted.ItemId!).Code);
        Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(admitted.JobId!).State); f.AssertUntouched();
    }
    [Fact]
    public void SingleAssetHumanExceptionPersistsPreviewRuleAndAuthorityWithoutChangingGlobalProfile()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner, f.Zip(width: 39, height: 50));
        Assert.Equal(PreparationCode.GeometryPolicyRequired, f.Service.Prepare(owner, admitted.ItemId!).Code);
        var effective = f.ExceptionalRule(78, 100); var parameters = f.Normalization() with { Ratio = new(39, 50) };
        var preview = f.Service.ProposeException(owner, admitted.ItemId!, effective, parameters);
        var decision = f.Queue.Decisions(admitted.ItemId!).Single();
        f.Queue.RecordDecision(owner, f.Queue.Get(admitted.ItemId!).Revision, decision with { Version = 2, State = ExceptionalDecisionState.Approved, Actor = "human_reviewer", Utc = DateTimeOffset.UtcNow });
        var reopened = new DurablePreparationService(f.Context, f.Queue);
        var result = reopened.ResumeException(owner, admitted.ItemId!, preview.DecisionId);
        Assert.Equal(PreparationCode.Planned, result.Code); Assert.Equal(admitted.JobId, result.JobId);
        var record = reopened.State.Get(admitted.ItemId!)!; Assert.Equal(NormalizationAuthorityKind.HumanDecision, record.Authority!.Kind); Assert.Null(record.Authority.Policy);
        Assert.Equal(effective, record.EffectiveProfile); Assert.Equal(40, f.Context.Profile.AssetRules.Single().Conversion!.OutputWidth);
        Assert.Equal(AutomationPolicyState.Disabled, f.Queue.CurrentPolicy().State); Assert.Equal(2, f.Queue.Lineage(admitted.ItemId!).Count); f.AssertUntouched();
    }
    [Fact]
    public void UnapprovedOrRevokedExceptionalDecisionCannotResume()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner, f.Zip(width: 39));
        f.Service.Prepare(owner, admitted.ItemId!); var preview = f.Service.ProposeException(owner, admitted.ItemId!, f.ExceptionalRule(78, 100), f.Normalization() with { Ratio = new(39, 50) });
        Assert.Throws<AutomationException>(() => f.Service.ResumeException(owner, admitted.ItemId!, preview.DecisionId));
        var decision = f.Queue.Decisions(admitted.ItemId!).Single();
        f.Queue.RecordDecision(owner, f.Queue.Get(admitted.ItemId!).Revision, decision with { Version = 2, State = ExceptionalDecisionState.Revoked, Actor = "human_reviewer", Utc = DateTimeOffset.UtcNow });
        Assert.Throws<AutomationException>(() => f.Service.ResumeException(owner, admitted.ItemId!, preview.DecisionId)); Assert.Null(f.Service.State.Get(admitted.ItemId!)!.Candidate); f.AssertUntouched();
    }
    [Fact]
    public void CorruptLedgerFailsClosedAndOwnerAffinityIsEnforced()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var admitted = f.Admit(owner);
        Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, admitted.ItemId!).Code);
        var thread = new Thread(() => Assert.Throws<AutomationException>(() => f.Service.Prepare(owner, admitted.ItemId!))); thread.Start(); thread.Join();
        File.WriteAllText(f.Service.State.DatabasePath, "corrupt database");
        Assert.Equal(AutomationError.CorruptStore, Assert.Throws<AutomationException>(() => f.Service.Prepare(owner, admitted.ItemId!)).Code); f.AssertUntouched();
    }
}
