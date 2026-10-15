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
        InboxAdmissionEvidence.DirectoryBoundary(directory);
        long retained = 0;
        int count = 0, exceeded = 0;
        ExceptionDispatchInfo? failure = null;
        try
        {
            Parallel.ForEach(Partitioner.Create(Directory.EnumerateFileSystemEntries(directory), EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, (entry, state) =>
            {
                var pending = new Stack<IEnumerator<string>>();
                try
                {
                    if (Volatile.Read(ref exceeded) != 0 || Volatile.Read(ref failure) is not null) { state.Stop(); return; }
                    Visit(entry);
                    while (pending.Count != 0 && Volatile.Read(ref exceeded) == 0 && Volatile.Read(ref failure) is null)
                    {
                        var cursor = pending.Peek();
                        if (!cursor.MoveNext()) { cursor.Dispose(); pending.Pop(); continue; }
                        Visit(cursor.Current);
                    }
                    void Visit(string path)
                    {
                        if (Interlocked.Increment(ref count) > 100000) { Interlocked.Exchange(ref exceeded, 1); return; }
                        var attributes = ProductionPaths.CheckPath(path, NapIssueCodes.PackageStructureInvalid)!.Value;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            InboxAdmissionEvidence.DirectoryBoundary(path);
                            pending.Push(Directory.EnumerateFileSystemEntries(path).GetEnumerator());
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
                    foreach (var cursor in pending) cursor.Dispose();
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
