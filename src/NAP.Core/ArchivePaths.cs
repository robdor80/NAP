namespace NAP.Core;

/// <summary>Portable logical paths and attribute checks; never resolves filesystem aliases.</summary>
internal static class ArchivePaths
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static bool Same(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), Comparison);

    internal static bool Within(string root, string candidate) => ProductionDestinationResolver.IsStrictlyWithinRoot(root, candidate);

    internal static bool Overlaps(string first, string second) => Same(first, second) || Within(first, second) || Within(second, first);

    internal static void RequireRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') ||
            relative.Split('/').Any(segment => !SafeSegment(segment)))
            throw new ArgumentException("Expected a safe portable relative directory.", nameof(relative));
        // This namespace belongs exclusively to NAP infrastructure.
        if (relative.Split('/')[0].Equals("_nap", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An asset directory cannot use the reserved _nap namespace.", nameof(relative));
    }

    internal static bool SafeSegment(string value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*'))
            return false;
        var device = value.Split('.')[0];
        return !device.Equals("con", StringComparison.OrdinalIgnoreCase) &&
            !device.Equals("prn", StringComparison.OrdinalIgnoreCase) &&
            !device.Equals("aux", StringComparison.OrdinalIgnoreCase) &&
            !device.Equals("nul", StringComparison.OrdinalIgnoreCase) &&
            !device.Equals("conin$", StringComparison.OrdinalIgnoreCase) &&
            !device.Equals("conout$", StringComparison.OrdinalIgnoreCase) &&
            !(device.Length == 4 && device[3] is >= '1' and <= '9' or '¹' or '²' or '³' &&
              (device.StartsWith("com", StringComparison.OrdinalIgnoreCase) || device.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)));
    }

    internal static string Resolve(string root, string relative)
    {
        RequireRelative(relative);
        var destination = Path.GetFullPath(relative.Split('/').Aggregate(root, Path.Combine));
        if (!Within(root, destination)) throw new ArgumentException("Archive destinations must stay strictly inside ArchiveRoot.");
        return destination;
    }

    internal static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    // Walk from the volume root down, so a parent junction is rejected before its child is inspected.
    internal static FileAttributes? CheckPath(string path, string invalidCode, string reparseCode)
    {
        var full = Path.GetFullPath(path);
        var volume = Path.GetPathRoot(full)!;
        var components = full[volume.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = volume;
        var attributes = Attributes(current);
        CheckAttributes(attributes, current);
        for (var i = 0; i < components.Length; i++)
        {
            if (attributes is null) return null;
            if ((attributes & FileAttributes.Directory) == 0)
                throw ArchiveStorageException.Stop(invalidCode, "A path component must be a directory.", current);
            current = Path.Combine(current, components[i]);
            attributes = Attributes(current);
            CheckAttributes(attributes, current);
        }
        return attributes;

        void CheckAttributes(FileAttributes? value, string entry)
        {
            if ((value & FileAttributes.ReparsePoint) != 0 && value is not null)
                throw ArchiveStorageException.Stop(reparseCode, "Archive operations cannot follow reparse points.", entry);
        }
    }

    internal static bool FileExists(string path, string invalidCode = NapIssueCodes.ArchiveFileCollision)
    {
        var attributes = CheckPath(path, invalidCode, NapIssueCodes.ArchiveEntryReparse);
        if (attributes is null) return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw ArchiveStorageException.Stop(invalidCode, "The archive entry must be a regular file.", path);
        return true;
    }

    internal static void EnsureDirectory(UniverseContext context, string path)
    {
        ArchiveRootValidator.Require(context);
        if (!Within(context.Storage.ArchiveRoot, path)) throw new ArgumentException("Cannot create directories outside ArchiveRoot.", nameof(path));
        var relative = Path.GetRelativePath(context.Storage.ArchiveRoot, path);
        var current = context.Storage.ArchiveRoot;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]))
        {
            current = Path.Combine(current, segment);
            var attributes = CheckPath(current, NapIssueCodes.ArchiveFileCollision, NapIssueCodes.ArchiveEntryReparse);
            if (attributes is null)
            {
                // Recheck the authorized root before each controlled directory creation.
                ArchiveRootValidator.Require(context);
                Directory.CreateDirectory(current);
                attributes = CheckPath(current, NapIssueCodes.ArchiveFileCollision, NapIssueCodes.ArchiveEntryReparse);
            }
            if ((attributes & FileAttributes.Directory) == 0)
                throw ArchiveStorageException.Stop(NapIssueCodes.ArchiveFileCollision, "A file blocks the archive directory.", current);
        }
    }
}
