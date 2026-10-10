namespace NAP.Core;

/// <summary>
/// Copies ready Inbox package candidates into a staging directory without modifying the original file.
/// </summary>
public sealed class InboxPackageStager
{
    private readonly InboxPackageReadinessChecker _readinessChecker;

    /// <summary>
    /// Creates a stager with the default readiness checker.
    /// </summary>
    public InboxPackageStager()
        : this(new InboxPackageReadinessChecker())
    {
    }

    /// <summary>
    /// Creates a stager that uses the specified readiness checker.
    /// </summary>
    /// <param name="readinessChecker">The checker used before every staging attempt.</param>
    public InboxPackageStager(InboxPackageReadinessChecker readinessChecker)
    {
        ArgumentNullException.ThrowIfNull(readinessChecker);
        _readinessChecker = readinessChecker;
    }

    /// <summary>
    /// Stages a ready package by copying it through an operation-specific partial file.
    /// </summary>
    /// <param name="candidate">The Inbox package candidate to stage.</param>
    /// <param name="stagingRoot">The configured root directory for staging.</param>
    /// <param name="cancellationToken">A token that can cancel the copy operation.</param>
    /// <returns>The outcome of the staging attempt.</returns>
    public async Task<InboxPackageStagingResult> StageAsync(
        InboxPackageCandidate candidate,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.FullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        var sourcePath = Path.GetFullPath(candidate.FullPath);
        var normalizedStagingRoot = Path.GetFullPath(stagingRoot);
        var finalFileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(finalFileName))
        {
            throw new ArgumentException("The candidate path must identify a file.", nameof(candidate));
        }

        Directory.CreateDirectory(normalizedStagingRoot);

        var finalStagedPath = Path.Combine(normalizedStagingRoot, finalFileName);
        EnsureSourceAndDestinationDiffer(sourcePath, finalStagedPath);

        var readinessResult = await _readinessChecker.CheckAsync(candidate, cancellationToken);
        if (!readinessResult.IsReady)
        {
            return new InboxPackageStagingResult(
                InboxPackageStagingStatus.NotReady,
                sourcePath,
                finalStagedPath,
                readinessResult.Status);
        }

        if (File.Exists(finalStagedPath))
        {
            return new InboxPackageStagingResult(
                InboxPackageStagingStatus.Collision,
                sourcePath,
                finalStagedPath);
        }

        var partialPath = Path.Combine(
            normalizedStagingRoot,
            $".{finalFileName}.{Guid.NewGuid():N}.partial");
        var finalCreatedByThisOperation = false;

        try
        {
            long copiedLength;

            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var partial = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await CopyBytesAsync(source, partial, long.MaxValue, cancellationToken);
                await partial.FlushAsync(cancellationToken);
                copiedLength = partial.Length;

                if (source.Length != copiedLength)
                {
                    throw new IOException("The staged copy length does not match the source length.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                File.Move(partialPath, finalStagedPath, overwrite: false);
                finalCreatedByThisOperation = true;
            }
            catch (IOException) when (File.Exists(finalStagedPath))
            {
                return new InboxPackageStagingResult(
                    InboxPackageStagingStatus.Collision,
                    sourcePath,
                    finalStagedPath);
            }

            if (!File.Exists(finalStagedPath) || new FileInfo(finalStagedPath).Length != copiedLength)
            {
                throw new IOException("The final staged file could not be verified.");
            }

            return new InboxPackageStagingResult(
                InboxPackageStagingStatus.Staged,
                sourcePath,
                finalStagedPath);
        }
        catch
        {
            if (finalCreatedByThisOperation && File.Exists(finalStagedPath))
            {
                File.Delete(finalStagedPath);
            }

            throw;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static void EnsureSourceAndDestinationDiffer(string sourcePath, string destinationPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(sourcePath, destinationPath, comparison))
        {
            throw new InvalidOperationException("The staging destination must not be the source file.");
        }
    }

    // Admission owns and keeps the source handle open across hashing and copying. The public manual
    // API retains its readiness contract; this adapter removes its separate-handle race.
    internal static async Task StageSnapshotAsync(Stream source, string destination, long maxBytes, CancellationToken token)
    {
        var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        var created = false;
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                await CopyBytesAsync(source, output, maxBytes, token);
                await output.FlushAsync(token); output.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(partial, destination, overwrite: false);
        }
        finally { if (created && File.Exists(partial)) File.Delete(partial); } // only this invocation's CreateNew temporary
    }
    private static async Task CopyBytesAsync(Stream source, Stream output, long maxBytes, CancellationToken token)
    {
        var buffer = new byte[81920]; long bytes = 0; int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        {
            if (count > maxBytes - bytes) throw new InvalidDataException("Snapshot exceeds its byte budget.");
            bytes += count; await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }
}
