using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class PreparationPolicyTests
{
    [Fact]
    public void ExplicitPreparationActivationPersistsPauseResumeAndTerminalRevocation()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        Assert.Equal(AutomationPolicyState.Disabled, f.Queue.CurrentPolicy().State);
        Assert.False(f.Queue.CurrentPolicy().IsPreparationAuthorized);
        var policy = f.Policy(owner);
        Assert.False(policy.IsPreparationAuthorized); // authorization declaration is not activation
        policy = f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly);
        var reopened = new AutomationQueueStore(f.Roots);
        Assert.Equal(JsonSerializer.Serialize(policy), JsonSerializer.Serialize(reopened.CurrentPolicy()));
        Assert.True(reopened.CurrentPolicy().IsPreparationAuthorized);
        Assert.False(policy.IsExecutionAuthorized);
        var history = reopened.PolicyHistory(policy.Reference);
        Assert.Equal(new[] { AutomationPolicyState.Disabled, AutomationPolicyState.Disabled, AutomationPolicyState.PreparationOnly }, history.Select(p => p.State));
        policy = f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Paused);
        Assert.False(policy.IsPreparationAuthorized);
        policy = f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly);
        Assert.True(policy.IsPreparationAuthorized);
        policy = f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Revoked);
        Assert.False(new AutomationQueueStore(f.Roots).CurrentPolicy().IsPreparationAuthorized);
        Assert.Equal(AutomationError.IllegalTransition, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly)).Code);
        Assert.Equal(AutomationError.Disabled, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled)).Code);
        f.AssertUntouched();
    }

    [Theory]
    [InlineData(AutomationOperations.Archive)] [InlineData(AutomationOperations.Produce)] [InlineData(AutomationOperations.Register)]
    [InlineData(AutomationOperations.Archive | AutomationOperations.Produce | AutomationOperations.Register)]
    public void PreparationCapabilityRejectsEveryProductivePermissionEvenWithExplicitAuthorization(AutomationOperations forbidden)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var prior = f.Policy(owner);
        var policy = f.Queue.SavePolicy(owner, prior.Definition with { Version = 2, Operations = prior.Definition.Operations | forbidden });
        policy = f.Queue.RecordAuthorization(owner, policy.Reference, policy.Revision,
            new("synthetic_v2", f.Context.Id, policy.Reference, "synthetic_responsible", DateTimeOffset.UtcNow));
        Assert.Equal(AutomationError.PolicyMismatch, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly)).Code);
        Assert.Equal(JsonSerializer.Serialize(policy), JsonSerializer.Serialize(f.Queue.CurrentPolicy())); Assert.False(policy.IsPreparationAuthorized);
        Assert.Equal(2, f.Queue.PolicyHistory(policy.Reference).Count); f.AssertUntouched();
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void ActivationNeedsAuthorizationAndAnAdmittedPreparationScope(bool authorization)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var prior = f.Policy(owner);
        var policy = f.Queue.SavePolicy(owner, prior.Definition with { Version = 2,
            Operations = authorization ? AutomationOperations.Audit : AutomationOperations.Admit | AutomationOperations.Audit });
        if (authorization) policy = f.Queue.RecordAuthorization(owner, policy.Reference, policy.Revision,
            new("synthetic_v2", f.Context.Id, policy.Reference, "synthetic_responsible", DateTimeOffset.UtcNow));
        Assert.Equal(AutomationError.PolicyMismatch, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly)).Code);
        Assert.Equal(AutomationPolicyState.Disabled, f.Queue.CurrentPolicy().State); f.AssertUntouched();
    }

    [Fact]
    public void ANewPolicyVersionNeverInheritsAuthorityAndAnOldVersionCannotBeActivated()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var prior = f.Policy(owner);
        var changed = f.Queue.SavePolicy(owner, prior.Definition with { Version = 2, Audit = prior.Definition.Audit with { MaxCost = 1 } });
        Assert.Null(changed.Authorization); Assert.False(changed.IsPreparationAuthorized);
        Assert.Equal(AutomationError.PolicyMismatch, Assert.Throws<AutomationException>(() =>
            f.Queue.RecordAuthorization(owner, changed.Reference, changed.Revision, prior.Authorization!)).Code);
        Assert.Equal(AutomationError.PolicyMismatch, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, prior.Reference, prior.Revision, AutomationPolicyState.PreparationOnly)).Code);
        Assert.Equal(AutomationError.RevisionConflict, Assert.Throws<AutomationException>(() =>
            f.Queue.SetPolicyState(owner, changed.Reference, changed.Revision + 1, AutomationPolicyState.PreparationOnly)).Code);
        f.AssertUntouched();
    }

    [Theory] [InlineData(AutomationPolicyState.Paused)] [InlineData(AutomationPolicyState.Revoked)]
    public void PausedOrRevokedScopeStopsBeforePreservationOrJournalChanges(AutomationPolicyState state)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner, authorizePreparation: true);
        var a = f.Admit(owner); f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, state);
        Assert.Equal(PreparationCode.PolicyInactive, f.Service.Prepare(owner, a.ItemId!).Code);
        Assert.Null(f.Service.State.Get(a.ItemId!)); Assert.False(Directory.Exists(Path.Combine(f.Roots.StateRoot, "automation", "originals")));
        Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(a.JobId!).State);
        Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State); f.AssertUntouched();
    }

    [Theory] [InlineData(AutomationPolicyState.Paused, false)] [InlineData(AutomationPolicyState.Revoked, false)]
    [InlineData(AutomationPolicyState.Paused, true)] [InlineData(AutomationPolicyState.Revoked, true)]
    public void IndividualDecisionCannotBypassPausedOrRevokedScope(AutomationPolicyState state, bool resume)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner, authorizePreparation: true);
        var a = f.Admit(owner, f.Zip(width: 39));
        Assert.Equal(PreparationCode.GeometryPolicyRequired, f.Service.Prepare(owner, a.ItemId!).Code);
        string? decisionId = null;
        if (resume)
        {
            var preview = f.Service.ProposeException(owner, a.ItemId!, f.ExceptionalRule(40, 50), f.Normalization()); decisionId = preview.DecisionId;
            var decision = f.Queue.Decisions(a.ItemId!).Single();
            f.Queue.RecordDecision(owner, f.Queue.Get(a.ItemId!).Revision, decision with { Version = 2,
                State = ExceptionalDecisionState.Approved, Actor = "synthetic_human", Utc = DateTimeOffset.UtcNow });
        }
        f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, state);
        Assert.Equal(AutomationError.Disabled, Assert.Throws<AutomationException>(() =>
        {
            if (resume) f.Service.ResumeException(owner, a.ItemId!, decisionId!);
            else f.Service.ProposeException(owner, a.ItemId!, f.ExceptionalRule(40, 50), f.Normalization());
        }).Code);
        Assert.Null(f.Service.State.Get(a.ItemId!)!.Candidate); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State);
        Assert.Equal(JobState.Staged, new JobStateStore(f.Context).Load(a.JobId!).State); f.AssertUntouched();
    }

    [Fact]
    public void PreparationActivationRollsBackWithItsHistoryOnInterruptedTransaction()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner);
        var history = JsonSerializer.Serialize(f.Queue.PolicyHistory(policy.Reference));
        var hook = typeof(AutomationQueueStore).GetProperty("BeforeCommit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        hook.SetValue(f.Queue, (Action<string>)(_ => throw new PreparationTestFixture.SimulatedCrash()));
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() =>
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly));
        hook.SetValue(f.Queue, null);
        var reopened = new AutomationQueueStore(f.Roots);
        Assert.Equal(JsonSerializer.Serialize(policy), JsonSerializer.Serialize(reopened.CurrentPolicy()));
        Assert.Equal(history, JsonSerializer.Serialize(reopened.PolicyHistory(policy.Reference))); f.AssertUntouched();
    }

    [Theory] [InlineData("PreparationOnly")] [InlineData("Enabled")]
    public void ForgedActiveStateWithoutAuthorityFailsClosedOnReopening(string forged)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Queue.CurrentPolicy();
        using (var db = new SqliteConnection("Data Source=" + f.Queue.DatabasePath + ";Pooling=False"))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "UPDATE policies SET payload=replace(payload, '\"State\":\"Disabled\"', $replacement); " +
                "UPDATE policy_history SET payload=replace(payload, '\"State\":\"Disabled\"', $replacement)";
            command.Parameters.AddWithValue("$replacement", "\"State\":\"" + forged + "\""); command.ExecuteNonQuery();
        }
        Assert.Equal(AutomationError.CorruptStore, Assert.Throws<AutomationException>(() =>
            new AutomationQueueStore(f.Roots).CurrentPolicy()).Code);
    }

    [Fact]
    public void UnknownProfileRemainsParkedUnderPreparationOnlyWithoutAutomaticAssignment()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true);
        var zip = f.Zip();
        using (var file = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("portrait_example_001_manifest.json")!;
            string manifest; using (var reader = new StreamReader(entry.Open())) manifest = reader.ReadToEnd();
            entry.Delete(); using var writer = new StreamWriter(archive.CreateEntry("portrait_example_001_manifest.json").Open());
            writer.Write(manifest.Replace("portrait_npc", "unknown_profile", StringComparison.Ordinal));
        }
        var received = File.ReadAllBytes(zip); var result = new InboxAdmissionService(f.Context, f.Queue, PreparationTestFixture.Fast).Admit(owner, zip);
        Assert.Equal(InboxAdmissionCode.ProfileUnknown, result.Code); Assert.Equal(QueueState.NeedsReview, f.Queue.Get(result.ItemId!).State);
        Assert.Equal(PreparationCode.NeedsReview, f.Service.Prepare(owner, result.ItemId!).Code);
        Assert.Null(f.Service.State.Get(result.ItemId!)); Assert.Equal(received, File.ReadAllBytes(zip)); f.AssertUntouched();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DuplicateMasterOverrideRequiresExplicitAuthorizedTerms(bool allowDuplicate)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        f.Policy(owner, authorizePreparation: true, allowDuplicate: allowDuplicate);
        var a = f.Admit(owner); Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        var b = f.Admit(owner, f.Zip("other.zip", asset: "portrait_other_001"));
        Assert.Equal(allowDuplicate ? PreparationCode.Planned : PreparationCode.MasterDuplicate, f.Service.Prepare(owner, b.ItemId!).Code);
        Assert.NotEqual(a.JobId, b.JobId); f.AssertUntouched();
    }

    [Fact]
    public void TermsAttestedByAnotherActorCannotOverrideDefaultMasterDuplicateReview()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var definition = AutomationPolicyDefinition.DisabledDefault(f.Roots) with { PolicyId = "synthetic_policy", Operations = AutomationOperations.Admit };
        var policy = f.Queue.SavePolicy(owner, definition);
        policy = f.Queue.RecordAuthorization(owner, policy.Reference, policy.Revision,
            new("synthetic_authorization", f.Context.Id, policy.Reference, "synthetic_responsible", DateTimeOffset.UtcNow));
        var unrelated = policy with { Authorization = policy.Authorization! with { Actor = "unrelated_actor" } };
        f.Service.State.RegisterGrant(owner, unrelated, new("unrelated_terms", 1, policy.Reference, policy.Authorization!.AuthorizationId,
            "unrelated_actor", DateTimeOffset.UtcNow, "synthetic_provider", "synthetic_model", 0, [], 2, 60, true));
        f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.PreparationOnly);
        var a = f.Admit(owner); Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        var b = f.Admit(owner, f.Zip("other.zip", asset: "portrait_other_001"));
        Assert.Equal(PreparationCode.MasterDuplicate, f.Service.Prepare(owner, b.ItemId!).Code); f.AssertUntouched();
    }
}
