using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NAP.Core;

/// <summary>Protocol v2: persistent private files and nonblocking kernel locks, independent of CLR namespaces.</summary>
internal sealed class ExecutionFileLease : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly string _path;
    private readonly FileIdentity _identity;
    private bool _disposed;
    private ExecutionFileLease(SafeFileHandle handle, string path, FileIdentity identity)
    { _handle = handle; _path = path; _identity = identity; }

    internal static string PathFor(string name) => Path.Combine(PrivateDirectory(), name + ".lease");

    internal static ExecutionFileLease Acquire(string name)
    {
        var path = PathFor(name);
        SafeFileHandle handle;
        if (OperatingSystem.IsWindows())
            handle = CreateFile(path, 0xc0000000, 0, IntPtr.Zero, 4, 0x00200080, IntPtr.Zero);
        else if (OperatingSystem.IsLinux())
            // O_RDWR | O_CREAT | O_NONBLOCK | O_NOFOLLOW | O_CLOEXEC; mode 0600 at creation.
            handle = new SafeFileHandle((IntPtr)Open(path, 2 | 0x40 | 0x800 | 0x20000 | 0x80000, 0x180), true);
        else throw new IOException("Execution coordination supports only Windows and Linux.");
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw Failure(error); }
        try
        {
            var identity = Inspect(handle);
            if (OperatingSystem.IsLinux() && Flock(handle, 2 | 4) != 0) throw Failure(Marshal.GetLastPInvokeError());
            RequirePath(path, identity);
            return new ExecutionFileLease(handle, path, identity);
        }
        catch { handle.Dispose(); throw; }
    }

    internal void Require()
    {
        if (_disposed || _handle.IsClosed || _handle.IsInvalid) throw new IOException("The execution file lease is not live.");
        _ = PrivateDirectory();
        if (Inspect(_handle) != _identity) throw new IOException("The execution file identity changed.");
        RequirePath(_path, _identity);
    }

    private static void RequirePath(string path, FileIdentity expected)
    {
        if (OperatingSystem.IsLinux())
        {
            if (StatPath(-100, path, 0x100, 0x7ff, out var value) != 0) throw Failure(Marshal.GetLastPInvokeError());
            if (Regular(value) != expected) throw new IOException("The execution lock path was replaced.");
        }
        else
        {
            // Share-none prevents replacement of the held file; inspect the path without following reparse points.
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) != 0)
                throw new IOException("The execution lock path is not an ordinary file.");
        }
    }

    private static FileIdentity Inspect(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Information(handle, out var value)) throw Failure(Marshal.GetLastPInvokeError());
            if ((value.Attributes & (0x400u | 0x10u | 0x40u)) != 0 || value.Links != 1 || value.SizeHigh != 0 || value.SizeLow != 0)
                throw new IOException("The execution lock must be an empty ordinary file with one link.");
            return new(value.Volume, 0, ((ulong)value.IndexHigh << 32) | value.IndexLow);
        }
        if (StatHandle(handle, "", 0x1000 | 0x100, 0x7ff, out var stat) != 0) throw Failure(Marshal.GetLastPInvokeError());
        return Regular(stat);
    }

    private static FileIdentity Regular(Stat value)
    {
        if ((value.Mask & 0x7ff) != 0x7ff || (value.Mode & 0xf000) != 0x8000 || value.Links != 1 || value.Uid != EffectiveUid() ||
            (value.Mode & 0x1ff) != 0x180 || value.Size != 0)
            throw new IOException("The execution lock must be a private empty ordinary file with one link.");
        return new(value.DeviceMajor, value.DeviceMinor, value.Inode);
    }

    private static string PrivateDirectory()
    {
        string path;
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new IOException("Execution coordination requires a user identity.");
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(common)) throw new IOException("The machine coordination directory is unavailable.");
            path = Path.Combine(common, "NAP-coordination-v2-" + owner.Value);
            CheckComponents(path);
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                var security = new DirectorySecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) }.Distinct())
                    security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                directory.Create(security);
            }
            CheckComponents(path);
            var current = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!owner.Equals(current.GetOwner(typeof(SecurityIdentifier))) || !current.AreAccessRulesProtected)
                throw new IOException("The execution coordination directory owner or ACL is invalid.");
            foreach (FileSystemAccessRule rule in current.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType != AccessControlType.Allow ||
                    (!rule.IdentityReference.Equals(owner) && !rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null))))
                    throw new IOException("The execution coordination directory is not private.");
        }
        else if (OperatingSystem.IsLinux())
        {
            // Account database, not HOME/TMPDIR/XDG; persistent storage is not subject to /tmp cleanup.
            path = Path.Combine(AccountHome(), ".nap-coordination-v2");
            CheckComponents(path);
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            CheckComponents(path);
            if (StatPath(-100, path, 0x100, 0x7ff, out var value) != 0) throw Failure(Marshal.GetLastPInvokeError());
            if ((value.Mode & 0xf000) != 0x4000 || (value.Mode & 0x1ff) != 0x1c0 || value.Uid != EffectiveUid())
                throw new IOException("The execution coordination directory is not private.");
        }
        else throw new IOException("Execution coordination supports only Windows and Linux.");
        return path;
    }

    private static void CheckComponents(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            FileAttributes value;
            try { value = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((value & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0 || (value & FileAttributes.Directory) == 0)
                throw new IOException("Execution coordination paths cannot traverse links or non-directories.");
        }
    }

    private static string AccountHome()
    {
        var buffer = Marshal.AllocHGlobal(65536);
        try
        {
            var error = Account(EffectiveUid(), out var value, buffer, 65536, out var found);
            if (error != 0 || found == IntPtr.Zero) throw Failure(error);
            var home = Marshal.PtrToStringUTF8(value.Home);
            if (string.IsNullOrEmpty(home) || !Path.IsPathFullyQualified(home)) throw new IOException("The writer account has no absolute home directory.");
            return home;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IOException Failure(int error) => new("The execution file lock is occupied or unavailable.", new Win32Exception(error));
    public void Dispose() { if (_disposed) return; _handle.Dispose(); _disposed = true; } // Never unlink a lease file.
    private readonly record struct FileIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode);
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint Links;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Passwd
    {
        public IntPtr Name, Password;
        public uint Uid, Gid;
        public IntPtr Gecos, Home, Shell;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Information(SafeFileHandle handle, out WindowsInformation value);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int Flock(SafeFileHandle fd, int operation);
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint EffectiveUid();
    [DllImport("libc", EntryPoint = "getpwuid_r")] private static extern int Account(uint uid, out Passwd value, IntPtr buffer, nuint size, out IntPtr found);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int StatPath(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int StatHandle(SafeFileHandle fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Stat value);
}
