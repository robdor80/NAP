using System.Text.Json;

namespace NAP.Core;

/// <summary>Per-Job provenance, outside production. Only verified no-overwrite publications acquire ownership.</summary>
internal sealed class ProductionExecutionEvidence
{
    private readonly UniverseContext _context;
    private readonly ProductionAssetPlan _plan;
    private readonly HashSet<string> _published;
    private readonly Sha256Digest _snapshot;
    private string DirectoryPath => Path.Combine(_context.Storage.StateRoot, "production-executions");
    private string ReceiptPath => Path.Combine(DirectoryPath, _plan.JobId!.Value + ".json");

    private ProductionExecutionEvidence(UniverseContext context, ProductionAssetPlan plan, HashSet<string> published)
    { _context = context; _plan = plan; _published = published; _snapshot = Snapshot(plan); }

    internal bool Owns(ProductionAssetFile file) => _published.Contains(file.FileName);
    internal bool IsComplete => _published.Count == _plan.Files.Count;

    internal static ProductionExecutionEvidence? Load(UniverseContext context, ProductionAssetPlan plan)
    {
        if (plan.JobId is null) return null;
        var evidence = new ProductionExecutionEvidence(context, plan, new(StringComparer.Ordinal));
        var state = State(context, plan.JobId);
        if (state is not (JobState.Audited or JobState.Executed or JobState.Verified or JobState.Completed))
            throw Invalid("Production provenance requires the same active audited Job or its COMPLETED checkpoint.");
        if (!ProductionPaths.FileExists(evidence.ReceiptPath, NapIssueCodes.ProductionRecoveryInvalid))
        {
            if (state != JobState.Audited) throw Invalid("The execution checkpoint has no durable production provenance.");
            return null;
        }
        try
        {
            using var input = new FileStream(evidence.ReceiptPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;
            var expected = new HashSet<string>(["schema_version", "job_id", "universe_id", "asset_id", "production_root", "relative_directory", "snapshot_sha256", "published"], StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException();
            foreach (var property in root.EnumerateObject()) if (!expected.Remove(property.Name)) throw new FormatException();
            if (expected.Count != 0 || root.GetProperty("schema_version").GetRawText() != "1" ||
                root.GetProperty("job_id").GetString() != plan.JobId.Value || root.GetProperty("universe_id").GetString() != plan.AssetKey.UniverseId.Value ||
                root.GetProperty("asset_id").GetString() != plan.AssetKey.AssetId ||
                !ProductionPaths.Same(root.GetProperty("production_root").GetString()!, plan.ProductionRoot) ||
                root.GetProperty("relative_directory").GetString() != plan.RelativeDirectory ||
                new Sha256Digest(root.GetProperty("snapshot_sha256").GetString()!) != evidence._snapshot) throw new FormatException();
            foreach (var value in root.GetProperty("published").EnumerateArray())
            {
                var name = value.GetString();
                if (name is null || !plan.Files.Any(f => f.FileName == name) || !evidence._published.Add(name)) throw new FormatException();
            }
            return evidence;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException)
        { throw Invalid("The durable production receipt does not match this Job, asset, roots or frozen execution.", ex); }
    }

    internal static ProductionExecutionEvidence? Begin(UniverseContext context, ProductionAssetPlan plan, ExecutionMutex? jobLease)
    {
        if (plan.JobId is null) return null;
        jobLease!.Require("Job", context.Storage.StateRoot, plan.JobId.Value);
        if (State(context, plan.JobId) == JobState.Completed) throw Invalid("COMPLETED provenance is read-only.");
        var evidence = Load(context, plan);
        if (evidence is not null) return evidence;
        ProductionAssetPlanner.RequireFreshDestination(context, plan.RelativeDirectory);
        evidence = new ProductionExecutionEvidence(context, plan, new(StringComparer.Ordinal));
        evidence.Save(overwrite: false, jobLease);
        return evidence;
    }

    internal void Record(ProductionAssetFile file, ExecutionMutex jobLease)
    {
        jobLease.Require("Job", _context.Storage.StateRoot, _plan.JobId!.Value);
        if (State(_context, _plan.JobId) == JobState.Completed) throw Invalid("COMPLETED provenance is read-only.");
        if (!_plan.Files.Contains(file) || _published.Contains(file.FileName)) throw Invalid("Ownership can only follow a new verified publication.");
        _published.Add(file.FileName);
        Save(overwrite: true, jobLease);
    }

    internal static JobState State(UniverseContext context, JobId job)
    {
        var attributes = ProductionPaths.CheckPath(context.Storage.StateRoot, NapIssueCodes.ProductionRecoveryInvalid);
        if (attributes is null || (attributes & FileAttributes.Directory) == 0) throw Invalid("Production execution requires an existing controlled StateRoot.");
        ProductionPaths.FileExists(Path.Combine(context.Storage.StateRoot, job.Value + ".json"), NapIssueCodes.ProductionRecoveryInvalid);
        try { return new JobStateStore(context).Load(job).State; }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
        { throw Invalid("The same scoped Job journal must exist and be valid.", ex); }
    }

    private void Save(bool overwrite, ExecutionMutex jobLease)
    {
        jobLease.Require("Job", _context.Storage.StateRoot, _plan.JobId!.Value);
        State(_context, _plan.JobId);
        var attributes = ProductionPaths.CheckPath(DirectoryPath, NapIssueCodes.ProductionRecoveryInvalid);
        if (attributes is null) Directory.CreateDirectory(DirectoryPath);
        attributes = ProductionPaths.CheckPath(DirectoryPath, NapIssueCodes.ProductionRecoveryInvalid);
        if (attributes is null || (attributes & FileAttributes.Directory) == 0) throw Invalid("The provenance directory is not a controlled directory.");
        ProductionPaths.FileExists(ReceiptPath, NapIssueCodes.ProductionRecoveryInvalid);
        var temp = ReceiptPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); writer.WriteNumber("schema_version", 1);
                writer.WriteString("job_id", _plan.JobId.Value); writer.WriteString("universe_id", _plan.AssetKey.UniverseId.Value);
                writer.WriteString("asset_id", _plan.AssetKey.AssetId); writer.WriteString("production_root", _plan.ProductionRoot);
                writer.WriteString("relative_directory", _plan.RelativeDirectory); writer.WriteString("snapshot_sha256", _snapshot.Hex);
                writer.WriteStartArray("published"); foreach (var name in _published.Order(StringComparer.Ordinal)) writer.WriteStringValue(name);
                writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            }
            output.Flush(flushToDisk: true);
        }
        ProductionPaths.CheckPath(DirectoryPath, NapIssueCodes.ProductionRecoveryInvalid);
        ProductionPaths.FileExists(ReceiptPath, NapIssueCodes.ProductionRecoveryInvalid);
        // This replace applies only to StateRoot provenance, never to production assets.
        File.Move(temp, ReceiptPath, overwrite);
    }

    private static Sha256Digest Snapshot(ProductionAssetPlan plan)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject(); writer.WriteString("job_id", plan.JobId?.Value);
            writer.WriteString("universe_id", plan.AssetKey.UniverseId.Value); writer.WriteString("asset_id", plan.AssetKey.AssetId);
            writer.WriteString("production_root", plan.ProductionRoot); writer.WriteString("relative_directory", plan.RelativeDirectory);
            writer.WriteString("asset_type", plan.AssetType);
            writer.WriteString("master_sha256", plan.Archive.MasterDigest.Hex);
            writer.WriteString("source_path", plan.Conversion.SourcePath); writer.WriteString("source_role", plan.Conversion.SourceRole);
            writer.WriteString("kind", plan.Conversion.Kind.ToString()); writer.WriteNumber("width", plan.Conversion.OutputWidth);
            writer.WriteNumber("height", plan.Conversion.OutputHeight); writer.WriteNumber("quality", plan.Conversion.WebpQuality);
            writer.WriteNumber("max_input_pixels", plan.Conversion.MaxInputPixels); writer.WriteStartArray("files");
            foreach (var file in plan.Files.OrderBy(f => f.FileName, StringComparer.Ordinal))
            {
                writer.WriteStartObject(); writer.WriteString("name", file.FileName); writer.WriteString("kind", file.Kind.ToString());
                writer.WriteString("role", file.Role); writer.WriteString("sha256", file.Digest.Hex); writer.WriteNumber("size", file.SizeBytes); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
        }
        bytes.Position = 0; return new Sha256Hasher().Compute(bytes);
    }

    private static ProductionStorageException Invalid(string message, Exception? inner = null) =>
        ProductionStorageException.Stop(NapIssueCodes.ProductionRecoveryInvalid, message, inner: inner);
}
