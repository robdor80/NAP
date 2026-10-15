using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ExecutionCoordinationProcessTests
{
    private static readonly Type MutexType = typeof(AssetCatalog).Assembly.GetType("NAP.Core.ExecutionMutex")!;
    private static string[] Runtimes => (Environment.GetEnvironmentVariable("NAP_COORDINATION_RUNTIMES") ?? Environment.Version.ToString()).Split(',');

    [Fact]
    public void PeerLoadsExactlyTheCoreBinaryUnderTest()
    {
        var expected = SHA256.HashData(File.ReadAllBytes(typeof(AssetCatalog).Assembly.Location));
        var actual = SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "coordination-peer", "NAP.Core.dll")));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReleaseUnlocksTheDescriptionEvenWhenAForkStyleDuplicateIsStillOpen()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = SyntheticRoot(); var held = Acquire("Production", root, "");
        var file = MutexType.GetField("_file", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(held)!;
        var handle = (SafeFileHandle)file.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(file)!;
        var clone = Duplicate(handle); Assert.True(clone >= 0);
        using var inherited = new SafeFileHandle((IntPtr)clone, true);
        held.Dispose();
        using var contender = CoordinationTestPeer.Start("core-try", "Production", root);
        Assert.Equal(0, CoordinationTestPeer.Exit(contender));
    }

    [Fact]
    public async Task QueueOwnerDeathAdvancesDurableFencingEpochAndDoesNotLoseEvidence()
    {
        var root = SyntheticRoot(); var roots = new UniverseStorageConfig(new("test_universe"), Path.Combine(root, "workspace"), Path.Combine(root, "production"), Path.Combine(root, "archive"));
        Directory.CreateDirectory(roots.WorkspaceRoot);
        var store = new AutomationQueueStore(roots);
        long previous;
        using (var initial = store.AcquireOwner()) previous = initial.Epoch;
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var part in new[] { "--fx-version", Runtimes[^1], Path.Combine(AppContext.BaseDirectory, "coordination-peer", "NAP.CoordinationProbe.dll"), "queue-owner", "test_universe", roots.WorkspaceRoot, roots.ProductionRoot, roots.ArchiveRoot }) info.ArgumentList.Add(part);
        using var peer = Process.Start(info)!;
        try
        {
            var epoch = await peer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (epoch is null) Assert.Fail(peer.StandardError.ReadToEnd());
            Assert.Equal(previous + 1, long.Parse(epoch!));
            Assert.Equal(AutomationError.Busy, Assert.Throws<AutomationException>(() => store.AcquireOwner()).Code);
        }
        finally { if (!peer.HasExited) { peer.Kill(true); peer.WaitForExit(); } }
        using var recovered = store.AcquireOwner(); Assert.Equal(previous + 2, recovered.Epoch);
        Assert.Equal(AutomationPolicyState.Disabled, store.CurrentPolicy().State);
    }

    [Theory]
    [InlineData("AutomationOwner")] [InlineData("Job")] [InlineData("Production")] [InlineData("Catalog")]
    public void MixedRuntimesExcludeAllPeersAndRecoverAfterProcessDeath(string scope)
    {
        var root = SyntheticRoot(); var identity = "synthetic_identity";
        foreach (var ownerRuntime in Runtimes)
        {
            using var owner = CoordinationTestPeer.Start("core-hold", scope, root, identity, ownerRuntime);
            try
            {
                CoordinationTestPeer.Ready(owner);
                foreach (var contenderRuntime in Runtimes)
                {
                    using var contender = CoordinationTestPeer.Start("core-try", scope, root, identity, contenderRuntime);
                    Assert.Equal(10, CoordinationTestPeer.Exit(contender));
                }
            }
            finally { if (!owner.HasExited) { owner.Kill(true); Assert.True(owner.WaitForExit(15000)); } }
            foreach (var contenderRuntime in Runtimes)
            {
                using var recovered = CoordinationTestPeer.Start("core-try", scope, root, identity, contenderRuntime);
                Assert.Equal(0, CoordinationTestPeer.Exit(recovered));
            }
        }
        Assert.False(Directory.Exists(root)); // Coordination never creates an asset/state root.
    }

    [Theory]
    [InlineData("AutomationOwner")] [InlineData("Job")] [InlineData("Production")] [InlineData("Catalog")]
    public void SimultaneousProcessesHaveExactlyOneWinner(string scope)
    {
        var root = SyntheticRoot();
        using var a = CoordinationTestPeer.Start("core-race", scope, root, runtime: Runtimes[0]);
        using var b = CoordinationTestPeer.Start("core-race", scope, root, runtime: Runtimes[^1]);
        try
        {
            CoordinationTestPeer.Ready(a, "READY"); CoordinationTestPeer.Ready(b, "READY");
            a.StandardInput.WriteLine("go"); a.StandardInput.Flush(); b.StandardInput.WriteLine("go"); b.StandardInput.Flush();
            var signals = new[] { a, b }.Select(p => p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()).ToArray();
            Assert.Single(signals, s => s == "LOCKED"); Assert.Single(signals, s => s == "BUSY");
            for (var i = 0; i < signals.Length; i++)
                if (signals[i] == "LOCKED") CoordinationTestPeer.Release(i == 0 ? a : b);
                else Assert.Equal(10, CoordinationTestPeer.Exit(i == 0 ? a : b));
        }
        finally { foreach (var p in new[] { a, b }) if (!p.HasExited) { p.Kill(true); p.WaitForExit(); } }
    }

    [Fact]
    public void LeaseIsNonreentrantBoundToThreadScopeAndRootAndRemainsOnDisk()
    {
        var root = SyntheticRoot(); var path = LeasePath("Job", root, "one");
        using (var held = Acquire("Job", root, "one"))
        {
            Assert.Throws<IOException>(() => Acquire("Job", root, "one"));
            Require(held, "Job", root, "one");
            Assert.Throws<InvalidOperationException>(() => Require(held, "Job", root, "two"));
            Assert.Throws<InvalidOperationException>(() => Require(held, "Job", root + "other", "one"));
            Exception? error = null; var thread = new Thread(() => error = Record.Exception(() => Require(held, "Job", root, "one")));
            thread.Start(); Assert.True(thread.Join(10000)); Assert.IsType<InvalidOperationException>(error);
            using var different = Acquire("Job", root, "two");
            Assert.Equal(0, new FileInfo(path).Length);
        }
        Assert.True(File.Exists(path)); using var reopened = Acquire("Job", root, "one");
    }

    [Fact]
    public void NonemptyLockFileFailsClosedWithoutChangingBytes()
    {
        var root = SyntheticRoot(); var path = LeasePath("Catalog", root, "AssetCatalog.db");
        using (Acquire("Catalog", root, "AssetCatalog.db")) { }
        File.WriteAllText(path, "foreign evidence");
        Assert.Throws<IOException>(() => Acquire("Catalog", root, "AssetCatalog.db"));
        Assert.Equal("foreign evidence", File.ReadAllText(path));
    }

    [Fact]
    public void LinkedLockFailsClosedWithoutOpeningOrChangingTarget()
    {
        var root = SyntheticRoot(); var path = LeasePath("Production", root, "");
        var directory = SyntheticRoot(); Directory.CreateDirectory(directory); var target = Path.Combine(directory, "foreign");
        File.WriteAllText(target, "foreign evidence");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // A directory junction exercises a reparse leaf without requiring symlink privilege locally.
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var part in new[] { "/c", "mklink", "/J", path, directory }) start.ArgumentList.Add(part);
                using var process = Process.Start(start)!; Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
            }
            else File.CreateSymbolicLink(path, target);
            Assert.Throws<IOException>(() => Acquire("Production", root, ""));
            Assert.Equal("foreign evidence", File.ReadAllText(target));
        }
        finally
        {
            if (OperatingSystem.IsWindows()) { if (Directory.Exists(path)) Directory.Delete(path, false); }
            else if (File.Exists(path)) File.Delete(path);
            File.Delete(target); Directory.Delete(directory, false);
        }
    }

    [Fact]
    public void AlteredLockPermissionsOrHardlinksAreRejected()
    {
        var root = SyntheticRoot(); var path = LeasePath("Job", root, "");
        using (Acquire("Job", root, "")) { }
        var alias = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid().ToString("N") + ".hardlink");
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe") : new ProcessStartInfo("ln");
        start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        foreach (var part in OperatingSystem.IsWindows() ? new[] { "/c", "mklink", "/H", alias, path } : new[] { path, alias }) start.ArgumentList.Add(part);
        using var process = Process.Start(start)!; Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<IOException>(() => Acquire("Job", root, "")); }
        finally { File.Delete(alias); }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite);
            Assert.Throws<IOException>(() => Acquire("Job", root, ""));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        using var restored = Acquire("Job", root, "");
    }

    internal static IDisposable Acquire(string scope, string root, string identity) =>
        (IDisposable)Invoke(MutexType.GetMethod("Acquire", BindingFlags.Static | BindingFlags.NonPublic)!, null, [scope, root, identity])!;
    private static void Require(IDisposable lease, string scope, string root, string identity) =>
        Invoke(MutexType.GetMethod("Require", BindingFlags.Instance | BindingFlags.NonPublic)!, lease, [scope, root, identity]);
    private static object? Invoke(MethodInfo method, object? target, object[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
    private static string SyntheticRoot() => Path.Combine(Path.GetTempPath(), "nap-coordination-test-" + Guid.NewGuid().ToString("N"));
    private static string LeasePath(string scope, string root, string identity)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        var name = "NAP.Execution." + scope + "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + "\n" + identity))).ToLowerInvariant();
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.ExecutionFileLease")!;
        return (string)Invoke(type.GetMethod("PathFor", BindingFlags.Static | BindingFlags.NonPublic)!, null, [name])!;
    }
    [DllImport("libc", EntryPoint = "dup", SetLastError = true)] private static extern int Duplicate(SafeFileHandle handle);
}
