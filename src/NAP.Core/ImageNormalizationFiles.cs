namespace NAP.Core;

internal static class ImageNormalizationFiles
{
    internal static FileAttributes? Guard(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Normalization paths must be absolute.", nameof(path));
        try { return ArchivePaths.CheckPath(path, "normalization_path_invalid", "normalization_reparse_point"); }
        catch (ArchiveStorageException ex) { throw ImageNormalizationException.Stop("normalization_path_invalid", "La normalización no sigue links ni rutas inseguras.", ex); }
    }

    internal static byte[] Read(string path, long maxBytes, CancellationToken ct)
    {
        if (Guard(path) is not { } attributes || (attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw ImageNormalizationException.Stop("normalization_source_invalid", "El origen debe ser un archivo regular existente.");
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length > maxBytes) throw ImageNormalizationException.Stop("normalization_file_limit", "El archivo supera el límite de bytes.");
        return ReadStream(source, maxBytes, ct);
    }

    internal static byte[] ReadStream(Stream source, long maxBytes, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[81920]; int count;
        while ((count = source.Read(buffer)) != 0)
        {
            ct.ThrowIfCancellationRequested();
            if (output.Length + count > maxBytes) throw ImageNormalizationException.Stop("normalization_file_limit", "El archivo creció por encima del límite.");
            output.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested(); return output.ToArray();
    }

    internal static void Write(string path, ReadOnlySpan<byte> bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Guard(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true); ct.ThrowIfCancellationRequested();
    }

    internal static void RemoveOwnedTemporary(string root, string path)
    {
        if (!ArchivePaths.Within(root, path) || !Path.GetFileName(path).StartsWith(".nap-", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a private NAP temporary directory may be removed.");
        if (Guard(path) is null) return;
        CheckTree(path); Directory.Delete(path, true);
    }

    internal static void RemoveTemporaryChild(string temporary, string child)
    {
        if (!Path.GetFileName(temporary).StartsWith(".nap-", StringComparison.Ordinal) || !ArchivePaths.Within(temporary, child))
            throw new InvalidOperationException("Only a private operation's child may be removed.");
        Guard(temporary); Guard(child); CheckTree(child); Directory.Delete(child, true);
    }

    private static void CheckTree(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            Guard(entry.FullName);
            if ((entry.Attributes & FileAttributes.Directory) != 0) CheckTree(entry.FullName);
        }
    }
}
