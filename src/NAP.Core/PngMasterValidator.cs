using System.Buffers.Binary;
using System.Text;

namespace NAP.Core;

/// <summary>
/// Inspects a static PNG container and chunk integrity with bounded memory, without decoding IDAT.
/// Filesystem errors propagate. Stream validation starts at the current position and leaves ownership to the caller.
/// </summary>
public sealed class PngMasterValidator
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public PngValidationResult Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Validate(stream);
    }

    public PngValidationResult Validate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The PNG stream must be readable.", nameof(stream));
        }

        try
        {
            return new(PngValidationStatus.Valid, Inspect(stream));
        }
        catch (EndOfStreamException)
        {
            return new(PngValidationStatus.Invalid, Reason: "PNG is truncated or lacks a required chunk.");
        }
        catch (InvalidDataException ex)
        {
            return new(PngValidationStatus.Invalid, Reason: ex.Message);
        }
        catch (UnsupportedPngException ex)
        {
            return new(PngValidationStatus.UnsupportedFeature, Reason: ex.Message);
        }
    }

    private static PngImageInfo Inspect(Stream stream)
    {
        var header = new byte[8];
        stream.ReadExactly(header);
        if (!header.AsSpan().SequenceEqual(Signature))
        {
            throw new InvalidDataException("PNG signature is invalid.");
        }

        var buffer = new byte[8192];
        var ihdr = new byte[13];
        var crcBytes = new byte[4];
        PngImageInfo? info = null;
        var hasPalette = false;
        var hasIdat = false;
        var idatEnded = false;
        var hasImageData = false;

        while (true)
        {
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > int.MaxValue)
            {
                throw new InvalidDataException("PNG chunk length exceeds the format limit.");
            }
            if (stream.CanSeek && (stream.Length - stream.Position < 4 ||
                length > stream.Length - stream.Position - 4))
            {
                throw new InvalidDataException("PNG chunk payload or CRC is truncated.");
            }

            var typeBytes = header.AsSpan(4, 4);
            foreach (var value in typeBytes)
            {
                if (value is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                {
                    throw new InvalidDataException("PNG chunk type must contain four ASCII letters.");
                }
            }
            if (typeBytes[2] is not (>= (byte)'A' and <= (byte)'Z'))
            {
                throw new InvalidDataException("PNG chunk type has a nonzero reserved bit.");
            }
            var type = Encoding.ASCII.GetString(typeBytes);
            if (info is null && type != "IHDR")
            {
                throw new InvalidDataException("IHDR must be the first PNG chunk.");
            }
            if (type == "IHDR" && (info is not null || length != 13))
            {
                throw new InvalidDataException("IHDR must occur exactly once and have length 13.");
            }

            var crc = PngChunkCrc32.Update(uint.MaxValue, typeBytes);
            if (type == "IHDR")
            {
                stream.ReadExactly(ihdr);
                crc = PngChunkCrc32.Update(crc, ihdr);
            }
            else
            {
                var remaining = length;
                while (remaining > 0)
                {
                    var count = (int)Math.Min(remaining, (uint)buffer.Length);
                    stream.ReadExactly(buffer.AsSpan(0, count));
                    crc = PngChunkCrc32.Update(crc, buffer.AsSpan(0, count));
                    remaining -= (uint)count;
                }
            }
            stream.ReadExactly(crcBytes);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(crcBytes))
            {
                throw new InvalidDataException($"PNG chunk {type} has an invalid CRC.");
            }

            switch (type)
            {
                case "IHDR":
                    info = ReadImageInfo(ihdr);
                    break;
                case "PLTE":
                    if (hasPalette || hasIdat || info!.ColorType is 0 or 4 ||
                        length == 0 || length % 3 != 0 || length > 768 ||
                        (info.ColorType == 3 && length / 3 > (1U << info.BitDepth)))
                    {
                        throw new InvalidDataException("PLTE length, order or color-type relationship is invalid.");
                    }
                    hasPalette = true;
                    break;
                case "IDAT":
                    if (idatEnded || (info!.ColorType == 3 && !hasPalette))
                    {
                        throw new InvalidDataException("IDAT chunks must be consecutive and indexed images require PLTE first.");
                    }
                    hasIdat = true;
                    hasImageData |= length > 0;
                    break;
                case "IEND":
                    if (length != 0 || !hasIdat || !hasImageData)
                    {
                        throw new InvalidDataException("IEND must be empty and follow nonempty image data.");
                    }
                    if (stream.ReadByte() != -1)
                    {
                        throw new InvalidDataException("PNG contains bytes after IEND.");
                    }
                    return info!;
                case "acTL":
                case "fcTL":
                case "fdAT":
                    throw new UnsupportedPngException("APNG animation chunks are not supported for static masters.");
                default:
                    if ((typeBytes[0] & 32) == 0)
                    {
                        throw new UnsupportedPngException($"Unknown critical PNG chunk: {type}.");
                    }
                    break;
            }
            if (hasIdat && type != "IDAT")
            {
                idatEnded = true;
            }
        }
    }

    private static PngImageInfo ReadImageInfo(byte[] ihdr)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4));
        var depth = ihdr[8];
        var colorType = ihdr[9];
        var validDepth = colorType switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false
        };
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue || !validDepth ||
            ihdr[10] != 0 || ihdr[11] != 0 || ihdr[12] > 1)
        {
            throw new InvalidDataException("IHDR dimensions, color/depth combination or methods are invalid.");
        }
        return new((int)width, (int)height, depth, colorType, ihdr[12]);
    }

    private sealed class UnsupportedPngException(string message) : Exception(message);
}
