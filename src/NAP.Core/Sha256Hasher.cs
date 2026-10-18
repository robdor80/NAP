using System.Security.Cryptography;
using System.Buffers;

namespace NAP.Core;

/// <summary>Computes SHA-256 of exact input bytes without writes or workflow decisions.</summary>
public sealed class Sha256Hasher
{
    /// <summary>Reads the entire file with a caller-supplied path and disposes its internal stream.</summary>
    public Sha256Digest Compute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Compute(stream);
    }

    /// <summary>Consumes the readable stream from its current position to EOF; leaves it open and does not seek.</summary>
    public Sha256Digest Compute(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("The stream must be readable.", nameof(stream));
        // The framework stream helper reads 4 KiB at a time. Fresh whole-ledger hashing
        // at every boundary made large batches issue millions of tiny OS reads.
        // A bounded pooled buffer still consumes every byte, including short reads,
        // without retaining data, seeking or owning the caller's stream.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int count;
            while ((count = stream.Read(buffer, 0, 128 * 1024)) != 0) hash.AppendData(buffer, 0, count);
            return new Sha256Digest(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
