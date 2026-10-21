using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace NAP.Presentation;

/// <summary>Disposable client bytes only. On Windows pin every ancestor against rename/reparse
/// replacement and delete only a created file's recorded identity, through its own handle.</summary>
internal sealed class AutomationThumbnailCache : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nap-host-client-" + Guid.NewGuid().ToString("N"));
    private readonly List<SafeFileHandle> _parents = [];
    private readonly Dictionary<string, Identity> _files = new();
    private readonly object _gate = new();
    private long _bytes;
    private bool _disposed;
    internal string Write(byte[] bytes)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var path = Path.Combine(_root, Convert.ToHexString(SHA256.HashData(bytes)) + ".png");
            if (_files.ContainsKey(path)) return path;
            if (_files.Count >= 1024 || bytes.LongLength > 128L * 1024 * 1024 - _bytes)
                throw new IOException("Client thumbnail cache budget exhausted.");
            if (!Directory.Exists(_root))
            {
                if (OperatingSystem.IsWindows())
                {
                    var chain = new Stack<string>();
                    for (var current = Path.GetDirectoryName(_root); current is not null; current = Path.GetDirectoryName(current)) chain.Push(current);
                    while (chain.TryPop(out var parent)) _parents.Add(Pin(parent));
                }
                Directory.CreateDirectory(_root);
                if (OperatingSystem.IsWindows()) _parents.Add(Pin(_root));
            }
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(bytes); file.Flush();
            var identity = OperatingSystem.IsWindows() ? Inspect(file.SafeFileHandle, directory: false) : default;
            _files.Add(path, identity); _bytes += bytes.LongLength;
            return path;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try
            {
                if (!OperatingSystem.IsWindows()) return; // No path-based unlink fallback on unpinned platforms.
                foreach (var (path, expected) in _files)
                {
                    using var file = Open(NativePath(path), 0x00010080, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                    if (file.IsInvalid) continue; // busy/missing files are retained
                    try
                    {
                        if (Inspect(file, directory: false) != expected) continue;
                        var remove = 1;
                        _ = Disposition(file, 4, ref remove, 4);
                    }
                    catch (IOException) { /* preserve replaced/linked/unknown evidence */ }
                }
            }
            finally { foreach (var parent in Enumerable.Reverse(_parents)) parent.Dispose(); _parents.Clear(); _files.Clear(); }
            // Retain the empty directory: removing it by path after releasing its pin permits replacement races.
        }
    }

    private static SafeFileHandle Pin(string path)
    {
        // Metadata-only opens are exempt from parts of Windows sharing checks. A real
        // directory read handle is required to deny rename/delete of an ancestor.
        var handle = Open(NativePath(path), 0x80000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); // share read/write, never delete
        try { _ = Inspect(handle, directory: true); return handle; }
        catch { handle.Dispose(); throw; }
    }
    private static string NativePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full :
            full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }
    private static Identity Inspect(SafeFileHandle handle, bool directory)
    {
        if (handle.IsInvalid || !Information(handle, out var info)) throw new IOException("Client cache identity unavailable.", new Win32Exception(Marshal.GetLastPInvokeError()));
        if ((info.Attributes & 0x440) != 0 || ((info.Attributes & 0x10) != 0) != directory || !directory && info.Links != 1)
            throw new IOException("Client cache boundary is linked or has the wrong type.");
        return new(info.Volume, info.IndexHigh, info.IndexLow);
    }
    private readonly record struct Identity(uint Volume, uint High, uint Low);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes; internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle Open(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    private static extern bool Information(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    private static extern bool Disposition(SafeFileHandle handle, int kind, ref int info, int size);
}
