using System.Security.Cryptography;
using System.Text.Json;
using NAP.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace NAP.Tests;

public sealed class LegacyMasterEvidenceIndexTests
{
    [Fact]
    public void ThirtyNineDownloadedPngsAreValidatedAndStoredWithoutChangingNativeOrHistoricalCatalog()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Catalog.Initialize();
        var nativeBefore = SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath));
        var source = Path.Combine(f.Root, "originals_downloaded");
        var assets = Path.Combine(f.Root, "existing_legacy_webps");
        var ids = new[] { "portrait_treskal_farmer_female_001" }
            .Concat(Enumerable.Range(1, 38).Select(n => $"portrait_treskal_farmer_male_{n:D3}")).ToArray();
        var examples = new List<object>();
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            var dir = Path.Combine(assets, "portraits", "norgard", "treskal", "farmer",
                i == 0 ? "female" : "male", id);
            Directory.CreateDirectory(dir);
            using (var image = new Image<Rgba32>(16, 20))
                image.Save(Path.Combine(dir, id + ".webp"), new WebpEncoder());
            File.WriteAllText(Path.Combine(dir, id + "_info.md"), "Approved pre-NAP portrait");
            File.WriteAllText(Path.Combine(dir, id + "_prompt.md"), "Old approved prompt");
            File.WriteAllText(Path.Combine(dir, id + "_visual_identity.json"),
                JsonSerializer.Serialize(new { asset_id = id, identity = new { culture = "Norgard",
                    location = "Treskal", profession = "farmer", sex = i == 0 ? "female" : "male" } }));

            var pngDir = Path.Combine(source, "Assets", "portraits", "Norgard", "Treskal",
                "farmer", i == 0 ? "female" : "male", id);
            Directory.CreateDirectory(pngDir);
            var png = Path.Combine(pngDir, id + ".png");
            using (var image = new Image<Rgba32>(16, 20))
                image.SaveAsPng(png);
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(png))).ToLowerInvariant();
            var old = i == 0 ? new string('0', 64) : i < 15 ? digest : null;
            var status = i == 0 ? "identico_a_png_git_2026_09_26" :
                i < 15 ? "coincide_sha_registro_historico" :
                "png_valido_sha_nuevo_sin_referencia_previa";
            examples.Add(new { asset_id = id, png_file = id + ".png", sha256_copia_local = digest,
                sha256_registro_septiembre_2026 = old, estado_conciliacion = status,
                cloud_api_verificada = false });
        }
        Assert.Equal(39, new LegacyPortraitIndex(f.Context).Import(assets));
        var legacyPath = new LegacyPortraitIndex(f.Context).DatabasePath;
        var legacyBefore = SHA256.HashData(File.ReadAllBytes(legacyPath));
        var report = Path.Combine(f.Root, "evidence.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new {
            schema = "nimroel_historical_master_reconciliation", schema_version = "1.0",
            universe = "nimroel", entries = examples
        }));

        var index = new LegacyMasterEvidenceIndex(f.Context);
        Assert.Equal(39, index.Import(report, source));
        Assert.Equal(39, index.ReadAll().Count);
        Assert.Equal(14, index.ReadAll().Count(x => x.EvidenceKind == "historic_sha_match"));
        Assert.Equal(24, index.ReadAll().Count(x => x.EvidenceKind == "new_local_fingerprint"));
        var exception = index.Get("portrait_treskal_farmer_female_001");
        Assert.NotNull(exception);
        Assert.Equal("git_20260926_match", exception.EvidenceKind);
        Assert.False(exception.RemoteCloudVerified);
        Assert.Equal("66c2de7dae0625dda21b6073da688e28e1ccbcb8", exception.GitCommit);
        Assert.Equal(39, index.Import(report, source)); // idempotent: no silent overwrites
        Assert.Equal(nativeBefore, SHA256.HashData(File.ReadAllBytes(f.Catalog.CatalogPath)));
        Assert.Equal(legacyBefore, SHA256.HashData(File.ReadAllBytes(legacyPath)));
    }

    [Fact]
    public void InvalidOrIncompleteManifestDoesNotCreateEvidenceDatabase()
    {
        using var f = new CatalogTestFixture(nimroel: true);
        f.Catalog.Initialize();
        var path = Path.Combine(f.Root, "empty.json");
        File.WriteAllText(path, "{\"schema\":\"nimroel_historical_master_reconciliation\",\"schema_version\":\"1.0\",\"universe\":\"nimroel\",\"entries\":[]}");
        var index = new LegacyMasterEvidenceIndex(f.Context);
        Assert.Throws<InvalidDataException>(() => index.Import(path, f.Root));
        Assert.False(File.Exists(index.DatabasePath));
    }
}
