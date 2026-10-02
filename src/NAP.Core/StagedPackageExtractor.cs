using System.IO.Compression;

namespace NAP.Core;

/// <summary>
/// Extracts a staged ZIP into a private temporary directory, then publishes under the ZIP stem.
/// The extraction root must be controlled by NAP: concurrent external filesystem mutation is unsupported.
/// Cancellation and filesystem errors propagate after temporary cleanup.
/// </summary>
public sealed class StagedPackageExtractor
{
    private readonly StagedPackageExtractionOptions _options;

    public StagedPackageExtractor(StagedPackageExtractionOptions? options = null)
    {
        _options = options ?? new StagedPackageExtractionOptions();
        if (_options.MaxEntries <= 0 || _options.MaxEntryBytes <= 0 || _options.MaxTotalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Extraction limits must be positive.");
        }
    }

    public async Task<StagedPackageExtractionResult> ExtractAsync(
        string stagedZipPath,
        string extractionRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedZipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var zipPath = Path.GetFullPath(stagedZipPath);
        var root = Path.GetFullPath(extractionRoot);
        var folderName = Path.GetFileNameWithoutExtension(zipPath);
        if (string.IsNullOrWhiteSpace(folderName) || folderName is "." or "..")
        {
            throw new ArgumentException("The staged ZIP needs a valid file name.", nameof(stagedZipPath));
        }

        var finalPath = Path.Combine(root, folderName);
        string? temporaryPath = null;
        try
        {
            if (IsUnsafeSegment(folderName))
            {
                throw new RejectedPackageException("The staged ZIP stem is not a safe extraction directory name.");
            }
            RejectReparseAncestors(root);
            Directory.CreateDirectory(root);
            RejectReparseAncestors(root);
            if (HasFinalCollision(root, folderName))
            {
                return new(StagedPackageExtractionStatus.Collision, zipPath, Reason: "Extraction destination already exists.");
            }

            // The ZIP is read only; no entry may choose its own extraction root.
            using var source = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Bound the declared count before ZipArchive allocates entry objects.
            var checksums = ZipEntryIntegrity.ReadChecksums(source, _options.MaxEntries, cancellationToken);
            source.Position = 0;
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
            var entries = ValidateEntries(archive, finalPath, cancellationToken);
            if (checksums.Length != entries.Count)
            {
                throw new InvalidDataException("ZIP entry counts disagree.");
            }

            var candidateTemporaryPath = Path.Combine(root, $".nap-{Guid.NewGuid():N}.extracting");
            // Never adopt or clean a preexisting directory, even in the event of a GUID collision.
            if (File.Exists(candidateTemporaryPath) || Directory.Exists(candidateTemporaryPath))
            {
                throw new IOException("Operation temporary destination already exists.");
            }
            Directory.CreateDirectory(candidateTemporaryPath);
            temporaryPath = candidateTemporaryPath;
            var expectedBytes = 0L;
            var writtenEntries = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(temporaryPath, entry.RelativePath));
                EnsureContained(temporaryPath, destination);
                if (entry.IsDirectory)
                {
                    if (checksums[writtenEntries] != 0)
                    {
                        throw new InvalidDataException("ZIP directory entry has a nonempty CRC.");
                    }
                    Directory.CreateDirectory(destination);
                    writtenEntries++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var input = OpenEntry(entry.Source);
                var buffer = new byte[81920];
                long written = 0;
                var crc = uint.MaxValue;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    if (count > _options.MaxEntryBytes - written || count > _options.MaxTotalBytes - expectedBytes)
                    {
                        throw new RejectedPackageException("ZIP uncompressed size limit exceeded.");
                    }

                    written += count;
                    expectedBytes += count;
                    crc = ZipEntryIntegrity.Update(crc, buffer.AsSpan(0, count));
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }

                if (written != entry.Source.Length || ~crc != checksums[writtenEntries])
                {
                    throw new InvalidDataException("ZIP entry length or CRC does not match the extracted bytes.");
                }

                writtenEntries++;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (writtenEntries != entries.Count)
            {
                throw new InvalidDataException("Not all ZIP entries were extracted.");
            }

            EnsureContained(root, temporaryPath);
            RejectReparseAncestors(root);
            if (HasFinalCollision(root, folderName))
            {
                return new(StagedPackageExtractionStatus.Collision, zipPath, Reason: "Extraction destination already exists.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(temporaryPath, finalPath);
            }
            catch (IOException) when (HasFinalCollision(root, folderName))
            {
                return new(StagedPackageExtractionStatus.Collision, zipPath, Reason: "Extraction destination already exists.");
            }

            temporaryPath = null;
            return new(StagedPackageExtractionStatus.Extracted, zipPath, finalPath);
        }
        catch (RejectedPackageException ex)
        {
            return new(StagedPackageExtractionStatus.Rejected, zipPath, Reason: ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return new(StagedPackageExtractionStatus.InvalidArchive, zipPath, Reason: ex.Message);
        }
        finally
        {
            if (temporaryPath is not null && Directory.Exists(temporaryPath))
            {
                Directory.Delete(temporaryPath, recursive: true);
            }
        }
    }

    private static Stream OpenEntry(ZipArchiveEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (EndOfStreamException ex)
        {
            // .NET 8 can report a truncated local header as EndOfStreamException.
            throw new InvalidDataException("ZIP local header is truncated.", ex);
        }
    }

    private List<ValidatedEntry> ValidateEntries(ZipArchive archive, string finalPath, CancellationToken cancellationToken)
    {
        if (archive.Entries.Count > _options.MaxEntries)
        {
            throw new RejectedPackageException("ZIP entry count limit exceeded.");
        }

        var entries = new List<ValidatedEntry>(archive.Entries.Count);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directorySpelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = entry.FullName.Replace('\\', '/');
            if (raw.Length == 0 || raw.StartsWith('/') || raw.Contains('\0'))
            {
                throw new RejectedPackageException("ZIP entry has an unsafe path.");
            }

            var isDirectory = raw.EndsWith('/');
            var parts = (isDirectory ? raw[..^1] : raw).Split('/');
            if (parts.Any(IsUnsafeSegment))
            {
                throw new RejectedPackageException("ZIP entry has an unsafe path.");
            }

            // Resolve the destination as well as enforcing conservative Windows-compatible names.
            EnsureContained(finalPath, Path.GetFullPath(Path.Combine(finalPath, Path.Combine(parts))));

            var relative = string.Join('/', parts);
            if (!explicitPaths.Add(relative))
            {
                throw new RejectedPackageException("ZIP entries have colliding paths.");
            }

            // Unix type bits are reliable when present. Reject links and all other special types.
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((unixType != 0 && unixType != (isDirectory ? 0x4000 : 0x8000)) ||
                (entry.ExternalAttributes & 0x400) != 0 ||
                (!isDirectory && (entry.ExternalAttributes & 0x10) != 0))
            {
                throw new RejectedPackageException("ZIP entry has an unsupported file type.");
            }

            if (isDirectory)
            {
                if (entry.Length != 0 || files.Contains(relative))
                {
                    throw new RejectedPackageException("ZIP entries have a file/directory collision.");
                }
                CheckDirectorySpelling(relative, directorySpelling);
                directories.Add(relative);
            }
            else
            {
                if (directories.Contains(relative) || !files.Add(relative))
                {
                    throw new RejectedPackageException("ZIP entries have a file/directory collision.");
                }
                if (entry.Length > _options.MaxEntryBytes ||
                    declaredTotal > _options.MaxTotalBytes - entry.Length)
                {
                    throw new RejectedPackageException("ZIP uncompressed size limit exceeded.");
                }
                declaredTotal += entry.Length;
            }

            for (var i = 1; i < parts.Length; i++)
            {
                var parent = string.Join('/', parts[..i]);
                if (files.Contains(parent))
                {
                    throw new RejectedPackageException("ZIP entries have a file/directory collision.");
                }
                CheckDirectorySpelling(parent, directorySpelling);
                directories.Add(parent);
            }

            entries.Add(new(entry, Path.Combine(parts), isDirectory));
        }

        return entries;
    }

    private static void CheckDirectorySpelling(string path, Dictionary<string, string> spellings)
    {
        if (spellings.TryGetValue(path, out var original) && !string.Equals(path, original, StringComparison.Ordinal))
        {
            throw new RejectedPackageException("ZIP entries have case-colliding directory paths.");
        }
        spellings[path] = path;
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.IndexOfAny(new[] { ':', '<', '>', '"', '|', '?', '*' }) >= 0 ||
            segment.Any(char.IsControl))
        {
            return true;
        }

        var stem = segment.Split('.')[0].TrimEnd(' ');
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                                     stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (stem[3] is >= '1' and <= '9' || stem[3] is '¹' or '²' or '³'));
    }

    private static void EnsureContained(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, comparison))
        {
            throw new RejectedPackageException("ZIP entry escapes the extraction directory.");
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(directory.FullName);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new RejectedPackageException("Extraction root must not traverse links or reparse points.");
            }
        }
    }

    private static bool HasFinalCollision(string root, string folderName) =>
        Directory.EnumerateFileSystemEntries(root)
            .Any(path => string.Equals(Path.GetFileName(path), folderName, StringComparison.OrdinalIgnoreCase));

    private sealed record ValidatedEntry(ZipArchiveEntry Source, string RelativePath, bool IsDirectory);
}

internal sealed class RejectedPackageException(string message) : Exception(message);
