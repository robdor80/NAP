namespace NAP.Core;

/// <summary>Production-specific attribute and canonical casing checks. Does not depend on archive path policy.</summary>
internal static class ProductionPaths
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static bool Same(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), Comparison);
    internal static bool Within(string root, string candidate) => ProductionDestinationResolver.IsStrictlyWithinRoot(root, candidate);
    internal static bool Overlaps(string first, string second) => Same(first, second) || Within(first, second) || Within(second, first);

    internal static bool SafeSegment(string value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') ||
            value.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')) return false;
        var device = value.Split('.')[0];
        return !new[] { "con", "prn", "aux", "nul", "conin$", "conout$" }.Contains(device, StringComparer.OrdinalIgnoreCase) &&
            !(device.Length == 4 && device[3] is >= '1' and <= '9' or '¹' or '²' or '³' &&
                (device.StartsWith("com", StringComparison.OrdinalIgnoreCase) || device.StartsWith("lpt", StringComparison.OrdinalIgnoreCase)));
    }

    internal static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(s => !SafeSegment(s)) ||
            relative.Split('/').Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase) || s.Equals("_nap", StringComparison.OrdinalIgnoreCase)))
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionPathInvalid, "Expected a safe canonical production relative directory.");
        var full = Path.GetFullPath(relative.Split('/').Aggregate(root, Path.Combine));
        if (!Within(root, full)) throw ProductionStorageException.Stop(NapIssueCodes.ProductionPathInvalid, "Production destinations must stay strictly inside ProductionRoot.");
        return full;
    }

    internal static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static FileAttributes? CheckPath(string path, string invalidCode, string reparseCode = NapIssueCodes.ProductionEntryReparse)
    {
        var full = Path.GetFullPath(path);
        var volume = Path.GetPathRoot(full)!;
        var components = full[volume.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = volume;
        var attributes = Attributes(current);
        CheckReparse();
        foreach (var component in components)
        {
            if (attributes is null) return null;
            if ((attributes & FileAttributes.Directory) == 0) throw ProductionStorageException.Stop(invalidCode, "A required path component is not a directory.", current);
            current = Path.Combine(current, component);
            attributes = Attributes(current);
            CheckReparse();
        }
        return attributes;
        void CheckReparse()
        {
            if (attributes is { } value && (value & FileAttributes.ReparsePoint) != 0)
                throw ProductionStorageException.Stop(reparseCode, "Production operations cannot follow reparse points.", current);
        }
    }

    internal static bool FileExists(string path, string code)
    {
        var attributes = CheckPath(path, code);
        if (attributes is null) return false;
        if ((attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw ProductionStorageException.Stop(code, "An expected entry is not a regular file.", path);
        return true;
    }

    internal static void CheckCasing(string parent, string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var entry in new DirectoryInfo(parent).EnumerateFileSystemInfos())
            if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && entry.Name != name)
                throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "An existing path conflicts with canonical production casing.", entry.FullName);
    }

    internal static void Destination(UniverseContext context, string relative, bool create = false)
    {
        ProductionStorageRootValidator.Require(context);
        Resolve(context.Storage.ProductionRoot, relative);
        var current = context.Storage.ProductionRoot;
        foreach (var segment in relative.Split('/'))
        {
            var parent = current;
            CheckCasing(parent, segment);
            current = Path.Combine(parent, segment);
            var attributes = CheckPath(current, NapIssueCodes.ProductionFileCollision);
            if (attributes is null)
            {
                if (!create) return;
                ProductionStorageRootValidator.Require(context);
                var parentAttributes = CheckPath(parent, NapIssueCodes.ProductionFileCollision);
                if (parentAttributes is null || (parentAttributes & FileAttributes.Directory) == 0)
                    throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "The canonical parent directory disappeared.", parent);
                CheckCasing(parent, segment);
                Directory.CreateDirectory(current);
                attributes = CheckPath(current, NapIssueCodes.ProductionFileCollision);
                CheckCasing(parent, segment);
            }
            if (attributes is null || (attributes & FileAttributes.Directory) == 0)
                throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "A file blocks a canonical production directory.", current);
        }
    }

    internal static void Verify(string path, Sha256Digest digest, long size, string code)
    {
        if (!FileExists(path, code)) throw ProductionStorageException.Stop(code, "An expected file is missing.", path);
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = input.Length;
            var actual = new Sha256Hasher().Compute(input);
            if (length != size || input.Length != size || actual != digest || !FileExists(path, code))
                throw ProductionStorageException.Stop(code, "The file size or SHA-256 differs from the frozen expected bytes.", path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw ProductionStorageException.Stop(code, "An expected file disappeared while reading.", path, ex); }
    }
}
