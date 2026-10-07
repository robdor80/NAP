using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NAP.Core;

/// <summary>Offline verification independent of the production repo: full-bundle SHA is checked separately;
/// this checks a self-contained header and the embedded PACK checksum without invoking Git or any transport.</summary>
internal static class GitBundleIntegrity
{
    internal static void Verify(string path, string expectedHead)
    {
        BackupStorage.Check(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var headerBytes = 0; var first = Line();
        if (first is not ("# v2 git bundle" or "# v3 git bundle")) throw Invalid();
        var oidLength = 40; var refs = 0; var capability = false; var headFound = false;
        while (true)
        {
            var line = Line(); if (line.Length == 0) break;
            if (line[0] == '@')
            {
                if (first != "# v3 git bundle" || refs != 0 || capability) throw Invalid();
                oidLength = line switch { "@object-format=sha1" => 40, "@object-format=sha256" => 64, _ => throw Invalid() };
                capability = true; continue;
            }
            if (line[0] == '-' || line.Length <= oidLength + 1 || line[oidLength] != ' ' ||
                line[..oidLength].Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
                line[(oidLength + 1)..].Any(char.IsControl)) throw Invalid();
            refs++; headFound |= line[..oidLength] == expectedHead;
        }
        if (refs == 0 || !headFound) throw Invalid(); var packStart = file.Position; var trailerLength = oidLength / 2;
        if (file.Length - packStart < 12 + trailerLength) throw Invalid();
        Span<byte> packHeader = stackalloc byte[12]; file.ReadExactly(packHeader);
        if (!packHeader[..4].SequenceEqual("PACK"u8) || BinaryPrimitives.ReadUInt32BigEndian(packHeader[4..8]) is not (2 or 3) ||
            BinaryPrimitives.ReadUInt32BigEndian(packHeader[8..]) == 0) throw Invalid();
        file.Position = packStart; var remaining = file.Length - packStart - trailerLength;
        using var hash = IncrementalHash.CreateHash(oidLength == 40 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var count = file.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length)); if (count == 0) throw Invalid();
            hash.AppendData(buffer, 0, count); remaining -= count;
        }
        Span<byte> trailer = stackalloc byte[32]; file.ReadExactly(trailer[..trailerLength]);
        if (!hash.GetHashAndReset().AsSpan().SequenceEqual(trailer[..trailerLength])) throw Invalid();
        string Line()
        {
            var bytes = new List<byte>();
            while (true)
            {
                var value = file.ReadByte(); if (value < 0 || ++headerBytes > 1024 * 1024 || bytes.Count > 16384) throw Invalid();
                if (value == '\n') break; bytes.Add((byte)value);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
    private static BackupException Invalid() => BackupException.Stop(NapIssueCodes.BackupIntegrityFailed, "Git bundle header or PACK checksum is invalid, unsupported or not self-contained.");
}
