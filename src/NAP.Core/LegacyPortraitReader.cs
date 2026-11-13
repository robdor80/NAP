using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NAP.Core;

/// <summary>
/// Read-only compatibility inventory for approved pre-NAP portraits.
/// Does not invent PNG masters, manifests, AI approvals, or catalog records.
/// </summary>
public sealed record LegacyPortrait(
    string AssetId, string Culture, string Location, string Profession, string Sex,
    string RelativeDirectory, string WebpPath, string PromptPath, string InfoPath,
    string VisualIdentityPath, string WebpSha256, long WebpBytes, int Width, int Height)
{
    public string Label => $"{AssetId} · {Profession} · {Sex}";
}

public sealed record LegacyPortraitInventory(
    IReadOnlyList<LegacyPortrait> Portraits, IReadOnlyList<string> Issues);

public static class LegacyPortraitReader
{
    private const long MaxFileBytes = 32L * 1024 * 1024;

    public static LegacyPortraitInventory Scan(string assetsRoot)
    {
        if (!Path.IsPathFullyQualified(assetsRoot))
            throw new ArgumentException("Choose an absolute Assets directory.", nameof(assetsRoot));
        var root = Path.GetFullPath(assetsRoot);
        var portraits = Path.Combine(root, "portraits", "norgard", "treskal");
        if (!Directory.Exists(portraits))
            throw new DirectoryNotFoundException("No se encuentra portraits/norgard/treskal en la carpeta Assets seleccionada.");
        var result = new List<LegacyPortrait>();
        var issues = new List<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false
        };
        // Legacy assets occupy one directory per portrait; all writes are deliberately forbidden.
        var folders = Directory.EnumerateDirectories(portraits, "portrait_*", options)
            .OrderBy(path => path, StringComparer.Ordinal).Take(300).ToArray();
        foreach (var folder in folders)
        {
            var assetId = Path.GetFileName(folder);
            if (!AssetNamingRules.IsValidAssetId(assetId)) continue;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            {
                issues.Add(assetId + ": enlace de directorio rechazado.");
                continue;
            }
            if (File.Exists(Path.Combine(folder, assetId + "_manifest.json")))
                continue; // Current NAP asset: not a historical entry.
            var webp = Path.Combine(folder, assetId + ".webp");
            var prompt = Path.Combine(folder, assetId + "_prompt.md");
            var info = Path.Combine(folder, assetId + "_info.md");
            var identity = Path.Combine(folder, assetId + "_visual_identity.json");
            try
            {
                var expected = new HashSet<string>(new[] { Path.GetFileName(webp), Path.GetFileName(prompt),
                    Path.GetFileName(info), Path.GetFileName(identity) }, StringComparer.Ordinal);
                var actual = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToArray();
                if (actual.Length != 4 || actual.Any(x => !expected.Contains(x!)))
                    throw new InvalidDataException("El retrato histórico no tiene exactamente sus cuatro archivos.");
                foreach (var path in new[] { webp, prompt, info, identity })
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw new InvalidDataException("Archivo histórico no regular o enlace.");
                    var size = new FileInfo(path).Length;
                    if (size <= 0 || size > MaxFileBytes)
                        throw new InvalidDataException("Archivo histórico vacío o excesivamente grande.");
                }
                using var metadata = JsonDocument.Parse(File.ReadAllBytes(identity));
                var doc = metadata.RootElement;
                if (!string.Equals(doc.GetProperty("asset_id").GetString(), assetId, StringComparison.Ordinal))
                    throw new InvalidDataException("Identificador de identidad visual incoherente.");
                var id = doc.GetProperty("identity");
                var culture = id.GetProperty("culture").GetString() ?? "";
                var location = id.GetProperty("location").GetString() ?? "";
                var role = id.GetProperty("profession").GetString() ?? "";
                var sex = id.GetProperty("sex").GetString() ?? "";
                if (culture.Length == 0 || location.Length == 0 || role.Length == 0 || sex.Length == 0)
                    throw new InvalidDataException("Clasificación histórica incompleta.");
                using (var file = File.OpenRead(webp))
                {
                    Span<byte> signature = stackalloc byte[12];
                    file.ReadExactly(signature);
                    if (!signature[..4].SequenceEqual("RIFF"u8) || !signature[8..].SequenceEqual("WEBP"u8))
                        throw new InvalidDataException("El archivo no es WebP.");
                }
                var image = Image.Identify(webp) ?? throw new InvalidDataException("Imagen WebP ilegible.");
                using var input = File.OpenRead(webp);
                var sha = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
                result.Add(new LegacyPortrait(assetId, culture, location, role, sex,
                    Path.GetRelativePath(root, folder).Replace('\\', '/'),
                    webp, prompt, info, identity, sha, input.Length, image.Width, image.Height));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                issues.Add(assetId + ": " + e.Message);
            }
        }
        return new LegacyPortraitInventory(result.OrderBy(p => p.AssetId, StringComparer.Ordinal).ToArray(), issues.ToArray());
    }

    public static byte[] PreviewPng(LegacyPortrait portrait)
    {
        using var image = Image.Load(portrait.WebpPath);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(300, 375), Mode = ResizeMode.Max
        }));
        using var memory = new MemoryStream();
        image.SaveAsPng(memory);
        return memory.ToArray();
    }
}
