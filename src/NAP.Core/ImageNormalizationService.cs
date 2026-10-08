using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NAP.Core;

/// <summary>Explicit preparation only. Atomic, isolated candidate/control bundle; no Inbox, archive, production or Job writes.</summary>
public sealed class ImageNormalizationService
{
    private readonly UniverseContext _context;
    private readonly ImageNormalizationPolicy _policy;
    private readonly Action<string>? _checkpoint;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };
    public ImageNormalizationService(UniverseContext context, ImageNormalizationPolicy? policy = null)
        : this(context, policy, null) { }
    // Private fault-observation seam used by interruption tests; no public caller can replace filesystem operations.
    private ImageNormalizationService(UniverseContext context, ImageNormalizationPolicy? policy, Action<string>? checkpoint)
    { ArgumentNullException.ThrowIfNull(context); _context = context; _policy = policy ?? new(); _policy.Validate(); _checkpoint = checkpoint; }
    public string Root => Path.Combine(_context.Storage.WorkspaceRoot, "normalization");

    public ImageNormalizationProposal PrepareImage(string path, string assetId, string assetType, string productionProfile, CancellationToken ct = default)
    {
        RequireRoot(); var rule = Rule(assetType, productionProfile);
        var source = ImageNormalizationFiles.Read(path, _policy.MaxFileBytes, ct);
        var preview = new ImageNormalizationEngine().Prepare(source, rule.Conversion!, _policy, ct);
        VerifySource(path, ImageNormalizationEngine.Hash(source), _policy.MaxFileBytes, ct);
        return new(_context, Path.GetFullPath(path), ImageNormalizationEngine.Hash(source), assetId, assetType, productionProfile, rule, _policy, preview, null);
    }

    /// <summary>Explicit target selection can propose a profile correction; final generation still needs separate metadata approval.</summary>
    public async Task<ImageNormalizationProposal> PreparePackageAsync(string zipPath, string? targetProfile = null, CancellationToken ct = default)
    {
        RequireRoot(); ct.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(zipPath);
        var sourceBytes = ImageNormalizationFiles.Read(sourcePath, _policy.MaxPackageBytes, ct);
        var sourceDigest = ImageNormalizationEngine.Hash(sourceBytes);
        var sourceName = Path.GetFileName(sourcePath);
        if (!ArchivePaths.SafeSegment(sourceName)) throw ImageNormalizationException.Stop("normalization_source_invalid", "El nombre del ZIP debe ser seguro y portable.");
        EnsureRoot();
        var temporary = NewTemporary("inspecting");
        try
        {
            // Extract exactly the bytes whose digest binds the proposal, even if the Inbox changes and later reverts.
            var snapshotRoot = Path.Combine(temporary, "snapshot"); Directory.CreateDirectory(snapshotRoot);
            var snapshotPath = Path.Combine(snapshotRoot, sourceName); ImageNormalizationFiles.Write(snapshotPath, sourceBytes, ct);
            _checkpoint?.Invoke("source-snapshotted");
            var extracted = await new StagedPackageExtractor(new() { MaxEntries = _policy.MaxPackageEntries,
                MaxEntryBytes = _policy.MaxFileBytes, MaxTotalBytes = _policy.MaxPackageBytes }).ExtractAsync(snapshotPath, Path.Combine(temporary, "extracted"), ct).ConfigureAwait(false);
            if (extracted.Status != StagedPackageExtractionStatus.Extracted)
                throw ImageNormalizationException.Stop("normalization_zip_invalid", "ZIP rechazado: " + extracted.Reason);
            var packageRoot = extracted.FinalPath!;
            var entries = new DirectoryInfo(packageRoot).EnumerateFileSystemInfos().ToArray();
            if (entries.Any(e => (e.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0))
                throw ImageNormalizationException.Stop("normalization_package_invalid", "La normalización requiere un paquete plano sin links.");
            var manifestFiles = entries.Where(e => e.Name.EndsWith("_manifest.json", StringComparison.Ordinal)).ToArray();
            if (manifestFiles.Length != 1) throw ImageNormalizationException.Stop("normalization_manifest_invalid", "Debe existir un manifest único.");
            AssetManifestV2 manifest;
            try { manifest = AssetManifestV2Loader.Load(manifestFiles[0].FullName); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw ImageNormalizationException.Stop("normalization_manifest_invalid", "El manifest original no es válido.", ex); }
            if (manifest.UniverseId != _context.Id.Value)
                throw ImageNormalizationException.Stop("normalization_universe_mismatch", "El paquete pertenece a otro universo.");
            var selected = targetProfile ?? manifest.ProductionProfile;
            var rule = Rule(manifest.AssetType, selected);
            var conversion = rule.Conversion!;
            var sourceRule = rule.PackageFiles.Single(f => f.Role == conversion.SourceRole);
            var pngPath = Path.Combine(packageRoot, sourceRule.ResolveFileName(manifest.AssetId));
            var files = entries.ToDictionary(e => e.Name, e => ImageNormalizationFiles.Read(e.FullName, _policy.MaxFileBytes, ct), StringComparer.Ordinal);
            if (!files.TryGetValue(Path.GetFileName(pngPath), out var png))
                throw ImageNormalizationException.Stop("normalization_package_invalid", "Falta el maestro del perfil seleccionado.");
            // Validate the complete target contract on a private copy, before producing a preview.
            var manifestBytes = files[manifestFiles[0].Name];
            if (selected != manifest.ProductionProfile)
            {
                var node = JsonNode.Parse(manifestBytes)!.AsObject(); node["production_profile"] = selected;
                files[manifestFiles[0].Name] = JsonSerializer.SerializeToUtf8Bytes(node, Json);
                File.WriteAllBytes(manifestFiles[0].FullName, files[manifestFiles[0].Name]);
            }
            ValidatePackage(packageRoot);
            var preview = new ImageNormalizationEngine().Prepare(png, conversion, _policy, ct);
            files[Path.GetFileName(pngPath)] = preview.GetCandidatePng();
            File.WriteAllBytes(pngPath, files[Path.GetFileName(pngPath)]);
            ValidatePackage(packageRoot);
            _checkpoint?.Invoke("package-inspected");
            VerifySource(sourcePath, sourceDigest, _policy.MaxPackageBytes, ct);
            return new(_context, sourcePath, sourceDigest, manifest.AssetId, manifest.AssetType, manifest.ProductionProfile,
                rule, _policy, preview, files);
        }
        finally { ImageNormalizationFiles.RemoveOwnedTemporary(Root, temporary); }
    }

    public ImageNormalizationResult Decide(ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposal); ArgumentNullException.ThrowIfNull(authorization);
        if (proposal.Context != _context || proposal.Policy != _policy || authorization.OperationId != proposal.OperationId ||
            authorization.EvidenceSha256 != proposal.EvidenceSha256)
            throw ImageNormalizationException.Stop("normalization_approval_mismatch", "La autorización no corresponde a esta propuesta y contexto.");
        if (authorization.Approved && proposal.RequiresMetadataApproval && !authorization.MetadataCorrectionApproved)
            throw ImageNormalizationException.Stop("normalization_metadata_approval_required", "La corrección del perfil requiere autorización humana separada.");
        try { return DecideAuthorized(proposal, authorization, ct); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ImageNormalizationException or OperationCanceledException)
        {
            RecordFailure(proposal, authorization, error);
            throw;
        }
    }

    private ImageNormalizationResult DecideAuthorized(ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, CancellationToken ct)
    {
        RequireRoot(); ct.ThrowIfCancellationRequested();
        VerifySource(proposal.SourcePath, proposal.SourceSha256, proposal.IsPackage ? _policy.MaxPackageBytes : _policy.MaxFileBytes, ct);
        using var lease = ExecutionMutex.Acquire("Normalization", Root, _context.Id.Value);
        var final = Path.Combine(Root, proposal.OperationId);
        var decision = !authorization.Approved ? "Rejected" : !proposal.Preview.Geometry.HasChanges && !proposal.RequiresMetadataApproval ? "Unchanged" : "Approved";
        if (ImageNormalizationFiles.Guard(final) is not null)
        {
            var existing = ReadReceipt(proposal.OperationId);
            if (existing.Receipt.EvidenceSha256 != proposal.EvidenceSha256 || existing.Receipt.Decision != decision ||
                existing.Receipt.SourcePath != proposal.SourcePath || existing.Receipt.SourceSha256 != proposal.SourceSha256 ||
                existing.Receipt.OriginalPngSha256 != proposal.Preview.OriginalSha256 || existing.Receipt.CandidatePngSha256 != proposal.Preview.CandidateSha256 ||
                existing.Receipt.Geometry != proposal.Preview.Geometry || existing.Receipt.Policy != proposal.Policy ||
                existing.Receipt.MetadataCorrectionApproved != authorization.MetadataCorrectionApproved)
                throw ImageNormalizationException.Stop("normalization_collision", "El destino ya contiene una decisión diferente.");
            return existing;
        }
        EnsureRoot(); var temporary = NewTemporary("pending");
        try
        {
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (decision == "Approved")
            {
                var candidateRoot = Path.Combine(temporary, "candidate"); Directory.CreateDirectory(candidateRoot);
                if (proposal.Files is not null)
                {
                    var validationRoot = Path.Combine(temporary, "validation", proposal.AssetKey.AssetId);
                    Directory.CreateDirectory(validationRoot);
                    foreach (var (name, bytes) in proposal.Files) ImageNormalizationFiles.Write(Path.Combine(validationRoot, name), bytes, ct);
                    ValidatePackage(validationRoot);
                    var fileName = proposal.AssetKey.AssetId + ".zip"; var zip = Path.Combine(candidateRoot, fileName);
                    WriteZip(zip, proposal.Files, ct);
                    // Inspect the actual ZIP we will publish through the standard safe extractor/validator.
                    var extracted = new StagedPackageExtractor(new() { MaxEntries = _policy.MaxPackageEntries,
                        MaxEntryBytes = _policy.MaxFileBytes, MaxTotalBytes = _policy.MaxPackageBytes }).ExtractAsync(zip, Path.Combine(temporary, "verified"), ct).GetAwaiter().GetResult();
                    if (extracted.Status != StagedPackageExtractionStatus.Extracted) throw ImageNormalizationException.Stop("normalization_output_invalid", "El ZIP generado no se puede revalidar.");
                    ValidatePackage(extracted.FinalPath!);
                    foreach (var (name, bytes) in proposal.Files)
                        if (!ImageNormalizationFiles.Read(Path.Combine(extracted.FinalPath!, name), _policy.MaxFileBytes, ct).AsSpan().SequenceEqual(bytes))
                            throw ImageNormalizationException.Stop("normalization_output_invalid", "El ZIP generado no conserva los bytes aprobados.");
                    // Only the final candidate and its control record survive the atomic publication.
                    ImageNormalizationFiles.RemoveTemporaryChild(temporary, Path.Combine(temporary, "validation"));
                    ImageNormalizationFiles.RemoveTemporaryChild(temporary, Path.Combine(temporary, "verified"));
                    outputs.Add(fileName, ImageNormalizationEngine.Hash(ImageNormalizationFiles.Read(zip, _policy.MaxPackageBytes, ct)));
                }
                else
                {
                    var name = proposal.CandidatePngFileName; var path = Path.Combine(candidateRoot, name);
                    ImageNormalizationFiles.Write(path, proposal.Preview.Candidate, ct);
                    var digest = ImageNormalizationEngine.Hash(ImageNormalizationFiles.Read(path, _policy.MaxFileBytes, ct));
                    if (digest != proposal.Preview.CandidateSha256) throw ImageNormalizationException.Stop("normalization_output_invalid", "El candidato no coincide con la vista previa aprobada.");
                    outputs.Add(name, digest);
                }
            }
            _checkpoint?.Invoke("candidate-written");
            var receipt = Receipt(proposal, authorization, decision, outputs, decision == "Rejected" ? ["human_rejected"] : []);
            Directory.CreateDirectory(Path.Combine(temporary, "control"));
            ImageNormalizationFiles.Write(Path.Combine(temporary, "control", "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, Json), ct);
            VerifySource(proposal.SourcePath, proposal.SourceSha256, proposal.IsPackage ? _policy.MaxPackageBytes : _policy.MaxFileBytes, ct);
            _checkpoint?.Invoke("before-publish");
            RequireRoot(); ct.ThrowIfCancellationRequested(); ImageNormalizationFiles.Guard(final);
            Directory.Move(temporary, final); // Single commit point, no overwrite. A cancelled late caller can recover this exact receipt.
            _checkpoint?.Invoke("after-publish");
            return ReadReceipt(proposal.OperationId, alreadyRecorded: false);
        }
        finally { ImageNormalizationFiles.RemoveOwnedTemporary(Root, temporary); }
    }

    public ImageNormalizationResult ReadReceipt(string operationId, bool alreadyRecorded = true)
    {
        RequireRoot(); RequireId(operationId);
        return ReadAtDirectory(operationId, Path.Combine(Root, operationId), alreadyRecorded);
    }

    private ImageNormalizationResult ReadAtDirectory(string operationId, string directory, bool alreadyRecorded)
    {
        var bytes = ImageNormalizationFiles.Read(Path.Combine(directory, "control", "receipt.json"), 1024 * 1024, default);
        ImageNormalizationReceipt receipt;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateKeys(document.RootElement);
            receipt = JsonSerializer.Deserialize<ImageNormalizationReceipt>(bytes, Json) ?? throw new JsonException();
        }
        catch (JsonException ex) { throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El registro de control no es válido.", ex); }
        if (receipt.SchemaVersion != 1 || receipt.OperationId != operationId || receipt.UniverseId != _context.Id.Value ||
            receipt.Decision is not ("Approved" or "Rejected" or "Unchanged" or "Failed" or "Cancelled") || receipt.OutputSha256 is null ||
            receipt.IncidentCodes is null || receipt.IncidentCodes.Count > 16 || receipt.IncidentCodes.Any(code => string.IsNullOrWhiteSpace(code) || code.Length > 128) ||
            receipt.Conversion is null || receipt.Geometry is null || receipt.Policy is null ||
            string.IsNullOrEmpty(receipt.AssetId) || string.IsNullOrEmpty(receipt.AssetType) || string.IsNullOrEmpty(receipt.OriginalProfile) ||
            string.IsNullOrEmpty(receipt.SourcePath) || !Path.IsPathFullyQualified(receipt.SourcePath) || receipt.DecidedAtUtc.Offset != TimeSpan.Zero ||
            receipt.OutputSha256.Count != (receipt.Decision == "Approved" ? 1 : 0))
            throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El registro no corresponde a esta operación y universo.");
        new UniverseAssetKey(_context.Id, receipt.AssetId);
        receipt.Policy.Validate();
        var rule = Rule(receipt.AssetType, receipt.TargetProfile);
        if (rule.Conversion != receipt.Conversion || !ArchivePaths.SafeSegment(receipt.OriginalPngFileName) ||
            receipt.CandidatePngFileName != rule.PackageFiles.Single(f => f.Role == receipt.Conversion.SourceRole).ResolveFileName(receipt.AssetId) ||
            ImageNormalizationGeometry.Calculate(new(receipt.Geometry.OriginalWidth, receipt.Geometry.OriginalHeight, 8, 6, 0), receipt.Conversion, receipt.Policy) != receipt.Geometry ||
            receipt.Method != (receipt.Geometry.HasChanges ? "nearest-edge-canvas-v1" : "unchanged") ||
            (receipt.Decision is "Approved" or "Unchanged") && !receipt.ApprovedByUser || receipt.Decision == "Rejected" && receipt.ApprovedByUser ||
            receipt.Decision == "Unchanged" && (receipt.Geometry.HasChanges || receipt.OriginalProfile != receipt.TargetProfile || receipt.OriginalPngSha256 != receipt.CandidatePngSha256) ||
            receipt.Decision == "Approved" && receipt.OriginalProfile != receipt.TargetProfile && !receipt.MetadataCorrectionApproved ||
            new[] { receipt.SourceSha256, receipt.OriginalPngSha256, receipt.CandidatePngSha256, receipt.EvidenceSha256 }.Any(hash => !IsHash(hash)))
            throw ImageNormalizationException.Stop("normalization_receipt_invalid", "La geometría, el perfil o la autorización del registro no son coherentes.");
        foreach (var (name, hash) in receipt.OutputSha256)
        {
            if (name != (receipt.IsPackage ? receipt.AssetId + ".zip" : receipt.CandidatePngFileName) || !IsHash(hash))
                throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El candidato registrado tiene una ruta insegura.");
            var candidatePath = Path.Combine(directory, "candidate", name);
            VerifySource(candidatePath, hash, receipt.Policy.MaxPackageBytes, default);
            byte[] png;
            if (receipt.IsPackage)
            {
                using var zip = ZipFile.OpenRead(candidatePath);
                if (zip.Entries.Count > receipt.Policy.MaxPackageEntries) throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El candidato excede el contrato registrado.");
                var entry = zip.GetEntry(receipt.CandidatePngFileName) ?? throw ImageNormalizationException.Stop("normalization_receipt_invalid", "Falta el maestro registrado.");
                using var input = entry.Open(); png = ImageNormalizationFiles.ReadStream(input, receipt.Policy.MaxFileBytes, default);
            }
            else png = ImageNormalizationFiles.Read(candidatePath, receipt.Policy.MaxFileBytes, default);
            if (ImageNormalizationEngine.Hash(png) != receipt.CandidatePngSha256)
                throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El hash del maestro candidato no coincide con su registro.");
            using var pngStream = new MemoryStream(png, false);
            var validated = new PngMasterValidator().Validate(pngStream);
            if (!validated.IsValid || validated.ImageInfo!.Width != receipt.Geometry.CanvasWidth || validated.ImageInfo.Height != receipt.Geometry.CanvasHeight)
                throw ImageNormalizationException.Stop("normalization_receipt_invalid", "El maestro candidato no coincide con la geometría registrada.");
        }
        var expected = new HashSet<string>(StringComparer.Ordinal) { Path.Combine("control", "receipt.json") };
        foreach (var name in receipt.OutputSha256.Keys) expected.Add(Path.Combine("candidate", name));
        Inspect(directory);
        if (expected.Count != 0) throw ImageNormalizationException.Stop("normalization_receipt_invalid", "La operación registrada está incompleta.");
        return new(receipt, directory, alreadyRecorded);

        void Inspect(string parent)
        {
            foreach (var entry in new DirectoryInfo(parent).EnumerateFileSystemInfos())
            {
                var attributes = ImageNormalizationFiles.Guard(entry.FullName);
                var relative = Path.GetRelativePath(directory, entry.FullName);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (relative is not ("candidate" or "control")) throw ImageNormalizationException.Stop("normalization_receipt_invalid", "Carpeta inesperada en la operación.");
                    Inspect(entry.FullName);
                }
                else if (!expected.Remove(relative)) throw ImageNormalizationException.Stop("normalization_receipt_invalid", "Archivo inesperado en la operación.");
            }
        }
    }

    public ImageNormalizationRecoverySnapshot Scan()
    {
        RequireRoot(); var recorded = new List<ImageNormalizationResult>(); var incomplete = new List<string>(); var problems = new List<string>();
        if (ImageNormalizationFiles.Guard(Root) is null) return new(recorded, incomplete, problems);
        var entries = new DirectoryInfo(Root).EnumerateFileSystemInfos().Take(501).ToArray();
        if (entries.Length > 500) return new(recorded, incomplete, ["Más de 500 operaciones: revisar el almacenamiento explícitamente antes de cargar el historial."]);
        foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (recorded.Count >= 500) { problems.Add("Más de 500 registros de normalización; revisión explícita necesaria."); break; }
            try
            {
                ImageNormalizationFiles.Guard(entry.FullName);
                if ((entry.Attributes & FileAttributes.Directory) == 0) throw new InvalidDataException("Unexpected normalization file.");
                if (entry.Name == "failures")
                {
                    foreach (var attempt in new DirectoryInfo(entry.FullName).EnumerateFileSystemInfos().Take(501))
                    {
                        if (recorded.Count >= 500) { problems.Add("Más de 500 registros de normalización; revisión explícita necesaria."); break; }
                        try
                        {
                            ImageNormalizationFiles.Guard(attempt.FullName);
                            var parts = attempt.Name.Split('-');
                            if (parts.Length != 2) throw new ArgumentException("Invalid failure attempt name.");
                            RequireId(parts[0]); RequireId(parts[1]);
                            var result = ReadAtDirectory(parts[0], attempt.FullName, true);
                            if (result.Receipt.Decision is not ("Failed" or "Cancelled")) throw new InvalidDataException("Invalid failure outcome.");
                            recorded.Add(result);
                        }
                        catch (Exception error) when (error is ImageNormalizationException or IOException or UnauthorizedAccessException or ArgumentException)
                        { problems.Add(attempt.Name + ": " + error.Message); }
                    }
                }
                else
                {
                    if (entry.Name.StartsWith(".nap-", StringComparison.Ordinal)) incomplete.Add(entry.FullName);
                    else { RequireId(entry.Name); recorded.Add(ReadReceipt(entry.Name)); }
                }
            }
            catch (Exception ex) when (ex is ImageNormalizationException or IOException or UnauthorizedAccessException or ArgumentException)
            { problems.Add(entry.Name + ": " + ex.Message); }
        }
        return new(recorded.AsReadOnly(), incomplete.AsReadOnly(), problems.AsReadOnly());
    }

    private UniverseAssetRule Rule(string type, string profile)
    {
        if (!_context.Profile.TryGetAssetRule(type, profile, out var rule) || rule.Conversion is null)
            throw ImageNormalizationException.Stop("normalization_profile_unknown", "El perfil del manifest no está habilitado. Selecciona explícitamente un perfil de la misma familia; su corrección requiere aprobación separada.");
        if (rule.PackageFiles.SingleOrDefault(f => f.Role == rule.Conversion.SourceRole) is not { Required: true, ContentValidator: "png_master", Extension: ".png" })
            throw ImageNormalizationException.Stop("normalization_profile_unsupported", "El perfil debe declarar un maestro PNG requerido como origen de conversión.");
        return rule;
    }
    private void ValidatePackage(string root)
    {
        var result = new PackageSemanticValidator().Validate(root, _context);
        if (!result.IsValid) throw ImageNormalizationException.Stop("normalization_package_invalid", "El candidato no cumple el contrato del perfil: " + string.Join(", ", result.Issues.Issues.Select(i => i.Code)));
    }
    private void RequireRoot()
    {
        LocalUniverseSettingsStore.ValidateStructure([_context.Storage]);
        if (_context.Storage.WorkspaceRoot.StartsWith("\\\\", StringComparison.Ordinal) || new DriveInfo(_context.Storage.WorkspaceRoot).DriveType == DriveType.Network)
            throw ImageNormalizationException.Stop("normalization_workspace_invalid", "WorkspaceRoot debe ser local para la publicación atómica de candidatos.");
        if (ImageNormalizationFiles.Guard(_context.Storage.WorkspaceRoot) is not { } attributes || (attributes & FileAttributes.Directory) == 0)
            throw ImageNormalizationException.Stop("normalization_workspace_invalid", "WorkspaceRoot debe existir y estar controlado por NAP.");
        for (var directory = _context.Storage.WorkspaceRoot; directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, ".git")) || Directory.Exists(Path.Combine(directory, ".git")))
                throw ImageNormalizationException.Stop("normalization_workspace_invalid", "La preparación de candidatos no escribe en checkouts Git.");
        var value = ImageNormalizationFiles.Guard(Root);
        if (value is not null && (value & FileAttributes.Directory) == 0) throw ImageNormalizationException.Stop("normalization_collision", "El almacenamiento de normalización está ocupado.");
    }
    private void EnsureRoot() { RequireRoot(); Directory.CreateDirectory(Root); RequireRoot(); }
    private string NewTemporary(string suffix)
    {
        var path = Path.Combine(Root, $".nap-{Guid.NewGuid():N}.{suffix}");
        if (ImageNormalizationFiles.Guard(path) is not null) throw ImageNormalizationException.Stop("normalization_collision", "Temporal ocupado.");
        Directory.CreateDirectory(path); return path;
    }
    private static void RequireId(string id) { if (!Guid.TryParseExact(id, "N", out var value) || value.ToString("N") != id) throw new ArgumentException("Canonical normalization ID required.", nameof(id)); }
    private ImageNormalizationReceipt Receipt(ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization,
        string decision, Dictionary<string, string> outputs, IReadOnlyList<string> incidents) => new()
    {
        SchemaVersion = 1, OperationId = proposal.OperationId, UniverseId = _context.Id.Value, AssetId = proposal.AssetKey.AssetId,
        AssetType = proposal.AssetType, IsPackage = proposal.IsPackage, SourcePath = proposal.SourcePath, SourceSha256 = proposal.SourceSha256,
        OriginalPngSha256 = proposal.Preview.OriginalSha256, CandidatePngSha256 = proposal.Preview.CandidateSha256,
        OriginalPngFileName = proposal.OriginalPngFileName, CandidatePngFileName = proposal.CandidatePngFileName,
        EvidenceSha256 = proposal.EvidenceSha256, OriginalProfile = proposal.OriginalProfile, TargetProfile = proposal.TargetProfile,
        Conversion = proposal.Conversion, Geometry = proposal.Preview.Geometry, Policy = proposal.Policy, Method = proposal.Preview.Method,
        Decision = decision, ApprovedByUser = authorization.Approved, IncidentCodes = incidents,
        MetadataCorrectionApproved = authorization.MetadataCorrectionApproved, DecidedAtUtc = DateTimeOffset.UtcNow,
        OutputSha256 = new ReadOnlyDictionary<string, string>(outputs)
    };

    private void RecordFailure(ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, Exception error)
    {
        string? temporary = null;
        try
        {
            RequireRoot();
            // A committed result is already durable; acknowledgement loss cannot turn it into a failure.
            if (ImageNormalizationFiles.Guard(Path.Combine(Root, proposal.OperationId)) is not null) return;
            EnsureRoot(); var failures = Path.Combine(Root, "failures");
            ImageNormalizationFiles.Guard(failures); Directory.CreateDirectory(failures); ImageNormalizationFiles.Guard(failures);
            var code = error switch { ImageNormalizationException issue => issue.Code, OperationCanceledException => "normalization_cancelled",
                UnauthorizedAccessException => "normalization_access_denied", _ => "normalization_write_failed" };
            temporary = NewTemporary("failure-pending"); Directory.CreateDirectory(Path.Combine(temporary, "control"));
            var receipt = Receipt(proposal, authorization, error is OperationCanceledException ? "Cancelled" : "Failed", [], [code]);
            ImageNormalizationFiles.Write(Path.Combine(temporary, "control", "receipt.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, Json), default);
            RequireRoot(); ImageNormalizationFiles.Guard(failures);
            Directory.Move(temporary, Path.Combine(failures, proposal.OperationId + "-" + Guid.NewGuid().ToString("N")));
        }
        catch (Exception recordingError) when (recordingError is IOException or UnauthorizedAccessException or ImageNormalizationException or ArgumentException)
        { error.Data["normalization_control_record_unavailable"] = true; } // Preserve the original STOP; no repair or silent approval.
        finally
        {
            if (temporary is not null)
                try { ImageNormalizationFiles.RemoveOwnedTemporary(Root, temporary); }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or ImageNormalizationException)
                { error.Data["normalization_control_record_unavailable"] = true; }
        }
    }
    private static bool IsHash(string? hash) => hash?.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new JsonException("Duplicate control field."); RejectDuplicateKeys(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item);
    }
    private static void VerifySource(string path, string hash, long limit, CancellationToken ct)
    {
        if (ImageNormalizationEngine.Hash(ImageNormalizationFiles.Read(path, limit, ct)) != hash)
            throw ImageNormalizationException.Stop("normalization_source_changed", "El archivo ya no coincide con los bytes verificados. Prepara una nueva propuesta.");
    }
    private static void WriteZip(string path, Dictionary<string, byte[]> files, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var output = entry.Open(); output.Write(bytes);
            }
        file.Flush(true); ct.ThrowIfCancellationRequested();
    }
}
