using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class Nap09PersonalModeTests
{
    [VisualExplorerWindowsFact]
    public async Task PersonalHostUsesExistingUserWithoutStartingWork()
    {
        using var fixture = new AutomationHostTests.Fixture();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NAP09-personal-smoke-" + Guid.NewGuid().ToString("N"));
        var control = Path.Combine(root, "control");
        Directory.CreateDirectory(control);
        try
        {
            var config = new AutomationHostLaunchConfiguration(1, root, control,
                new("nap-personal-" + Guid.NewGuid().ToString("N"),
                    Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
                [new(fixture.A.Roots, UniverseProfileSnapshotRenderer.Render(fixture.A.Context.Profile))],
                PersonalMode: true);
            Assert.Single(config.Contexts());
            Assert.Throws<InvalidDataException>(() => (config with { SyntheticAudit = true }).Contexts());
            await using (var host = new AutomationHost(control, config.Contexts(),
                new() { LaboratoryRoot = root, PersonalMode = true }))
            {
                await host.Started;
                Assert.NotEmpty(host.SessionId);
                var snapshot = host.Snapshot();
                Assert.Single(snapshot.Universes);
                // Newly configured universes stay paused until the user authorizes them.
                var status = host.ReadControl(fixture.A.Context.Id);
                Assert.True(status.Paused);
                Assert.True(status.Stopped);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [VisualExplorerWindowsFact]
    public async Task PersonalExecutableStartsWithExistingRootsAndOneWindowsUser()
    {
        using var fixture = new AutomationHostTests.Fixture();
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NAP09-personal-process-" + Guid.NewGuid().ToString("N"));
        var control = Path.Combine(root, "control");
        var mixed = Path.Combine(fixture.Root, "mixed");
        Directory.CreateDirectory(control);
        Directory.CreateDirectory(mixed);
        var launch = Path.Combine(root, "launch.json");
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var config = new AutomationHostLaunchConfiguration(1, root, control,
            new("nap-personal-" + Guid.NewGuid().ToString("N"), key),
            [new(fixture.A.Roots, UniverseProfileSnapshotRenderer.Render(fixture.A.Context.Profile))],
            MixedInboxRoot: mixed, PersonalMode: true);
        File.WriteAllText(launch, System.Text.Json.JsonSerializer.Serialize(config, AutomationProtocol.Json));
        var host = Path.Combine(AutomationHostProcessFixture.Repository, "src/NAP.AutomationHost/bin",
            AutomationHostProcessFixture.BuildConfiguration, "net10.0/NAP.AutomationHost.dll");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(host); start.ArgumentList.Add("--personal-config");
        start.ArgumentList.Add(launch);
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (line is null)
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var error = await child.StandardError.ReadToEndAsync();
                Assert.Fail("Personal Host exited: " + error);
            }
            Assert.StartsWith("NAP personal host ready: ", line);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

}
