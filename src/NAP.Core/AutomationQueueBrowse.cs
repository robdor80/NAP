using static NAP.Core.AutomationQueueSchema;
namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    public (int Total, IReadOnlyList<QueueItem> Items) Browse(WorkflowPageRequest request)
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
        var where = states + " AND ($b IS NULL OR EXISTS (SELECT 1 FROM observations o WHERE o.item_id=items.id AND o.batch_id=$b)) AND ($s='' OR instr(lower(substr(json_extract(payload,'$.SourcePath'),length($r)+2)),lower($s))>0)";
        return Read(db =>
        {
            using var tx = db.BeginTransaction(deferred: true);
            var total = Convert.ToInt32(Scalar(db, tx, "SELECT COUNT(*) FROM items WHERE " + where, "$b", request.Batch?.Value, "$s", request.Search, "$r", Path.TrimEndingDirectorySeparator(_roots.InboxRoot)));
            var items = Payloads<QueueItem>(db, tx, "SELECT payload FROM items WHERE " + where + " ORDER BY json_extract(payload,'$.UpdatedUtc') DESC,rowid DESC LIMIT $l OFFSET $o",
                "$b", request.Batch?.Value, "$s", request.Search, "$r", Path.TrimEndingDirectorySeparator(_roots.InboxRoot), "$l", request.Limit, "$o", request.Offset);
            return (total, items);
        });
    }
    public IReadOnlyList<WorkflowBatch> BrowseWorkflowBatches() => Read(db =>
    {
        // Cohorts already belong to the queue; browsing must not initialize a runtime ledger.
        using var tx=db.BeginTransaction(deferred:true);
        var batches=Payloads<QueueBatch>(db,tx,"SELECT payload FROM batches ORDER BY rowid DESC LIMIT 40");
        return batches.Select(b=>new WorkflowBatch(UniverseId,b.Id,b.CreatedUtc,
            Convert.ToInt32(Scalar(db,tx,"SELECT COUNT(DISTINCT item_id) FROM observations WHERE batch_id=$b","$b",b.Id.Value)))).ToArray();
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
    public (IReadOnlyList<QueueEvent> Events, IReadOnlyList<ExceptionalConfigurationDecision> Decisions, long EventThrough, long DecisionThrough)
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
            return(events,decisions,eventsThrough,decisionsThrough);
        });
    }
    public IReadOnlyList<QueueEvent> BrowseEvents(QueueItemId item, int offset)
    {
        if (offset < 0) throw AutomationValidation.Invalid("Invalid event offset.");
        return Read(db => { Item(db, null, item); return Payloads<QueueEvent>(db, null,
            "SELECT payload FROM events WHERE item_id=$i ORDER BY sequence DESC LIMIT 21 OFFSET $o", "$i", item.Value, "$o", offset); });
    }
}
