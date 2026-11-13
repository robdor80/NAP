using System.IO.Compression;

namespace NAP.Core;

/// <summary>Read-only status of a shared, explicit ingestion directory. Never describes a publication.</summary>
public sealed record MixedInboxRoutingReport(int Examined, int Forwarded, int AlreadyPresent, int NeedsReview,
    string? Diagnostic, IReadOnlyList<UniverseId> RoutedUniverses, string? LastProblemFile = null);

/// <summary>Transport-only distributor for an explicitly configured mixed Inbox.
/// The native universe admission remains the sole authority for ZIP validation,
/// JobId, conversion, master archive and SQLite catalog writes.</summary>
internal sealed class MixedUniverseInboxRouter
{
    private readonly string _root;
    private readonly IReadOnlyDictionary<UniverseId, UniverseContext> _contexts;
    private readonly InboxAdmissionOptions _options;
    private int _offset;
    internal MixedUniverseInboxRouter(string root, IEnumerable<UniverseContext> contexts, InboxAdmissionOptions options)
    { _root = Path.GetFullPath(root); _contexts = contexts.ToDictionary(c => c.Id); _options = options; }

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
                if (!readiness.IsReady) continue;
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
                    throw new InvalidDataException("Declared universe is not installed in this Host.");
                if (!isAuthorized(universe)) continue; // Policy and live Host authority must both allow routing.
                InboxAdmissionEvidence.DirectoryBoundary(target.Storage.InboxRoot);
                archive.Dispose(); source.Position = 0;
                var digest = new Sha256Hasher().Compute(source);
                source.Position = 0;
                var filename = "nap-" + digest.Hex.ToLowerInvariant() + ".zip";
                var destination = Path.Combine(target.Storage.InboxRoot, filename);
                if (ProductionPaths.FileExists(destination, NapIssueCodes.PackageStructureInvalid))
                {
                    ProductionPaths.Verify(destination, digest, length, NapIssueCodes.PackageStructureInvalid);
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
                    File.Move(partial, destination, overwrite: false);
                    routed.Add(universe); forwarded++;
                }
                catch (IOException) when (ProductionPaths.FileExists(destination, NapIssueCodes.PackageStructureInvalid))
                {
                    ProductionPaths.Verify(destination, digest, length, NapIssueCodes.PackageStructureInvalid);
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
            }
        }
        return new(examined, forwarded, present, review, diagnostic, routed.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(), lastProblem);
    }
}
