using System.Runtime.InteropServices;

namespace NAP.Core;

internal static class BackupFileTypes
{
    // FileAttributes alone does not distinguish FIFOs/sockets/devices on Unix.
    // Use the same Linux statx ABI already used by CatalogFileStamp, without opening the entry.
    internal static void RequireOrdinary(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            if (LinuxStatx(-100, path, 0x100, 1, out var info) != 0 || (info.Mask & 1) == 0 ||
                (info.Mode & 0xf000) is not (0x8000 or 0x4000))
                throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "Backup entries must be ordinary files or directories.");
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        { throw BackupException.Stop(NapIssueCodes.BackupSourceInvalid, "The host cannot verify ordinary filesystem entry types."); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Info
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out Info info);
}
