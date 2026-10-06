using System.Text.Json;

namespace NAP.Core;

/// <summary>One interpretation of manifests/profiles/routing, applied to deterministic archive packages and production finals.</summary>
internal sealed class CatalogAssetReader(UniverseContext context)
{
    internal CatalogAssetSnapshot Capture(string relative)
    {
        try
        {
            ProductionStorageRootValidator.Require(context); ArchiveRootValidator.Require(context);
            ProductionPaths.Destination(context, relative);
            var production = ProductionPaths.Resolve(context.Storage.ProductionRoot, relative);
            RequireDirectory(production);
            var manifests = new DirectoryInfo(production).EnumerateFiles("*_manifest.json").ToArray();
            if (manifests.Length != 1) throw Invalid("Exactly one canonical manifest is required.");
            ProductionPaths.FileExists(manifests[0].FullName, NapIssueCodes.CatalogSourceInvalid);
            var manifest = AssetManifestV2Loader.Load(manifests[0].FullName);
            if (manifest.UniverseId != context.Id.Value || manifests[0].Name != manifest.AssetId + "_manifest.json") throw Invalid("Manifest identity/universe is incoherent.");
            var archive = ProductionPaths.Resolve(context.Storage.ArchiveRoot, relative);
            RequireDirectory(archive);
            var validated = new PackageSemanticValidator().ValidateArchiveCopy(archive, context);
            if (!validated.IsValid) throw new CatalogException(validated.Issues);
            var package = validated.Package!;
            var repository = new ProductionRepositoryValidator().Validate(context).Repository!;
            var route = new ProductionDestinationResolver().Resolve(package, repository);
            if (route.RelativeDirectory != relative || !ProductionPaths.Same(route.FullDirectoryPath, production)) throw Invalid("Historical/noncanonical layouts require explicit review; no migration is performed.");
            var conversion = new ImageConversionResolver().Resolve(package, long.MaxValue) ?? throw Invalid("The asset has no declared image conversion.");
            var sourceGeometry = new PngMasterValidator().Validate(conversion.SourcePath);
            if (!sourceGeometry.IsValid || new ImageConversionGeometryValidator().Validate(sourceGeometry.ImageInfo!, conversion).ShouldStop)
                throw Invalid("The archived master cannot satisfy the declared full-frame conversion geometry.");
            var masterRole = package.AssetRule.PackageFiles.Single(f => f.ContentValidator == "png_master").Role;
            var index = new ArchiveMasterIndexStore(context).Load();
            var entry = index.Entries.SingleOrDefault(e => e.AssetId == manifest.AssetId);
            if (entry is null || !entry.Verified || entry.AssetType != package.Manifest.AssetType || entry.RelativeDirectory != relative)
                throw Invalid("The archive master index does not identify this exact asset and canonical route.");
            var files = new List<CatalogFile>(); var documents = new List<CatalogDocument>();
            var archiveFiles = package.FilesByRole.Append(new KeyValuePair<string, string>("manifest", package.ManifestPath));
            var expectedProduction = new HashSet<string>(StringComparer.Ordinal) { manifest.AssetId + ".webp" };
            foreach (var (role, path) in archiveFiles)
            {
                var name = Path.GetFileName(path); var original = Read(path);
                var archivedFile = FileRecord(CatalogFileLocation.Archive, role, role == masterRole ? "master" : "document", relative + "/" + name, original);
                files.Add(archivedFile);
                if (role == masterRole)
                {
                    if (archivedFile.Digest != entry.MasterSha256 || archivedFile.SizeBytes != entry.MasterSizeBytes) throw Invalid("The indexed PNG master fingerprint is inconsistent.");
                    continue;
                }
                expectedProduction.Add(name);
                var final = Read(Path.Combine(production, name));
                if (!original.AsSpan().SequenceEqual(final)) throw Invalid("Production companions and manifest must match the archived originals byte-for-byte.");
                files.Add(FileRecord(CatalogFileLocation.Production, role, "document", relative + "/" + name, final));
                documents.Add(new CatalogDocument(role, final));
            }
            foreach (var item in new DirectoryInfo(production).EnumerateFileSystemInfos())
            {
                var attributes = ProductionPaths.CheckPath(item.FullName, NapIssueCodes.CatalogSourceInvalid);
                if (attributes is null || (attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0 ||
                    (!expectedProduction.Contains(item.Name) && !ProductionAssetPlanner.IsOwnTemp(expectedProduction, item.Name)))
                    throw Invalid("Production contains an unexpected entry, PNG master or noncanonical filename.");
            }
            var webp = Read(Path.Combine(production, manifest.AssetId + ".webp")); ProductionImageEngine.Validate(webp, conversion);
            var output = FileRecord(CatalogFileLocation.Production, "generated_webp", "generated_webp", relative + "/" + manifest.AssetId + ".webp", webp);
            files.Add(output);
            // File identity is profile-driven. Any JSON companion ending in _visual_identity.json has the canonical document capability.
            var identity = documents.SingleOrDefault(d => files.Any(f => f.Location == CatalogFileLocation.Production && f.Role == d.Role && f.RelativePath.EndsWith("_visual_identity.json", StringComparison.Ordinal)));
            var traits = identity is null ? [] : CatalogVisualIdentity.Extract(identity.ToArray());
            return new CatalogAssetSnapshot(package.AssetKey, package.Manifest.AssetType, package.Manifest.ProductionProfile, relative,
                entry.MasterSha256, entry.MasterSizeBytes, output.Digest, output.SizeBytes, package.Manifest.Classification, files, documents, traits);
        }
        catch (CatalogException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "The physical asset cannot be cataloged safely.", relative, ex); }
    }

    internal CatalogAssetSnapshot FromVerified(ValidatedAssetPackage package, ProcessingPlan processing, ArchiveMasterResult archive, ProductionAssetResult production)
    {
        ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(processing); ArgumentNullException.ThrowIfNull(archive); ArgumentNullException.ThrowIfNull(production);
        if (package.AssetKey.UniverseId != context.Id || package.AssetKey != processing.AssetKey || package.AssetKey != archive.AssetKey || package.AssetKey != production.AssetKey ||
            archive.Issues.ShouldStop || production.Issues.ShouldStop || processing.ProductionDestination.RelativeDirectory != production.RelativeDirectory || archive.RelativeDirectory != production.RelativeDirectory)
            throw Invalid("Only coherent verified results for this explicit universe may be registered.");
        var snapshot = Capture(production.RelativeDirectory);
        if (snapshot.AssetKey != package.AssetKey || snapshot.AssetType != processing.AssetType || snapshot.ProductionProfile != processing.ProductionProfile || snapshot.MasterDigest != archive.MasterDigest ||
            !snapshot.Classification.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(processing.Classification.OrderBy(p => p.Key, StringComparer.Ordinal)) ||
            !ProductionPaths.Same(processing.ProductionDestination.RootPath, context.Storage.ProductionRoot) ||
            archive.FilesVerified.Count != snapshot.Files.Count(f => f.Location == CatalogFileLocation.Archive) ||
            production.FilesVerified.Count != snapshot.Files.Count(f => f.Location == CatalogFileLocation.Production)) throw Invalid("The verified inputs differ from the physical snapshot.");
        foreach (var file in archive.FilesVerified)
        {
            var source = file.Role == "manifest" ? package.ManifestPath : package.FilesByRole.GetValueOrDefault(file.Role);
            if (source is null || !ProductionPaths.Same(source, file.SourcePath)) throw Invalid("Archive results do not freeze this package's original sources.");
            try { ProductionPaths.Verify(source, file.Digest, file.SizeBytes, NapIssueCodes.CatalogSourceInvalid); }
            catch (ProductionStorageException ex) { throw CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, "The original source changed after physical verification.", inner: ex); }
            Match(CatalogFileLocation.Archive, file.Role, file.FileName, file.Digest, file.SizeBytes, file.DestinationPath);
        }
        foreach (var file in production.FilesVerified)
            Match(CatalogFileLocation.Production, file.Role, file.FileName, file.Digest, file.SizeBytes, file.DestinationPath);
        return snapshot;
        void Match(CatalogFileLocation location, string role, string name, Sha256Digest digest, long size, string destination)
        {
            var file = snapshot.Files.SingleOrDefault(f => f.Location == location && f.Role == role);
            var root = location == CatalogFileLocation.Archive ? context.Storage.ArchiveRoot : context.Storage.ProductionRoot;
            if (file is null || file.RelativePath != snapshot.ProductionRelativeDirectory + "/" + name || file.Digest != digest || file.SizeBytes != size || !ProductionPaths.Same(destination, ProductionPaths.Resolve(root, file.RelativePath)))
                throw Invalid("Verified result files do not match the canonical physical files.");
        }
    }

    private static void RequireDirectory(string path)
    {
        var attributes = ProductionPaths.CheckPath(path, NapIssueCodes.CatalogSourceInvalid);
        if (attributes is null || (attributes & FileAttributes.Directory) == 0) throw Invalid("A canonical physical asset directory is missing.");
    }
    private static byte[] Read(string path)
    {
        if (!ProductionPaths.FileExists(path, NapIssueCodes.CatalogSourceInvalid)) throw Invalid("A required physical file is missing.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var bytes = new MemoryStream(); input.CopyTo(bytes);
        if (input.Length != bytes.Length) throw Invalid("A source changed while being read.");
        ProductionPaths.CheckPath(path, NapIssueCodes.CatalogSourceInvalid); return bytes.ToArray();
    }
    private static CatalogFile FileRecord(CatalogFileLocation location, string role, string kind, string relative, byte[] bytes)
    { using var input = new MemoryStream(bytes, writable: false); return new(location, role, kind, relative, new Sha256Hasher().Compute(input), bytes.LongLength, true); }
    private static CatalogException Invalid(string message) => CatalogException.Stop(NapIssueCodes.CatalogSourceInvalid, message);
}
