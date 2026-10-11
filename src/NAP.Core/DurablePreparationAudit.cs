using System.Text;

namespace NAP.Core;

public sealed partial class DurablePreparationService
{
    public PreparationResult Audit(AutomationQueueOwner owner, QueueItemId itemId, IIdentifiedAiAuditClient client,
        CancellationToken token = default)
    {
        owner.Require(_context.Storage.StateRoot); token.ThrowIfCancellationRequested();
        var prepared = Prepare(owner, itemId, token);
        if (prepared.Code != PreparationCode.Planned) return prepared; // STOP precedes every provider invocation
        var record = GetRecord(itemId)!; var item = _queue.Get(itemId); var policy = _queue.GetPolicy(item.Policy);
        var grant = _state.Grant(policy.Reference);
        if (!policy.IsPreparationAuthorized || policy.Authorization is null || !policy.Definition.Operations.HasFlag(AutomationOperations.Audit))
            return Park(owner, item, record, PreparationCode.PolicyInactive);
        if (grant is null || grant.PolicyAuthorizationId != policy.Authorization.AuthorizationId || grant.Actor != policy.Authorization.Actor ||
            client.ProviderId != grant.ProviderId || client.ModelId != grant.ModelId)
            return Park(owner, item, record, PreparationCode.AuditConfigurationRequired);
        var snapshot = record.Snapshot!;
        var root = EffectiveRoot(record); var effective = UniverseProfileSnapshotRenderer.Context(record.EffectiveProfile, _context.Storage);
        var package = _preparation.Validate(effective, root); var plans = _preparation.Plan(effective, package);
        var request = new WorkflowAiAuditRequestBuilder().Build(plans.Processing, plans.Validation, snapshot, grant);
        var requestJson = new AiAuditRequestJsonRenderer().Render(request); var requestHash = AutomationJson.HashBytes(Encoding.UTF8.GetBytes(requestJson));
        if (record.RequestHash is not null && (record.RequestHash != requestHash || record.AuditGrant?.Hash != grant.Hash))
            return Park(owner, item, record, PreparationCode.EvidenceChanged);
        if (record.AuditState is DurableAuditState.Pass or DurableAuditState.Warning or DurableAuditState.Fail or DurableAuditState.ResponseReady)
            return FinishAuditResponse(owner, record, requestHash, grant);
        if (record.AuditState == DurableAuditState.Blocked) return Result(record, record.Code);
        if (record.AuditState == DurableAuditState.RequestIntent)
            return AuditFailure(owner, record, new(AiAuditErrorKind.Transport, "Interrupted audit has no bound provider response."));
        if (record.RetryAtUtc is { } due && due > _clock.GetUtcNow()) return Result(record, PreparationCode.AuditRetryScheduled);
        var budget = policy.Definition.Audit;
        var calls = _state.List().Where(r => r.Policy == policy.Reference).SelectMany(r => r.Calls).ToArray();
        if (record.Calls.Count >= budget.MaxAttempts || calls.Length >= budget.MaxRequests ||
            (calls.Length + 1) * grant.MaximumCostPerRequest > budget.MaxCost)
            return Park(owner, item, record, PreparationCode.AuditBudgetExhausted);
        var dependencyCalls = _state.List().Where(r => r.AuditGrant?.Hash == grant.Hash).SelectMany(r => r.Calls)
            .OrderByDescending(c => c.FinishedUtc ?? c.StartedUtc).ToArray();
        if (dependencyCalls.Any(c => c.Error is AiAuditErrorKind.Unauthorized or AiAuditErrorKind.Forbidden or AiAuditErrorKind.Configuration))
            return Park(owner, item, record, PreparationCode.AuditDependencyBlocked);
        var recent = dependencyCalls.TakeWhile(c => c.State != AuditCallState.Response).ToArray();
        var lastFailure = recent.FirstOrDefault(c => c.State == AuditCallState.TransientFailure);
        if (recent.Count(c => c.State == AuditCallState.TransientFailure) >= grant.CircuitFailureThreshold &&
            lastFailure!.FinishedUtc!.Value.AddSeconds(grant.CircuitOpenSeconds) > _clock.GetUtcNow())
        {
            record = Save(owner, record with { AuditState = DurableAuditState.RetryScheduled, Code = PreparationCode.AuditRetryScheduled,
                RetryAtUtc = lastFailure.FinishedUtc!.Value.AddSeconds(grant.CircuitOpenSeconds) }, QueueStage.Audit);
            return Result(record, PreparationCode.AuditRetryScheduled);
        }
        var call = new DurableAuditCall("audit_" + Guid.NewGuid().ToString("N"), record.Calls.Count + 1, _clock.GetUtcNow(),
            AuditCallState.Started, null, null, null);
        record = Save(owner, record with { Calls = record.Calls.Append(call).ToArray(), AuditState = DurableAuditState.RequestIntent,
            RequestHash = requestHash, RequestJson = requestJson, AuditGrant = grant, RetryAtUtc = null, Code = PreparationCode.Pending }, QueueStage.Audit);
        Checkpoint?.Invoke("audit-request-intent");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancel.CancelAfter(TimeSpan.FromSeconds(budget.TimeoutSeconds));
        try
        {
            var report = client.AuditAsync(request, cancel.Token).WaitAsync(TimeSpan.FromSeconds(budget.TimeoutSeconds), token).GetAwaiter().GetResult();
            Checkpoint?.Invoke("audit-response-received");
            if (report is null) throw new AiAuditClientException(AiAuditErrorKind.InvalidResponse, "No validated audit report.");
            var verified = new AiAuditReport(report.Decision, report.Summary, report.Findings);
            var receipt = new DurableAuditReceipt(1, call.RequestId, itemId, record.AttemptId, record.JobId, _context.Id,
                snapshot.Hash, requestHash, item.Policy, grant.Hash, client.ProviderId, client.ModelId, _clock.GetUtcNow(),
                verified.Decision, verified.Summary, verified.Findings);
            var bytes = Encoding.UTF8.GetBytes(AutomationJson.Encode(receipt));
            var inventory = new[] { new AdmissionFile("report.json", AutomationJson.HashBytes(bytes), bytes.LongLength) };
            var bundle = _files.Describe(record, "audit", "automation/preparation/audit/" + itemId.Value + "/" + call.RequestId, inventory);
            record = Save(owner, record with { AuditState = DurableAuditState.ResponseReady, ExpectedAuditReceipt = receipt, AuditResponse = bundle }, QueueStage.Audit);
            Checkpoint?.Invoke("audit-response-bound");
            return FinishAuditResponse(owner, record, requestHash, grant);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancel.Cancel(); throw; }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        { cancel.Cancel(); return AuditFailure(owner, record, new(AiAuditErrorKind.Timeout, "Audit deadline elapsed.")); }
        catch (AiAuditClientException failure) { return AuditFailure(owner, record, failure); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException)
        { return AuditFailure(owner, record, new(AiAuditErrorKind.InvalidResponse, "Audit report was inconsistent.")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ProductionStorageException)
        { return Park(owner, _queue.Get(itemId), record, PreparationCode.StorageUnavailable); }
    }
    private PreparationResult FinishAuditResponse(AutomationQueueOwner owner, PreparationRecord record, Sha256Digest requestHash,
        PreparationOperationalGrant grant)
    {
        try
        {
            var fresh = Prepare(owner, record.ItemId);
            if (fresh.Code != PreparationCode.Planned) return fresh;
            var policy = _queue.GetPolicy(record.Policy);
            if (!policy.IsPreparationAuthorized || policy.Authorization is not { } authorization ||
                !policy.Definition.Operations.HasFlag(AutomationOperations.Audit) || _queue.CurrentPolicy().Reference != record.Policy ||
                authorization.AuthorizationId != grant.PolicyAuthorizationId || authorization.Actor != grant.Actor)
                return Park(owner, _queue.Get(record.ItemId), record, PreparationCode.PolicyInactive);
            var expected = record.ExpectedAuditReceipt ?? throw new InvalidDataException("No durable provider response.");
            if (expected.RequestId != record.Calls.Last().RequestId || expected.ItemId != record.ItemId || expected.AttemptId != record.AttemptId ||
                expected.JobId != record.JobId || expected.UniverseId != _context.Id || expected.Policy != record.Policy || expected.SnapshotHash != record.Snapshot!.Hash ||
                expected.RequestHash != requestHash || expected.GrantHash != grant.Hash || expected.ProviderId != grant.ProviderId || expected.ModelId != grant.ModelId ||
                expected.SchemaVersion != 1 || expected.RespondedUtc.Offset != TimeSpan.Zero)
                throw new InvalidDataException("Audit response scope or snapshot no longer agrees.");
            expected.ValidateReport();
            if (record.AuditState == DurableAuditState.ResponseReady)
            {
                var bytes = Encoding.UTF8.GetBytes(AutomationJson.Encode(expected));
                _files.Publish(record, "audit", record.AuditResponse!, new Dictionary<string, string>(),
                    new Dictionary<string, byte[]> { ["report.json"] = bytes }, _queue.GetPolicy(record.Policy).Definition.Resources.MaxStagingBytes,
                    default, Checkpoint);
                var call = record.Calls.Last() with { State = AuditCallState.Response, FinishedUtc = expected.RespondedUtc };
                var state = expected.Decision switch { AiAuditDecision.Pass => DurableAuditState.Pass, AiAuditDecision.Warning => DurableAuditState.Warning, _ => DurableAuditState.Fail };
                var code = expected.Decision switch { AiAuditDecision.Pass => PreparationCode.AuditPass, AiAuditDecision.Warning => PreparationCode.AuditWarning, _ => PreparationCode.AuditFail };
                record = Save(owner, record with { Calls = record.Calls.SkipLast(1).Append(call).ToArray(), AuditState = state, Code = code }, QueueStage.Audit);
                Checkpoint?.Invoke("audit-decision-bound");
            }
            _files.Verify(record, "audit", record.AuditResponse!);
            var responsePath = Path.Combine(_files.Resolve(record.AuditResponse!), "report.json");
            var receipt = AutomationJson.Decode<DurableAuditReceipt>(File.ReadAllText(responsePath));
            if (AutomationJson.Encode(receipt) != AutomationJson.Encode(expected)) throw new InvalidDataException("Audit response differs from its bound contract.");
            if (record.AuditState != DurableAuditState.Pass) return Park(owner, _queue.Get(record.ItemId), record, record.Code);
            return Result(record, PreparationCode.AuditPass);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or ProductionStorageException or System.Text.Json.JsonException)
        { return Park(owner, _queue.Get(record.ItemId), record, PreparationCode.EvidenceChanged); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return Park(owner, _queue.Get(record.ItemId), record, PreparationCode.StorageUnavailable); }
    }
    private PreparationResult AuditFailure(AutomationQueueOwner owner, PreparationRecord record, AiAuditClientException failure)
    {
        var current = GetRecord(record.ItemId)!;
        if (current.AuditState != DurableAuditState.RequestIntent) throw new InvalidDataException("A committed response must not be replaced by transport failure.");
        var transient = failure.Code is AiAuditErrorKind.Transport or AiAuditErrorKind.RateLimited or AiAuditErrorKind.Server or AiAuditErrorKind.Timeout;
        var call = current.Calls.Last() with { State = transient ? AuditCallState.TransientFailure : AuditCallState.PermanentFailure,
            Error = failure.Code, HttpStatus = failure.HttpStatus, FinishedUtc = _clock.GetUtcNow() };
        record = Save(owner, current with { Calls = current.Calls.SkipLast(1).Append(call).ToArray() }, QueueStage.Audit);
        var policy = _queue.GetPolicy(record.Policy);
        if (transient && record.Calls.Count < policy.Definition.Audit.MaxAttempts)
        {
            var delay = TimeSpan.FromSeconds(record.AuditGrant!.RetryDelaySeconds[record.Calls.Count - 1]);
            if (failure.RetryAfter is { } after && after > delay)
            {
                if (after > TimeSpan.FromDays(7)) return Park(owner, _queue.Get(record.ItemId), record, PreparationCode.AuditInvalidResponse);
                delay = after;
            }
            record = Save(owner, record with { AuditState = DurableAuditState.RetryScheduled, Code = PreparationCode.AuditRetryScheduled,
                RetryAtUtc = _clock.GetUtcNow() + delay }, QueueStage.Audit);
            return Result(record, PreparationCode.AuditRetryScheduled);
        }
        var code = transient ? PreparationCode.AuditBudgetExhausted :
            failure.Code is AiAuditErrorKind.Unauthorized or AiAuditErrorKind.Forbidden or AiAuditErrorKind.Configuration ? PreparationCode.AuditDependencyBlocked : PreparationCode.AuditInvalidResponse;
        record = Save(owner, record with { AuditState = DurableAuditState.Blocked, Code = code }, QueueStage.Audit);
        return Park(owner, _queue.Get(record.ItemId), record, code);
    }
    private string EffectiveRoot(PreparationRecord record)
    {
        var admitted = _admission.VerifyAdmitted(record.ItemId);
        return Path.Combine(_files.Resolve(record.Candidate ?? record.Originals!), "package", admitted.AssetId!);
    }
}
