using System.Security.AccessControl;
using System.Security.Principal;

namespace NAP.Core;

/// <summary>A fresh, empty, non-repository hook directory owned exclusively by this invocation.</summary>
internal sealed class GitProductionHooks : IDisposable
{
    internal string Path { get; }
    private GitProductionHooks(string path) => Path = path;

    internal static GitProductionHooks Create(UniverseContext context)
    {
        // No caller-supplied path, existing directory, or repository hook is ever accepted.
        var directory = Directory.CreateTempSubdirectory("nap-git-hooks-");
        var lease = new GitProductionHooks(directory.FullName);
        try
        {
            var relative = System.IO.Path.GetRelativePath(context.Storage.ProductionRoot, lease.Path);
            if (!System.IO.Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw GitProductionException.Stop(NapIssueCodes.GitCommitFailed, "Hook isolation must be outside the repository.");
            if (OperatingSystem.IsWindows())
            {
                var owner = WindowsIdentity.GetCurrent().User!;
                var security = new DirectorySecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.ReadAndExecute | FileSystemRights.Delete, AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            else File.SetUnixFileMode(lease.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            lease.RequireEmpty(); return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal void RequireEmpty()
    {
        if ((File.GetAttributes(Path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory ||
            Directory.EnumerateFileSystemEntries(Path).Any())
            throw GitProductionException.Stop(NapIssueCodes.GitCommitFailed, "The controlled hook directory must remain ordinary and empty.");
    }

    public void Dispose()
    {
        // Never recursively remove entries, including scripts introduced by an external actor.
        if ((File.GetAttributes(Path) & FileAttributes.ReparsePoint) == 0) Directory.Delete(Path, recursive: false);
    }
}
