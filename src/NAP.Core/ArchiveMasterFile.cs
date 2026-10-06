namespace NAP.Core;

/// <summary>A frozen original package file; paths remain internal to NAP, outside the AI audit request.</summary>
public sealed class ArchiveMasterFile
{
    internal ArchiveMasterFile(string role, string sourcePath, string fileName, string destinationPath, Sha256Digest digest, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(digest);
        if (!ArchivePaths.SafeSegment(fileName)) throw new ArgumentException("Unsafe package filename.", nameof(fileName));
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        Role = role;
        SourcePath = sourcePath;
        FileName = fileName;
        DestinationPath = destinationPath;
        Digest = digest;
        SizeBytes = sizeBytes;
    }

    public string Role { get; }
    public string SourcePath { get; }
    public string FileName { get; }
    public string DestinationPath { get; }
    public Sha256Digest Digest { get; }
    public long SizeBytes { get; }
}
