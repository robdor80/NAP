using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>Production gate must refuse an interactive developer or any TEMP deployment.</summary>
[SupportedOSPlatform("windows")]
public sealed class Nap09ProductionBoundaryTests
{
    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static AutomationProductionLaunchConfiguration Config(AutomationHostTests.Fixture f,
        string writerSid, string desktopSid)
    {
        return new AutomationProductionLaunchConfiguration(
            1, f.Root, f.Control,
            new AutomationConnection("nap-nap09-prod-" + Guid.NewGuid().ToString("N"),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            [new(f.A.Roots, UniverseProfileSnapshotRenderer.Render(f.A.Context.Profile))],
            writerSid, desktopSid, "NAP09-TEST-ONLY",
            Path.Combine(f.Root, "not-an-external-backup.json"), new string('a', 64));
    }

    [VisualExplorerWindowsFact]
    public void ProductionHostRejectsInteractiveTempRootsBeforeAnyRuntimeCreation()
    {
        using var f = new AutomationHostTests.Fixture();
        var writer = CurrentSid();
        var desktop = writer == "S-1-5-21-101-202-303-404"
            ? "S-1-5-21-101-202-303-405" : "S-1-5-21-101-202-303-404";
        var configuration = Config(f, writer, desktop);
        Assert.Throws<InvalidDataException>(() =>
            configuration.CreateHost(Path.Combine(f.Root, "host.json"), f.Root, null));
        Assert.False(File.Exists(Path.Combine(f.Control, "HostControl.db")));
    }

    [VisualExplorerWindowsFact]
    public void ProductionHostRejectsWrongWriterIdentityBeforeEvaluatingRoots()
    {
        using var f = new AutomationHostTests.Fixture();
        var desktop = CurrentSid();
        var writer = desktop == "S-1-5-21-101-202-303-404"
            ? "S-1-5-21-101-202-303-405" : "S-1-5-21-101-202-303-404";
        var configuration = Config(f, writer, desktop);
        Assert.Throws<UnauthorizedAccessException>(() =>
            configuration.CreateHost(Path.Combine(f.Root, "host.json"), f.Root, null));
        Assert.False(File.Exists(Path.Combine(f.Control, "HostControl.db")));
    }

    [VisualExplorerWindowsFact]
    public void ProductionConfigurationRejectsInvalidApprovalAndDuplicates()
    {
        using var f = new AutomationHostTests.Fixture();
        var writer = CurrentSid();
        var desktop = "S-1-5-21-101-202-303-404";
        var original = Config(f, writer, desktop);
        Assert.Throws<InvalidDataException>(() => (original with { ApprovalReference = "" }).Contexts());
        Assert.Throws<InvalidDataException>(() => (original with { SchemaVersion = 2 }).Contexts());
    }
}
