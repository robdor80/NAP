using System.Diagnostics;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>Isolated synthetic storage; snapshots record links without following them.</summary>
internal sealed class ProductionRepositoryTestFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("nap-production-boundary-").FullName;
    internal string ProductionRoot => Path.Combine(Root, "production");

    internal ProductionRepositoryTestFixture()
    {
        File.WriteAllText(Path.Combine(Root, "outside.txt"), "outside sentinel");
    }

    internal UniverseContext Context(string? productionRoot = null, string universe = "nimroel")
    {
        var profile = new UniverseProfile(new UniverseId(universe), "Test universe");
        // Equal IDs with different references ensure the output preserves context.Id itself.
        var storage = new UniverseStorageConfig(new UniverseId(universe), Path.Combine(Root, "workspace"),
            productionRoot ?? ProductionRoot, Path.Combine(Root, "archive"));
        return new UniverseContext(profile, storage);
    }

    internal ProductionRepositoryValidationResult ValidateReadOnly(UniverseContext context)
    {
        var before = Snapshot();
        try { return new ProductionRepositoryValidator().Validate(context); }
        finally { AssertSnapshot(before); }
    }

    internal SortedDictionary<string, string> Snapshot()
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Visit(new DirectoryInfo(Root));
        return snapshot;

        void Visit(FileSystemInfo entry)
        {
            var attributes = entry.Attributes;
            var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var content = isLink ? entry.LinkTarget : isDirectory ? "" : Convert.ToHexString(File.ReadAllBytes(entry.FullName));
            // Directory timestamps can flush late after fixture setup (notably junction creation).
            var timestamps = isDirectory ? "" : $"{entry.CreationTimeUtc.Ticks}|{entry.LastWriteTimeUtc.Ticks}";
            snapshot.Add(entry.FullName, $"{attributes}|{timestamps}|{content}");
            if (isDirectory && !isLink)
                foreach (var child in ((DirectoryInfo)entry).EnumerateFileSystemInfos()) Visit(child);
        }
    }

    internal void AssertSnapshot(SortedDictionary<string, string> before) => Assert.Equal(before, Snapshot());

    internal static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // Junctions work on Windows without developer mode or symbolic-link privileges.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = $"/c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
