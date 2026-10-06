namespace NAP.Core;

public enum JobState
{
    Detected = 0,
    Staged = 1,
    Validated = 2,
    Planned = 3,
    Audited = 4,
    Executed = 5,
    Verified = 6,
    Completed = 7,
    Failed = 8
}
