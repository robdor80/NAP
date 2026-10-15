using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NAP.Core;

/// <summary>Windows directory identities pinned against delete/rename during a quota read.
/// OPEN_REPARSE_POINT inspects each component itself before its children can be read.</summary>
internal sealed class WindowsInventoryDirectories : IDisposable
{
    private readonly List<SafeFileHandle> _handles = [];
    internal static WindowsInventoryDirectories? PinAncestors(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var held = new WindowsInventoryDirectories();
        try
        {
            var components = new Stack<string>();
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current)) components.Push(current);
            while (components.TryPop(out var component)) held._handles.Add(PinDirectory(component)!);
            return held;
        }
        // Some ancestors grant traversal/attributes without directory-list access. Preserve
        // the original full component checks there; never optimize using an unpinned chain.
        catch (IOException) { held.Dispose(); return null; }
        catch { held.Dispose(); throw; }
    }
    internal static SafeFileHandle? PinDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES; share read/write but NOT delete; OPEN_EXISTING.
        // Attribute-only opens do not participate in delete sharing and cannot protect this walk.
        // BACKUP_SEMANTICS | OPEN_REPARSE_POINT. Handles are read-only and not inheritable.
        var handle = CreateFile(path, 0x81, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw Failure(error); }
        try
        {
            if (!Information(handle, out var info)) throw Failure(Marshal.GetLastPInvokeError());
            if ((info.Attributes & 0x10) == 0 || (info.Attributes & (0x400u | 0x40u)) != 0)
                throw ProductionStorageException.Stop(NapIssueCodes.PackageStructureInvalid, "Retention accounting cannot traverse links or non-directories.", path);
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() { for (var i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
    private static IOException Failure(int error) => new("A retention directory cannot be pinned safely.", new Win32Exception(error));
    [StructLayout(LayoutKind.Sequential)]
    private struct Info { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Information(SafeFileHandle handle, out Info info);
}
