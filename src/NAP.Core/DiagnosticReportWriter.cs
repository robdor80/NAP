using System.Text;
namespace NAP.Core;

public static class DiagnosticReportWriter
{
    /// <summary>Create a new report outside all known universe and application metadata roots. Never overwrite a source artifact.</summary>
    public static void Export(string path, string report, IEnumerable<UniverseStorageConfig> universes, string administrativeRoot)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || Encoding.UTF8.GetByteCount(report) > 256 * 1024)
            throw new InvalidDataException("A bounded text report destination is required.");
        var protectedRoots=universes.SelectMany(u=>new[]{u.WorkspaceRoot,u.ProductionRoot,u.ArchiveRoot}).Append(administrativeRoot);
        if(protectedRoots.Any(r=>ProductionPaths.Within(r,path)))throw new InvalidDataException("Reports must remain outside application and universe storage.");
        ProductionPaths.CheckPath(path,NapIssueCodes.CatalogInvalid);
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        var bytes=new UTF8Encoding(false).GetBytes(report);file.Write(bytes);file.Flush(true);
    }
}
