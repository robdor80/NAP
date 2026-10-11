using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NAP.Core;

internal sealed class PreparationFiles(UniverseContext context)
{
    internal string Resolve(PreparationBundle bundle) => ProductionPaths.Resolve(context.Storage.StateRoot, bundle.RelativeDirectory);
    internal PreparationBundle Describe(PreparationRecord record, string purpose, string relative,
        IReadOnlyList<AdmissionFile> files, NormalizationAuthority? authority = null)
    {
        var receipt = Receipt(record, purpose, files, authority);
        return new(relative, AutomationJson.Hash(receipt), files);
    }
    private static PreparationBundleReceipt Receipt(PreparationRecord record, string purpose,
        IReadOnlyList<AdmissionFile> files, NormalizationAuthority? authority) => new(1, record.OperationId,
        record.ItemId, record.AttemptId, record.JobId, record.UniverseId, purpose, record.AdmissionHash, files, authority);
    internal void Publish(PreparationRecord record, string purpose, PreparationBundle bundle,
        IReadOnlyDictionary<string, string> sources, IReadOnlyDictionary<string, byte[]>? generated,
        long maxBytes, CancellationToken token, Action<string>? checkpoint)
    {
        var final = Resolve(bundle); var receipt = Receipt(record, purpose, bundle.Files, purpose == "candidate" ? record.Authority : null);
        if (AutomationJson.Hash(receipt) != bundle.ReceiptHash) throw new InvalidDataException("Bundle intention does not bind its receipt.");
        if (ProductionPaths.CheckPath(final, NapIssueCodes.PackageStructureInvalid) is not null) { Verify(record, purpose, bundle); return; }
        if (bundle.Files.Count > 10001 || bundle.Files.Sum(f => f.SizeBytes) > maxBytes) throw new InvalidDataException("Preservation exceeds its storage budget.");
        var parent = Path.GetDirectoryName(final)!; InboxAdmissionEvidence.EnsureParents(parent, context.Storage.StateRoot);
        var temp = Path.Combine(parent, ".pending_" + record.OperationId + "_" + purpose);
        if (ProductionPaths.CheckPath(temp, NapIssueCodes.PackageStructureInvalid) is not null) throw new InvalidDataException("Interrupted bundle temporary needs review.");
        Directory.CreateDirectory(temp); checkpoint?.Invoke(purpose + "-temporary");
        foreach (var file in bundle.Files)
        {
            token.ThrowIfCancellationRequested(); var outputPath = ProductionPaths.Resolve(temp, file.RelativePath);
            var outputParent = Path.GetDirectoryName(outputPath)!;
            if (!ProductionPaths.Same(outputParent, temp)) InboxAdmissionEvidence.EnsureParents(outputParent, temp);
            if (generated is not null && generated.TryGetValue(file.RelativePath, out var bytes)) WriteNew(outputPath, bytes);
            else
            {
                var source = sources[file.RelativePath]; ProductionPaths.Verify(source, file.Hash, file.SizeBytes, NapIssueCodes.ProductionSourceChanged);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920]; long count = 0; int length;
                while ((length = input.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested(); count += length;
                    if (count > file.SizeBytes) throw new InvalidDataException("Preservation source changed while copying.");
                    output.Write(buffer, 0, length);
                }
                output.Flush(true);
                if (count != file.SizeBytes) throw new InvalidDataException("Preservation copy is incomplete.");
            }
            ProductionPaths.Verify(outputPath, file.Hash, file.SizeBytes, NapIssueCodes.ProductionSourceChanged);
        }
        WriteNew(Path.Combine(temp, "control.json"), Encoding.UTF8.GetBytes(AutomationJson.Encode(receipt)));
        checkpoint?.Invoke(purpose + "-before-publish"); token.ThrowIfCancellationRequested();
        InboxAdmissionEvidence.DirectoryBoundary(parent); ProductionPaths.CheckPath(final, NapIssueCodes.PackageStructureInvalid);
        Directory.Move(temp, final); checkpoint?.Invoke(purpose + "-published"); Verify(record, purpose, bundle);
    }
    internal void Verify(PreparationRecord record, string purpose, PreparationBundle bundle)
    {
        var root = Resolve(bundle); InboxAdmissionEvidence.DirectoryBoundary(root);
        var control = Path.Combine(root, "control.json");
        if (!ProductionPaths.FileExists(control, NapIssueCodes.PackageStructureInvalid) || new FileInfo(control).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Bundle control record is unavailable or excessive.");
        var bytes = File.ReadAllBytes(control);
        if (AutomationJson.HashBytes(bytes) != bundle.ReceiptHash) throw new InvalidDataException("Bundle control record is not bound to the durable intention.");
        var receipt = AutomationJson.Decode<PreparationBundleReceipt>(Encoding.UTF8.GetString(bytes));
        if (AutomationJson.Encode(receipt) != AutomationJson.Encode(Receipt(record, purpose, bundle.Files, purpose == "candidate" ? record.Authority : null)))
            throw new InvalidDataException("Bundle scope or inventory differs from its intention.");
        var expected = bundle.Files.Select(f => f.RelativePath).Append("control.json").ToHashSet(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in expected)
        {
            ProductionPaths.Resolve(root, relative);
            var parts = relative.Split('/'); for (var i = 1; i < parts.Length; i++) directories.Add(string.Join('/', parts.Take(i)));
        }
        var pending = new Stack<string>(); pending.Push(root); int inspected = 0; var maximumEntries = expected.Count + directories.Count;
        while (pending.TryPop(out var directory))
        {
            InboxAdmissionEvidence.DirectoryBoundary(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++inspected > maximumEntries) throw new InvalidDataException("Bundle contains unknown artifacts.");
                var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                var a = ProductionPaths.CheckPath(entry, NapIssueCodes.PackageStructureInvalid)!.Value;
                if ((a & FileAttributes.Directory) != 0) { if (!directories.Contains(relative)) throw new InvalidDataException("Unknown bundle directory."); pending.Push(entry); }
                else if (!expected.Remove(relative)) throw new InvalidDataException("Unknown bundle file.");
            }
        }
        if (expected.Count != 0) throw new InvalidDataException("Bundle is incomplete.");
        foreach (var file in bundle.Files) ProductionPaths.Verify(ProductionPaths.Resolve(root, file.RelativePath), file.Hash, file.SizeBytes, NapIssueCodes.ProductionSourceChanged);
    }
    internal static void VerifyZip(string path, IReadOnlyList<AdmissionFile> expected, CancellationToken token)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var crc = ZipEntryIntegrity.ReadChecksums(input, 10000, token); input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != expected.Count || crc.Length != expected.Count) throw new InvalidDataException("ZIP and extracted inventory differ.");
        var names = expected.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        for (var i = 0; i < zip.Entries.Count; i++)
        {
            var entry = zip.Entries[i];
            if (!ProductionPaths.SafeSegment(entry.FullName) || !names.Remove(entry.FullName, out var file) || entry.Length != file.SizeBytes)
                throw new InvalidDataException("ZIP entry differs from the verified inventory.");
            using var content = entry.Open(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long size = 0; uint checksum = uint.MaxValue; int count;
            while ((count = content.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested(); size += count;
                if (size > file.SizeBytes) throw new InvalidDataException("ZIP entry exceeded its declared bound.");
                hash.AppendData(buffer, 0, count); checksum = ZipEntryIntegrity.Update(checksum, buffer.AsSpan(0, count));
            }
            if (size != file.SizeBytes || new Sha256Digest(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()) != file.Hash || ~checksum != crc[i])
                throw new InvalidDataException("ZIP entry hash, size or CRC differs.");
        }
    }
    internal static byte[] ZipBytes(IReadOnlyDictionary<string, byte[]> files, long limit)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var (name, data) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                if (!ProductionPaths.SafeSegment(name)) throw new InvalidDataException("Candidate name is unsafe.");
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var output = entry.Open(); output.Write(data);
            }
        if (bytes.Length > limit) throw new InvalidDataException("Candidate ZIP exceeds its budget."); return bytes.ToArray();
    }
    internal static void WriteNew(string path, byte[] bytes)
    { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes); file.Flush(true); }
}
