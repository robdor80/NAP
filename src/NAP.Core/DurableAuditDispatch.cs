namespace NAP.Core;

public sealed record PreparationAuditStep(PreparationResult Result, DurableAuditDispatch? Dispatch);

/// <summary>Immutable HTTP facts, not an ownership capability. Only its originating owner may accept completion.</summary>
public sealed class DurableAuditDispatch
{
    public QueueItemId ItemId { get; }
    public WorkflowAttemptId AttemptId { get; }
    public JobId JobId { get; }
    public UniverseId UniverseId { get; }
    public long OwnerEpoch { get; }
    public string RequestId { get; }
    public Sha256Digest SnapshotHash { get; }
    public Sha256Digest RequestHash { get; }
    public Sha256Digest GrantHash { get; }
    public string ProviderId { get; }
    public string ModelId { get; }
    public TimeSpan Timeout { get; }
    public AiAuditRequest Request { get; }
    internal DurableAuditDispatch(QueueItemId item, WorkflowAttemptId attempt, JobId job, UniverseId universe, long epoch,
        string requestId, Sha256Digest snapshot, Sha256Digest requestHash, Sha256Digest grantHash, string provider, string model,
        AiAuditRequest request, TimeSpan timeout)
    { ItemId = item; AttemptId = attempt; JobId = job; UniverseId = universe; OwnerEpoch = epoch; RequestId = requestId;
        SnapshotHash = snapshot; RequestHash = requestHash; GrantHash = grantHash; ProviderId = provider; ModelId = model; Request = request; Timeout = timeout; }
}

public sealed record AuditTransportOutcome(AiAuditReport? Report, AiAuditClientException? Failure, bool Cancelled,
    Task RemoteCompletion);

public static class AutomationAuditTransport
{
    public static async Task<AuditTransportOutcome> SendAsync(DurableAuditDispatch dispatch, IIdentifiedAiAuditClient client,
        CancellationToken cancellation = default, TimeProvider? clock = null)
    {
        if (client.ProviderId != dispatch.ProviderId || client.ModelId != dispatch.ModelId) throw new ArgumentException("Audit transport identity differs.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task<AiAuditReport> remote = Task.FromCanceled<AiAuditReport>(new CancellationToken(true));
        try
        {
            cancellation.ThrowIfCancellationRequested();
            remote = client.AuditAsync(dispatch.Request, stop.Token);
            // Observe exceptions from clients which finish after a local timeout/cancellation. No ledger callback runs here.
            _ = remote.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var report = await remote.WaitAsync(dispatch.Timeout, clock ?? TimeProvider.System, cancellation).ConfigureAwait(false);
            return new(report, null, false, remote);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { stop.Cancel(); return new(null, null, true, remote); }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        { stop.Cancel(); return new(null, new(AiAuditErrorKind.Timeout, "Local deadline elapsed; remote completion is unknown."), false, remote); }
        catch (AiAuditClientException failure) { return new(null, failure, false, remote); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { return new(null, new(AiAuditErrorKind.InvalidResponse, "Invalid provider response."), false, remote); }
    }
}
