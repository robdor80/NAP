using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

internal sealed class AutomationHostProcessFixture : IAsyncDisposable
{
    internal readonly AutomationHostTests.Fixture Files = new();
    internal AutomationConnection Connection { get; } = new("nap-process-" + Guid.NewGuid().ToString("N"), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    internal string ConfigurationPath => Path.Combine(Files.Root, "launch.json");
    internal AutomationHostClient Client => new(Connection);
    internal Process? Process;
    internal Task<string>? Errors;
    private bool _faultInjection;
    internal static string Repository => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal static string BuildConfiguration => Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name;
    internal async Task StartAsync(string? checkpoint = null, bool bothUniverses = false, bool fastAdmission = false)
    {
        _faultInjection = checkpoint is not null;
        {
            var config = new AutomationHostLaunchConfiguration(1, Files.Root, Files.Control, Connection,
                (bothUniverses ? new[] { Files.A, Files.B } : [Files.A]).Select(f => new AutomationUniverseDefinition(f.Roots, UniverseProfileSnapshotRenderer.Render(f.Context.Profile))).ToArray(),
                true, checkpoint, fastAdmission ? Files.Options.Admission : null);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(config, AutomationProtocol.Json));
        }
        var executable = Path.Combine(Repository, "src/NAP.AutomationHost/bin", BuildConfiguration, "net8.0/NAP.AutomationHost.dll");
        Assert.True(File.Exists(executable));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(executable); start.ArgumentList.Add("--laboratory-config"); start.ArgumentList.Add(ConfigurationPath);
        Process = System.Diagnostics.Process.Start(start)!; Errors = Process.StandardError.ReadToEndAsync();
        var ready = await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(ready?.StartsWith("NAP laboratory host ready:", StringComparison.Ordinal) == true, ready ?? "Host exited before ready.");
    }
    internal async Task KillAsync()
    {
        if (Process is null) return;
        if (!Process.HasExited) Process.Kill(entireProcessTree: true);
        await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (Errors is not null) _ = await Errors;
        Process.Dispose(); Process = null;
    }
    internal async Task<AutomationHostSnapshot> SnapshotAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return AutomationProtocol.Read<AutomationHostSnapshot>(await Client.CallAsync(AutomationOperation.Status, null, new { }, deadline.Token));
    }
    internal async Task StopAsync()
    {
        if (Process is null || Process.HasExited) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await Client.CallAsync(AutomationOperation.ShutdownHost, null, new { }, deadline.Token);
        Assert.Equal(AutomationHostState.Stopped, AutomationProtocol.Read<AutomationHostSnapshot>(result).State);
        await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(0, Process.ExitCode);
    }
    internal async Task PreserveFailureAsync()
    {
        // Drain before copying SQLite sidecars. Never archive launch.json: it holds the pipe key.
        await StopAsync();
        var directory = Path.Combine(Repository, "artifacts/phase5/cloud-resume/failures", Path.GetFileName(Files.Root));
        Directory.CreateDirectory(directory);
        foreach (var path in Directory.EnumerateFiles(Files.Root, "*", SearchOption.AllDirectories))
        {
            if (path == ConfigurationPath) continue;
            var destination = Path.Combine(directory, Path.GetRelativePath(Files.Root, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination, false);
        }
        if (Errors is not null) File.WriteAllText(Path.Combine(directory, "host.stderr.log"), await Errors);
    }
    public async ValueTask DisposeAsync()
    {
        try { if (!_faultInjection) await StopAsync(); }
        finally { await KillAsync(); Files.Dispose(); }
    }
}
