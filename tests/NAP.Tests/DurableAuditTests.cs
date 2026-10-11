using System.Net;
using NAP.AI;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class DurableAuditTests
{
    private sealed class Client(Func<CancellationToken, Task<AiAuditReport>> response) : IIdentifiedAiAuditClient
    {
        public string ProviderId => "synthetic_provider";
        public string ModelId => "synthetic_model";
        internal int Calls;
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken token = default) { Calls++; return response(token); }
    }
    private static AiAuditReport Report(AiAuditDecision decision = AiAuditDecision.Pass) => new(decision, "Synthetic controlled report",
        decision == AiAuditDecision.Pass ? [] : [new("synthetic_finding", decision == AiAuditDecision.Warning ? AiAuditFindingSeverity.Warning : AiAuditFindingSeverity.Error, "Synthetic concern")]);
    [Fact]
    public void DisabledPolicyNeverCallsProviderDespiteRecordedAuthorizationAndBudget()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner); var a = f.Admit(owner);
        var client = new Client(_ => Task.FromResult(Report()));
        Assert.Equal(PreparationCode.PolicyInactive, f.Service.Audit(owner, a.ItemId!, client).Code); Assert.Equal(0, client.Calls);
        Assert.Empty(f.Service.State.Get(a.ItemId!)!.Calls); Assert.Equal(policy.Reference, new PreparationStateStore(f.Roots).Grant(policy.Reference)!.Policy);
        Assert.Equal(AutomationError.Disabled, Assert.Throws<AutomationException>(() => f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, AutomationPolicyState.Enabled)).Code);
        f.AssertUntouched();
    }
    [Fact]
    public void DeterministicStopPreventsProviderInvocationAndRequestIntent()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner);
        var a = f.Admit(owner, f.Zip(invalidPng: true)); var client = new Client(_ => Task.FromResult(Report()));
        Assert.Equal(PreparationCode.InvalidPackage, f.Service.Audit(owner, a.ItemId!, client).Code); Assert.Equal(0, client.Calls);
        Assert.Equal(DurableAuditState.NotRequested, f.Service.State.Get(a.ItemId!)!.AuditState); f.AssertUntouched();
    }
    [Theory] [InlineData("foreign_universe", AutomationError.UniverseMismatch)] [InlineData("test_universe", AutomationError.PolicyMismatch)]
    public void OperationalTermsCannotBeRegisteredUnderDifferentUniverseOrRoots(string universe, AutomationError expected)
    {
        using var a = new PreparationTestFixture(); using var b = new PreparationTestFixture(universe);
        using var ao = a.Queue.AcquireOwner(); using var bo = b.Queue.AcquireOwner(); var policy = b.Policy(bo);
        Assert.Equal(expected, Assert.Throws<AutomationException>(() => a.Service.State.RegisterGrant(ao, policy, b.Service.State.Grant(policy.Reference)!)).Code);
        Assert.False(File.Exists(a.Service.State.DatabasePath)); a.AssertUntouched(); b.AssertUntouched();
    }
    [Fact]
    public void AuditRequestBindsCompleteSnapshotWithoutExposingAbsoluteRootsOrActor()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner); var a = f.Admit(owner);
        f.Service.Prepare(owner, a.ItemId!); var record = f.Service.State.Get(a.ItemId!)!;
        var root = Path.Combine(f.Roots.StateRoot, record.Originals!.RelativeDirectory, "package", "portrait_example_001");
        var core = new PackagePreparationService(); var plans = core.Plan(f.Context, core.Validate(f.Context, root));
        var request = new WorkflowAiAuditRequestBuilder().Build(plans.Processing, plans.Validation, record.Snapshot!, f.Service.State.Grant(policy.Reference)!);
        var json = new AiAuditRequestJsonRenderer().Render(request);
        Assert.Contains(record.Snapshot!.Hash.Hex, json); Assert.Contains(record.MasterHash!.Hex, json);
        Assert.Contains("OriginalFiles", json); Assert.Contains("EffectiveProfile", json); Assert.Contains("EngineIdentity", json);
        Assert.DoesNotContain(f.Root, json); Assert.DoesNotContain(policy.Authorization!.Actor, json); f.AssertUntouched();
    }
    [Theory] [InlineData(429, AiAuditErrorKind.RateLimited)] [InlineData(500, AiAuditErrorKind.Server)] [InlineData(503, AiAuditErrorKind.Server)]
    [InlineData(401, AiAuditErrorKind.Unauthorized)] [InlineData(403, AiAuditErrorKind.Forbidden)]
    public async Task GeminiErrorsAreTypedAndDoNotDiscloseHttpBody(int status, AiAuditErrorKind expected)
    {
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private server diagnostic") };
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(7)); return Task.FromResult(response);
        }));
        var client = new GeminiOperationAuditClient(http, "synthetic_key", "synthetic_model");
        var error = await Assert.ThrowsAsync<AiAuditClientException>(() => client.AuditAsync(AiAuditTestData.Request()));
        Assert.Equal(expected, error.Code); Assert.Equal(status, error.HttpStatus); Assert.Equal(TimeSpan.FromSeconds(7), error.RetryAfter);
        Assert.DoesNotContain("private server", error.ToString());
        Assert.Equal("gemini-interactions-v1", ((IIdentifiedAiAuditClient)client).ProviderId);
    }
    [Theory] [InlineData("invalid_json")] [InlineData("oversize")]
    public async Task InvalidOrUnboundedProviderResponseNeverProducesReport(string kind)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(kind == "oversize" ? new string('x', 1024 * 1024 + 1) : "not json") })));
        var error = await Assert.ThrowsAsync<AiAuditClientException>(() => new GeminiOperationAuditClient(http, "synthetic_key", "synthetic_model").AuditAsync(AiAuditTestData.Request()));
        Assert.Equal(AiAuditErrorKind.InvalidResponse, error.Code);
    }
    [Theory] [InlineData(AiAuditDecision.Pass, PreparationCode.AuditPass)]
    [InlineData(AiAuditDecision.Warning, PreparationCode.AuditWarning)] [InlineData(AiAuditDecision.Fail, PreparationCode.AuditFail)]
    public void DurableDecisionSurvivesReopeningAndNeverRepeatsWarningOrFail(AiAuditDecision decision, PreparationCode expected)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        var client = new Client(_ => Task.FromResult(Report(decision)));
        Assert.Equal(expected, f.Service.Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(expected, new DurablePreparationService(f.Context, f.Queue, f.Clock).Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(1, client.Calls); Assert.Equal(a.JobId, f.Service.State.Get(a.ItemId!)!.JobId); f.AssertUntouched();
    }
    [Theory] [InlineData(300, true, PreparationCode.Planned)]
    [InlineData(299, true, PreparationCode.NormalizationLimitExceeded)] [InlineData(300, false, PreparationCode.GeometryPolicyRequired)]
    public void AutomaticNormalizationNeedsOperationallyValidatedPolicyAndIndependentLimits(int area, bool operational, PreparationCode expected)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        f.Policy(owner, f.Normalization(area, 300, operational), authorizePreparation: true); var a = f.Admit(owner, f.Zip(width: 412, height: 500));
        Assert.Equal(expected, f.Service.Prepare(owner, a.ItemId!).Code);
        var record = f.Service.State.Get(a.ItemId!)!;
        if (expected == PreparationCode.Planned)
        {
            Assert.Equal(NormalizationAuthorityKind.AuthorizedPolicy, record.Authority!.Kind);
            Assert.Equal(f.Queue.CurrentPolicy().Reference, record.Authority.Policy); Assert.Empty(f.Queue.Decisions(a.ItemId!));
            Assert.Equal(PreparationCode.Planned, new DurablePreparationService(f.Context, f.Queue).Prepare(owner, a.ItemId!).Code);
            var preserved = Path.Combine(f.Roots.StateRoot, record.Originals!.RelativeDirectory);
            Assert.Equal(File.ReadAllBytes(Path.Combine(f.Roots.InboxRoot, "input.zip")), File.ReadAllBytes(Path.Combine(preserved, "received.zip")));
            var original = Path.Combine(preserved, "package", "portrait_example_001", "portrait_example_001.png");
            Assert.Equal(PreparationTestFixture.Png(412, 500, 1), File.ReadAllBytes(original));
            using var input = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(original);
            using var derivative = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                Path.Combine(f.Roots.StateRoot, record.Candidate!.RelativeDirectory, "package", "portrait_example_001", "portrait_example_001.png"));
            Assert.Equal(412, derivative.Width); Assert.Equal(515, derivative.Height);
            for (var y = 0; y < input.Height; y++) for (var x = 0; x < input.Width; x++)
                Assert.Equal(input[x, y], derivative[x + record.Geometry!.Left, y + record.Geometry.Top]);
        }
        else Assert.Null(record.Candidate);
        f.AssertUntouched();
    }
    [Theory] [InlineData(AutomationPolicyState.Paused)] [InlineData(AutomationPolicyState.Revoked)] [InlineData(AutomationPolicyState.Disabled)]
    public void InactivePolicyInvalidatesExistingPassWithoutAnotherProviderCall(AutomationPolicyState state)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        var policy = f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        var client = new Client(_ => Task.FromResult(Report())); Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
        var receipt = f.Service.State.Get(a.ItemId!)!.AuditResponse!;
        var path = Path.Combine(f.Roots.StateRoot, receipt.RelativeDirectory, "report.json"); var received = File.ReadAllBytes(path);
        f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, state);
        Assert.Equal(PreparationCode.PolicyInactive, new DurablePreparationService(f.Context, new AutomationQueueStore(f.Roots), f.Clock).Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(1, client.Calls); Assert.Equal(received, File.ReadAllBytes(path));
        Assert.Equal(JobState.Planned, new JobStateStore(f.Context).Load(a.JobId!).State); f.AssertUntouched();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExternallyParkedItemCannotReusePlannedOrPassCode(bool audited)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true);
        var a = f.Admit(owner); var client = new Client(_ => Task.FromResult(Report()));
        if (audited) Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
        else Assert.Equal(PreparationCode.Planned, f.Service.Prepare(owner, a.ItemId!).Code);
        var item = f.Queue.Get(a.ItemId!);
        f.Queue.Transition(owner, item.Id, item.Revision, QueueState.NeedsReview, QueueIncident.InvalidEvidence);
        Assert.Equal(PreparationCode.NeedsReview, new DurablePreparationService(f.Context, f.Queue, f.Clock).Audit(owner, item.Id, client).Code);
        Assert.Equal(audited ? 1 : 0, client.Calls); f.AssertUntouched();
    }
    [Theory] [InlineData(AutomationOperations.Audit)] [InlineData(AutomationOperations.Normalize)]
    public void PreparationOnlyDoesNotGrantAnOperationOmittedFromItsExactPolicy(AutomationOperations missing)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner();
        f.Policy(owner, f.Normalization(), authorizePreparation: true,
            operations: AutomationOperations.Admit | (missing == AutomationOperations.Audit ? AutomationOperations.Normalize : AutomationOperations.Audit));
        var a = f.Admit(owner, f.Zip(width: missing == AutomationOperations.Normalize ? 39 : 40));
        var client = new Client(_ => Task.FromResult(Report()));
        Assert.Equal(missing == AutomationOperations.Normalize ? PreparationCode.GeometryPolicyRequired : PreparationCode.PolicyInactive,
            f.Service.Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(0, client.Calls); Assert.Null(f.Service.State.Get(a.ItemId!)!.Candidate); f.AssertUntouched();
    }
    [Theory] [InlineData(AutomationPolicyState.Paused)] [InlineData(AutomationPolicyState.Revoked)]
    public void RevocationBeforeResponsePublicationCannotBecomePass(AutomationPolicyState state)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner, authorizePreparation: true);
        var a = f.Admit(owner); var client = new Client(_ =>
        {
            f.Queue.SetPolicyState(owner, policy.Reference, policy.Revision, state);
            return Task.FromResult(Report());
        });
        Assert.Equal(PreparationCode.PolicyInactive, f.Service.Audit(owner, a.ItemId!, client).Code);
        var record = f.Service.State.Get(a.ItemId!)!;
        Assert.Equal(DurableAuditState.ResponseReady, record.AuditState); // bound response retained, no operative PASS
        Assert.False(Directory.Exists(Path.Combine(f.Roots.StateRoot, record.AuditResponse!.RelativeDirectory)));
        Assert.Equal(QueueState.NeedsReview, f.Queue.Get(a.ItemId!).State); Assert.Equal(1, client.Calls); f.AssertUntouched();
    }
    [Theory] [InlineData("candidate-intent", true)] [InlineData("candidate-temporary", false)]
    [InlineData("candidate-before-publish", false)] [InlineData("candidate-published", true)] [InlineData("candidate-ready", true)]
    public void AuthorizedNormalizationRecoversOnlyItsOwnBoundDerivative(string stage, bool recoverable)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, f.Normalization(), authorizePreparation: true);
        var zip = f.Zip(width: 39); var received = File.ReadAllBytes(zip); var a = f.Admit(owner, zip);
        PreparationTestFixture.Hook(f.Service, s => { if (s == stage) throw new PreparationTestFixture.SimulatedCrash(); });
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Service.Prepare(owner, a.ItemId!));
        var reopened = new DurablePreparationService(f.Context, new AutomationQueueStore(f.Roots), f.Clock);
        Assert.Equal(recoverable ? PreparationCode.Planned : PreparationCode.EvidenceChanged, reopened.Prepare(owner, a.ItemId!).Code);
        Assert.Equal(a.JobId, reopened.State.Get(a.ItemId!)!.JobId); Assert.Equal(1, f.Queue.Get(a.ItemId!).AttemptCount);
        Assert.Equal(received, File.ReadAllBytes(zip)); Assert.Empty(f.Queue.Decisions(a.ItemId!)); f.AssertUntouched();
    }
    [Theory] [InlineData("profile")] [InlineData("manifest")] [InlineData("policy")] [InlineData("receipt")]
    public void ChangingRelevantScopeInvalidatesBoundPassWithoutProviderReuse(string change)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); var policy = f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        var client = new Client(_ => Task.FromResult(Report())); Assert.Equal(PreparationCode.AuditPass, f.Service.Audit(owner, a.ItemId!, client).Code);
        var record = f.Service.State.Get(a.ItemId!)!; var context = f.Context;
        if (change == "profile") context = UniverseProfileSnapshotRenderer.Context(f.ExceptionalRule(80, 100), f.Roots);
        if (change == "policy") f.Queue.SavePolicy(owner, policy.Definition with { Version = 2 });
        if (change == "manifest") File.AppendAllText(Path.Combine(f.Roots.StateRoot, record.Originals!.RelativeDirectory, "package", "portrait_example_001", "portrait_example_001_manifest.json"), " ");
        if (change == "receipt") File.AppendAllText(Path.Combine(f.Roots.StateRoot, record.AuditResponse!.RelativeDirectory, "report.json"), " ");
        Assert.NotEqual(PreparationCode.AuditPass, new DurablePreparationService(context, f.Queue, f.Clock).Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(1, client.Calls); f.AssertUntouched();
    }
    [Theory] [InlineData(AiAuditErrorKind.RateLimited, 429)] [InlineData(AiAuditErrorKind.Server, 503)]
    [InlineData(AiAuditErrorKind.Timeout, 0)] [InlineData(AiAuditErrorKind.Transport, 0)]
    public void TransientRetriesAreDurableBoundedAndChargeEachRequest(AiAuditErrorKind error, int status)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true, circuitThreshold: 100); var a = f.Admit(owner);
        var client = new Client(_ => throw new AiAuditClientException(error, "controlled error", status == 0 ? null : status));
        Assert.Equal(PreparationCode.AuditRetryScheduled, f.Service.Audit(owner, a.ItemId!, client).Code);
        var reopened = new DurablePreparationService(f.Context, f.Queue, f.Clock);
        Assert.Equal(PreparationCode.AuditRetryScheduled, reopened.Audit(owner, a.ItemId!, client).Code); Assert.Equal(1, client.Calls);
        f.Clock.Advance(5); Assert.Equal(PreparationCode.AuditRetryScheduled, reopened.Audit(owner, a.ItemId!, client).Code);
        f.Clock.Advance(5); Assert.Equal(PreparationCode.AuditBudgetExhausted, reopened.Audit(owner, a.ItemId!, client).Code);
        Assert.Equal(3, client.Calls); Assert.Equal(3, reopened.State.Get(a.ItemId!)!.Calls.Count); f.AssertUntouched();
    }
    [Theory] [InlineData(AiAuditErrorKind.Unauthorized)] [InlineData(AiAuditErrorKind.Forbidden)] [InlineData(AiAuditErrorKind.InvalidResponse)]
    public void PermanentFailureIsNeverRetried(AiAuditErrorKind error)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        var client = new Client(_ => throw new AiAuditClientException(error, "controlled error"));
        var result = f.Service.Audit(owner, a.ItemId!, client); Assert.NotEqual(PreparationCode.AuditPass, result.Code);
        Assert.Equal(result.Code, new DurablePreparationService(f.Context, f.Queue, f.Clock).Audit(owner, a.ItemId!, client).Code); Assert.Equal(1, client.Calls); f.AssertUntouched();
    }
    [Theory] [InlineData("audit-request-intent", false)] [InlineData("audit-response-received", false)]
    [InlineData("audit-response-bound", true)] [InlineData("audit-published", true)] [InlineData("audit-decision-bound", true)]
    public void CrashReusesOnlyResponseBoundBeforeFilesystemPublication(string stage, bool hasReceipt)
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true); var a = f.Admit(owner);
        var client = new Client(_ => Task.FromResult(Report()));
        PreparationTestFixture.Hook(f.Service, s => { if (s == stage) throw new PreparationTestFixture.SimulatedCrash(); });
        Assert.Throws<PreparationTestFixture.SimulatedCrash>(() => f.Service.Audit(owner, a.ItemId!, client)); var priorCalls = client.Calls;
        var result = new DurablePreparationService(f.Context, f.Queue, f.Clock).Audit(owner, a.ItemId!, client);
        Assert.Equal(hasReceipt ? PreparationCode.AuditPass : PreparationCode.AuditRetryScheduled, result.Code); Assert.Equal(priorCalls, client.Calls); f.AssertUntouched();
    }
    [Fact]
    public void ProviderTimeoutAndZeroBudgetCannotBecomePass()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true, requests: 0); var a = f.Admit(owner);
        var client = new Client(_ => new TaskCompletionSource<AiAuditReport>().Task);
        Assert.Equal(PreparationCode.AuditBudgetExhausted, f.Service.Audit(owner, a.ItemId!, client).Code); Assert.Equal(0, client.Calls); f.AssertUntouched();
        using var t = new PreparationTestFixture(); using var to = t.Queue.AcquireOwner(); t.Policy(to, authorizePreparation: true); var ta = t.Admit(to);
        Assert.Equal(PreparationCode.AuditRetryScheduled, t.Service.Audit(to, ta.ItemId!, client).Code); Assert.Equal(1, client.Calls); t.AssertUntouched();
    }
    [Fact]
    public void CircuitBreakerAndSharedBudgetProtectOtherItems()
    {
        using var f = new PreparationTestFixture(); using var owner = f.Queue.AcquireOwner(); f.Policy(owner, authorizePreparation: true);
        var a = f.Admit(owner); var b = f.Admit(owner, f.Zip("second.zip", asset: "portrait_other_001", seed: 2));
        var client = new Client(_ => throw new AiAuditClientException(AiAuditErrorKind.Server, "controlled error", 503));
        f.Service.Audit(owner, a.ItemId!, client); f.Clock.Advance(5); f.Service.Audit(owner, a.ItemId!, client);
        Assert.Equal(PreparationCode.AuditRetryScheduled, f.Service.Audit(owner, b.ItemId!, client).Code); Assert.Equal(2, client.Calls);
        f.Clock.Advance(60); Assert.Equal(PreparationCode.AuditRetryScheduled, f.Service.Audit(owner, b.ItemId!, client).Code); Assert.Equal(3, client.Calls); f.AssertUntouched();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
