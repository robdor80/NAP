namespace NAP.Core;

/// <summary>Explicit, culture-independent v1 journal vocabulary.</summary>
internal static class JobStateTokens
{
    internal static string ToToken(JobState state) => state switch
    {
        JobState.Detected => "DETECTED",
        JobState.Staged => "STAGED",
        JobState.Validated => "VALIDATED",
        JobState.Planned => "PLANNED",
        JobState.Audited => "AUDITED",
        JobState.Executed => "EXECUTED",
        JobState.Verified => "VERIFIED",
        JobState.Completed => "COMPLETED",
        JobState.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    internal static JobState FromToken(string token) => token switch
    {
        "DETECTED" => JobState.Detected,
        "STAGED" => JobState.Staged,
        "VALIDATED" => JobState.Validated,
        "PLANNED" => JobState.Planned,
        "AUDITED" => JobState.Audited,
        "EXECUTED" => JobState.Executed,
        "VERIFIED" => JobState.Verified,
        "COMPLETED" => JobState.Completed,
        "FAILED" => JobState.Failed,
        _ => throw new InvalidDataException("The persisted Job state token is invalid.")
    };
}
