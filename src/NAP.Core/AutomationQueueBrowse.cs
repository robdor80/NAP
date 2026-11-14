using static NAP.Core.AutomationQueueSchema;
namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    public (int Total, IReadOnlyList<QueueItem> Items) Browse(WorkflowPageRequest request, IReadOnlyList<string>? archived = null)
    {
        if (request.Offset < 0 || request.Limit is < 1 or > 40 || request.Search is null || request.Search.Length > 80 || !Enum.IsDefined(request.Filter))
            throw AutomationValidation.Invalid("Invalid bounded workflow query.");
        var states = request.Filter switch
        {
            WorkflowFilter.Pending => "state IN ('Observed','WaitingStable','Queued','RetryScheduled')",
            WorkflowFilter.Processing => "state='Running'",
            WorkflowFilter.Completed => "state IN ('Completed','Duplicate')",
            WorkflowFilter.Review => "state IN ('NeedsReview','Rejected','MissingSource')",
            _ => "1=1"
        };
        var exclusions = System.Text.Json.JsonSerializer.Serialize(archived ?? []);
        var archiveFilter = request.Filter == WorkflowFilter.Archived ? " AND id IN (SELECT value FROM json_each($hidden))" :
            " AND id NOT IN (SELECT value FROM json_each($hidden))";
        var where = states + archiveFilter + " AND ($b IS NULL OR EXISTS (SELECT 1 FROM observations o WHERE o.item_id=items.id AND o.batch_id=$b)) AND ($s='' OR instr(lower(substr(json_extract(payload,'$.SourcePath'),length($r)+2)),lower($s))>0)";
        return Read(db =>
        {
            using var tx = db.BeginTransaction(deferred: true);
            var total = Convert.ToInt32(Scalar(db, tx, "SELECT COUNT(*) FROM items WHERE " + where, "$b", request.Batch?.Value, "$s", request.Search, "$r", Path.TrimEndingDirectorySeparator(_roots.InboxRoot), "$hidden", exclusions));
            var items = Payloads<QueueItem>(db, tx, "SELECT payload FROM items WHERE " + where + " ORDER BY json_extract(payload,'$.UpdatedUtc') DESC,rowid DESC LIMIT $l OFFSET $o",
                "$b", request.Batch?.Value, "$s", request.Search, "$r", Path.TrimEndingDirectorySeparator(_roots.InboxRoot), "$l", request.Limit, "$o", request.Offset, "$hidden", exclusions);
            return (total, items);
        });
    }
    public IReadOnlyList<WorkflowBatch> BrowseWorkflowBatches(IReadOnlyList<string>? archived = null) => Read(db =>
    {
        // Cohorts already belong to the queue; browsing must not initialize a runtime ledger.
        using var tx=db.BeginTransaction(deferred:true);
        var hidden = System.Text.Json.JsonSerializer.Serialize(archived ?? []);
        var batches=Payloads<QueueBatch>(db,tx,"SELECT payload FROM batches ORDER BY rowid DESC LIMIT 40");
        return batches.Select(b =>
        {
            // Read progress in the same read transaction as the batch list; no second
            // ledger, no double counting when one ZIP has repeated observations.
            var items = Payloads<QueueItem>(db, tx,
                "SELECT DISTINCT i.payload FROM items i JOIN observations o ON o.item_id=i.id WHERE o.batch_id=$b AND i.id NOT IN (SELECT value FROM json_each($hidden))", "$b", b.Id.Value, "$hidden", hidden);
            var observations = Convert.ToInt32(Scalar(db, tx,
                "SELECT COUNT(*) FROM observations WHERE batch_id=$b AND item_id NOT IN (SELECT value FROM json_each($hidden))", "$b", b.Id.Value, "$hidden", hidden));
            var summary = new QueueBatchSummary(observations, items.Count,
                items.Count(i => i.State == QueueState.Completed),
                items.Count(i => i.State == QueueState.Duplicate),
                items.Count(i => i.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.MissingSource),
                items.Count(i => i.State is QueueState.Observed or QueueState.WaitingStable or QueueState.Queued or QueueState.Running or QueueState.RetryScheduled));
            return new WorkflowBatch(UniverseId, b.Id, b.CreatedUtc, items.Count, summary);
        }).ToArray();
    });
    public IReadOnlyList<ExceptionalConfigurationDecision> BrowseDecisions(QueueItemId item, int offset)
    {
        if(offset<0)throw AutomationValidation.Invalid("Invalid decision offset.");
        return Read(db=>{Item(db,null,item);return Payloads<ExceptionalConfigurationDecision>(db,null,
            "SELECT payload FROM decisions WHERE item_id=$i ORDER BY json_extract(payload,'$.Utc') DESC,version DESC,rowid DESC LIMIT 21 OFFSET $o","$i",item.Value,"$o",offset);});
    }
    public ExceptionalConfigurationDecision? LatestReviewDecision(QueueItemId item) => Read(db=>
    {
        Item(db,null,item);
        return Payloads<ExceptionalConfigurationDecision>(db,null,
            "SELECT payload FROM decisions WHERE item_id=$i AND decision_id=(SELECT decision_id FROM decisions WHERE item_id=$i AND version=1 ORDER BY json_extract(payload,'$.Utc') DESC,rowid DESC LIMIT 1) ORDER BY version DESC LIMIT 1",
            "$i",item.Value).SingleOrDefault();
    });
    public (IReadOnlyList<QueueEvent> Events, IReadOnlyList<ExceptionalConfigurationDecision> Decisions, long EventThrough, long DecisionThrough, bool MoreEvents, bool MoreDecisions)
        BrowseWorkflowActivity(WorkflowDetailRequest request)
    {
        if(request.EventOffset<0||request.DecisionOffset<0||request.EventThrough is <0||request.DecisionThrough is <0)
            throw AutomationValidation.Invalid("Invalid activity page.");
        return Read(db=>
        {
            using var tx=db.BeginTransaction(deferred:true);Item(db,tx,request.ItemId);
            var eventMax=Convert.ToInt64(Scalar(db,tx,"SELECT coalesce(max(sequence),0) FROM events WHERE item_id=$i","$i",request.ItemId.Value));
            var decisionMax=Convert.ToInt64(Scalar(db,tx,"SELECT coalesce(max(rowid),0) FROM decisions WHERE item_id=$i","$i",request.ItemId.Value));
            var eventsThrough=request.EventThrough??eventMax;var decisionsThrough=request.DecisionThrough??decisionMax;
            if(eventsThrough>eventMax||decisionsThrough>decisionMax)throw AutomationValidation.Invalid("Activity cursor is outside this asset.");
            // Stable upper bounds keep new records from shifting an open reader's pages.
            var events=Payloads<QueueEvent>(db,tx,"SELECT payload FROM events WHERE item_id=$i AND sequence<=$t ORDER BY sequence DESC LIMIT 21 OFFSET $o",
                "$i",request.ItemId.Value,"$t",eventsThrough,"$o",request.EventOffset);
            var decisions=Payloads<ExceptionalConfigurationDecision>(db,tx,"SELECT payload FROM decisions WHERE item_id=$i AND rowid<=$t ORDER BY json_extract(payload,'$.Utc') DESC,version DESC,rowid DESC LIMIT 21 OFFSET $o",
                "$i",request.ItemId.Value,"$t",decisionsThrough,"$o",request.DecisionOffset);
            return(events.Take(20).ToArray(),decisions.Take(20).ToArray(),eventsThrough,decisionsThrough,events.Count>20,decisions.Count>20);
        });
    }
    public IReadOnlyList<QueueEvent> BrowseEvents(QueueItemId item, int offset)
    {
        if (offset < 0) throw AutomationValidation.Invalid("Invalid event offset.");
        return Read(db => { Item(db, null, item); return Payloads<QueueEvent>(db, null,
            "SELECT payload FROM events WHERE item_id=$i ORDER BY sequence DESC LIMIT 21 OFFSET $o", "$i", item.Value, "$o", offset); });
    }
}
