using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

/// <summary>Small flat packages under a controlled temp root; never uses production storage.</summary>
internal sealed class PackageSemanticTestFixture : IDisposable
{
    internal const string DefaultAssetId = "portrait_example_001";
    internal string Root { get; } = Directory.CreateTempSubdirectory("nap-semantic-").FullName;
    internal string PackageRoot { get; private set; }
    internal UniverseContext Context { get; }
    internal AssetManifestV2 Manifest { get; set; }
    internal string ManifestPath => Path.Combine(PackageRoot, Manifest.AssetId + "_manifest.json");

    internal PackageSemanticTestFixture(UniverseProfile? profile = null, string assetId = DefaultAssetId,
        string assetType = "portrait", string productionProfile = "portrait_npc")
    {
        profile ??= UniverseProfileLoader.Load(UniverseProfileLoaderTests.ConfigPath);
        Context = new UniverseContext(profile, new UniverseStorageConfig(profile.Id,
            Path.Combine(Root, "workspace"), Path.Combine(Root, "production"), Path.Combine(Root, "archive")));
        PackageRoot = Directory.CreateDirectory(Path.Combine(Root, assetId)).FullName;
        profile.TryGetAssetRule(assetType, productionProfile, out var rule);
        Manifest = new AssetManifestV2
        {
            SchemaVersion = 2, UniverseId = profile.Id.Value, AssetId = assetId, AssetType = assetType,
            ProductionProfile = productionProfile,
            Classification = rule?.RequiredClassification.ToDictionary(name => name, _ => "unregistered_value", StringComparer.Ordinal) ?? []
        };
        WriteManifest();
        foreach (var file in rule?.PackageFiles ?? [])
        {
            File.WriteAllBytes(PathFor(file), file.ContentValidator == "png_master" ? Png() : Encoding.UTF8.GetBytes("opaque companion, deliberately not JSON"));
        }
        File.WriteAllText(Path.Combine(Root, "outside.txt"), "outside sentinel");
    }

    internal string PathFor(AssetPackageFileRule file) => Path.Combine(PackageRoot, file.ResolveFileName(Manifest.AssetId));
    internal void WriteManifest() => File.WriteAllText(ManifestPath, JsonSerializer.Serialize(Manifest));
    internal void RenameRoot(string name)
    {
        var newPath = Path.Combine(Root, name);
        Directory.Move(PackageRoot, newPath);
        PackageRoot = newPath;
    }

    internal PackageSemanticValidationResult ValidateReadOnly(string? path = null, UniverseContext? context = null)
    {
        var before = Snapshot();
        var result = new PackageSemanticValidator().Validate(path ?? PackageRoot, context ?? Context);
        AssertSnapshot(before);
        if (!result.IsValid)
        {
            Assert.Null(result.Package);
            Assert.True(result.Issues.ShouldStop);
            Assert.False(result.Issues.CanContinue);
            Assert.All(result.Issues.Issues, issue =>
            {
                Assert.Equal(NapIssueSeverity.Error, issue.Severity);
                Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
                Assert.True(Path.IsPathFullyQualified(issue.SubjectPath!));
            });
        }
        else
        {
            Assert.NotNull(result.Package);
            Assert.True(result.Issues.IsClean);
            Assert.True(result.Issues.CanContinue);
        }
        return result;
    }

    internal void AssertReadOnly(Action operation)
    {
        var before = Snapshot();
        operation();
        AssertSnapshot(before);
    }

    // Reparse entries are recorded but never followed, including during test assertions.
    private Dictionary<string, byte[]?> Snapshot()
    {
        var snapshot = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        Visit(new DirectoryInfo(Root));
        return snapshot;
        void Visit(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                {
                    snapshot.Add(entry.FullName, null);
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        Visit((DirectoryInfo)entry);
                }
                else snapshot.Add(entry.FullName, File.ReadAllBytes(entry.FullName));
            }
        }
    }

    private void AssertSnapshot(Dictionary<string, byte[]?> before)
    {
        var after = Snapshot();
        Assert.Equal(before.Keys.OrderBy(path => path, StringComparer.Ordinal), after.Keys.OrderBy(path => path, StringComparer.Ordinal));
        foreach (var (path, bytes) in before)
            Assert.Equal(bytes, after[path]);
    }

    internal static byte[] Png(bool unsupported = false)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 3);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 2); // Deliberately not 4:5.
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk("IHDR", ihdr);
        if (unsupported) Chunk("acTL", [0, 0, 0, 1, 0, 0, 0, 0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(new byte[(3 * 4 + 1) * 2]);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return output.ToArray();

        void Chunk(string type, byte[] payload)
        {
            var number = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)payload.Length);
            output.Write(number);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes); output.Write(payload);
            var crc = uint.MaxValue;
            foreach (var value in typeBytes.Concat(payload))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320;
            }
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
