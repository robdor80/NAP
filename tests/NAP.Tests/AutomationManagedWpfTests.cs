using System.Diagnostics;
using System.Text.Json;
using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

/// <summary>Real Windows processes, real authenticated IPC, synthetic TEMP-only assets.</summary>
public sealed class AutomationManagedWpfTests
{
    private sealed class Accept : IUserConfirmation
    {
        public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation)
            => Task.FromResult(true);
    }

    [VisualExplorerWindowsFact]
    public async Task ManagedWpfStartsHostAndReattachesWithoutSecondSessionOrLocalWriter()
    {
        await using var f = new AutomationHostProcessFixture();
        var launch = new AutomationHostLaunchConfiguration(1, f.Files.Root, f.Files.Control, f.Connection,
            [new(f.Files.A.Roots, UniverseProfileSnapshotRenderer.Render(f.Files.A.Context.Profile))], true);
        File.WriteAllText(f.ConfigurationPath, JsonSerializer.Serialize(launch, AutomationProtocol.Json));

        async Task RunDesktopAsync()
        {
            var application = Path.Combine(AutomationHostProcessFixture.Repository, "src/NAP.App/bin",
                AutomationHostProcessFixture.BuildConfiguration, "net10.0-windows/NAP.App.dll");
            var packagedHost = Path.Combine(Path.GetDirectoryName(application)!, "NAP.AutomationHost.dll");
            Assert.True(File.Exists(packagedHost), "The normal WPF application must ship with AutomationHost.");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                application, "--automation-managed-config=" + f.ConfigurationPath,
                "--smoke-automation", "--smoke-output=" + Path.Combine(f.Files.Root, "wpf-captures")
            }) start.ArgumentList.Add(argument);
            using var window = Process.Start(start)!;
            var stdout = window.StandardOutput.ReadToEndAsync();
            var stderr = window.StandardError.ReadToEndAsync();
            await window.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var output = await stdout; var errors = await stderr;
            Assert.True(window.ExitCode == 0, output + errors);
            Assert.Contains("\"automationConnected\":true", output);
            Assert.Contains("\"bindingErrors\":0", output);
            Assert.Contains("\"settingsManagedByHost\":true", output);
        }

        try
        {
            await RunDesktopAsync();
            var first = AutomationProtocol.Read<AutomationHostSnapshot>(
                await f.Client.CallAsync(AutomationOperation.Status, null, new { }));
            Assert.NotNull(first.SessionId);
            await RunDesktopAsync();
            var second = AutomationProtocol.Read<AutomationHostSnapshot>(
                await f.Client.CallAsync(AutomationOperation.Status, null, new { }));
            Assert.Equal(first.SessionId, second.SessionId);
            Assert.Equal(first.InstallationId, second.InstallationId);
            Assert.False(File.Exists(Path.Combine(f.Files.Root, "desktop-unused-settings.json")));
        }
        finally
        {
            // Host deliberately outlives WPF: the fixture must explicitly shut it down.
            try { await f.Client.CallAsync(AutomationOperation.ShutdownHost, null, new { }); await Task.Delay(300); }
            catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException) { }
        }
    }

    [VisualExplorerWindowsFact]
    public async Task WpfControlStopsOnlySelectedUniverseAndKeepsHostAlive()
    {
        await using var f = new AutomationHostProcessFixture();
        var a = AutomationHostTests.Fixture.Enable(f.Files.A);
        var b = AutomationHostTests.Fixture.Enable(f.Files.B);
        await f.StartAsync(bothUniverses: true);
        await f.Client.CallAsync(AutomationOperation.Activate, f.Files.A.Context.Id.Value, new AutomationActivation(a.Reference, false));
        await f.Client.CallAsync(AutomationOperation.Activate, f.Files.B.Context.Id.Value, new AutomationActivation(b.Reference, false));
        using var controls = new AutomationControlViewModel(f.Client, () => f.Files.A.Context, new Accept());
        await controls.RefreshAsync();
        Assert.True(controls.CanStopUniverse);
        await controls.StopUniverse.ExecuteAsync();
        var stoppedA = AutomationProtocol.Read<AutomationHostControl>(
            await f.Client.CallAsync(AutomationOperation.CurrentControl, f.Files.A.Context.Id.Value, new { }));
        var runningB = AutomationProtocol.Read<AutomationHostControl>(
            await f.Client.CallAsync(AutomationOperation.CurrentControl, f.Files.B.Context.Id.Value, new { }));
        Assert.True(stoppedA.Stopped);
        Assert.False(runningB.Stopped);
        Assert.False(f.Process!.HasExited);
        Assert.NotEqual(AutomationHostState.Stopped, (await f.SnapshotAsync()).State);
    }
}
