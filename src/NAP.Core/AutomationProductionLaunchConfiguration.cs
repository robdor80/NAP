using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Runtime.Versioning;

namespace NAP.Core;

/// <summary>
/// An explicitly provisioned Windows deployment. The ordinary laboratory launch contract
/// cannot opt into production. Configuration bytes alone never prove safe ownership or ACLs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed record AutomationProductionLaunchConfiguration(
    int SchemaVersion,
    string DataRoot,
    string ControlRoot,
    AutomationConnection Connection,
    IReadOnlyList<AutomationUniverseDefinition> Universes,
    string WriterSid,
    string DesktopSid,
    string ApprovalReference,
    string BackupReceiptPath,
    string BackupReceiptSha256,
    string? MixedInboxRoot = null,
    InboxAdmissionOptions? Admission = null)
{
    public IReadOnlyList<UniverseContext> Contexts()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(ApprovalReference) ||
            ApprovalReference.Length > 160 || ApprovalReference.Any(char.IsControl))
            throw new InvalidDataException("Invalid explicit production approval reference.");
        var legacy = new AutomationHostLaunchConfiguration(1, DataRoot, ControlRoot, Connection, Universes);
        return legacy.Contexts();
    }

    /// <summary>Validates the protected installed distribution before any Host constructor may write.</summary>
    public AutomationHost CreateHost(string configurationPath, string installedDirectory, IIdentifiedAiAuditClient? auditor)
    {
        var contexts = Contexts();
        var boundary = AutomationProductionBoundary.Validate(this, contexts, configurationPath, installedDirectory);
        var options = new AutomationHostOptions
        {
            LaboratoryRoot = DataRoot,
            MixedInboxRoot = MixedInboxRoot,
            Admission = Admission ?? new(),
            ProductionBoundary = boundary
        };
        return new AutomationHost(ControlRoot, contexts, options, auditor);
    }
}

/// <summary>
/// Runtime witness: only a Windows ACL and path inspection can create it.
/// This is a technical gate, not evidence that a human CUTOVER has been approved.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AutomationProductionBoundary
{
    private readonly string _dataRoot;
    private readonly string _controlRoot;
    private readonly string _writerSid;
    private readonly string _desktopSid;

    private AutomationProductionBoundary(string dataRoot, string controlRoot, string writerSid, string desktopSid)
    {
        _dataRoot = dataRoot;
        _controlRoot = controlRoot;
        _writerSid = writerSid;
        _desktopSid = desktopSid;
    }

    internal static AutomationProductionBoundary Validate(AutomationProductionLaunchConfiguration config,
        IReadOnlyList<UniverseContext> contexts, string configurationPath, string installedDirectory)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Production NAP Host is Windows-only.");
        var access = new AutomationWindowsEndpointAccess(config.WriterSid, config.DesktopSid);
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != access.WriterSid)
            throw new UnauthorizedAccessException("Production Host requires its dedicated writer identity.");
        if (config.Universes.Count != contexts.Count || contexts.Count is < 1 or > 64 ||
            config.Universes.Select(x => x.Storage.UniverseId).Distinct().Count() != contexts.Count)
            throw new InvalidDataException("Universe roots must be explicit and unique.");

        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var programData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        var requiredData = Path.Combine(programData, "NAP-Production-v1");
        var data = Full(config.DataRoot);
        var control = Full(config.ControlRoot);
        var install = Full(installedDirectory);
        var launch = Full(configurationPath);
        var backup = Full(config.BackupReceiptPath);
        if (!ProductionPaths.Same(requiredData, data) ||
            !ProductionPaths.Within(programFiles, install) ||
            !ProductionPaths.Within(install, launch) ||
            !ProductionPaths.Within(data, control) ||
            !ProductionPaths.Within(data, config.MixedInboxRoot ?? control) && config.MixedInboxRoot is not null ||
            ProductionPaths.Within(data, backup) || ProductionPaths.Within(install, backup) ||
            ProductionPaths.Within(Path.GetTempPath(), backup))
            throw new InvalidDataException("Production roots, configuration and backup must follow distinct protected boundaries.");

        // A user-writable developer checkout or LocalAppData installation fails this gate.
        // For files/binaries the writer SID itself must NOT have write permissions.
        CheckProtectedDirectory(install, access.WriterSid, access.DesktopSid, false);
        CheckProtectedFile(launch, access.WriterSid, access.DesktopSid);
        // Ensure the complete published binary/configuration tree is not mutable
        // by either the interactive desktop or the runtime writer.
        var distributedFiles = Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories).Take(5001).ToArray();
        if (distributedFiles.Length is < 2 or > 5000)
            throw new InvalidDataException("The protected NAP distribution has an invalid inventory.");
        var hostExe = Path.Combine(install, "host", "NAP.AutomationHost.exe");
        var hostCore = Path.Combine(install, "host", "NAP.Core.dll");
        CheckProtectedFile(hostExe, access.WriterSid, access.DesktopSid);
        CheckProtectedFile(hostCore, access.WriterSid, access.DesktopSid);
        foreach (var file in distributedFiles)
            CheckProtectedFile(file, access.WriterSid, access.DesktopSid);
        foreach (var directory in Directory.EnumerateDirectories(install, "*", SearchOption.AllDirectories))
            CheckProtectedDirectory(directory, access.WriterSid, access.DesktopSid, false, false);
        CheckProtectedDirectory(data, access.WriterSid, access.DesktopSid, true);
        CheckProtectedDirectory(control, access.WriterSid, access.DesktopSid, true);
        var roots = contexts.SelectMany(c => new[] { c.Storage.WorkspaceRoot, c.Storage.ProductionRoot, c.Storage.ArchiveRoot }).ToArray();
        if (roots.Any(path => !ProductionPaths.Within(data, path) ||
                ProductionPaths.Overlaps(path, control) ||
                (config.MixedInboxRoot is not null && ProductionPaths.Overlaps(path, config.MixedInboxRoot))))
            throw new InvalidDataException("Universe roots must stay separate under the protected data boundary.");
        LocalUniverseSettingsStore.ValidateStructure(contexts.Select(c => c.Storage));
        foreach (var root in roots) CheckProtectedDirectory(root, access.WriterSid, access.DesktopSid, true);
        if (config.MixedInboxRoot is not null)
            CheckProtectedDirectory(config.MixedInboxRoot, access.WriterSid, access.DesktopSid, true);

        CheckParentChain(data, access.DesktopSid);
        CheckParentChain(install, access.DesktopSid);
        CheckParentChain(launch, access.DesktopSid);
        if (config.BackupReceiptSha256 is not { Length: 64 } ||
            !config.BackupReceiptSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !File.Exists(backup))
            throw new InvalidDataException("A pinned external backup receipt is required.");
        var attributes = ProductionPaths.CheckPath(backup, NapIssueCodes.PackageRootInvalid);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) != 0 ||
            new FileInfo(backup).Length is < 1 or > 2 * 1024 * 1024)
            throw new InvalidDataException("Invalid pinned backup receipt.");
        using (var input = File.OpenRead(backup))
        {
            var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            if (!string.Equals(hash, config.BackupReceiptSha256, StringComparison.Ordinal))
                throw new InvalidDataException("The external backup receipt changed.");
        }
        // Existence and checksum alone do NOT prove a restorable off-machine backup.
        return new AutomationProductionBoundary(data, control, access.WriterSid, access.DesktopSid);
    }

    internal void Require(string requestedRoot, string requestedControl, IReadOnlyList<UniverseContext> contexts)
    {
        if (!ProductionPaths.Same(_dataRoot, requestedRoot) || !ProductionPaths.Same(_controlRoot, requestedControl))
            throw new InvalidOperationException("Host production boundary changed after inspection.");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != _writerSid || string.Equals(_writerSid, _desktopSid, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Production writer identity is no longer valid.");
        if (contexts.Count == 0) throw new InvalidDataException("Production requires explicit universes.");
    }

    private static string Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Production paths must be absolute.");
        return Path.GetFullPath(path);
    }

    private static void CheckProtectedFile(string path, string writer, string desktop)
    {
        if (!ProductionPaths.FileExists(path, NapIssueCodes.PackageRootInvalid))
            throw new InvalidDataException("Protected distribution is missing a required file.");
        var acl = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        CheckAcl(acl, writer, desktop, allowWriterWrite: false, requireExplicitDacl: false);
    }

    private static void CheckProtectedDirectory(string path, string writer, string desktop, bool allowWriterWrite,
        bool requireExplicitDacl = true)
    {
        var attributes = ProductionPaths.CheckPath(path, NapIssueCodes.PackageRootInvalid);
        if (attributes is null || (attributes.Value & FileAttributes.Directory) == 0)
            throw new InvalidDataException("A required protected directory is missing.");
        var acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        CheckAcl(acl, writer, desktop, allowWriterWrite, requireExplicitDacl);
    }

    private static void CheckAcl(FileSystemSecurity acl, string writer, string desktop,
        bool allowWriterWrite, bool requireExplicitDacl)
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;
        var installer = new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464").Value;
        var owner = ((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))!).Value;
        if (owner != system && owner != admin && owner != installer &&
            !(allowWriterWrite && owner == writer))
            throw new UnauthorizedAccessException("Untrusted owner on protected NAP path.");
        if (requireExplicitDacl && !acl.AreAccessRulesProtected)
            throw new UnauthorizedAccessException("Protected deployment directory inherits mutable ACLs.");
        const FileSystemRights mutating = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.FileSystemRights & mutating) == 0) continue;
            var principal = ((SecurityIdentifier)rule.IdentityReference).Value;
            if (principal == system || principal == admin || principal == installer ||
                (allowWriterWrite && principal == writer)) continue;
            throw new UnauthorizedAccessException("An untrusted principal has write permissions on protected NAP files.");
        }
        if (owner == desktop) throw new UnauthorizedAccessException("Desktop owns a protected writer path.");
    }

    private static void CheckParentChain(string path, string desktop)
    {
        // Reject desktop ownership or direct delete/ACL-control on any existing parent.
        for (var current = Path.GetDirectoryName(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (ProductionPaths.CheckPath(current, NapIssueCodes.PackageRootInvalid) is null)
                throw new DirectoryNotFoundException("Protected NAP parent disappeared.");
            var acl = new DirectoryInfo(current).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (((SecurityIdentifier)acl.GetOwner(typeof(SecurityIdentifier))!).Value == desktop)
                throw new UnauthorizedAccessException("Desktop can reclaim a parent of protected NAP files.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    ((SecurityIdentifier)rule.IdentityReference).Value == desktop &&
                    (rule.FileSystemRights & (FileSystemRights.DeleteSubdirectoriesAndFiles |
                        FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0)
                    throw new UnauthorizedAccessException("Desktop can modify a protected NAP parent.");
            }
        }
    }
}
