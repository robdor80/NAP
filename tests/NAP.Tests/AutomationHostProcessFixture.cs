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
    private int _readRetries;
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
        return AutomationProtocol.Read<AutomationHostSnapshot>(await ReadAsync(AutomationOperation.Status, null, new { }));
    }
    internal async Task<JsonElement> ReadAsync<T>(AutomationOperation operation, string? universe, T payload)
    {
        if (operation is not (AutomationOperation.Status or AutomationOperation.QueuePage)) throw new InvalidOperationException("Only read-only polling may be retried.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        for (var attempt = 0; ; attempt++)
        {
            try { return await Client.CallAsync(operation, universe, payload, deadline.Token); }
            catch (IOException error) when (attempt < 2 && Process is { HasExited: false } &&
                error.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionReset })
            {
                // Queries may lose a local connection while restarting/under concurrent load.
                // Every retry authenticates again; no mutation or unknown effect is replayed.
                if (Interlocked.Increment(ref _readRetries) <= 16)
                {
                    var directory = Path.Combine(Repository, "artifacts/phase5/cloud-resume/read-retries"); Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, Path.GetFileName(Files.Root) + ".log"),
                        $"{DateTimeOffset.UtcNow:O} {operation} {error.GetType().Name} 0x{error.HResult:X8}\n");
                }
                await Task.Delay(50, deadline.Token);
            }
        }
    }
    internal async Task StopAsync()
    {
        if (Process is null || Process.HasExited) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        JsonElement result;
        try { result = await Client.CallAsync(AutomationOperation.ShutdownHost, null, new { }, deadline.Token); }
        catch (Exception error) when (error is OperationCanceledException or IOException or TimeoutException)
        {
            // A second caller can lose the pipe just BEFORE the externally stopped process exits.
            // This is cleanup only: require real successful exit; never infer a durable stop here.
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(0, Process.ExitCode);
            return;
        }
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
