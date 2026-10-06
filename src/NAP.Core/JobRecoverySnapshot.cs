namespace NAP.Core;

/// <summary>An immutable, ordered view of durable checkpoints and recovery diagnostics.</summary>
public sealed class JobRecoverySnapshot
{
    public JobRecoverySnapshot(IEnumerable<JobStateRecord> recoverableJobs,
        IEnumerable<JobStateRecord> completedJobs, IEnumerable<JobStateRecord> failedJobs,
        IEnumerable<JobRecoveryTempArtifact> orphanTemps, NapIssueReport issues)
    {
        ArgumentNullException.ThrowIfNull(recoverableJobs);
        ArgumentNullException.ThrowIfNull(completedJobs);
        ArgumentNullException.ThrowIfNull(failedJobs);
        ArgumentNullException.ThrowIfNull(orphanTemps);
        ArgumentNullException.ThrowIfNull(issues);
        var recoverable = recoverableJobs.ToArray();
        var completed = completedJobs.ToArray();
        var failed = failedJobs.ToArray();
        var temps = orphanTemps.ToArray();
        var jobIds = new HashSet<JobId>();
        UniverseId? universe = null;
        Validate(recoverable, state => state is not (JobState.Completed or JobState.Failed), nameof(recoverableJobs));
        Validate(completed, state => state == JobState.Completed, nameof(completedJobs));
        Validate(failed, state => state == JobState.Failed, nameof(failedJobs));
        if (temps.Any(temp => temp is null))
            throw new ArgumentException("Orphan temps cannot contain null elements.", nameof(orphanTemps));
        RecoverableJobs = Array.AsReadOnly(recoverable.OrderBy(job => job.JobId.Value, StringComparer.Ordinal).ToArray());
        CompletedJobs = Array.AsReadOnly(completed.OrderBy(job => job.JobId.Value, StringComparer.Ordinal).ToArray());
        FailedJobs = Array.AsReadOnly(failed.OrderBy(job => job.JobId.Value, StringComparer.Ordinal).ToArray());
        OrphanTemps = Array.AsReadOnly(temps.OrderBy(temp => temp.FileName, StringComparer.Ordinal).ToArray());
        Issues = issues;

        void Validate(JobStateRecord[] records, Func<JobState, bool> allowed, string parameter)
        {
            foreach (var record in records)
            {
                if (record is null || !allowed(record.State))
                    throw new ArgumentException("A Job must be non-null and in the collection's state category.", parameter);
                if (!jobIds.Add(record.JobId))
                    throw new ArgumentException("A Job ID cannot appear more than once across the Job collections.", parameter);
                universe ??= record.UniverseId;
                if (universe != record.UniverseId)
                    throw new ArgumentException("All recovered Jobs must belong to the same universe.", parameter);
            }
        }
    }

    public IReadOnlyList<JobStateRecord> RecoverableJobs { get; }
    public IReadOnlyList<JobStateRecord> CompletedJobs { get; }
    public IReadOnlyList<JobStateRecord> FailedJobs { get; }
    public IReadOnlyList<JobRecoveryTempArtifact> OrphanTemps { get; }
    public NapIssueReport Issues { get; }
}
