namespace NAP.Core;

/// <summary>
/// Configures the sampling used to check whether an Inbox package is stable.
/// </summary>
public sealed record InboxPackageReadinessOptions
{
    /// <summary>
    /// Gets the number of filesystem samples required to establish stability.
    /// </summary>
    public int RequiredSamples { get; init; } = 2;

    /// <summary>
    /// Gets the interval between filesystem samples.
    /// </summary>
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(1);
}
