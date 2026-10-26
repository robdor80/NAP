using static NAP.Core.AutomationQueueSchema;
namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    /// <summary>Stable event keyset cursor. Includes late completions of old items; read transaction ends before projection/Git.</summary>
    internal IReadOnlyList<(long Sequence, QueueItemId Item)> GitCompletions(long after, int limit)
    {
        if (after < 0 || limit is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limit));
        return Read(db =>
        {
            using var command = Command(db, null,
                "SELECT rowid,item_id FROM events WHERE rowid>$s AND json_extract(payload,'$.State')='Completed' AND json_extract(payload,'$.Code')='Transitioned' ORDER BY rowid LIMIT $l", "$s", after, "$l", limit);
            using var rows = command.ExecuteReader(); var results = new List<(long, QueueItemId)>();
            while (rows.Read()) results.Add((rows.GetInt64(0), new(rows.GetString(1))));
            return (IReadOnlyList<(long, QueueItemId)>)results;
        });
    }
}
