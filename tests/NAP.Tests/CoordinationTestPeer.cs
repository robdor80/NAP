using System.Diagnostics;
using Xunit;

namespace NAP.Tests;

internal static class CoordinationTestPeer
{
    internal static Process Start(string mode, string scope, string root, string identity = "", string? runtime = null, string? environmentRoot = null)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (runtime is not null) { info.ArgumentList.Add("--fx-version"); info.ArgumentList.Add(runtime); }
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "coordination-peer", "NAP.CoordinationProbe.dll"));
        foreach (var part in new[] { mode, scope, root, identity }) info.ArgumentList.Add(part);
        if (environmentRoot is not null)
            foreach (var key in new[] { "HOME", "XDG_RUNTIME_DIR", "XDG_DATA_HOME", "TMPDIR" }) info.Environment[key] = environmentRoot;
        return Process.Start(info)!;
    }
    internal static void Ready(Process process, string signal = "LOCKED") =>
        Assert.Equal(signal, process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
    internal static int Exit(Process process)
    { if (!process.WaitForExit(15000)) { process.Kill(true); Assert.Fail("Coordination peer timed out."); } return process.ExitCode; }
    internal static void Release(Process process)
    {
        if (!process.HasExited) { process.StandardInput.WriteLine("release"); process.StandardInput.Flush(); }
        Assert.Equal(0, Exit(process));
    }
}
