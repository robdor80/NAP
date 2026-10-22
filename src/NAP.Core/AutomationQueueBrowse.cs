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
        var where = states + " AND ($b IS NULL OR batch_id=$b) AND ($s='' OR instr(lower(json_extract(payload,'$.SourcePath')),lower($s))>0)";
        return Read(db =>
        {
            using var tx = db.BeginTransaction(deferred: true);
            var total = Convert.ToInt32(Scalar(db, tx, "SELECT COUNT(*) FROM items WHERE " + where, "$b", request.Batch?.Value, "$s", request.Search));
            var items = Payloads<QueueItem>(db, tx, "SELECT payload FROM items WHERE " + where + " ORDER BY json_extract(payload,'$.UpdatedUtc') DESC,rowid DESC LIMIT $l OFFSET $o",
                "$b", request.Batch?.Value, "$s", request.Search, "$l", request.Limit, "$o", request.Offset);
            return (total, items);
        });
    }
    public IReadOnlyList<QueueEvent> BrowseEvents(QueueItemId item, int offset)
    {
        if (offset < 0) throw AutomationValidation.Invalid("Invalid event offset.");
        return Read(db => { Item(db, null, item); return Payloads<QueueEvent>(db, null,
            "SELECT payload FROM events WHERE item_id=$i ORDER BY sequence DESC LIMIT 21 OFFSET $o", "$i", item.Value, "$o", offset); });
    }
}
