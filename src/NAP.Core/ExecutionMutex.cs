using System.Collections.Concurrent;
using System.Text;

namespace NAP.Core;

/// <summary>Nonblocking root/job process coordination with no lock files, including non-reentrant in-process ownership.</summary>
internal sealed class ExecutionMutex : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> Owners = new(StringComparer.Ordinal);
    private readonly Mutex _mutex;
    private readonly string _name;
    private bool _disposed;

    private ExecutionMutex(Mutex mutex, string name) { _mutex = mutex; _name = name; }

    internal static ExecutionMutex Acquire(string scope, string root, string identity = "")
    {
        var name = Name(scope, root, identity);
        if (!Owners.TryAdd(name, 0)) throw new IOException("The execution lock is occupied.");
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(false, name);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The execution lock is occupied.");
            return new ExecutionMutex(mutex, name);
        }
        catch { mutex?.Dispose(); Owners.TryRemove(name, out _); throw; }
    }

    internal void Require(string scope, string root, string identity = "")
    {
        if (_disposed || _name != Name(scope, root, identity)) throw new InvalidOperationException("The live execution lease does not authorize this Job/root.");
    }

    private static string Name(string scope, string root, string identity)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(canonical + "\n" + identity));
        return "NAP.Execution." + scope + "." + new Sha256Hasher().Compute(bytes).Hex;
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { _mutex.ReleaseMutex(); }
        finally { _mutex.Dispose(); Owners.TryRemove(_name, out _); _disposed = true; }
    }
}
