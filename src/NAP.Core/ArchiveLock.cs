using System.Text;

namespace NAP.Core;

/// <summary>Nonblocking process coordination plus the exclusive archive.lock stream.</summary>
internal sealed class ArchiveLock : IDisposable
{
    private readonly UniverseContext _context;
    private readonly Mutex _mutex;
    private FileStream? _stream;
    private bool _disposed;

    private ArchiveLock(UniverseContext context, Mutex mutex)
    {
        _context = context;
        _mutex = mutex;
    }

    internal static ArchiveLock Acquire(UniverseContext context)
    {
        ArchiveRootValidator.Require(context);
        var canonical = Path.TrimEndingDirectorySeparator(context.Storage.ArchiveRoot);
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        using var nameBytes = new MemoryStream(Encoding.UTF8.GetBytes(canonical));
        var name = "NAP.Archive." + new Sha256Hasher().Compute(nameBytes).Hex;
        var mutex = new Mutex(false, name);
        bool acquired;
        try
        {
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The archive lock is occupied.");
        }
        catch { mutex.Dispose(); throw; }

        var lease = new ArchiveLock(context, mutex);
        try
        {
            // Existing archives can be verified without creating any infrastructure.
            var path = lease.LockPath;
            if (ArchivePaths.FileExists(path))
                lease._stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private string LockPath => Path.Combine(_context.Storage.ArchiveRoot, "_nap", "archive.lock");

    internal void EnableWrites()
    {
        Require(_context);
        if (_stream is not null) return;
        ArchivePaths.EnsureDirectory(_context, Path.GetDirectoryName(LockPath)!);
        ArchivePaths.FileExists(LockPath);
        _stream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal void Require(UniverseContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (context.Id != _context.Id || !ArchivePaths.Same(context.Storage.ArchiveRoot, _context.Storage.ArchiveRoot))
            throw new ArgumentException("The archive lock belongs to another context.", nameof(context));
    }

    internal void RequireWrites(UniverseContext context)
    {
        Require(context);
        if (_stream is null) throw new InvalidOperationException("Publishing requires the exclusive archive.lock stream.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _stream?.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
