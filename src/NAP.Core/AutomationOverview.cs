using static NAP.Core.AutomationQueueSchema;

namespace NAP.Core;

/// <summary>Display-only counts of unique durable items, never counts of observations or Inbox probes.</summary>
public sealed record AutomationQueueCounts(int Pending, int Processing, int Completed, int NeedsReview, int Duplicates);
public sealed record AutomationReviewItem(QueueItemId ItemId, string FileName, QueueIncident Incident, DateTimeOffset UpdatedUtc, bool SourcePresent = true);
public sealed record AutomationRecentItem(WorkflowRow Item, QueueStage Stage, string? Preparation, string? AuditModel,
    string? AuditSummary, string? AuditError, string? ProductionPath, string? ArchivePath, bool Archived);
public sealed record AutomationQueueOverview(UniverseId UniverseId, DateTimeOffset ObservedUtc,
    AutomationQueueCounts Counts, IReadOnlyList<AutomationReviewItem> Review, StoredAutomationPolicy Policy,
    IReadOnlyList<AutomationRecentItem>? Recent = null, int ArchivedReview = 0);
public sealed record AutomationDesktopOverview(AutomationHostSnapshot Host, AutomationQueueOverview Queue, AutomationHostControl Control);

public sealed partial class AutomationQueueStore
{
    /// <summary>One validated, read-only snapshot. Bounded details; no repair, claims or ledger writes.</summary>
    public AutomationQueueOverview Overview(IReadOnlyList<string>? archived = null) => Read(db =>
    {
        using var tx = db.BeginTransaction(deferred: true);
        var hidden = System.Text.Json.JsonSerializer.Serialize(archived ?? []);
        var counts = new Dictionary<QueueState, int>();
        using (var cmd = Command(db, tx, "SELECT state,COUNT(*) FROM items WHERE id NOT IN (SELECT value FROM json_each($hidden)) GROUP BY state", "$hidden", hidden))
        using (var rows = cmd.ExecuteReader())
            while (rows.Read()) counts.Add(Enum.Parse<QueueState>(rows.GetString(0)), rows.GetInt32(1));
        int Count(params QueueState[] states) => states.Sum(s => counts.GetValueOrDefault(s));
        var totals = new AutomationQueueCounts(Count(QueueState.Observed, QueueState.WaitingStable, QueueState.Queued, QueueState.RetryScheduled),
            Count(QueueState.Running), Count(QueueState.Completed), Count(QueueState.NeedsReview, QueueState.Rejected, QueueState.MissingSource), Count(QueueState.Duplicate));
        var review = Payloads<QueueItem>(db, tx,
            "SELECT payload FROM items WHERE state IN ('NeedsReview','Rejected','MissingSource') AND id NOT IN (SELECT value FROM json_each($hidden)) ORDER BY rowid DESC LIMIT 8", "$hidden", hidden)
            .Select(i => new AutomationReviewItem(i.Id, Path.GetFileName(i.SourcePath), i.Incident, i.UpdatedUtc, File.Exists(i.SourcePath))).ToArray();
        var archivedCount = Convert.ToInt32(Scalar(db, tx, "SELECT COUNT(*) FROM items WHERE state IN ('NeedsReview','Rejected','MissingSource') AND id IN (SELECT value FROM json_each($hidden))", "$hidden", hidden));
        return new AutomationQueueOverview(UniverseId, DateTimeOffset.UtcNow, totals, review, CurrentPolicy(db, tx), ArchivedReview: archivedCount);
    });

    public AutomationQueueOverview DesktopOverview(UniverseContext context, IReadOnlyList<string> archived)
    {
        var overview = Overview(archived);
        var reader = new WorkflowDesktopReader(context, this);
        var recent = Browse(new WorkflowPageRequest(Limit: 8), archived).Items.Select(item =>
        {
            var stage = Events(item.Id).LastOrDefault()?.Stage ?? QueueStage.Observation;
            PreparationRecord? preparation = null;
            try { preparation = new PreparationStateStore(context.Storage).Get(item.Id); }
            catch (AutomationException e) when (e.Code == AutomationError.Missing) { }
            return new AutomationRecentItem(reader.Row(item), stage, preparation?.Code.ToString(), preparation?.AuditGrant?.ModelId,
                preparation?.ExpectedAuditReceipt?.Summary, preparation?.Calls.LastOrDefault()?.Error?.ToString(),
                preparation?.Snapshot is { } snapshot ? Path.Combine(context.Storage.ProductionRoot, snapshot.Plan.DestinationRelativeDirectory) : null,
                preparation?.Snapshot is { } archive ? Path.Combine(context.Storage.ArchiveRoot, archive.Plan.ArchiveRelativeDirectory) : null,
                false);
        }).ToArray();
        return overview with { Recent = recent };
    }
}
