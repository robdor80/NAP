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
    internal static string Repository => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    internal async Task StartAsync()
    {
        if (!File.Exists(ConfigurationPath))
        {
            var config = new AutomationHostLaunchConfiguration(1, Files.Root, Files.Control, Connection,
                [new(Files.A.Roots, UniverseProfileSnapshotRenderer.Render(Files.A.Context.Profile))], true);
            File.WriteAllText(ConfigurationPath, JsonSerializer.Serialize(config, AutomationProtocol.Json));
        }
        var executable = Path.Combine(Repository, "src/NAP.AutomationHost/bin/Release/net8.0/NAP.AutomationHost.dll");
        Assert.True(File.Exists(executable));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(executable); start.ArgumentList.Add("--laboratory-config"); start.ArgumentList.Add(ConfigurationPath);
        Process = System.Diagnostics.Process.Start(start)!; Errors = Process.StandardError.ReadToEndAsync();
        var ready = await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(ready?.StartsWith("NAP laboratory host ready:", StringComparison.Ordinal) == true, ready ?? "Host exited before ready.");
    }
    internal async Task<AutomationHostSnapshot> SnapshotAsync() => AutomationProtocol.Read<AutomationHostSnapshot>(await Client.CallAsync(AutomationOperation.Status, null, new { }));
    internal async Task StopAsync()
    {
        if (Process is null || Process.HasExited) return;
        var result = await Client.CallAsync(AutomationOperation.ShutdownHost, null, new { });
        Assert.Equal(AutomationHostState.Stopped, AutomationProtocol.Read<AutomationHostSnapshot>(result).State);
        await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(0, Process.ExitCode);
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(); Process?.Dispose(); Files.Dispose();
    }
}
