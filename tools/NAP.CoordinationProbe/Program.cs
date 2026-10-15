using System.Runtime.InteropServices;
using System.Text.Json;

// Diagnostic-only peer; does not load NAP, configuration, assets, journals or a database.
if (args.Length == 1 && args[0] == "info")
{
    Console.WriteLine(JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(),
        Framework = RuntimeInformation.FrameworkDescription, RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier }));
    return 0;
}
if (args.Length < 3 || args[0] is not ("hold" or "try") || args[1] is not ("mutex" or "file" or "dual") ||
    args.Length != (args[1] == "dual" ? 4 : 3)) return 64;
IDisposable? lease = null;
IDisposable? fileLease = null;
try
{
    if (args[1] is "mutex" or "dual")
    {
        var mutex = new Mutex(false, args[2]);
        bool held;
        try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) { mutex.Dispose(); return 10; }
        lease = new MutexLease(mutex);
    }
    if (args[1] is "file" or "dual")
    {
        // The driver owns this newly-created temporary directory. Never use real NAP roots.
        var path = Path.GetFullPath(args[^1]);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(temp, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Path.GetFileName(path).Equals("probe.lock", StringComparison.Ordinal)) return 64;
        try { fileLease = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 10; }
    }
    if (args[0] == "hold") { Console.WriteLine("LOCKED"); Console.Out.Flush(); Console.ReadLine(); }
    return 0;
}
finally { fileLease?.Dispose(); lease?.Dispose(); }

sealed class MutexLease(Mutex value) : IDisposable
{
    public void Dispose() { value.ReleaseMutex(); value.Dispose(); }
}
