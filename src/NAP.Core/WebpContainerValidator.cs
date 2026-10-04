using System.Buffers.Binary;

namespace NAP.Core;

/// <summary>Checks only existing RIFF/WEBP container bounds; pixel decode remains mandatory.</summary>
internal static class WebpContainerValidator
{
    internal static bool IsComplete(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ||
            (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8 != bytes.Length)
            return false;
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8) return false;
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            var chunkSize = 8L + length + (length & 1);
            if (chunkSize > bytes.Length - offset) return false;
            offset += (int)chunkSize;
        }
        return true;
    }
}
