namespace NAP.Core;

/// <summary>Pure normal progression and terminal failure; does not execute or recover jobs.</summary>
public sealed class JobStateMachine
{
    public bool CanTransition(JobState from, JobState to)
    {
        if (!Enum.IsDefined(from))
            throw new ArgumentOutOfRangeException(nameof(from));
        if (!Enum.IsDefined(to))
            throw new ArgumentOutOfRangeException(nameof(to));
        return from is not (JobState.Completed or JobState.Failed) &&
            (to == JobState.Failed || (int)to == (int)from + 1);
    }

    public JobStateRecord Create(JobId jobId, UniverseId universeId) =>
        new(jobId, universeId, JobState.Detected);

    public JobStateRecord Transition(JobStateRecord current, JobState nextState)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!Enum.IsDefined(nextState))
            throw new ArgumentOutOfRangeException(nameof(nextState));
        if (!CanTransition(current.State, nextState))
            throw new InvalidOperationException($"Invalid Job state transition from '{JobStateTokens.ToToken(current.State)}' to '{JobStateTokens.ToToken(nextState)}'.");
        return new JobStateRecord(current.JobId, current.UniverseId, nextState);
    }
}
