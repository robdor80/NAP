using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NAP.Core;

/// <summary>Bounded observation: physical file identity/change metadata plus the 100-byte SQLite header.
/// No retained handle, watcher, DB hash, or catalog traversal. Unsupported metadata disables reuse.</summary>
internal sealed record CatalogFileStamp(long Length, long Creation, long LastWrite, string Header, CatalogNativeStamp? Native)
{
    internal bool CanReuse => Native is not null;
    internal static CatalogFileStamp Capture(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 100);
        Span<byte> header = stackalloc byte[100]; var read = 0;
        while (read < header.Length) { var count = file.Read(header[read..]); if (count == 0) break; read += count; }
        var info = new FileInfo(path);
        return new(file.Length, info.CreationTimeUtc.Ticks, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(header[..read]), NativeStamp(file.SafeFileHandle));
    }
    private static CatalogNativeStamp? NativeStamp(SafeFileHandle handle)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!WindowsIdentity(handle, 18, out var id, 24) || !WindowsBasic(handle, 0, out var basic, 40)) return null;
                return new(id.Volume, id.Low, id.High, basic.Change, 0, basic.Write, 0);
            }
            if (OperatingSystem.IsLinux())
            {
                // Linux UAPI statx is a fixed 256-byte ABI, unlike architecture-specific struct stat.
                const uint required = 0x3c0; // INO | SIZE | CTIME | MTIME
                if (LinuxStatx(handle.DangerousGetHandle().ToInt32(), "", 0x1000, required, out var info) != 0 || (info.Mask & required) != required) return null;
                return new(((ulong)info.DeviceMajor << 32) | info.DeviceMinor, info.Inode, 0, info.ChangeSeconds, info.ChangeNanos, info.WriteSeconds, info.WriteNanos);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return null;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsId { public ulong Volume; public ulong Low; public ulong High; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInfo { public long Creation; public long Access; public long Write; public long Change; public uint Attributes; }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxInfo
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(96)] public long ChangeSeconds;
        [FieldOffset(104)] public uint ChangeNanos;
        [FieldOffset(112)] public long WriteSeconds;
        [FieldOffset(120)] public uint WriteNanos;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsIdentity(SafeFileHandle handle, int kind, out WindowsId information, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsBasic(SafeFileHandle handle, int kind, out WindowsInfo information, uint size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatx(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxInfo information);
}
internal sealed record CatalogNativeStamp(ulong Device, ulong IdLow, ulong IdHigh, long Change, uint ChangeNanos, long Write, uint WriteNanos);
