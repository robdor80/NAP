using System.Security.Cryptography;

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
        return new Sha256Digest(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }
}
