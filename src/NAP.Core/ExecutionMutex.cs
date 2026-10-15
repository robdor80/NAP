using System.Collections.Concurrent;
using System.Text;

namespace NAP.Core;

/// <summary>Nonblocking protocol-v2 kernel file coordination, retaining the CLR mutex as an additional legacy guard.</summary>
internal sealed class ExecutionMutex : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> Owners = new(StringComparer.Ordinal);
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly ExecutionFileLease _file;
    private readonly int _thread;
    private bool _disposed;

    private ExecutionMutex(Mutex mutex, string name, ExecutionFileLease file)
    { _mutex = mutex; _name = name; _file = file; _thread = Environment.CurrentManagedThreadId; }

    internal static ExecutionMutex Acquire(string scope, string root, string identity = "")
    {
        var name = Name(scope, root, identity);
        if (!Owners.TryAdd(name, 0)) throw new IOException("The execution lock is occupied.");
        Mutex? mutex = null;
        bool acquired = false;
        try
        {
            mutex = new Mutex(false, name);
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The execution lock is occupied.");
            var file = ExecutionFileLease.Acquire(name);
            return new ExecutionMutex(mutex, name, file);
        }
        catch { if (acquired) mutex!.ReleaseMutex(); mutex?.Dispose(); Owners.TryRemove(name, out _); throw; }
    }

    internal void Require(string scope, string root, string identity = "")
    {
        if (_disposed || _thread != Environment.CurrentManagedThreadId || _name != Name(scope, root, identity))
            throw new InvalidOperationException("The live execution lease does not authorize this thread/Job/root.");
        _file.Require();
    }

    private static string Name(string scope, string root, string identity)
    {
        if (string.IsNullOrEmpty(scope) || scope.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid execution scope.", nameof(scope));
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(canonical + "\n" + identity));
        return "NAP.Execution." + scope + "." + new Sha256Hasher().Compute(bytes).Hex;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("The execution lease belongs to another thread.");
        try { _mutex.ReleaseMutex(); }
        finally { _file.Dispose(); _mutex.Dispose(); Owners.TryRemove(_name, out _); _disposed = true; }
    }
}
