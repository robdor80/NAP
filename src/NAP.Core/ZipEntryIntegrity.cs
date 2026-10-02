using System.Buffers.Binary;

namespace NAP.Core;

// .NET 8 does not expose ZipArchiveEntry's CRC or verify it while reading.
// Read only the central-directory CRC fields; ZipArchive remains responsible for extraction.
internal static class ZipEntryIntegrity
{
    private static readonly uint[] Table = CreateTable();

    internal static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = Table[(crc ^ value) & 255] ^ (crc >> 8);
        }
        return crc;
    }

    internal static uint[] ReadChecksums(Stream source, int maxEntries, CancellationToken token)
    {
        var tail = new byte[(int)Math.Min(source.Length, 22 + ushort.MaxValue)];
        source.Position = source.Length - tail.Length;
        ReadExactly(source, tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length)
            {
                end = i;
                break;
            }
        }
        if (end < 0)
        {
            throw new InvalidDataException("ZIP end record is missing.");
        }

        if (U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0)
        {
            throw new RejectedPackageException("Split ZIP archives are unsupported.");
        }
        ulong entryCount = U16(tail, end + 10);
        ulong offset = U32(tail, end + 16);
        if (offset == uint.MaxValue || U16(tail, end + 10) == ushort.MaxValue ||
            U32(tail, end + 12) == uint.MaxValue)
        {
            // ZIP64 locator immediately precedes the ordinary end record.
            var locatorOffset = source.Length - tail.Length + end - 20;
            if (locatorOffset < 0)
            {
                throw new InvalidDataException("ZIP64 locator is missing.");
            }
            source.Position = locatorOffset;
            var locator = new byte[20];
            ReadExactly(source, locator);
            if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
            {
                throw new InvalidDataException("ZIP64 locator is missing.");
            }
            source.Position = CheckedOffset(U64(locator, 8), source.Length, 56);
            var zip64 = new byte[56];
            ReadExactly(source, zip64);
            if (U32(zip64, 0) != 0x06064b50 || U32(zip64, 16) != 0 || U32(zip64, 20) != 0 ||
                U64(zip64, 24) != U64(zip64, 32))
            {
                throw new InvalidDataException("ZIP64 end record is invalid.");
            }
            entryCount = U64(zip64, 32);
            offset = U64(zip64, 48);
        }
        else if (U16(tail, end + 8) != entryCount)
        {
            throw new InvalidDataException("ZIP entry counts disagree.");
        }

        if (entryCount > (ulong)maxEntries)
        {
            throw new RejectedPackageException("ZIP entry count limit exceeded.");
        }

        source.Position = CheckedOffset(offset, source.Length, 0);
        var checksums = new uint[(int)entryCount];
        var header = new byte[46];
        for (var index = 0; index < checksums.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            ReadExactly(source, header);
            if (U32(header, 0) != 0x02014b50)
            {
                throw new InvalidDataException("ZIP central directory is invalid.");
            }
            checksums[index] = U32(header, 16);
            var skip = U16(header, 28) + U16(header, 30) + U16(header, 32);
            source.Position = CheckedOffset((ulong)source.Position + (ulong)skip, source.Length, 0);
        }
        return checksums;
    }

    private static long CheckedOffset(ulong offset, long length, int requiredBytes)
    {
        if (offset > (ulong)length || (ulong)requiredBytes > (ulong)length - offset)
        {
            throw new InvalidDataException("ZIP directory offset is outside the archive.");
        }
        return (long)offset;
    }

    private static void ReadExactly(Stream source, byte[] buffer)
    {
        try
        {
            source.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("ZIP directory is truncated.", ex);
        }
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static ulong U64(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320;
            }
            table[i] = value;
        }
        return table;
    }
}
