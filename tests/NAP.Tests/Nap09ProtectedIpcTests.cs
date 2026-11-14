using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.Versioning;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>
/// NAP09: optional pipe transport only. Never starts AutomationHost or touches production.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Nap09ProtectedIpcTests
{
    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static string OtherAccountSid(string current)
    {
        // A syntactically valid user SID, not a provisioned identity or credential.
        const string candidate = "S-1-5-21-101-202-303-404";
        return candidate == current ? "S-1-5-21-101-202-303-405" : candidate;
    }

    [VisualExplorerWindowsFact]
    public void CrossPrincipalPolicyRejectsGroupsAndSameIdentity()
    {
        var current = CurrentSid();
        Assert.Throws<ArgumentException>(() => new AutomationWindowsEndpointAccess(current, current));
        Assert.Throws<ArgumentException>(() => new AutomationWindowsEndpointAccess(current, "S-1-1-0"));
        Assert.Throws<ArgumentException>(() => new AutomationWindowsEndpointAccess("S-1-5-32-545", current));
    }

    [VisualExplorerWindowsFact]
    public void ProtectedServerRejectsDifferentWriterIdentityBeforeBinding()
    {
        var current = CurrentSid();
        var other = OtherAccountSid(current);
        var policy = new AutomationWindowsEndpointAccess(other, current);
        var connection = new AutomationConnection("nap-nap09-test-" + Guid.NewGuid().ToString("N"),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<UnauthorizedAccessException>(() => new AutomationPipeServer(connection,
            (_, _) => Task.FromResult(AutomationProtocol.Payload(new { ok = true })), policy));
    }

    [VisualExplorerWindowsFact]
    public async Task ProtectedOptInCanUseExactWriterSidWithoutChangingLaboratoryDefault()
    {
        var current = CurrentSid();
        var policy = new AutomationWindowsEndpointAccess(current, OtherAccountSid(current));
        var connection = new AutomationConnection("nap-nap09-test-" + Guid.NewGuid().ToString("N"),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var server = new AutomationPipeServer(connection,
            (_, principal) => Task.FromResult(AutomationProtocol.Payload(new { authorized = principal == current })), policy);
        var client = new AutomationHostClient(connection, policy);
        var response = await client.CallAsync(AutomationOperation.Status, null, new { });
        Assert.True(response.GetProperty("authorized").GetBoolean());
    }
}
