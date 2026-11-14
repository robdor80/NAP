using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace NAP.Core;

/// <summary>
/// Opt-in Windows cross-principal pipe ACL. Never selected by laboratory or default Host.
/// This is transport authorization, NOT an approval to use real production roots.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AutomationWindowsEndpointAccess
{
    public string WriterSid { get; }
    public string DesktopSid { get; }

    public AutomationWindowsEndpointAccess(string writerSid, string desktopSid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Protected automation pipes require Windows.");
        var writer = UserSid(writerSid, nameof(writerSid));
        var desktop = UserSid(desktopSid, nameof(desktopSid));
        if (writer.Equals(desktop))
            throw new ArgumentException("The desktop and protected writer must be separate principals.");
        WriterSid = writer.Value;
        DesktopSid = desktop.Value;
    }

    private static SecurityIdentifier UserSid(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A user SID is required.", parameterName);
        SecurityIdentifier sid;
        try { sid = new SecurityIdentifier(value); }
        catch (ArgumentException e) { throw new ArgumentException("Invalid user SID.", parameterName, e); }
        if (!sid.IsAccountSid()) throw new ArgumentException("Groups and generic identities cannot be granted access.", parameterName);
        return sid;
    }

    internal PipeSecurity ServerAcl()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != WriterSid)
            throw new UnauthorizedAccessException("Protected pipe server must run as the exact writer SID.");
        var writer = new SecurityIdentifier(WriterSid);
        var desktop = new SecurityIdentifier(DesktopSid);
        var security = new PipeSecurity();
        security.SetOwner(writer);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(writer, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(desktop, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }

    internal void RequireCaller(string principal)
    {
        if (!string.Equals(principal, WriterSid, StringComparison.Ordinal) &&
            !string.Equals(principal, DesktopSid, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Client SID was not authorized for this endpoint.");
    }
}
