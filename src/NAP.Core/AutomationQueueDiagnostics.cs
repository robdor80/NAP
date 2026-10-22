using static NAP.Core.AutomationQueueSchema;
namespace NAP.Core;

public sealed partial class AutomationQueueStore
{
    /// <summary>Read-only, bounded keyset page. No asset paths, IDs, payload strings or mutation receipts leave this projection.</summary>
    public DiagnosticPage BrowseDiagnostics(DiagnosticQuery query)
    {
        if (query.From > query.Until || query.Before is < 1 || query.Through is < 0 ||
            query.Severity is { } severity && !Enum.IsDefined(severity) || query.Category is { } category && !Enum.IsDefined(category))
            throw AutomationValidation.Invalid("Invalid diagnostic filters.");
        return Read(db =>
        {
            using var tx = db.BeginTransaction(deferred: true);
            var max = Convert.ToInt64(Scalar(db, tx, "SELECT coalesce(max(rowid),0) FROM events"));
            var through = query.Through ?? max;
            if (through > max) throw AutomationValidation.Invalid("Diagnostic cursor is outside this universe.");
            // Use the queue's canonical string enums, with bound filter parameters.
            var level = "CASE WHEN json_extract(payload,'$.Result')='Rejected' THEN 2 WHEN json_extract(payload,'$.Result')='ReviewRequired' OR json_extract(payload,'$.Incident')<>'None' THEN 1 ELSE 0 END";
            using var cmd = Command(db, tx, "SELECT rowid,payload FROM events WHERE rowid<=$t AND ($b IS NULL OR rowid<$b) " +
                "AND ($f IS NULL OR julianday(json_extract(payload,'$.Utc'))>=julianday($f)) AND ($u IS NULL OR julianday(json_extract(payload,'$.Utc'))<julianday($u)) " +
                "AND ($s IS NULL OR " + level + "=$s) AND ($c IS NULL OR json_extract(payload,'$.Stage')=$c) ORDER BY rowid DESC LIMIT 31",
                "$t", through, "$b", query.Before, "$f", query.From?.ToString("O"), "$u", query.Until?.ToString("O"), "$s", query.Severity is null ? null : (int)query.Severity, "$c", query.Category is null ? null : query.Category.Value.ToString());
            using var rows = cmd.ExecuteReader(); var events = new List<DiagnosticEvent>();
            while (rows.Read())
            {
                var e = AutomationJson.Decode<QueueEvent>(rows.GetString(1));
                events.Add(new(rows.GetInt64(0), e.Utc, e.Stage, e.Result == QueueEventResult.Rejected ? DiagnosticSeverity.Error : e.Result == QueueEventResult.ReviewRequired || e.Incident != QueueIncident.None ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information, e.Code, e.Result, e.State, e.Incident));
            }
            return new DiagnosticPage(UniverseId, events.Take(30).ToArray(), events.Count > 30, through);
        });
    }
}
