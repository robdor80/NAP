using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

namespace NAP.Core;

/// <summary>Private local directories, canonical closed JSON, authenticated atomic finals. Orphans require review.</summary>
internal static class GitPublicationFiles
{
    internal static void DirectoryGuard(string path, bool create)
    {
        ProductionPaths.CheckPath(path, NapIssueCodes.GitReceiptInvalid);
        if (create && !Directory.Exists(path))
        {
            if (OperatingSystem.IsWindows())
            {
                var owner = WindowsIdentity.GetCurrent().User!;
                var acl = new DirectorySecurity(); acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }.Distinct())
                    acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(path).Create(acl);
            }
            else if (OperatingSystem.IsLinux()) Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else throw new IOException("Unsupported private storage platform.");
        }
        if (!Directory.Exists(path)) return;
        ProductionPaths.CheckPath(path, NapIssueCodes.GitReceiptInvalid);
        if (new DriveInfo(path).DriveType == DriveType.Network) throw new IOException("Git policy and Outbox require local storage.");
        Private(path, true);
    }
    private static void Private(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User!;
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            if (!owner.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("Private storage owner changed.");
            if (directory && !acl.AreAccessRulesProtected) throw new IOException("Private storage ACL is inherited.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType != AccessControlType.Allow || (!owner.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference)))
                    throw new IOException("Git publication storage is not private.");
        }
        else if (OperatingSystem.IsLinux())
        {
            if (StatPath(-100, path, 0x100, 0x7ff, out var s) != 0 || s.Uid != EffectiveUid() ||
                (s.Mode & 0xf000) != (directory ? 0x4000 : 0x8000) || (s.Mode & 0x1ff) != (directory ? 0x1c0 : 0x180) || !directory && s.Links != 1)
                throw new IOException("Git publication storage is not private and ordinary.");
        }
        else throw new IOException("Unsupported private storage platform.");
    }
    internal static void Inventory(string directory, params string[] names)
    {
        DirectoryGuard(directory, false);
        if (!Directory.Exists(directory)) return;
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
            if (!names.Contains(Path.GetFileName(entry), StringComparer.Ordinal)) throw new IOException("Unknown Git publication entry or orphan temp requires review.");
    }
    internal static byte[] ReadBytes(string path)
    {
        ProductionPaths.CheckPath(path, NapIssueCodes.GitReceiptInvalid); Private(path, false);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 2 * 1024 * 1024) throw new IOException("Git publication file exceeds bounds.");
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
    }
    internal static void WriteBytes(string path, byte[] bytes, bool replace)
    {
        DirectoryGuard(Path.GetDirectoryName(path)!, true);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var output = new FileStream(temp, options)) { output.Write(bytes); output.Flush(true); }
        Private(temp, false); ProductionPaths.CheckPath(path, NapIssueCodes.GitReceiptInvalid);
        File.Move(temp, path, replace);
        if (!ReadBytes(path).SequenceEqual(bytes)) throw new IOException("Git publication final differs from published bytes.");
    }
    private sealed record Envelope(int SchemaVersion, string Payload, string Mac);
    internal static T Read<T>(string path, byte[] key)
    {
        var e = AutomationJson.Decode<Envelope>(Encoding.UTF8.GetString(ReadBytes(path)));
        if (e.SchemaVersion != 1 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(e.Mac), HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(e.Payload))))
            throw new InvalidDataException("Git publication authentication failed.");
        return AutomationJson.Decode<T>(e.Payload);
    }
    internal static void Write<T>(string path, T value, byte[] key, bool replace)
    {
        var payload = AutomationJson.Encode(value);
        var bytes = Encoding.UTF8.GetBytes(AutomationJson.Encode(new Envelope(1, payload,
            Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))))));
        if (bytes.Length > 2 * 1024 * 1024) throw new IOException("Git publication ledger exceeds bounded format; review required.");
        WriteBytes(path, bytes, replace);
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)] private struct Stat
    {
        [FieldOffset(16)] public uint Links;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int StatPath(int fd, string path, int flags, uint mask, out Stat value);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint EffectiveUid();
}
