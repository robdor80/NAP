namespace NAP.Presentation;

/// <summary>Logical isolation only. No environment switch, arbitrary roots, creation or fallback.</summary>
public sealed class GitVisualLabContract
{
    public const string Consent = "NAP-F81-SYNTHETIC-READONLY-v1";
    public string Root { get; }
    private GitVisualLabContract(string root) => Root = root;

    public static GitVisualLabContract Validate(string[] args, string executableDirectory)
    {
        if (args.Length != 2 || args[0] != "--f81-git-visual-lab=" + Consent ||
            !args[1].StartsWith("--f81-root=", StringComparison.Ordinal))
            throw new InvalidDataException("F8.1 requires the exact explicit activation and isolated root; extra options are forbidden.");
        var supplied = args[1][11..];
        if (!Path.IsPathFullyQualified(supplied) || supplied.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidDataException("F8.1 requires a local absolute root.");
        var root = Path.GetFullPath(supplied).TrimEnd(Path.DirectorySeparatorChar);
        var name = Path.GetFileName(root);
        if (!name.StartsWith("NAP-F81-Lab-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[12..], "N", out var id) || id == Guid.Empty ||
            !Equal(Path.GetDirectoryName(root)!, Path.GetTempPath()))
            throw new InvalidDataException("F8.1 root must be a dedicated GUID directory directly inside the current temporary directory.");
        foreach (var prohibited in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NAP") })
            if (!string.IsNullOrEmpty(prohibited) && Within(root, prohibited))
                throw new InvalidDataException("F8.1 refuses a persistent NAP location.");
        if (!Equal(executableDirectory, Path.Combine(root, "app")))
            throw new InvalidDataException("F8.1 executable must be inside the dedicated root/app directory.");
        CheckAncestors(Path.GetFullPath(executableDirectory));
        var marker = Path.Combine(root, "f81-isolation.txt");
        if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != Consent.Length || File.ReadAllText(marker) != Consent)
            throw new InvalidDataException("F8.1 isolation contract missing or invalid.");
        // Reject roots accidentally reused for projects/data. Nothing below app is loaded as configuration.
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            if (Path.GetFileName(entry) is not ("app" or "build" or "evidence" or "f81-isolation.txt") ||
                (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unexpected data or link in F8.1 root.");
        return new(root);
    }

    private static bool Equal(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static bool Within(string a, string b) => Equal(a, b) || a.StartsWith(
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CheckAncestors(string path)
    {
        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
        {
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0 ||
                File.Exists(Path.Combine(dir.FullName, ".git")) || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                throw new InvalidDataException("F8.1 refuses absent directories, reparse points and repository ancestors.");
        }
    }
}
