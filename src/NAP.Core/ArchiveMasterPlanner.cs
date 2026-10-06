namespace NAP.Core;

/// <summary>Plans preservation of exactly the validated original package; never writes or scans the archive tree.</summary>
public sealed class ArchiveMasterPlanner
{
    private readonly UniverseContext _context;

    public ArchiveMasterPlanner(UniverseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public ArchiveMasterPlan Plan(ValidatedAssetPackage package, ProcessingPlan processingPlan)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(processingPlan);
        RequireCoherentInputs(package, processingPlan);
        ArchiveRootValidator.Require(_context);
        var masters = package.AssetRule.PackageFiles.Where(rule => rule.ContentValidator == "png_master").ToArray();
        if (masters.Length != 1 || !masters[0].Required || !package.FilesByRole.ContainsKey(masters[0].Role))
            throw new InvalidOperationException("Archiving requires exactly one required png_master file present in the validated package.");
        if (package.FilesByRole.ContainsKey("manifest"))
            throw new InvalidOperationException("A package role collides with the archive's synthetic manifest role.");

        var relative = processingPlan.ProductionDestination.RelativeDirectory;
        var destination = ArchivePaths.Resolve(_context.Storage.ArchiveRoot, relative);
        var paths = package.FilesByRole.Append(new KeyValuePair<string, string>("manifest", package.ManifestPath));
        var files = new List<ArchiveMasterFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, source) in paths.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!Path.IsPathFullyQualified(source) || !ArchivePaths.Same(Path.GetDirectoryName(source)!, package.PackageRoot) ||
                ArchivePaths.Overlaps(source, _context.Storage.ArchiveRoot))
                throw new InvalidOperationException("The source must be an original flat validated package file outside ArchiveRoot.");
            var name = Path.GetFileName(source);
            if (!ArchivePaths.SafeSegment(name) || !names.Add(name)) throw new InvalidOperationException("Unsafe or colliding archive filenames.");
            var final = Path.Combine(destination, name);
            var fingerprint = ReadFingerprint(source, NapIssueCodes.ArchiveSourceChanged);
            files.Add(new ArchiveMasterFile(role, source, name, final, fingerprint.Digest, fingerprint.Size));
        }
        var master = files.Single(file => file.Role == masters[0].Role);
        if (master.SizeBytes <= 0) throw new InvalidOperationException("The PNG master cannot be empty.");
        var index = new ArchiveMasterIndexStore(_context).Load();
        var provisional = new ArchiveMasterPlan(package.AssetKey, processingPlan.AssetType, _context.Storage.ArchiveRoot,
            relative, destination, master.Digest, master.SizeBytes, files, [], new NapIssueReport([]), ArchiveMasterAction.IndexExisting);
        return Revalidate(provisional, index);
    }

    internal ArchiveMasterPlan Revalidate(ArchiveMasterPlan plan, ArchiveMasterIndex index)
    {
        if (plan.AssetKey.UniverseId != _context.Id || index.UniverseId != _context.Id ||
            !ArchivePaths.Same(plan.ArchiveRoot, _context.Storage.ArchiveRoot) ||
            !ArchivePaths.Same(plan.DestinationDirectory, ArchivePaths.Resolve(_context.Storage.ArchiveRoot, plan.RelativeDirectory)))
            throw new ArgumentException("The archive plan and index must belong to this universe and authorized root.", nameof(plan));
        ArchiveRootValidator.Require(_context);
        var issues = new List<NapIssue>();
        var missing = new List<ArchiveMasterFile>();
        foreach (var file in plan.Files)
        {
            if (!ArchivePaths.Same(file.DestinationPath, Path.Combine(plan.DestinationDirectory, file.FileName)))
                throw new ArgumentException("The planned file escapes the canonical archive destination.", nameof(plan));
            VerifyFile(file.SourcePath, file, NapIssueCodes.ArchiveSourceChanged);
        }
        var entry = index.Entries.SingleOrDefault(item => item.AssetId == plan.AssetKey.AssetId);
        var candidate = new AssetContentFingerprint(plan.AssetKey, plan.MasterDigest);
        foreach (var known in index.Entries)
        {
            var analysis = new AssetDuplicateAnalyzer().Analyze(candidate,
                new AssetContentFingerprint(new UniverseAssetKey(_context.Id, known.AssetId), known.MasterSha256));
            if (analysis.IsCollision)
                Add(NapIssueCodes.ArchiveAssetCollision, "The asset ID is already archived with a different PNG master.");
            else if (analysis.IsPossibleDuplicate)
                issues.Add(new NapIssue(NapIssueCodes.ArchivePossibleDuplicate, NapIssueSeverity.Warning, NapIssueDisposition.Continue,
                    "The master content is indexed under a different asset ID; manual duplicate policy applies.", detail: analysis.Issues.Issues[0].Detail));
        }
        if (entry is not null && (entry.RelativeDirectory != plan.RelativeDirectory || entry.AssetType != plan.AssetType || entry.MasterSizeBytes != plan.MasterSizeBytes))
            Add(NapIssueCodes.ArchiveIndexInconsistent, "The indexed asset type, directory or master size differs from the candidate.");
        foreach (var file in plan.Files)
        {
            try
            {
                if (!ArchivePaths.FileExists(file.DestinationPath))
                {
                    missing.Add(file);
                    if (entry is not null) Add(NapIssueCodes.ArchiveIndexInconsistent, "An indexed package file is missing.", file.DestinationPath);
                }
                else VerifyFile(file.DestinationPath, file, NapIssueCodes.ArchiveFileCollision);
            }
            catch (ArchiveStorageException ex) { issues.AddRange(ex.Issues.Issues); }
        }
        var action = missing.Count > 0 ? ArchiveMasterAction.CopyAndIndex : entry is null ? ArchiveMasterAction.IndexExisting : ArchiveMasterAction.AlreadyArchived;
        // A verified indexed candidate is already known; do not repeat duplicate warnings on an exact no-op.
        if (action == ArchiveMasterAction.AlreadyArchived && !issues.Any(issue => issue.StopsProcessing))
            issues.RemoveAll(issue => issue.Code == NapIssueCodes.ArchivePossibleDuplicate);
        return new ArchiveMasterPlan(plan.AssetKey, plan.AssetType, plan.ArchiveRoot, plan.RelativeDirectory, plan.DestinationDirectory,
            plan.MasterDigest, plan.MasterSizeBytes, plan.Files, missing, new NapIssueReport(issues), action);

        void Add(string code, string message, string? path = null) =>
            issues.Add(new NapIssue(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, path));
    }

    internal static (Sha256Digest Digest, long Size) ReadFingerprint(string path, string failureCode)
    {
        try
        {
            if (!ArchivePaths.FileExists(path, failureCode)) throw ArchiveStorageException.Stop(failureCode, "An expected archive/source file is missing.", path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var size = stream.Length;
            var digest = new Sha256Hasher().Compute(stream);
            if (stream.Length != size) throw ArchiveStorageException.Stop(failureCode, "The file size changed while hashing.", path);
            if (!ArchivePaths.FileExists(path, failureCode))
                throw ArchiveStorageException.Stop(failureCode, "The file disappeared while hashing.", path);
            return (digest, size);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw ArchiveStorageException.Stop(failureCode, "An expected archive/source file disappeared.", path, ex);
        }
    }

    internal static void VerifyFile(string path, ArchiveMasterFile expected, string failureCode)
    {
        var actual = ReadFingerprint(path, failureCode);
        if (actual.Size != expected.SizeBytes || actual.Digest != expected.Digest)
            throw ArchiveStorageException.Stop(failureCode, "The file size or SHA-256 differs from the frozen original.", path);
    }

    private void RequireCoherentInputs(ValidatedAssetPackage package, ProcessingPlan plan)
    {
        var manifest = package.Manifest;
        var destination = plan.ProductionDestination;
        if (package.AssetKey.UniverseId != _context.Id || package.AssetKey != plan.AssetKey || destination.AssetKey != plan.AssetKey ||
            manifest.UniverseId != _context.Id.Value || manifest.AssetId != package.AssetKey.AssetId ||
            plan.AssetType != manifest.AssetType || plan.ProductionProfile != manifest.ProductionProfile ||
            !ArchivePaths.Same(plan.PackageRoot, package.PackageRoot) || !ArchivePaths.Same(plan.ManifestPath, package.ManifestPath) ||
            !ArchivePaths.Same(destination.RootPath, _context.Storage.ProductionRoot) ||
            !ArchivePaths.Same(destination.FullDirectoryPath, ArchivePaths.Resolve(destination.RootPath, destination.RelativeDirectory)) ||
            plan.FilesByRole.Count != package.FilesByRole.Count ||
            package.FilesByRole.Any(pair => !plan.FilesByRole.TryGetValue(pair.Key, out var path) || !ArchivePaths.Same(path, pair.Value)) ||
            plan.Classification.Count != manifest.Classification.Count ||
            manifest.Classification.Any(pair => !plan.Classification.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new ArgumentException("The validated package and processing plan must have coherent identity, files, classification and universe storage.", nameof(plan));
    }
}
