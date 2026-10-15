using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;

namespace NAP.Core;

/// <summary>Fresh bounded read-only accounting. Parallel branches retain every ancestor/reparse check;
/// the owner's thread waits for all readers before any admission effect can continue.</summary>
internal static class RetainedAdmissionInventory
{
    internal static bool Fits(string directory, long capacity)
    {
        if (!Directory.Exists(directory)) return true;
        using var pinnedAncestors = WindowsInventoryDirectories.PinAncestors(directory);
        InboxAdmissionEvidence.DirectoryBoundary(directory);
        long retained = 0;
        int count = 0, exceeded = 0;
        ExceptionDispatchInfo? failure = null;
        try
        {
            Parallel.ForEach(Partitioner.Create(Directory.EnumerateFileSystemEntries(directory), EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, (entry, state) =>
            {
                var pending = new Stack<(IEnumerator<string> Cursor, IDisposable? Pin)>();
                try
                {
                    if (Volatile.Read(ref exceeded) != 0 || Volatile.Read(ref failure) is not null) { state.Stop(); return; }
                    Visit(entry);
                    while (pending.Count != 0 && Volatile.Read(ref exceeded) == 0 && Volatile.Read(ref failure) is null)
                    {
                        var cursor = pending.Peek();
                        if (!cursor.Cursor.MoveNext()) { cursor.Cursor.Dispose(); cursor.Pin?.Dispose(); pending.Pop(); continue; }
                        Visit(cursor.Cursor.Current);
                    }
                    void Visit(string path)
                    {
                        if (Interlocked.Increment(ref count) > 100000) { Interlocked.Exchange(ref exceeded, 1); return; }
                        // Windows parents are held without FILE_SHARE_DELETE for this whole depth.
                        // Other platforms keep the full original component checks for every entry.
                        var attributes = pinnedAncestors is not null ? File.GetAttributes(path) : ProductionPaths.CheckPath(path, NapIssueCodes.PackageStructureInvalid)!.Value;
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                            throw ProductionStorageException.Stop(NapIssueCodes.PackageStructureInvalid, "Retention accounting cannot traverse links or devices.", path);
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            var pin = pinnedAncestors is not null ? WindowsInventoryDirectories.PinDirectory(path) : null;
                            try
                            {
                                if (pinnedAncestors is null) InboxAdmissionEvidence.DirectoryBoundary(path);
                                pending.Push((Directory.EnumerateFileSystemEntries(path).GetEnumerator(), pin));
                            }
                            catch { pin?.Dispose(); throw; }
                        }
                        else
                        {
                            var size = new FileInfo(path).Length;
                            while (true)
                            {
                                var prior = Volatile.Read(ref retained);
                                if (size > capacity - prior) { Interlocked.Exchange(ref exceeded, 1); break; }
                                if (Interlocked.CompareExchange(ref retained, prior + size, prior) == prior) break;
                            }
                        }
                    }
                }
                catch (Exception error) { Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(error), null); }
                finally
                {
                    foreach (var cursor in pending) { cursor.Cursor.Dispose(); cursor.Pin?.Dispose(); }
                    if (Volatile.Read(ref exceeded) != 0 || Volatile.Read(ref failure) is not null) state.Stop();
                }
            });
        }
        catch (AggregateException error)
        { ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw(); throw; }
        failure?.Throw();
        return Volatile.Read(ref exceeded) == 0;
    }
}
