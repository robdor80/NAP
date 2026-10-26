using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

/// <summary>Display-only counts of unique durable items, never counts of observations or Inbox probes.</summary>
public sealed record AutomationQueueCounts(int Pending, int Processing, int Completed, int NeedsReview, int Duplicates);
public sealed record AutomationReviewItem(QueueItemId ItemId, string FileName, QueueIncident Incident, DateTimeOffset UpdatedUtc);
public sealed record AutomationQueueOverview(UniverseId UniverseId, DateTimeOffset ObservedUtc,
    AutomationQueueCounts Counts, IReadOnlyList<AutomationReviewItem> Review, StoredAutomationPolicy Policy);
public sealed record AutomationDesktopOverview(AutomationHostSnapshot Host, AutomationQueueOverview Queue, AutomationHostControl Control);

public sealed partial class AutomationQueueStore
{
    /// <summary>One validated, read-only snapshot. Bounded details; no repair, claims or ledger writes.</summary>
    public AutomationQueueOverview Overview() => Read(db =>
    {
        using var tx = db.BeginTransaction(deferred: true);
        var counts = new Dictionary<QueueState, int>();
        using (var cmd = Command(db, tx, "SELECT state,COUNT(*) FROM items GROUP BY state"))
        using (var rows = cmd.ExecuteReader())
            while (rows.Read()) counts.Add(Enum.Parse<QueueState>(rows.GetString(0)), rows.GetInt32(1));
        int Count(params QueueState[] states) => states.Sum(s => counts.GetValueOrDefault(s));
        var totals = new AutomationQueueCounts(Count(QueueState.Observed, QueueState.WaitingStable, QueueState.Queued, QueueState.RetryScheduled),
            Count(QueueState.Running), Count(QueueState.Completed), Count(QueueState.NeedsReview, QueueState.Rejected, QueueState.MissingSource), Count(QueueState.Duplicate));
        var review = Payloads<QueueItem>(db, tx,
            "SELECT payload FROM items WHERE state IN ('NeedsReview','Rejected','MissingSource') ORDER BY rowid DESC LIMIT 8")
            .Select(i => new AutomationReviewItem(i.Id, Path.GetFileName(i.SourcePath), i.Incident, i.UpdatedUtc)).ToArray();
        return new AutomationQueueOverview(UniverseId, DateTimeOffset.UtcNow, totals, review, CurrentPolicy(db, tx));
    });
}
