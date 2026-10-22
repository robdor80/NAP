namespace NAP.Core;

public sealed partial class AutomationHost
{
    private void RequireReviewAuthority(Slot slot, AutomationUniverseScope scope, QueueItem item, bool continuing)
    {
        if (_stopping || slot.Status.State == AutomationHostState.Blocked) throw new AutomationException(AutomationError.Disabled, "Review is unavailable while stopping or blocked.");
        RequireGrant(slot, scope);
        var policy = scope.Queue.CurrentPolicy();
        if (item.UniverseId != scope.Context.Id || item.Policy != policy.Reference || !policy.Definition.Operations.HasFlag(AutomationOperations.Normalize))
            throw new AutomationException(AutomationError.PolicyMismatch, "Review requires this item's current authorized normalization policy.");
        lock (_controlsGate)
        {
            var control = Controls.Read(scope.Context.Id);
            if (control.Stopped || control.CancelledItems.Contains(item.Id.Value) || control.Grant is not { } grant ||
                !(grant.ResumeOnRestart || control.StartedSession == SessionId) || continuing && control.Paused)
                throw new AutomationException(AutomationError.Disabled, "The individual continuation is inhibited.");
        }
        if (item.State != QueueState.NeedsReview) throw new AutomationException(AutomationError.IllegalTransition, "Only an individually parked asset can be reviewed.");
    }
    public Task<ReviewCapability> ReviewCapabilitiesAsync(UniverseId universe, QueueItemId itemId) => QueryAsync(universe, s =>
    {
        var slot = Find(universe); var item = s.Queue.Get(itemId);
        try
        {
            RequireReviewAuthority(slot, s, item, false);
            var continuing = true;
            try { RequireReviewAuthority(slot, s, item, true); } catch (AutomationException) { continuing = false; }
            return new ReviewCapability(true, true, continuing);
        }
        catch (AutomationException) { return new ReviewCapability(false, false, false); }
    });
    /// <summary>Same heavy-work budget and owner thread as the scheduler. Other assets are not paused.
    /// An accepted effect is not cancelled by a UI disconnect; every operation rechecks the exact grant.</summary>
    public async Task<T> RunReviewAsync<T>(UniverseId universe, QueueItemId itemId, bool continuing,
        Func<AutomationUniverseScope, DurablePreparationService, T> operation)
    {
        await Started.ConfigureAwait(false); var slot = Find(universe);
        await _heavy.WaitAsync().ConfigureAwait(false);
        try
        {
            return await slot.Runtime!.InvokeAsync(s =>
            {
                RequireReviewAuthority(slot, s, s.Queue.Get(itemId), continuing);
                return operation(s, slot.Preparation!);
            }).ConfigureAwait(false);
        }
        finally { _heavy.Release(); Signal(); }
    }
}
