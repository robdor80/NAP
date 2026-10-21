using System.Diagnostics;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class AutomationWpfTests
{
    [VisualExplorerWindowsFact]
    public async Task RemoteShellRendersNavigatesAndClosesWithoutStoppingHostOrCreatingUserSettings()
    {
        await using var f = new AutomationHostProcessFixture(); await f.StartAsync();
        var app = Path.Combine(AutomationHostProcessFixture.Repository, "src/NAP.App/bin", AutomationHostProcessFixture.BuildConfiguration, "net8.0-windows/NAP.App.dll"); Assert.True(File.Exists(app));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { app, "--automation-config=" + f.ConfigurationPath, "--smoke-test", "--smoke-automation", "--smoke-output=" + Path.Combine(AutomationHostProcessFixture.Repository, "artifacts/phase5/wpf/remote-shell") }) start.ArgumentList.Add(arg);
        using var window = Process.Start(start)!; var output = window.StandardOutput.ReadToEndAsync(); var errors = window.StandardError.ReadToEndAsync();
        await window.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(window.ExitCode == 0, await output + await errors);
        using var report = JsonDocument.Parse(await output); Assert.True(report.RootElement.GetProperty("rendered").GetBoolean());
        Assert.Equal(0, report.RootElement.GetProperty("bindingErrors").GetInt32()); Assert.True(report.RootElement.GetProperty("trayVerified").GetBoolean());
        Assert.False(f.Process!.HasExited); Assert.NotEqual(AutomationHostState.Stopped, (await f.SnapshotAsync()).State);
        Assert.False(File.Exists(Path.Combine(f.Files.Root, "desktop-unused-settings.json")));
    }
}
