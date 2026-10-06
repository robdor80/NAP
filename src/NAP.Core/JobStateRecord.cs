namespace NAP.Core;

/// <summary>An immutable last-known Job state, independent of assets and execution metadata.</summary>
public sealed record JobStateRecord
{
    public JobStateRecord(JobId jobId, UniverseId universeId, JobState state)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(universeId);
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        JobId = jobId;
        UniverseId = universeId;
        State = state;
    }

    public JobId JobId { get; }
    public UniverseId UniverseId { get; }
    public JobState State { get; }
}
