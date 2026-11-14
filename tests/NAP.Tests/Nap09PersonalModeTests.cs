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
}
