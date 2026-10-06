namespace NAP.Core;

/// <summary>Read-only archive prerequisite, conversion in memory and physical production comparison.</summary>
public sealed class ProductionAssetPlanner
{
    private readonly UniverseContext _context;
    public ProductionAssetPlanner(UniverseContext context) { ArgumentNullException.ThrowIfNull(context); _context = context; }

    public ProductionAssetPlan Plan(ValidatedAssetPackage package, ProcessingPlan processingPlan, ArchiveMasterResult archiveResult, long maxInputPixels, JobId? jobId = null)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(processingPlan); ArgumentNullException.ThrowIfNull(archiveResult);
        ProductionStorageRootValidator.Require(_context);
        VerifyArchive(package, processingPlan, archiveResult);
        var conversion = new ImageConversionResolver().Resolve(package, maxInputPixels)
            ?? throw ProductionStorageException.Stop(NapIssueCodes.ProductionConversionFailed, "Production requires a declared image conversion.");
        var master = package.AssetRule.PackageFiles.Single(r => r.ContentValidator == "png_master");
        if (conversion.SourceRole != master.Role) throw Mismatch("The conversion source must be the archived required PNG master.");
        var destination = ProductionPaths.Resolve(_context.Storage.ProductionRoot, processingPlan.ProductionDestination.RelativeDirectory);
        var generated = ProductionImageEngine.Generate(conversion);
        VerifyArchive(package, processingPlan, archiveResult);
        var files = new List<ProductionAssetFile>();
        var webpName = package.AssetKey.AssetId + ".webp";
        using var webpStream = new MemoryStream(generated, writable: false);
        files.Add(new ProductionAssetFile(ProductionAssetFileKind.GeneratedWebp, "generated_webp", webpName, null,
            Path.Combine(destination, webpName), new Sha256Hasher().Compute(webpStream), generated.LongLength, generated));
        foreach (var archived in archiveResult.FilesVerified.Where(f => f.Role != conversion.SourceRole).OrderBy(f => f.FileName, StringComparer.Ordinal))
            files.Add(new ProductionAssetFile(archived.Role == "manifest" ? ProductionAssetFileKind.Manifest : ProductionAssetFileKind.Companion,
                archived.Role, archived.FileName, archived.SourcePath, Path.Combine(destination, archived.FileName), archived.Digest, archived.SizeBytes, null));
        if (files.Select(f => f.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw Mismatch("Production outputs have colliding filenames.");
        return Revalidate(new ProductionAssetPlan(package, processingPlan, archiveResult, _context.Storage.ProductionRoot, conversion, files, files, new NapIssueReport([]), jobId));
    }

    internal ProductionAssetPlan Revalidate(ProductionAssetPlan plan)
    {
        if (plan.AssetKey.UniverseId != _context.Id || !ProductionPaths.Same(plan.ProductionRoot, _context.Storage.ProductionRoot) ||
            !ProductionPaths.Same(plan.DestinationDirectory, ProductionPaths.Resolve(_context.Storage.ProductionRoot, plan.RelativeDirectory)))
            throw new ArgumentException("The production plan must belong to this authorized context.", nameof(plan));
        ProductionStorageRootValidator.Require(_context);
        VerifySources(plan);
        var missing = new List<ProductionAssetFile>(); var issues = new List<NapIssue>();
        try
        {
            ProductionPaths.Destination(_context, plan.RelativeDirectory);
            var evidence = ProductionExecutionEvidence.Load(_context, plan);
            if (evidence is null) RequireFreshDestination(_context, plan.RelativeDirectory);
            CheckEntries(plan);
            foreach (var file in plan.Files)
            {
                if (!ProductionPaths.Same(file.DestinationPath, Path.Combine(plan.DestinationDirectory, file.FileName))) throw new ArgumentException("A file escapes the canonical production destination.");
                if (!ProductionPaths.FileExists(file.DestinationPath, NapIssueCodes.ProductionFileCollision))
                {
                    if (evidence?.Owns(file) == true)
                        throw ProductionStorageException.Stop(NapIssueCodes.ProductionVerificationFailed, "A durably recorded publication is missing; explicit review is required.", file.DestinationPath);
                    missing.Add(file);
                }
                else
                {
                    if (evidence is null || !evidence.Owns(file))
                        throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "A preexisting final has no durable proof of publication by this same Job.", file.DestinationPath);
                    VerifyOutput(plan, file, file.DestinationPath, NapIssueCodes.ProductionFileCollision);
                }
            }
        }
        catch (ProductionStorageException ex) { issues.AddRange(ex.Issues.Issues); }
        return new ProductionAssetPlan(plan.Package, plan.Processing, plan.Archive, plan.ProductionRoot, plan.Conversion, plan.Files, missing, new NapIssueReport(issues), plan.JobId);
    }

    internal static void RequireFreshDestination(UniverseContext context, string relative)
    {
        ProductionPaths.Destination(context, relative);
        var destination = ProductionPaths.Resolve(context.Storage.ProductionRoot, relative);
        if (ProductionPaths.CheckPath(destination, NapIssueCodes.ProductionFileCollision) is not null)
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "A fresh execution cannot adopt a preexisting final destination, even with identical bytes.", destination);
    }

    internal void VerifySources(ProductionAssetPlan plan)
    {
        foreach (var file in plan.Archive.FilesVerified)
            ProductionPaths.Verify(file.SourcePath, file.Digest, file.SizeBytes, NapIssueCodes.ProductionSourceChanged);
        VerifyArchive(plan.Package, plan.Processing, plan.Archive);
    }

    private void VerifyArchive(ValidatedAssetPackage package, ProcessingPlan processing, ArchiveMasterResult archive)
    {
        if (archive.AssetKey != package.AssetKey || package.AssetKey.UniverseId != _context.Id || processing.AssetKey != package.AssetKey ||
            archive.RelativeDirectory != processing.ProductionDestination.RelativeDirectory || archive.Issues.ShouldStop || !Enum.IsDefined(archive.Outcome) ||
            !ProductionPaths.Same(processing.ProductionDestination.RootPath, _context.Storage.ProductionRoot) ||
            package.FilesByRole.ContainsKey("manifest") || ProductionPaths.Overlaps(package.PackageRoot, _context.Storage.ProductionRoot) ||
            archive.FilesVerified.Count != package.FilesByRole.Count + 1)
            throw Mismatch("The archive result does not correspond to this validated package and production plan.");
        ArchiveMasterPlan current;
        try { current = new ArchiveMasterPlanner(_context).Plan(package, processing); }
        catch (Exception ex) when (ex is ArchiveStorageException or ArgumentException or InvalidOperationException)
        { throw Mismatch("The package or physical archive no longer matches the verified prerequisite.", ex); }
        if (current.Issues.ShouldStop || current.Action != ArchiveMasterAction.AlreadyArchived || current.MasterDigest != archive.MasterDigest ||
            current.Files.Count != archive.FilesVerified.Count || current.Files.Any(file => !archive.FilesVerified.Any(f =>
                f.Role == file.Role && f.FileName == file.FileName && ProductionPaths.Same(f.SourcePath, file.SourcePath) &&
                ProductionPaths.Same(f.DestinationPath, file.DestinationPath) && f.Digest == file.Digest && f.SizeBytes == file.SizeBytes)))
            throw Mismatch("All original package files and the indexed physical master must match the archive result.");
    }

    internal static void CheckEntries(ProductionAssetPlan plan)
    {
        var attributes = ProductionPaths.CheckPath(plan.DestinationDirectory, NapIssueCodes.ProductionFileCollision);
        if (attributes is null) return;
        if ((attributes & FileAttributes.Directory) == 0) throw ProductionStorageException.Stop(NapIssueCodes.ProductionFileCollision, "The destination is not a directory.", plan.DestinationDirectory);
        foreach (var file in plan.Files) ProductionPaths.CheckCasing(plan.DestinationDirectory, file.FileName);
        foreach (var entry in new DirectoryInfo(plan.DestinationDirectory).EnumerateFileSystemInfos())
        {
            var value = ProductionPaths.CheckPath(entry.FullName, NapIssueCodes.ProductionUnexpectedEntry);
            if (value is null || (value & (FileAttributes.Directory | FileAttributes.Device)) != 0)
                throw ProductionStorageException.Stop(NapIssueCodes.ProductionUnexpectedEntry, "The final asset directory contains an unexpected entry.", entry.FullName);
            if (plan.Files.Any(f => f.FileName == entry.Name) || IsOwnTemp(plan, entry.Name)) continue;
            throw ProductionStorageException.Stop(NapIssueCodes.ProductionUnexpectedEntry, "Only declared output files and recognized NAP temporaries are allowed.", entry.FullName);
        }
    }

    internal static bool IsOwnTemp(ProductionAssetPlan plan, string name) => plan.Files.Any(file =>
    {
        var prefix = file.FileName + ".";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".tmp", StringComparison.Ordinal)) return false;
        var guid = name[prefix.Length..^4];
        return guid.Length == 32 && guid.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && Guid.TryParseExact(guid, "N", out var id) && id != Guid.Empty;
    });

    internal static void VerifyOutput(ProductionAssetPlan plan, ProductionAssetFile file, string path, string code)
    {
        ProductionPaths.Verify(path, file.Digest, file.SizeBytes, code);
        if (file.Kind != ProductionAssetFileKind.GeneratedWebp) return;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var content = new MemoryStream(bytes, writable: false);
            if (bytes.LongLength != file.SizeBytes || new Sha256Hasher().Compute(content) != file.Digest)
                throw ProductionStorageException.Stop(code, "The persisted WebP changed between fingerprint and decode verification.", path);
            ProductionImageEngine.Validate(bytes, plan.Conversion);
            ProductionPaths.CheckPath(path, code);
        }
        catch (ProductionStorageException ex) { throw ProductionStorageException.Stop(code, "The persisted WebP is invalid or has unexpected decoded dimensions.", path, ex); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw ProductionStorageException.Stop(code, "The persisted WebP disappeared during output verification.", path, ex); }
    }

    private static ProductionStorageException Mismatch(string message, Exception? inner = null) => ProductionStorageException.Stop(NapIssueCodes.ProductionArchiveMismatch, message, inner: inner);
}
