using System.IO.Compression;
using System.Text;

namespace NAP.Tests;

/// <summary>Small deterministic ZIP fixtures with explicit header offsets. Test infrastructure only.</summary>
internal static class AdversarialZipFactory
{
    internal sealed record Entry(string Name, byte[] Data, int Attributes = 0, bool Deflated = false);
    internal sealed record Fixture(byte[] Bytes, int[] LocalHeaders, int[] CentralHeaders, int EndOffset);

    internal static Entry Text(string name, string text = "data", int attributes = 0, bool deflated = false) =>
        new(name, Encoding.UTF8.GetBytes(text), attributes, deflated);

    internal static Fixture Create(params Entry[] entries)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        var local = new int[entries.Length];
        var central = new int[entries.Length];
        var names = entries.Select(entry => Encoding.UTF8.GetBytes(entry.Name)).ToArray();
        var payloads = entries.Select(entry => entry.Deflated ? Deflate(entry.Data) : entry.Data).ToArray();
        for (var i = 0; i < entries.Length; i++)
        {
            local[i] = (int)output.Position;
            writer.Write(0x04034b50U);
            writer.Write((ushort)20); // version needed
            writer.Write((ushort)0x800); // UTF-8 names
            writer.Write((ushort)(entries[i].Deflated ? 8 : 0));
            writer.Write((ushort)0); writer.Write((ushort)0x21); // fixed 1980-01-01
            writer.Write(Crc(entries[i].Data));
            writer.Write((uint)payloads[i].Length); writer.Write((uint)entries[i].Data.Length);
            writer.Write((ushort)names[i].Length); writer.Write((ushort)0);
            writer.Write(names[i]); writer.Write(payloads[i]);
        }
        var centralStart = (int)output.Position;
        for (var i = 0; i < entries.Length; i++)
        {
            central[i] = (int)output.Position;
            writer.Write(0x02014b50U);
            writer.Write((ushort)0x314); writer.Write((ushort)20); // Unix creator, version needed
            writer.Write((ushort)0x800);
            writer.Write((ushort)(entries[i].Deflated ? 8 : 0));
            writer.Write((ushort)0); writer.Write((ushort)0x21);
            writer.Write(Crc(entries[i].Data));
            writer.Write((uint)payloads[i].Length); writer.Write((uint)entries[i].Data.Length);
            writer.Write((ushort)names[i].Length); writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0);
            writer.Write(entries[i].Attributes); writer.Write((uint)local[i]);
            writer.Write(names[i]);
        }
        var end = (int)output.Position;
        writer.Write(0x06054b50U);
        writer.Write((ushort)0); writer.Write((ushort)0);
        writer.Write((ushort)entries.Length); writer.Write((ushort)entries.Length);
        writer.Write((uint)(end - centralStart)); writer.Write((uint)centralStart);
        writer.Write((ushort)0);
        return new(output.ToArray(), local, central, end);
    }

    internal static Fixture WithZip64(Fixture source)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(source.Bytes, 0, source.EndOffset);
        writer.Write(0x06064b50U); writer.Write(44UL);
        writer.Write((ushort)45); writer.Write((ushort)45);
        writer.Write(0U); writer.Write(0U);
        writer.Write((ulong)source.CentralHeaders.Length); writer.Write((ulong)source.CentralHeaders.Length);
        writer.Write((ulong)(source.EndOffset - source.CentralHeaders[0])); writer.Write((ulong)source.CentralHeaders[0]);
        writer.Write(0x07064b50U); writer.Write(0U); writer.Write((ulong)source.EndOffset); writer.Write(1U);
        var end = (int)output.Position;
        writer.Write(source.Bytes, source.EndOffset, 22);
        var bytes = output.ToArray();
        Array.Fill(bytes, (byte)0xff, end + 8, 12);
        return source with { Bytes = bytes, EndOffset = end };
    }

    private static byte[] Deflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var stream = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            stream.Write(bytes);
        return output.ToArray();
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
        }
        return ~crc;
    }
}
