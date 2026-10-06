using System.Text.Json;

namespace NAP.Core;

/// <summary>
/// Reads a stable, safely extracted flat package under an explicit universe context.
/// Does not mutate files. Operational I/O errors propagate; concurrent external mutation is unsupported.
/// </summary>
public sealed class PackageSemanticValidator
{
    public PackageSemanticValidationResult Validate(string packageRoot, UniverseContext context)
        => ValidateCore(packageRoot, context, allowArchivePublicationTemps: false);

    // Catalog-only reading of an archive copy. Original packages retain the exact strict public contract.
    internal PackageSemanticValidationResult ValidateArchiveCopy(string packageRoot, UniverseContext context)
        => ValidateCore(packageRoot, context, allowArchivePublicationTemps: true);

    private static PackageSemanticValidationResult ValidateCore(string packageRoot, UniverseContext context, bool allowArchivePublicationTemps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(context);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        var issues = new List<NapIssue>();
        var directory = new DirectoryInfo(root);
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(root);
        }
        catch (FileNotFoundException)
        {
            return Failure(Issue(NapIssueCodes.PackageRootInvalid, "Package root is not a directory.", root));
        }
        catch (DirectoryNotFoundException)
        {
            return Failure(Issue(NapIssueCodes.PackageRootInvalid, "Package root is not a directory.", root));
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return Failure(Issue(NapIssueCodes.PackageStructureInvalid, "Package root must not be a link or reparse point.", root));
        if ((attributes & FileAttributes.Directory) == 0)
            return Failure(Issue(NapIssueCodes.PackageRootInvalid, "Package root is not a directory.", root));

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in directory.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            var entryAttributes = entry.Attributes;
            if ((entryAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                issues.Add(Issue(NapIssueCodes.PackageStructureInvalid, "Packages must contain only top-level files, without links.", entry.FullName));
            else
                files.Add(entry.Name, entry.FullName);
        }
        // Do not read any candidate when the structure could traverse a directory or link.
        if (issues.Count != 0)
            return Failure(issues);
        var candidates = files.Keys.Where(name => name.EndsWith("_manifest.json", StringComparison.Ordinal)).ToArray();
        if (candidates.Length == 0)
            return Failure(Issue(NapIssueCodes.PackageManifestMissing, "The package manifest is missing.", root));
        if (candidates.Length != 1)
            return Failure(Issue(NapIssueCodes.PackageManifestAmbiguous, "The package has multiple manifest candidates.", root));
        var manifestName = candidates[0];
        var manifestPath = files[manifestName];
        AssetManifestV2 manifest;
        try
        {
            manifest = AssetManifestV2Loader.Load(manifestPath);
        }
        catch (JsonException)
        {
            return Failure(Issue(NapIssueCodes.PackageManifestInvalid, "The package manifest is invalid.", manifestPath,
                "Manifest JSON syntax, properties or value types are invalid."));
        }
        catch (InvalidDataException ex)
        {
            return Failure(Issue(NapIssueCodes.PackageManifestInvalid, "The package manifest is invalid.", manifestPath, ex.Message));
        }

        var canonicalManifest = manifest.AssetId + "_manifest.json";
        if (!string.Equals(manifestName, canonicalManifest, StringComparison.Ordinal))
            issues.Add(Issue(NapIssueCodes.PackageManifestFilenameMismatch, "The manifest filename is not canonical.", manifestPath));
        if (!string.Equals(directory.Name, manifest.AssetId, StringComparison.Ordinal))
            issues.Add(Issue(NapIssueCodes.PackageRootNameMismatch, "The package directory name does not match asset_id.", root));
        if (!ManifestUniverseScope.Matches(manifest, context))
        {
            issues.Add(Issue(NapIssueCodes.PackageUniverseMismatch, "The package belongs to another universe.", manifestPath));
            return Failure(issues);
        }
        if (!context.Profile.TryGetAssetRule(manifest.AssetType, manifest.ProductionProfile, out var rule))
        {
            issues.Add(Issue(NapIssueCodes.PackageRuleNotFound, "The asset type and production profile have no configured rule.", manifestPath));
            return Failure(issues);
        }

        var classification = rule.ValidateClassification(manifest.Classification);
        if (!classification.IsValid)
            issues.Add(Issue(NapIssueCodes.PackageClassificationInvalid, "Package classification dimensions are invalid.", manifestPath,
                $"missing: {string.Join(", ", classification.MissingRequired)}; not_allowed: {string.Join(", ", classification.NotAllowed.OrderBy(name => name, StringComparer.Ordinal))}"));
        var allowed = new HashSet<string>(StringComparer.Ordinal) { canonicalManifest };
        var present = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fileRule in rule.PackageFiles)
        {
            var name = fileRule.ResolveFileName(manifest.AssetId);
            allowed.Add(name);
            if (files.TryGetValue(name, out var path))
                present.Add(fileRule.Role, path);
            else if (fileRule.Required)
                issues.Add(Issue(NapIssueCodes.PackageRequiredFileMissing, "A required package file is missing.", Path.Combine(root, name), fileRule.Role));
        }
        foreach (var name in files.Keys.Where(name => !allowed.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
            if (!allowArchivePublicationTemps || !ProductionAssetPlanner.IsOwnTemp(allowed, name))
                issues.Add(Issue(NapIssueCodes.PackageUnexpectedFile, "The package contains an unexpected file.", files[name]));
        foreach (var fileRule in rule.PackageFiles)
        {
            if (fileRule.ContentValidator is null || !present.TryGetValue(fileRule.Role, out var path))
                continue;
            if (fileRule.ContentValidator == "png_master")
            {
                var issue = NapIssueMapper.Map(new PngMasterValidator().Validate(path), path);
                if (issue is not null)
                    issues.Add(issue);
            }
            else
                issues.Add(Issue(NapIssueCodes.PackageContentValidatorUnsupported, "The package content validator is unsupported.", path, fileRule.ContentValidator));
        }
        if (issues.Count != 0)
            return Failure(issues);
        var package = new ValidatedAssetPackage(new UniverseAssetKey(context.Id, manifest.AssetId), root, manifestPath, manifest, rule, present);
        return new PackageSemanticValidationResult(new NapIssueReport([]), package);
    }

    private static NapIssue Issue(string code, string message, string path, string? detail = null) =>
        new(code, NapIssueSeverity.Error, NapIssueDisposition.Stop, message, path, detail);

    private static PackageSemanticValidationResult Failure(NapIssue issue) => Failure([issue]);
    private static PackageSemanticValidationResult Failure(IEnumerable<NapIssue> issues) => new(new NapIssueReport(issues), null);
}
