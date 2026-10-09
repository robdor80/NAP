using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationQueueTests
{
    [Fact]
    public void ReopenPreservesExactItemsEventsObservationsAndBatches()
    {
        using var f = new Fixture(); QueueItem item; QueueBatch batch;
        using (var owner = f.Store.AcquireOwner())
        {
            batch = f.Store.CreateBatch(owner); item = f.Observe(owner, batch: batch.Id);
            item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.WaitingStable);
            Assert.Equal(item, f.Store.Get(item.Id)); f.Store.CloseBatch(owner, batch.Id);
        }
        var reopened = new AutomationQueueStore(f.Roots);
        Assert.Equal(item, reopened.Get(item.Id)); Assert.Equal([QueueEventCode.Observed, QueueEventCode.Transitioned], reopened.Events(item.Id).Select(e => e.Code));
        Assert.Equal([1L, 2L], reopened.Events(item.Id).Select(e => e.Sequence)); Assert.NotNull(reopened.GetBatch(batch.Id).ClosedUtc);
        Assert.Equal(new QueueBatchSummary(1, 1, 0, 0, 0, 1), reopened.BatchSummary(batch.Id)); Assert.Single(reopened.Observations(item.Id));
    }
    [Fact]
    public void DuplicateZipAliasesPathsWithoutSecondItemOrAttempt()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var a = f.Observe(owner);
        var b = f.Store.Observe(owner, f.Id, Path.Combine(f.Roots.InboxRoot, "another.zip"), a.Zip);
        Assert.Equal(a.Id, b.Id); Assert.Single(f.Store.List()); Assert.Equal(2, f.Store.Observations(a.Id).Count);
        Assert.Single(f.Store.Events(a.Id)); Assert.Equal(0, b.AttemptCount);
        Assert.Equal(a.Id, f.Store.Observe(owner, f.Id, a.SourcePath, a.Zip).Id); Assert.Equal(2, f.Store.Observations(a.Id).Count);
        Error(AutomationError.InvalidContract, () => f.Store.Observe(owner, f.Id, a.SourcePath, a.Zip with { SizeBytes = 12 }));
    }
    [Fact]
    public void AttemptJobReservationAndClaimRemainStableAfterRestart()
    {
        using var f = new Fixture(); QueueItem item; WorkflowAttempt attempt; QueueClaim claim;
        using (var owner = f.Store.AcquireOwner())
        {
            (item, attempt) = f.Ready(owner); f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b'));
            item = f.Store.Get(item.Id); claim = f.Store.Claim(owner, item.Id, item.Revision);
            Error(AutomationError.RevisionConflict, () => f.Store.Claim(owner, item.Id, item.Revision));
        }
        using (var owner = f.Store.AcquireOwner())
        {
            Assert.Equal(attempt, f.Store.GetAttempt(attempt.Id)); var running = f.Store.Get(item.Id);
            Error(AutomationError.InvalidClaim, () => f.Store.Transition(owner, item.Id, running.Revision, QueueState.Completed, claim: claim));
            var parked = f.Store.ParkInterruptedClaim(owner, item.Id, running.Revision);
            Assert.Equal(QueueIncident.InterruptedClaim, parked.Incident); Assert.Equal(attempt.JobId, f.Store.GetAttempt(attempt.Id).JobId);
            Assert.NotNull(f.Store.GetReservation(f.Asset));
        }
    }
    [Fact]
    public void ProductiveClaimRequiresAttemptAndReservation()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var item = f.Observe(owner);
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued);
        Error(AutomationError.ReservationOccupied, () => f.Store.Claim(owner, item.Id, item.Revision));
        var attempt = f.Store.CreateAttempt(owner, item.Id, item.Revision, f.Profile);
        item = f.Store.Get(item.Id); Error(AutomationError.ReservationOccupied, () => f.Store.Claim(owner, item.Id, item.Revision));
        Error(AutomationError.IllegalTransition, () => f.Store.CreateAttempt(owner, item.Id, item.Revision, f.Profile));
        f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id);
        var claim = f.Store.Claim(owner, item.Id, item.Revision); item = f.Store.Get(item.Id);
        Error(AutomationError.InvalidClaim, () => f.Store.Transition(owner, item.Id, item.Revision, QueueState.Completed, claim: claim with { Token = new string('1', 32) }));
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.Completed, claim: claim);
        Error(AutomationError.IllegalTransition, () => f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued));
        Assert.Equal(attempt.Id, item.ActiveAttempt);
    }
    [Theory]
    [InlineData(QueueState.Rejected)] [InlineData(QueueState.Completed)] [InlineData(QueueState.Duplicate)]
    public void TerminalStatesNeverReopen(QueueState state)
    { foreach (var next in Enum.GetValues<QueueState>()) Assert.False(QueueTransitions.CanTransition(state, next)); }
    [Theory]
    [InlineData(QueueState.Observed, QueueState.Completed)] [InlineData(QueueState.WaitingStable, QueueState.Running)]
    [InlineData(QueueState.Queued, QueueState.Completed)] [InlineData(QueueState.NeedsReview, QueueState.Running)]
    public void IllegalTransitionsLeaveBothStateAndEventsUntouched(QueueState from, QueueState to)
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var item = f.Observe(owner);
        if (from != QueueState.Observed) item = f.Store.Transition(owner, item.Id, item.Revision, from, from == QueueState.NeedsReview ? QueueIncident.ProfileUnknown : QueueIncident.None);
        var events = f.Store.Events(item.Id).ToArray(); Error(AutomationError.IllegalTransition, () => f.Store.Transition(owner, item.Id, item.Revision, to));
        Assert.Equal(item, f.Store.Get(item.Id)); Assert.Equal(events, f.Store.Events(item.Id));
    }
    [Fact]
    public void StateEventAndAttemptRollBackTogetherWhenCommitIsInterrupted()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var item = f.Observe(owner); var events = f.Store.Events(item.Id).ToArray();
        Fault(f.Store, () => f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued));
        var reopened = new AutomationQueueStore(f.Roots); Assert.Equal(item, reopened.Get(item.Id)); Assert.Equal(events, reopened.Events(item.Id));
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued);
        Fault(f.Store, () => f.Store.CreateAttempt(owner, item.Id, item.Revision, f.Profile));
        Assert.Equal(item, reopened.Get(item.Id)); Assert.Null(reopened.Get(item.Id).ActiveAttempt);
        var attempt = f.Store.CreateAttempt(owner, item.Id, item.Revision, f.Profile); Assert.Equal(1, attempt.Number);
    }
    [Fact]
    public void SameAssetDifferentContentCollidesAndSameContentCannotHaveSecondOwner()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (a, _) = f.Ready(owner);
        var reservation = f.Store.ReserveAsset(owner, a.Id, a.Revision, f.Asset, Fixture.Hash('b'));
        var (b, _) = f.Ready(owner, 'c');
        Error(AutomationError.AssetCollision, () => f.Store.ReserveAsset(owner, b.Id, b.Revision, f.Asset, Fixture.Hash('d')));
        Error(AutomationError.ReservationOccupied, () => f.Store.ReserveAsset(owner, b.Id, b.Revision, f.Asset, Fixture.Hash('b')));
        Assert.Equal(reservation, f.Store.GetReservation(f.Asset)); Assert.Equal(b, f.Store.Get(b.Id));
    }
    [Fact]
    public void UniversesHaveIndependentIdentityPoliciesQueuesAndReservations()
    {
        using var a = new Fixture("nimroel"); using var b = new Fixture("synthetic_universe");
        using var ao = a.Store.AcquireOwner(); using var bo = b.Store.AcquireOwner(); var (ai, _) = a.Ready(ao); var (bi, _) = b.Ready(bo);
        Assert.NotEqual(ai.Id, bi.Id); Assert.Equal(ai.Zip, bi.Zip);
        a.Store.ReserveAsset(ao, ai.Id, ai.Revision, a.Asset, Fixture.Hash('b')); b.Store.ReserveAsset(bo, bi.Id, bi.Revision, b.Asset, Fixture.Hash('b'));
        Assert.NotNull(a.Store.GetReservation(a.Asset)); Assert.NotNull(b.Store.GetReservation(b.Asset));
        Error(AutomationError.UniverseMismatch, () => a.Store.Observe(ao, b.Id, ai.SourcePath, ai.Zip));
        Error(AutomationError.UniverseMismatch, () => a.Store.GetReservation(b.Asset));
        Error(AutomationError.InvalidClaim, () => b.Store.CreateBatch(ao));
        var wrong = new UniverseStorageConfig(b.Id, a.Roots.WorkspaceRoot, a.Roots.ProductionRoot, a.Roots.ArchiveRoot);
        Error(AutomationError.CorruptStore, () => new AutomationQueueStore(wrong).CurrentPolicy());
    }
    [Fact]
    public void CompetingOwnersCannotClaimTwiceEvenAfterWinnerDisposes()
    {
        using var f = new Fixture(); QueueItem item;
        using (var owner = f.Store.AcquireOwner()) { (item, _) = f.Ready(owner); f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id); }
        var winners = 0; var errors = new System.Collections.Concurrent.ConcurrentBag<AutomationError>();
        Parallel.For(0, 2, _ =>
        {
            try { using var owner = new AutomationQueueStore(f.Roots).AcquireOwner(); f.Store.Claim(owner, item.Id, item.Revision); Interlocked.Increment(ref winners); }
            catch (AutomationException e) { errors.Add(e.Code); }
        });
        Assert.Equal(1, winners); Assert.Single(errors); Assert.All(errors, e => Assert.True(e is AutomationError.Busy or AutomationError.RevisionConflict));
    }
    [Fact]
    public void SeparateProcessObservesExclusiveNamedOwnerMutex()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(f.Roots.StateRoot)); if (OperatingSystem.IsWindows()) root = root.ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root + "\n"))).ToLowerInvariant();
        var script = "$m=[Threading.Mutex]::new($false,'NAP.Execution.AutomationOwner." + hash + "'); if($m.WaitOne(0)){ $m.ReleaseMutex(); exit 10 }; $m.Dispose(); exit 0";
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!; Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
    }
    [Fact]
    public void ReservationAndAttemptReleaseAreExplicitAndOnlyStoppedWorkCanRelease()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (item, attempt) = f.Ready(owner);
        f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id);
        Error(AutomationError.IllegalTransition, () => f.Store.ReleaseReservation(owner, item.Id, item.Revision));
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, QueueIncident.ExceptionDecisionRequired);
        Error(AutomationError.ReservationOccupied, () => f.Store.CloseAttempt(owner, item.Id, item.Revision));
        f.Store.ReleaseReservation(owner, item.Id, item.Revision); item = f.Store.Get(item.Id); f.Store.CloseAttempt(owner, item.Id, item.Revision);
        item = f.Store.Get(item.Id); var next = f.Store.CreateAttempt(owner, item.Id, item.Revision, f.Profile);
        Assert.Equal(attempt.Id, next.ParentId); Assert.NotEqual(attempt.JobId, next.JobId); Assert.Equal(2, next.Number);
    }
    [Fact]
    public void PolicyVersionsAuthorizationsPauseAndRevocationPersistWithoutActivation()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var initial = f.Store.CurrentPolicy();
        Assert.Equal(AutomationPolicyState.Disabled, initial.State); Assert.False(initial.IsExecutionAuthorized); Assert.Empty(initial.Definition.Normalization);
        var definition = f.Policy(); var saved = f.Store.SavePolicy(owner, definition); Assert.Equal(definition.Reference, saved.Reference);
        Assert.Equal(300, saved.Definition.Normalization[0].Limits.MaxAddedAreaBasisPoints); Assert.Equal(100, saved.Definition.Normalization[0].Limits.MaxAxisGrowthBasisPoints);
        Error(AutomationError.Disabled, () => f.Store.SetPolicyState(owner, saved.Reference, saved.Revision, AutomationPolicyState.Enabled));
        var grant = new AutomationAuthorization("grant_one", f.Id, saved.Reference, "synthetic_owner", DateTimeOffset.UtcNow);
        saved = f.Store.RecordAuthorization(owner, saved.Reference, saved.Revision, grant); Assert.False(saved.IsExecutionAuthorized);
        saved = f.Store.SetPolicyState(owner, saved.Reference, saved.Revision, AutomationPolicyState.Paused);
        saved = f.Store.SetPolicyState(owner, saved.Reference, saved.Revision, AutomationPolicyState.Revoked);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(saved), System.Text.Json.JsonSerializer.Serialize(new AutomationQueueStore(f.Roots).GetPolicy(saved.Reference)));
        Error(AutomationError.IllegalTransition, () => f.Store.SetPolicyState(owner, saved.Reference, saved.Revision, AutomationPolicyState.Disabled));
        Error(AutomationError.PolicyMismatch, () => f.Store.SavePolicy(owner, definition with { Operations = AutomationOperations.Produce }));
        var v2 = f.Store.SavePolicy(owner, definition with { Version = 2 }); Assert.Null(v2.Authorization); Assert.NotEqual(saved.Reference.Hash, v2.Reference.Hash);
        Error(AutomationError.PolicyMismatch, () => f.Store.RecordAuthorization(owner, v2.Reference, v2.Revision, grant));
        Assert.Equal(AutomationPolicyState.Disabled, v2.State);
    }
    [Fact]
    public void ScopeChangeCannotReuseSavedPolicyOrDatabase()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var changed = new UniverseStorageConfig(f.Id, f.Roots.WorkspaceRoot, Path.Combine(f.Root, "other_production"), f.Roots.ArchiveRoot);
        Error(AutomationError.PolicyMismatch, () => f.Store.SavePolicy(owner, f.Policy() with { Roots = changed }));
        Error(AutomationError.CorruptStore, () => new AutomationQueueStore(changed).CurrentPolicy());
    }
    [Fact]
    public void PolicyAndAuthorityDoNotPretendAnIndividualHumanApprovedAutomaticOperation()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var policy = f.Store.SavePolicy(owner, f.Policy());
        var grant = new AutomationAuthorization("global_grant", f.Id, policy.Reference, "responsible", DateTimeOffset.UtcNow);
        policy = f.Store.RecordAuthorization(owner, policy.Reference, policy.Revision, grant);
        var (item, attempt) = f.Ready(owner);
        var authority = new NormalizationAuthority(NormalizationAuthorityKind.AuthorizedPolicy, grant.AuthorizationId, grant.Actor, grant.AuthorizedUtc, policy.Reference);
        var lineage = f.Lineage(item, attempt, policy.Definition.Normalization[0], authority);
        f.Store.RecordLineage(owner, item.Revision, lineage);
        var read = f.Store.Lineage(item.Id).Single(); Assert.Equal(NormalizationAuthorityKind.AuthorizedPolicy, read.Authority.Kind); Assert.Equal(policy.Reference, read.Authority.Policy);
        item = f.Store.Get(item.Id);
        var human = lineage with { OperationId = "normalization_" + Guid.NewGuid().ToString("N"), Authority = new(NormalizationAuthorityKind.HumanDecision, "individual", "reviewer", DateTimeOffset.UtcNow, null) };
        f.Store.RecordLineage(owner, item.Revision, human); Assert.Equal(2, f.Store.Lineage(item.Id).Count);
        item = f.Store.Get(item.Id);
        Error(AutomationError.InvalidContract, () => f.Store.RecordLineage(owner, item.Revision, human with { OperationId = "normalization_" + Guid.NewGuid().ToString("N"), Authority = human.Authority with { Policy = policy.Reference } }));
        policy = f.Store.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Revoked);
        Assert.Equal(2, f.Store.Lineage(item.Id).Count); // historical evidence survives revocation
        Error(AutomationError.PolicyMismatch, () => f.Store.RecordLineage(owner, item.Revision, lineage with { OperationId = "normalization_" + Guid.NewGuid().ToString("N") }));
    }
    [Fact]
    public void ProvenanceRetainsSeparateIdentitiesAndVersionsWithoutCopyingFiles()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (item, attempt) = f.Ready(owner);
        var policy = f.Policy().Normalization.Single(); var human = new NormalizationAuthority(NormalizationAuthorityKind.HumanDecision, "individual", "reviewer", DateTimeOffset.UtcNow, null);
        var lineage = f.Lineage(item, attempt, policy, human); f.Store.RecordLineage(owner, item.Revision, lineage); item = f.Store.Get(item.Id);
        var geometry = ImageNormalizationGeometry.Calculate(new(400, 499, 8, 6, 0), attempt.Profile!.ValidateAndGetRule().Conversion!, policy.Limits);
        var produced = lineage with { Version = 2, State = NormalizationOperationState.DerivativeDeclared, DerivedPng = new(Fixture.Hash('d'), 123, "pending:derived"), CandidateZip = new(Fixture.Hash('e'), 456, "pending:candidate"), Geometry = geometry, Utc = DateTimeOffset.UtcNow };
        f.Store.RecordLineage(owner, item.Revision, produced);
        var reopened = new AutomationQueueStore(f.Roots); Assert.Equal(2, reopened.Lineage(item.Id).Count); Assert.Equal(produced, reopened.Lineage(item.Id)[1]); Assert.NotEqual(produced.OriginalPng.Hash, produced.DerivedPng!.Hash);
        Assert.False(Directory.Exists(Path.Combine(f.Roots.StateRoot, "automation", "originals"))); Assert.Empty(Directory.GetFiles(f.Root, "*.png", SearchOption.AllDirectories));
        item = f.Store.Get(item.Id); Error(AutomationError.InvalidContract, () => f.Store.RecordLineage(owner, item.Revision, lineage with { OperationId = "normalization_" + Guid.NewGuid().ToString("N"), JobId = JobId.Create() }));
    }
    [Fact]
    public void ExceptionalDecisionRequiresFullRuleAndSurvivesApprovalRevocationAndReopen()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (item, attempt) = f.Ready(owner);
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, QueueIncident.RatioUnsupported);
        var decision = new ExceptionalConfigurationDecision("decision_" + Guid.NewGuid().ToString("N"), 1, item.Id, attempt.Id, f.Id, "portrait_npc", f.Profile, new(8, 10), 768, 960, Fixture.Hash('f'), ExceptionalScope.SingleAsset, ExceptionalDecisionState.Proposed, null, DateTimeOffset.UtcNow);
        Error(AutomationError.InvalidContract, () => f.Store.RecordDecision(owner, item.Revision, decision with { Ratio = new(1, 1) }));
        f.Store.RecordDecision(owner, item.Revision, decision); item = f.Store.Get(item.Id);
        var approved = decision with { Version = 2, State = ExceptionalDecisionState.Approved, Actor = "reviewer", Utc = DateTimeOffset.UtcNow };
        f.Store.RecordDecision(owner, item.Revision, approved); item = f.Store.Get(item.Id);
        var revoked = approved with { Version = 3, State = ExceptionalDecisionState.Revoked, Utc = DateTimeOffset.UtcNow }; f.Store.RecordDecision(owner, item.Revision, revoked);
        Assert.Equal([decision, approved, revoked], new AutomationQueueStore(f.Roots).Decisions(item.Id));
        Assert.Equal(AutomationPolicyState.Disabled, f.Store.CurrentPolicy().State); Assert.Equal(f.Profile, f.Store.GetAttempt(attempt.Id).Profile);
    }
    [Theory]
    [InlineData(501, 100)] [InlineData(300, 501)] [InlineData(-1, 100)]
    public void NormalizationCannotRelaxExistingHardGrowthBounds(int area, int axis)
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var p = f.Policy();
        var entry = p.Normalization[0] with { Limits = new ImageNormalizationPolicy { MaxAddedAreaBasisPoints = area, MaxAxisGrowthBasisPoints = axis } };
        Error(AutomationError.InvalidContract, () => f.Store.SavePolicy(owner, p with { Normalization = [entry] }));
    }
    [Fact]
    public void ExistingJobJournalAndCatalogRemainByteExact()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Roots.StateRoot); var job = JobId.Create();
        var context = new UniverseContext(new UniverseProfile(f.Id, "Synthetic"), f.Roots);
        new JobStateStore(context).Create(job); new AssetCatalog(context).Initialize();
        var journal = Path.Combine(f.Roots.StateRoot, job.Value + ".json"); var journalBytes = File.ReadAllBytes(journal); var dbBytes = File.ReadAllBytes(f.Roots.CatalogPath);
        using (var owner = f.Store.AcquireOwner()) { f.Ready(owner); f.Store.SavePolicy(owner, f.Policy()); }
        Assert.Equal(journalBytes, File.ReadAllBytes(journal)); Assert.Equal(dbBytes, File.ReadAllBytes(f.Roots.CatalogPath));
        Assert.Equal(JobState.Detected, new JobStateStore(context).Load(job).State); new AssetCatalog(context).CheckIntegrity();
        Assert.Equal(4, System.Text.Json.JsonDocument.Parse(journalBytes).RootElement.EnumerateObject().Count());
    }
    [Theory]
    [InlineData("state")] [InlineData("event")] [InlineData("schema")] [InlineData("policy")] [InlineData("roots")] [InlineData("json")]
    public void CorruptOrUnknownStateFailsClosedWithoutRepair(string kind)
    {
        using var f = new Fixture(); using (var owner = f.Store.AcquireOwner()) f.Observe(owner);
        using (var db = f.Raw())
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = kind switch
            {
                "state" => "PRAGMA ignore_check_constraints=ON; UPDATE items SET state='Unknown'",
                "event" => "DELETE FROM events",
                "schema" => "PRAGMA user_version=99",
                "policy" => "UPDATE policies SET payload=replace(payload,'Disabled','Enabled')",
                "roots" => "UPDATE queue_metadata SET universe='foreign'",
                _ => "UPDATE items SET payload='{}'"
            }; cmd.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(f.Store.DatabasePath);
        Assert.Throws<AutomationException>(() => new AutomationQueueStore(f.Roots).List()); Assert.Equal(bytes, File.ReadAllBytes(f.Store.DatabasePath));
    }
    [Fact]
    public void InvalidDatabaseBytesFailClosed()
    {
        using var f = new Fixture(); using (f.Store.AcquireOwner()) { }
        File.WriteAllBytes(f.Store.DatabasePath, Encoding.UTF8.GetBytes("not SQLite")); var bytes = File.ReadAllBytes(f.Store.DatabasePath);
        Error(AutomationError.CorruptStore, () => f.Store.List()); Assert.Equal(bytes, File.ReadAllBytes(f.Store.DatabasePath));
    }
    [Fact]
    public void LinkedQueueDirectoryAndWalArtifactsAreNeverFollowed()
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Roots.StateRoot); var target = Path.Combine(f.Root, "unrelated"); Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.Combine(f.Roots.StateRoot, "automation"), target);
        Error(AutomationError.UnsafePath, () => f.Store.AcquireOwner()); Assert.Empty(Directory.GetFileSystemEntries(target));
        Directory.Delete(Path.Combine(f.Roots.StateRoot, "automation"));
        using (f.Store.AcquireOwner()) { }
        File.WriteAllText(f.Store.DatabasePath + "-wal", "foreign"); Error(AutomationError.CorruptStore, () => f.Store.List());
    }
    [Fact]
    public void UnsafeObservationAndWrongOwnerCannotWrite()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner();
        Error(AutomationError.UnsafePath, () => f.Store.Observe(owner, f.Id, Path.Combine(f.Roots.ProductionRoot, "x.zip"), new(Fixture.Hash('a'), 10)));
        var a = f.Observe(owner); owner.Dispose(); Error(AutomationError.InvalidClaim, () => f.Store.Transition(owner, a.Id, a.Revision, QueueState.Queued));
    }
    [Fact]
    public void UnresolvedProfilePreservesJobAndCannotClaimUntilCompleteRuleIsBound()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var item = f.Observe(owner);
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, QueueIncident.ProfileUnknown);
        var unresolved = f.Store.CreateAttempt(owner, item.Id, item.Revision); Assert.Null(unresolved.Profile); item = f.Store.Get(item.Id);
        Error(AutomationError.InvalidContract, () => f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')));
        var resolved = f.Store.BindProfile(owner, item.Id, item.Revision, f.Profile); Assert.Equal(unresolved.JobId, resolved.JobId); Assert.Equal(unresolved.Id, resolved.Id);
        item = f.Store.Get(item.Id); Error(AutomationError.IllegalTransition, () => f.Store.BindProfile(owner, item.Id, item.Revision, f.Profile));
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued); f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b'));
        item = f.Store.Get(item.Id); var claim = f.Store.Claim(owner, item.Id, item.Revision); item = f.Store.Get(item.Id);
        item = f.Store.RecordProgress(owner, item.Id, item.Revision, claim, QueueStage.Audit, QueueEventResult.Waiting);
        Assert.Equal(QueueStage.Audit, f.Store.Events(item.Id).Last().Stage); Assert.Equal(resolved, new AutomationQueueStore(f.Roots).GetAttempt(resolved.Id));
    }
    [Fact]
    public void RetryAndMissingSourceMetadataAreTypedAndNeverBecomeSuccess()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (item, _) = f.Ready(owner);
        f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id); var claim = f.Store.Claim(owner, item.Id, item.Revision); item = f.Store.Get(item.Id);
        Error(AutomationError.InvalidContract, () => f.Store.Transition(owner, item.Id, item.Revision, QueueState.RetryScheduled, claim: claim));
        var time = DateTimeOffset.UtcNow.AddMinutes(5); item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.RetryScheduled, QueueIncident.RetryExhausted, time, claim);
        Assert.Equal(1, item.RetryCount); Assert.Equal(time, item.RetryAtUtc); Assert.Null(item.ClaimToken);
        Error(AutomationError.IllegalTransition, () => f.Store.Transition(owner, item.Id, item.Revision, QueueState.Queued));
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.MissingSource, QueueIncident.MissingSource);
        item = f.Store.Transition(owner, item.Id, item.Revision, QueueState.WaitingStable); Assert.Equal(QueueIncident.None, item.Incident); Assert.NotNull(f.Store.GetReservation(f.Asset));
    }
    [Fact]
    public void ReservationClaimAndPolicyTransactionsRollBackWithoutPartialEvidence()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var (item, _) = f.Ready(owner);
        Fault(f.Store, () => f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b'))); Assert.Null(f.Store.GetReservation(f.Asset)); Assert.Equal(item, f.Store.Get(item.Id));
        f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id);
        Fault(f.Store, () => f.Store.Claim(owner, item.Id, item.Revision)); Assert.Equal(item, f.Store.Get(item.Id));
        var policy = f.Store.SavePolicy(owner, f.Policy()); var grant = new AutomationAuthorization("grant", f.Id, policy.Reference, "owner", DateTimeOffset.UtcNow);
        Fault(f.Store, () => f.Store.RecordAuthorization(owner, policy.Reference, policy.Revision, grant));
        Assert.Null(f.Store.GetPolicy(policy.Reference).Authorization); Assert.Single(f.Store.PolicyHistory(policy.Reference));
        policy = f.Store.RecordAuthorization(owner, policy.Reference, policy.Revision, grant); policy = f.Store.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Revoked);
        Assert.Equal([0L, 1L, 2L], f.Store.PolicyHistory(policy.Reference).Select(h => h.Revision));
    }
    [Fact]
    public void PolicyCollectionCannotBeChangedByMutatingCallerList()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var entries = f.Policy().Normalization.ToList();
        var definition = f.Policy() with { Normalization = entries }; var expected = definition.Reference;
        entries.Clear(); Assert.Single(definition.Normalization); Assert.Equal(expected, definition.Reference);
        Assert.Equal(expected, f.Store.SavePolicy(owner, definition).Reference);
    }
    [Fact]
    public void ProvenanceExceptionAndPolicyWritesLeaveForeignAssetsAndProfilesUntouched()
    {
        using var f = new Fixture(); var production = Path.Combine(f.Roots.ProductionRoot, "foreign.webp"); var archive = Path.Combine(f.Roots.ArchiveRoot, "foreign.png");
        File.WriteAllBytes(production, [1, 2, 3]); File.WriteAllBytes(archive, [4, 5, 6]);
        using (var owner = f.Store.AcquireOwner())
        {
            var (item, attempt) = f.Ready(owner); var parameters = f.Policy().Normalization.Single();
            f.Store.RecordLineage(owner, item.Revision, f.Lineage(item, attempt, parameters, new(NormalizationAuthorityKind.HumanDecision, "human", "reviewer", DateTimeOffset.UtcNow, null)));
            f.Store.SavePolicy(owner, f.Policy());
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(production)); Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(archive));
        Assert.Single(Directory.GetFileSystemEntries(f.Roots.ProductionRoot)); Assert.Single(Directory.GetFileSystemEntries(f.Roots.ArchiveRoot));
    }
    [Fact]
    public void RatioAloneOrIncompleteProfileCannotCreateEffectiveProductionRule()
    {
        using var f = new Fixture(); using var owner = f.Store.AcquireOwner(); var item = f.Observe(owner);
        var partial = "{\"schema_version\":1,\"universe_id\":\"synthetic_universe\",\"display_name\":\"Synthetic\",\"classification_dimensions\":[],\"asset_rules\":[]}";
        Error(AutomationError.InvalidContract, () => EffectiveRuleSnapshot.Create(f.Id, 1, "portrait", "portrait_npc", partial));
        Error(AutomationError.InvalidContract, () => new RationalProportion(0, 10)); Assert.Equal(new RationalProportion(4, 5), new RationalProportion(8, 10));
        Assert.Equal(new RationalProportion(4, 5), NimroelAutomationCanon.Portrait); Assert.Equal(NimroelAutomationCanon.Narrative, NimroelAutomationCanon.Cartography); Assert.Equal(new RationalProportion(1, 1), NimroelAutomationCanon.Seal);
        Assert.Equal(0, item.AttemptCount);
    }
    [Theory]
    [InlineData("database")] [InlineData("journal")] [InlineData("state")]
    public void LinkedBoundaryArtifactsNeverModifyTargets(string kind)
    {
        using var f = new Fixture(); using (f.Store.AcquireOwner()) { }
        var target = Path.Combine(f.Root, "outside"); File.WriteAllBytes(target, [9, 8, 7]);
        var link = kind == "database" ? f.Store.DatabasePath : f.Store.DatabasePath + "-journal";
        if (kind == "state")
        {
            Directory.Delete(f.Roots.StateRoot, true); var outside = Path.Combine(f.Root, "outside_dir"); Directory.CreateDirectory(outside); Directory.CreateSymbolicLink(f.Roots.StateRoot, outside);
        }
        else { if (File.Exists(link)) File.Delete(link); File.CreateSymbolicLink(link, target); }
        Error(AutomationError.UnsafePath, () => f.Store.AcquireOwner()); Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(target));
    }
    [Fact]
    public void ClaimFromUnrecordedFutureOwnerGenerationFailsClosed()
    {
        using var f = new Fixture(); QueueItem item;
        using (var owner = f.Store.AcquireOwner())
        {
            (item, _) = f.Ready(owner); f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b')); item = f.Store.Get(item.Id);
            f.Store.Claim(owner, item.Id, item.Revision); item = f.Store.Get(item.Id);
        }
        using (var db = f.Raw())
        {
            using var command = db.CreateCommand(); command.CommandText = "UPDATE items SET payload=replace(payload,$a,$b)";
            command.Parameters.AddWithValue("$a", "\"ClaimEpoch\":" + item.ClaimEpoch);
            command.Parameters.AddWithValue("$b", "\"ClaimEpoch\":" + (item.ClaimEpoch + 100)); command.ExecuteNonQuery();
        }
        Error(AutomationError.CorruptStore, () => f.Store.List());
    }
    [Fact]
    public void ReservedAttemptCannotLoseItsValidatedProfileOnReopen()
    {
        using var f = new Fixture(); WorkflowAttempt attempt;
        using (var owner = f.Store.AcquireOwner())
        {
            var (item, active) = f.Ready(owner); attempt = active;
            f.Store.ReserveAsset(owner, item.Id, item.Revision, f.Asset, Fixture.Hash('b'));
        }
        using (var db = f.Raw())
        {
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE attempts SET payload=$payload WHERE id=$id";
            var options = new System.Text.Json.JsonSerializerOptions();
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
            command.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(attempt with { Profile = null }, options));
            command.Parameters.AddWithValue("$id", attempt.Id.Value); command.ExecuteNonQuery();
        }
        Error(AutomationError.CorruptStore, () => f.Store.List());
    }
    [Fact]
    public void InvalidRootsAndMalformedEffectiveRulesReturnTypedErrors()
    {
        using var f = new Fixture();
        var overlapping = new UniverseStorageConfig(f.Id, f.Roots.WorkspaceRoot, Path.Combine(f.Roots.WorkspaceRoot, "production"), f.Roots.ArchiveRoot);
        Error(AutomationError.UnsafePath, () => new AutomationQueueStore(overlapping));
        Error(AutomationError.InvalidContract, () => EffectiveRuleSnapshot.Create(f.Id, 1, "portrait", "portrait_npc", "{malformed}"));
        Assert.False(Directory.Exists(f.Roots.StateRoot));
    }
    private static void Error(AutomationError code, Action action) => Assert.Equal(code, Assert.Throws<AutomationException>(action).Code);
    private static void Fault(AutomationQueueStore store, Action action)
    {
        var property = typeof(AutomationQueueStore).GetProperty("BeforeCommit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        property.SetValue(store, (Action<string>)(_ => throw new IOException("synthetic interruption")));
        try { Assert.Throws<AutomationException>(action); } finally { property.SetValue(store, null); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "nap-automation-" + Guid.NewGuid().ToString("N"));
        public UniverseId Id { get; }
        public UniverseStorageConfig Roots { get; }
        public AutomationQueueStore Store { get; }
        public EffectiveRuleSnapshot Profile { get; }
        public UniverseAssetKey Asset => new(Id, "portrait_example_001");
        public Fixture(string universe = "synthetic_universe")
        {
            Id = new(universe); Directory.CreateDirectory(Root);
            Roots = new(Id, Path.Combine(Root, "workspace"), Path.Combine(Root, "production"), Path.Combine(Root, "archive"));
            foreach (var path in new[] { Roots.WorkspaceRoot, Roots.ArchiveRoot, Roots.ProductionRoot }) Directory.CreateDirectory(path);
            Store = new(Roots);
            var json = """
            {"schema_version":4,"universe_id":"UNIVERSE","display_name":"Synthetic","classification_dimensions":[],"asset_rules":[{"asset_type":"portrait","production_profile":"portrait_npc","allowed_classification":[],"required_classification":[],"package_files":[{"role":"master","suffix":"","extension":".png","required":true,"content_validator":"png_master"}],"routing":{"segments":[{"literal":"portraits"},{"asset_id":true}]},"conversion":{"kind":"png_to_webp","source_role":"master","output_width":768,"output_height":960,"webp_quality":90}}]}
            """.Replace("UNIVERSE", Id.Value);
            Profile = EffectiveRuleSnapshot.Create(Id, 1, "portrait", "portrait_npc", json);
        }
        public static Sha256Digest Hash(char c) => new(new string(c, 64));
        public QueueItem Observe(AutomationQueueOwner owner, char hash = 'a', QueueBatchId? batch = null) => Store.Observe(owner, Id, Path.Combine(Roots.InboxRoot, hash + ".zip"), new(Hash(hash), 10), batch);
        public (QueueItem Item, WorkflowAttempt Attempt) Ready(AutomationQueueOwner owner, char hash = 'a')
        { var item = Observe(owner, hash); item = Store.Transition(owner, item.Id, item.Revision, QueueState.Queued); var attempt = Store.CreateAttempt(owner, item.Id, item.Revision, Profile, "staging/" + item.Id.Value, "packages/" + item.Id.Value, Hash('f')); return (Store.Get(item.Id), attempt); }
        public AutomationPolicyDefinition Policy() => new("synthetic_policy", 1, Roots, AutomationOperations.Normalize,
            new(512L * 1024 * 1024, 1024L * 1024 * 1024, 1000), new(100, 5, 60, 10),
            [new("portrait", "portrait_npc", new(4, 5), new() { MaxAddedAreaBasisPoints = 300, MaxAxisGrowthBasisPoints = 100 }, 512L * 1024 * 1024, "nearest-edge-canvas-v1", false)]);
        public NormalizationLineage Lineage(QueueItem item, WorkflowAttempt attempt, ProfileNormalizationPolicy parameters, NormalizationAuthority authority) => new(
            "normalization_" + Guid.NewGuid().ToString("N"), 1, item.Id, attempt.Id, Id, attempt.JobId, new(item.Zip.Hash, item.Zip.SizeBytes, null), new(Hash('b'), 100, null), null, null,
            NormalizationOperationState.Proposed, parameters, authority, Hash('c'), DateTimeOffset.UtcNow, null);
        public SqliteConnection Raw() { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store.DatabasePath, Pooling = false }.ToString()); db.Open(); return db; }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
