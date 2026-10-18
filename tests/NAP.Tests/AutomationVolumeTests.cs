using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAP.Core;
using Xunit;
using Xunit.Abstractions;

namespace NAP.Tests;

public sealed class AutomationVolumeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(1000)]
    [Trait("Category", "AutomationVolume")]
    public async Task SeparateHostCompletesUniqueMastersWithFairnessAndBoundedResources(int count)
    {
        await using var f = new AutomationHostProcessFixture(); var both = count > 1;
        var fixtures = both ? new[] { f.Files.A, f.Files.B } : [f.Files.A];
        var policies = fixtures.Select(u => AutomationHostTests.Fixture.Enable(u, count)).ToArray();
        for (var n = 0; n < count; n++)
        {
            var universe = fixtures[n % fixtures.Length];
            universe.Zip($"input-{n:D4}.zip", $"portrait_sample{n / fixtures.Length}_001", seed: n + 1);
        }
        var originals = fixtures.ToDictionary(u => u.Context.Id, u => DurableExecutionTests.Inventory(u.Roots.InboxRoot));
        await f.StartAsync(bothUniverses: both, fastAdmission: true);
        var timer = Stopwatch.StartNew(); var process = f.Process!;
        var initialCpu = process.TotalProcessorTime;
        long peakWorking = 0, peakPrivate = 0; int peakHandles = 0, peakThreads = 0, maxPending = 0;
        var samples = new List<object>(); var disk = new List<object>();
        var lastProgress = fixtures.Select(_ => TimeSpan.Zero).ToArray(); var previous = new int[fixtures.Length];
        var firstCompleted = new double?[fixtures.Length];
        var maximumProgressGap = TimeSpan.Zero;
        for (var n = 0; n < fixtures.Length; n++)
            await f.Client.CallAsync(AutomationOperation.Activate, fixtures[n].Context.Id.Value, new AutomationActivation(policies[n].Reference, false));
        var total = 0; var nextSample = TimeSpan.Zero; var nextDisk = TimeSpan.Zero;
        IoCounters? io = null;
        IReadOnlyDictionary<string, ulong>? linuxIo = null;
        try
        {
            while (total < count)
            {
                process.Refresh();
                peakWorking = Math.Max(peakWorking, process.WorkingSet64); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                peakHandles = Math.Max(peakHandles, process.HandleCount); peakThreads = Math.Max(peakThreads, process.Threads.Count);
                Assert.True(timer.Elapsed < TimeSpan.FromMinutes(55), $"Only {total}/{count} in {timer.Elapsed}.");
                var status = await f.SnapshotAsync(); Assert.DoesNotContain(status.Universes, u => u.State == AutomationHostState.Blocked);
                maxPending = Math.Max(maxPending, status.Universes.Max(u => u.PendingCandidates));
                if (timer.Elapsed >= nextSample)
                {
                    total = 0;
                    for (var u = 0; u < fixtures.Length; u++)
                    {
                        var completed = 0;
                        for (var offset = 0; ; offset += 100)
                        {
                            var payload = await f.Client.CallAsync(AutomationOperation.QueuePage, fixtures[u].Context.Id.Value, new AutomationPageRequest(offset, 100));
                            var items = AutomationProtocol.Read<QueueItem[]>(payload);
                            Assert.DoesNotContain(items, i => i.State is QueueState.NeedsReview or QueueState.Rejected or QueueState.Duplicate);
                            completed += items.Count(i => i.State == QueueState.Completed);
                            if (items.Length < 100) break;
                        }
                        if (completed > previous[u])
                        {
                            firstCompleted[u] ??= timer.Elapsed.TotalSeconds;
                            maximumProgressGap = TimeSpan.FromSeconds(Math.Max(maximumProgressGap.TotalSeconds, (timer.Elapsed - lastProgress[u]).TotalSeconds));
                            lastProgress[u] = timer.Elapsed; previous[u] = completed;
                        }
                        if (completed < (count + fixtures.Length - 1 - u) / fixtures.Length)
                            Assert.True(timer.Elapsed - lastProgress[u] < TimeSpan.FromMinutes(5), "A runnable universe made no progress for five minutes.");
                        total += completed;
                    }
                    samples.Add(new { seconds = timer.Elapsed.TotalSeconds, completed = total, workingBytes = process.WorkingSet64,
                        privateBytes = process.PrivateMemorySize64, handles = process.HandleCount, threads = process.Threads.Count,
                        cpuSeconds = (process.TotalProcessorTime - initialCpu).TotalSeconds,
                        completedByUniverse = previous.ToArray(), status });
                    nextSample = timer.Elapsed + TimeSpan.FromSeconds(count >= 100 ? 10 : .5);
                }
                if (timer.Elapsed >= nextDisk)
                { disk.Add(Disk(f, timer.Elapsed.TotalSeconds)); nextDisk = timer.Elapsed + TimeSpan.FromSeconds(60); }
                await Task.Delay(250);
            }
            Assert.InRange(maxPending, 0, 3);
            // Generous synthetic envelope: measured OS process values, separate from runner/image fixtures.
            Assert.True(peakPrivate < 1024L * 1024 * 1024, $"Host private bytes {peakPrivate}");
            Assert.True(peakHandles < 2048); Assert.True(peakThreads < 128);
            if (OperatingSystem.IsWindows() && GetProcessIoCounters(process.Handle, out var counters)) io = counters;
            linuxIo = ReadLinuxIo(process);
            await f.StopAsync();
            foreach (var universe in fixtures)
            {
                Assert.Equal(originals[universe.Context.Id], DurableExecutionTests.Inventory(universe.Roots.InboxRoot));
                var all = new List<QueueItem>();
                for (int offset = 0; ; offset += 1000) { var page = universe.Queue.List(offset, 1000); all.AddRange(page); if (page.Count < 1000) break; }
                Assert.All(all, i => { Assert.Equal(QueueState.Completed, i.State); Assert.Equal(1, i.AttemptCount); Assert.Single(universe.Service.State.Get(i.Id)!.Calls); });
                Assert.Equal(all.Count, all.Select(i => universe.Queue.GetAttempt(i.ActiveAttempt!).JobId).Distinct().Count());
                Assert.Equal(all.Count, Directory.EnumerateFiles(universe.Roots.ProductionRoot, "*.webp", SearchOption.AllDirectories).Count());
                Assert.Equal(all.Count, new AssetCatalog(universe.Context).Query().Count);
            }
        }
        finally
        {
            total = previous.Sum(); // an assertion may interrupt the per-universe accumulation
            if (total < count) await f.PreserveFailureAsync();
            if (!process.HasExited && OperatingSystem.IsWindows() && GetProcessIoCounters(process.Handle, out var counters)) io = counters;
            if (!process.HasExited) linuxIo = ReadLinuxIo(process);
            disk.Add(Disk(f, timer.Elapsed.TotalSeconds));
            var evidence = new { count, total, elapsedSeconds = timer.Elapsed.TotalSeconds, peakWorking, peakPrivate, peakHandles, peakThreads,
                maxPending, maximumProgressGapSeconds = maximumProgressGap.TotalSeconds, firstCompleted, io, linuxIo, samples, disk };
            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            var directory = Path.Combine(AutomationHostProcessFixture.Repository, "artifacts/phase5/p510/metrics"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"automation-{count}-{DateTime.UtcNow:HHmmss}.json"), json); output.WriteLine(json);
        }
    }

    private static IReadOnlyDictionary<string, ulong>? ReadLinuxIo(Process process)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var allowed = new HashSet<string>(["rchar", "wchar", "syscr", "syscw", "read_bytes", "write_bytes", "cancelled_write_bytes"], StringComparer.Ordinal);
            return File.ReadAllLines($"/proc/{process.Id}/io").Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2 && allowed.Contains(parts[0]))
                .ToDictionary(parts => parts[0], parts => ulong.Parse(parts[1].Trim()), StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static object Disk(AutomationHostProcessFixture f, double seconds)
    {
        var files = new List<(string Path, long Bytes)>();
        foreach (var path in Directory.EnumerateFiles(f.Files.Root, "*", SearchOption.AllDirectories))
            try { files.Add((path, new FileInfo(path).Length)); }
            catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        return new { seconds, files = files.Count, bytes = files.Sum(x => x.Bytes), ledgers = files.Where(x => Path.GetExtension(x.Path) is ".db" or ".sqlite" || x.Path.EndsWith("-wal"))
            .Select(x => new { path = Path.GetRelativePath(f.Files.Root, x.Path), bytes = x.Bytes }).ToArray() };
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessIoCounters(IntPtr handle, out IoCounters counters);
}
