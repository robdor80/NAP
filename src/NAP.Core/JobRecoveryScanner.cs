namespace NAP.Core;

/// <summary>Read-only, point-in-time recovery discovery; never executes, advances or repairs jobs.</summary>
public sealed class JobRecoveryScanner
{
    private readonly UniverseContext _context;
    private readonly JobStateStore _store;

    public JobRecoveryScanner(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _store = new JobStateStore(context);
    }

    public JobRecoverySnapshot Scan()
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(_context.Storage.StateRoot); }
        catch (FileNotFoundException) { return Empty(new NapIssueReport([])); }
        catch (DirectoryNotFoundException) { return Empty(new NapIssueReport([])); }
        var rootIssue = ReparseIssue(attributes, null);
        if (rootIssue is not null)
            return Empty(new NapIssueReport([rootIssue]));

        // Metadata supplied by the top-level enumeration; foreign entries are not opened or inspected.
        var entries = new DirectoryInfo(_context.Storage.StateRoot).EnumerateFileSystemInfos()
            .Where(entry => IsJobNamespace(entry.Name))
            .Select(entry => (entry.Name, entry.Attributes))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
        var finalIds = new HashSet<JobId>();
        foreach (var entry in entries)
            if ((entry.Attributes & FileAttributes.Directory) == 0 && TryFinalId(entry.Name, out var id))
                finalIds.Add(id!);

        var recoverable = new List<JobStateRecord>();
        var completed = new List<JobStateRecord>();
        var failed = new List<JobStateRecord>();
        var temps = new List<JobRecoveryTempArtifact>();
        var issues = new List<NapIssue>();
        foreach (var entry in entries)
        {
            var reparse = ReparseIssue(entry.Attributes, entry.Name);
            if (reparse is not null) { issues.Add(reparse); continue; }
            if ((entry.Attributes & FileAttributes.Directory) != 0) continue;
            if (entry.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                if (!TryFinalId(entry.Name, out var id))
                {
                    issues.Add(Error(NapIssueCodes.JobRecoveryInvalidJournalName,
                        "The Job journal filename is not canonical.", entry.Name));
                    continue;
                }
                var record = LoadJournal(id!, entry.Name, issues);
                if (record is null) continue;
                if (record.State == JobState.Completed) completed.Add(record);
                else if (record.State == JobState.Failed) failed.Add(record);
                else recoverable.Add(record);
            }
            else
            {
                JobRecoveryTempArtifact temp;
                try
                {
                    var id = new JobId(entry.Name.Length >= 36 ? entry.Name[..36] : entry.Name);
                    temp = new JobRecoveryTempArtifact(id, entry.Name, finalIds.Contains(id));
                }
                catch (ArgumentException)
                {
                    issues.Add(Error(NapIssueCodes.JobRecoveryInvalidTempName,
                        "The Job temporary filename is not canonical.", entry.Name));
                    continue;
                }
                temps.Add(temp);
                issues.Add(temp.HasFinalJournal
                    ? new NapIssue(NapIssueCodes.JobRecoveryOrphanTemp, NapIssueSeverity.Warning,
                        NapIssueDisposition.Continue, "An orphan Job state temporary file remains beside a final journal.", entry.Name)
                    : Error(NapIssueCodes.JobRecoveryTempWithoutJournal,
                        "A Job state temporary file has no authoritative final journal.", entry.Name));
            }
        }
        return new JobRecoverySnapshot(recoverable, completed, failed, temps, new NapIssueReport(issues));
    }

    private JobStateRecord? LoadJournal(JobId jobId, string fileName, List<NapIssue> issues)
    {
        try { return _store.Load(jobId); }
        catch (InvalidDataException)
        {
            issues.Add(Error(NapIssueCodes.JobRecoveryInvalidJournal, "The persisted Job journal is invalid.", fileName));
        }
        catch (FileNotFoundException)
        {
            issues.Add(Error(NapIssueCodes.JobRecoveryJournalChanged, "The Job journal changed during recovery discovery.", fileName));
        }
        return null;
    }

    private static bool IsJobNamespace(string name) => name.StartsWith("job_", StringComparison.Ordinal) &&
        (name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal));

    private static bool TryFinalId(string name, out JobId? id)
    {
        id = null;
        if (!name.EndsWith(".json", StringComparison.Ordinal)) return false;
        try { id = new JobId(name[..^5]); return true; }
        catch (ArgumentException) { return false; }
    }

    private static NapIssue? ReparseIssue(FileAttributes attributes, string? fileName) =>
        (attributes & FileAttributes.ReparsePoint) == 0 ? null : fileName is null
            ? Error(NapIssueCodes.JobRecoveryStateRootReparse, "The Job state root is a reparse point.", null)
            : Error(NapIssueCodes.JobRecoveryEntryReparse, "A Job recovery entry is a reparse point.", fileName);

    private static NapIssue Error(string code, string message, string? fileName) =>
        new(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, fileName);

    private static JobRecoverySnapshot Empty(NapIssueReport issues) => new([], [], [], [], issues);
}
