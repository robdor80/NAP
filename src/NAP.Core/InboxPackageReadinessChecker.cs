namespace NAP.Core;

/// <summary>
/// Performs a bounded filesystem check to determine whether an Inbox package appears ready for a later stage.
/// </summary>
public sealed class InboxPackageReadinessChecker
{
    private readonly InboxPackageReadinessOptions _options;

    /// <summary>
    /// Creates a readiness checker with the default sampling options.
    /// </summary>
    public InboxPackageReadinessChecker()
        : this(new InboxPackageReadinessOptions())
    {
    }

    /// <summary>
    /// Creates a readiness checker with explicit sampling options.
    /// </summary>
    /// <param name="options">The sampling options to use.</param>
    public InboxPackageReadinessChecker(InboxPackageReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.RequiredSamples < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least two samples are required to establish file stability.");
        }

        if (options.SampleInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sample interval cannot be negative.");
        }

        _options = options;
    }

    /// <summary>
    /// Checks whether a candidate exists, remains stable across samples and can be opened exclusively for reading.
    /// </summary>
    /// <param name="candidate">The candidate to check.</param>
    /// <param name="cancellationToken">A token that can cancel the bounded check.</param>
    /// <returns>A result describing the observed readiness state.</returns>
    public async Task<InboxPackageReadinessResult> CheckAsync(
        InboxPackageCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.FullPath);

        var fullPath = Path.GetFullPath(candidate.FullPath);
        FileSample? previousSample = null;

        for (var sampleIndex = 0; sampleIndex < _options.RequiredSamples; sampleIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sample = TryGetFileSample(fullPath);
            if (sample is null)
            {
                return new InboxPackageReadinessResult(InboxPackageReadinessStatus.Missing);
            }

            if (previousSample is not null && sample != previousSample)
            {
                return new InboxPackageReadinessResult(InboxPackageReadinessStatus.Changing);
            }

            previousSample = sample;

            if (sampleIndex < _options.RequiredSamples - 1)
            {
                await Task.Delay(_options.SampleInterval, cancellationToken);
            }
        }

        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return new InboxPackageReadinessResult(InboxPackageReadinessStatus.Ready);
        }
        catch (FileNotFoundException)
        {
            return new InboxPackageReadinessResult(InboxPackageReadinessStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new InboxPackageReadinessResult(InboxPackageReadinessStatus.Missing);
        }
        catch (IOException)
        {
            return new InboxPackageReadinessResult(InboxPackageReadinessStatus.InUse);
        }
    }

    private static FileSample? TryGetFileSample(string fullPath)
    {
        try
        {
            _ = File.GetAttributes(fullPath);
            var fileInfo = new FileInfo(fullPath);
            return new FileSample(fileInfo.Length, fileInfo.LastWriteTimeUtc);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private readonly record struct FileSample(long Length, DateTime LastWriteTimeUtc);
}
