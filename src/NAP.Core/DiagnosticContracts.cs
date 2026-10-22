namespace NAP.Core;

public enum DiagnosticSeverity { Information, Warning, Error }
public sealed record DiagnosticQuery(DateTimeOffset? From = null, DateTimeOffset? Until = null,
    DiagnosticSeverity? Severity = null, QueueStage? Category = null, long? Before = null, long? Through = null);
public sealed record DiagnosticEvent(long Cursor, DateTimeOffset Utc, QueueStage Category, DiagnosticSeverity Severity,
    QueueEventCode Code, QueueEventResult Result, QueueState State, QueueIncident Incident);
public sealed record DiagnosticPage(UniverseId UniverseId, IReadOnlyList<DiagnosticEvent> Events, bool More, long Through);
