using System.IO.Compression;
using System.Text.Json;

namespace NAP.Core;

/// <summary>Read-only status of a shared, explicit ingestion directory. Never describes a publication.</summary>
public sealed record MixedInboxRoutingReport(int Examined, int Forwarded, int AlreadyPresent, int NeedsReview,
    string? Diagnostic, IReadOnlyList<UniverseId> RoutedUniverses, string? LastProblemFile = null,
    IReadOnlyList<MixedInboxReceipt>? Receipts = null);
public sealed record MixedInboxReceipt(string FileName, string? Universe, string? InboxFileName, string Status, DateTimeOffset Utc);

/// <summary>Transport-only distributor for an explicitly configured mixed Inbox.
/// The native universe admission remains the sole authority for ZIP validation,
/// JobId, conversion, master archive and SQLite catalog writes.</summary>
internal sealed class MixedUniverseInboxRouter
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<UniverseId, UniverseContext> _contexts;
    private readonly InboxAdmissionOptions _options;
    private int _offset;
    private readonly string _historyPath;
    private readonly List<MixedInboxReceipt> _receipts = [];
    internal object PublicationGate { get; } = new();
    internal string HistoryPath => _historyPath;
    internal bool IsOriginal(string path) => ProductionPaths.Same(_root, Path.GetDirectoryName(path)!);

    internal IReadOnlyList<(string Original, string Copy)> FindReceipts(UniverseId universe, string[] copies)
    {
        lock (PublicationGate)
            return _receipts.Where(r => r.Universe == universe.Value && r.InboxFileName is { } name &&
                    copies.Contains(name, StringComparer.OrdinalIgnoreCase) && ProductionPaths.SafeSegment(name) &&
                    ProductionPaths.SafeSegment(r.FileName))
                .Select(r => (Path.Combine(_root, r.FileName), Path.Combine(_contexts[universe].Storage.InboxRoot, r.InboxFileName!))).ToArray();
    }

    internal void Forget(UniverseId universe, IReadOnlyList<string> copies)
    {
        lock (PublicationGate)
        {
            var remaining = _receipts.Where(r => r.Universe != universe.Value || !copies.Contains(r.InboxFileName!)).ToArray();
            if (remaining.Length == _receipts.Count) return;
            Save(remaining);
            _receipts.Clear();
            _receipts.AddRange(remaining);
        }
    }
    internal IReadOnlyList<MixedInboxReceipt> Receipts()
    {
        lock (PublicationGate) return _receipts.AsEnumerable().Reverse().ToArray();
    }
    internal MixedUniverseInboxRouter(string root, IEnumerable<UniverseContext> contexts, InboxAdmissionOptions options, string historyPath)
    {
        _root = Path.GetFullPath(root); _contexts = contexts.ToDictionary(c => c.Id); _options = options; _historyPath = historyPath;
        // Display history only: it never authorizes a copy or replaces byte verification.
        if (File.Exists(historyPath))
        {
            try
            {
                if (new FileInfo(historyPath).Length <= 128 * 1024 && !File.GetAttributes(historyPath).HasFlag(FileAttributes.ReparsePoint))
                    _receipts.AddRange((JsonSerializer.Deserialize<MixedInboxReceipt[]>(File.ReadAllBytes(historyPath)) ?? []).TakeLast(100));
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
    }
    private void Record(string name, string? universe, string? inboxName, string status)
    {
        lock (PublicationGate)
        {
            if (universe is not null && inboxName is { Length: 72 } &&
                inboxName.StartsWith("nap-", StringComparison.Ordinal) &&
                RemovedInboxPackages.Contains(_contexts[new UniverseId(universe)].Storage, new Sha256Digest(inboxName[4..^4]))) return;
            var prior = _receipts.LastOrDefault(r => r.FileName == name);
            if (prior is not null && prior.Universe == universe && prior.InboxFileName == inboxName &&
                (prior.Status == status || prior.Status == "forwarded" && status == "already_present")) return;
            var next = _receipts.Where(r => r.FileName != name).Append(new(name, universe, inboxName, status, DateTimeOffset.UtcNow)).TakeLast(100).ToArray();
            Save(next);
            _receipts.Clear();
            _receipts.AddRange(next);
        }
    }
    private void Save(IReadOnlyList<MixedInboxReceipt> receipts)
    {
        var temporary = _historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, receipts); output.Flush(true); }
            File.Move(temporary, _historyPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal async Task<MixedInboxRoutingReport> RouteAsync(Func<UniverseId, bool> isAuthorized, CancellationToken cancellation)
    {
        InboxAdmissionEvidence.DirectoryBoundary(_root);
        int examined = 0, forwarded = 0, present = 0, review = 0;
        string? diagnostic = null;
        string? lastProblem = null;
        var routed = new HashSet<UniverseId>();
        // Bounded sweeps; advance even when an invalid file must remain untouched.
        var candidates = new InboxPackageDetector().Detect(_root);
        var page = candidates.Skip(_offset).Take(_options.MaxPendingCandidates).ToArray();
        if (page.Length == 0 && candidates.Count > 0)
        { _offset = 0; page = candidates.Take(_options.MaxPendingCandidates).ToArray(); }
        _offset += page.Length;
        if (_offset >= candidates.Count) _offset = 0;
        var checker = new InboxPackageReadinessChecker(new()
            { RequiredSamples = _options.StabilitySamples, SampleInterval = _options.SampleInterval });
        foreach (var candidate in page)
        {
            cancellation.ThrowIfCancellationRequested();
            examined++;
            try
            {
                if (!ProductionPaths.SafeSegment(candidate.FileName) ||
                    !ProductionPaths.FileExists(candidate.FullPath, NapIssueCodes.PackageStructureInvalid))
                    throw new InvalidDataException("Unsafe shared source file.");
                var readiness = await checker.CheckAsync(candidate, cancellation).ConfigureAwait(false);
                if (!readiness.IsReady) { Record(candidate.FileName, null, null, "waiting_stable"); continue; }
                using var source = new FileStream(candidate.FullPath, FileMode.Open, FileAccess.Read, FileShare.None);
                var length = source.Length;
                if (length is < 1 || length > _options.MaxZipBytes)
                    throw new InvalidDataException("Shared ZIP length exceeds the admission budget.");
                using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count > _options.Extraction.MaxEntries) throw new InvalidDataException("ZIP entry count exceeds admission budget.");
                var manifests = archive.Entries.Where(e => e.Name.EndsWith("_manifest.json", StringComparison.Ordinal) &&
                    e.FullName == e.Name).Take(2).ToArray();
                if (manifests.Length != 1 || manifests[0].Length is < 1 or > 65536)
                    throw new InvalidDataException("A unique bounded root manifest is required.");
                using var data = new MemoryStream();
                using (var entry = manifests[0].Open())
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = await entry.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
                    {
                        if (data.Length + count > 65536) throw new InvalidDataException("Manifest exceeds the route budget.");
                        data.Write(buffer, 0, count);
                    }
                }
                data.Position = 0;
                var manifest = AssetManifestV2Loader.Load(data);
                if (manifests[0].Name != manifest.AssetId + "_manifest.json")
                    throw new InvalidDataException("Manifest name and identity disagree.");
                var universe = new UniverseId(manifest.UniverseId);
                if (!_contexts.TryGetValue(universe, out var target))
                { review++; lastProblem = candidate.FileName; diagnostic = "universe_not_installed"; Record(candidate.FileName, universe.Value, null, diagnostic); continue; }
                if (!isAuthorized(universe)) { Record(candidate.FileName, universe.Value, null, "universe_paused"); continue; }
                InboxAdmissionEvidence.DirectoryBoundary(target.Storage.InboxRoot);
                archive.Dispose(); source.Position = 0;
                var digest = new Sha256Hasher().Compute(source);
                source.Position = 0;
                if (RemovedInboxPackages.Contains(target.Storage, digest)) continue;
                var filename = "nap-" + digest.Hex.ToLowerInvariant() + ".zip";
                var destination = Path.Combine(target.Storage.InboxRoot, filename);
                if (ProductionPaths.FileExists(destination, NapIssueCodes.PackageStructureInvalid))
                {
                    ProductionPaths.Verify(destination, digest, length, NapIssueCodes.PackageStructureInvalid);
                    Record(candidate.FileName, universe.Value, filename, "already_present");
                    present++; continue;
                }
                // Only publish an exact, flushed transport copy, never overwrite or move a master.
                var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await source.CopyToAsync(output, 81920, cancellation).ConfigureAwait(false);
                        await output.FlushAsync(cancellation).ConfigureAwait(false); output.Flush(true);
                    }
                    ProductionPaths.Verify(partial, digest, length, NapIssueCodes.PackageStructureInvalid);
                    source.Position = 0;
                    if (new Sha256Hasher().Compute(source) != digest || source.Length != length)
                        throw new InvalidDataException("Shared source changed while routing.");
                    if (!isAuthorized(universe)) continue; // Pause while copying revokes publication.
                    InboxAdmissionEvidence.DirectoryBoundary(target.Storage.InboxRoot);
                    lock (PublicationGate)
                    {
                        if (RemovedInboxPackages.Contains(target.Storage, digest)) continue;
                        File.Move(partial, destination, overwrite: false);
                        Record(candidate.FileName, universe.Value, filename, "forwarded");
                        routed.Add(universe); forwarded++;
                    }
                }
                catch (IOException) when (ProductionPaths.FileExists(destination, NapIssueCodes.PackageStructureInvalid))
                {
                    ProductionPaths.Verify(destination, digest, length, NapIssueCodes.PackageStructureInvalid);
                    Record(candidate.FileName, universe.Value, filename, "already_present");
                    present++;
                }
                finally
                {
                    if (File.Exists(partial)) File.Delete(partial); // Only this invocation's named partial.
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Text.Json.JsonException or InvalidOperationException or ProductionStorageException or NotSupportedException or ArgumentException)
            {
                review++;
                lastProblem = candidate.FileName;
                diagnostic = error is ProductionStorageException ? "unsafe_storage" :
                    error is UnauthorizedAccessException ? "access_denied" : "invalid_or_unavailable_zip";
                Record(candidate.FileName, null, null, diagnostic);
            }
        }
        return new(examined, forwarded, present, review, diagnostic, routed.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(), lastProblem, Receipts());
    }
}
