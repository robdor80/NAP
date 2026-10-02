namespace NAP.Core;

// Kept local to PNG validation so the ZIP extractor and its integrity implementation remain unchanged.
internal static class PngChunkCrc32
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

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320;
            }
            table[index] = value;
        }
        return table;
    }
}
