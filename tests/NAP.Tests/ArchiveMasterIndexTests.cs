using System.Collections;
using System.Text;
using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ArchiveMasterIndexTests
{
    [Fact]
    public void MissingIndexIsEmptyScopedAndReadOnly()
    {
        using var fixture = new ArchiveTestFixture();
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        var index = fixture.Store.Load();
        Assert.Equal(fixture.Context.Id, index.UniverseId);
        Assert.Empty(index.Entries);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void SnapshotsAreDefensiveOrderedAndImmutable()
    {
        var first = Entry("portrait_example_001");
        var last = Entry("portrait_example_002");
        var list = new List<ArchiveMasterIndexEntry> { last, first };
        var index = new ArchiveMasterIndex(new UniverseId("nimroel"), list);
        list.Clear();
        Assert.Equal(new[] { first, last }, index.Entries);
        Assert.Throws<NotSupportedException>(() => ((IList)index.Entries)[0] = last);
        Assert.All(typeof(ArchiveMasterIndexEntry).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(ArchiveMasterIndex).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Throws<ArgumentException>(() => new ArchiveMasterIndex(index.UniverseId, [first, first]));
        Assert.Throws<ArgumentException>(() => new ArchiveMasterIndex(index.UniverseId, [null!]));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterIndex(null!, []));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterIndex(index.UniverseId, null!));
        Assert.Throws<ArgumentNullException>(() => new ArchiveMasterIndexEntry(first.AssetId, first.AssetType, null!, 1, "originals/asset", true));
        Assert.Throws<ArgumentException>(() => new ArchiveMasterIndexEntry(first.AssetId, first.AssetType, first.MasterSha256, 1, "originals/asset", false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveMasterIndexEntry(first.AssetId, first.AssetType, first.MasterSha256, 0, "originals/asset", true));
    }

    public static IEnumerable<object[]> InvalidIndexes()
    {
        var valid = ArchiveTestFixture.ValidIndex(entries: "[" + ArchiveTestFixture.ValidEntry() + "]");
        foreach (var json in new[] { "{", "[]", "null", "{}", valid + "{}",
                     valid.Replace("\"schema_version\":1,", ""), valid.Replace("\"universe_id\":\"nimroel\",", ""), valid.Replace("\"entries\":[", "\"extra\":0,\"entries\":["),
                     valid.Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1"),
                     valid.Replace("\"universe_id\":\"nimroel\"", "\"universe_id\":\"nimroel\",\"universe_id\":\"nimroel\""),
                     valid.Replace("\"entries\":[", "\"entries\":[],\"entries\":["),
                     valid.Replace("\"nimroel\"", "\"other_universe\""), valid.Replace("\"universe_id\":\"nimroel\"", "\"universe_id\":null"),
                     ArchiveTestFixture.ValidIndex(entries: "{}"), ArchiveTestFixture.ValidIndex(entries: "null"),
                     ArchiveTestFixture.ValidIndex(entries: "[null]"), ArchiveTestFixture.ValidIndex(entries: "[[]]"),
                     ArchiveTestFixture.ValidIndex(entries: "[" + ArchiveTestFixture.ValidEntry() + "," + ArchiveTestFixture.ValidEntry() + "]") }) yield return [json];
        foreach (var token in new[] { "0", "2", "-1", "1.0", "1e0", "\"1\"", "true", "null" })
            yield return [valid.Replace("\"schema_version\":1", "\"schema_version\":" + token)];
        using var doc = JsonDocument.Parse(ArchiveTestFixture.ValidEntry());
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var props = doc.RootElement.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText()).ToArray();
            yield return [ArchiveTestFixture.ValidIndex(entries: "[{" + string.Join(",", props.Where(p => !p.StartsWith(JsonSerializer.Serialize(property.Name) + ":", StringComparison.Ordinal))) + "}]")];
            yield return [ArchiveTestFixture.ValidIndex(entries: "[{" + string.Join(",", props) + "," + JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText() + "}]")];
            yield return [ArchiveTestFixture.ValidIndex(entries: "[{" + string.Join(",", props.Select(p => p.StartsWith(JsonSerializer.Serialize(property.Name) + ":", StringComparison.Ordinal) ? JsonSerializer.Serialize(property.Name) + ":null" : p)) + "}]")];
        }
        yield return [valid.Replace("\"asset_type\":\"portrait\"", "\"asset_type\":\"portrait\",\"extra\":true")];
        foreach (var id in new[] { "Bad", "portrait_example_000", "portrait_example_01", "portrait__example_001", "", "../asset_001" })
            yield return [valid.Replace(JsonSerializer.Serialize(ArchiveTestFixture.NimroelAsset), JsonSerializer.Serialize(id))];
        foreach (var type in new[] { "Portrait", "", "type__bad", "../type" })
            yield return [valid.Replace("\"asset_type\":\"portrait\"", "\"asset_type\":" + JsonSerializer.Serialize(type))];
        foreach (var sha in new[] { "", new string('A', 64), new string('g', 64), new string('a', 63), new string('a', 65) })
            yield return [valid.Replace(new string('a', 64), sha)];
        foreach (var size in new[] { "0", "-1", "1.1", "80.0", "8e1", "\"80\"", "true", "9223372036854775808" })
            yield return [valid.Replace("\"master_size_bytes\":80", "\"master_size_bytes\":" + size)];
        foreach (var path in new[] { "../escape", ".", "originals//asset", "originals\\asset", "C:/escape", "/escape", "//host/share", "originals/con", "originals/a:b", "_nap/asset", "originals/\u0001" })
            yield return [valid.Replace("\"relative_directory\":\"originals/" + ArchiveTestFixture.NimroelAsset + "\"", "\"relative_directory\":" + JsonSerializer.Serialize(path))];
        foreach (var verified in new[] { "false", "1", "\"true\"", "{}" }) yield return [valid.Replace("\"verified\":true", "\"verified\":" + verified)];
    }

    [Theory]
    [MemberData(nameof(InvalidIndexes))]
    public void StrictParserStopsAndNeverRepairsInvalidIndex(string json)
    {
        using var fixture = new ArchiveTestFixture();
        fixture.WriteIndex(json);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        ArchiveTestFixture.AssertStop(Assert.Throws<ArchiveStorageException>(() => fixture.Store.Load()), NapIssueCodes.ArchiveIndexInvalid);
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void ValidSchemaLoadsAndSortsWithoutNormalizingStoredBytes()
    {
        using var fixture = new ArchiveTestFixture();
        var first = ArchiveTestFixture.ValidEntry("portrait_example_001");
        var last = ArchiveTestFixture.ValidEntry("portrait_example_002");
        var json = ArchiveTestFixture.ValidIndex(entries: "[" + last + "," + first + "]");
        fixture.WriteIndex(json);
        var before = ArchiveTestFixture.Snapshot(fixture.Root);
        var index = fixture.Store.Load();
        Assert.Equal(new[] { "portrait_example_001", "portrait_example_002" }, index.Entries.Select(e => e.AssetId));
        Assert.All(index.Entries, e => Assert.True(e.Verified));
        ArchiveTestFixture.AssertSnapshot(before, fixture.Root);
    }

    [Fact]
    public void PublicationHasExactPropertiesUtf8OrderAndFinalLf()
    {
        using var fixture = new ArchiveTestFixture();
        var plan = fixture.Plan();
        fixture.Execute(plan);
        var bytes = File.ReadAllBytes(fixture.Store.IndexPath);
        Assert.Equal((byte)'\n', bytes[^1]);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFEFF", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("job_", json);
        using var doc = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "schema_version", "universe_id", "entries" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        var entry = Assert.Single(doc.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(new[] { "asset_id", "asset_type", "master_sha256", "master_size_bytes", "relative_directory", "verified" }, entry.EnumerateObject().Select(p => p.Name));
        Assert.Equal(plan.MasterDigest.Hex, entry.GetProperty("master_sha256").GetString());
        Assert.Equal(plan.MasterSizeBytes, entry.GetProperty("master_size_bytes").GetInt64());
        Assert.True(entry.GetProperty("verified").GetBoolean());
        Assert.Equal(new[] { "archive.lock", "master_index.json" }, Directory.GetFiles(Path.GetDirectoryName(fixture.Store.IndexPath)!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OrphanIndexTempIsNeverLoadedOrDeleted()
    {
        using var fixture = new ArchiveTestFixture();
        var orphan = Path.Combine(fixture.Context.Storage.ArchiveRoot, "_nap", $"master_index.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        File.WriteAllText(orphan, "partial JSON");
        Assert.Empty(fixture.Store.Load().Entries);
        fixture.Execute();
        Assert.Equal("partial JSON", File.ReadAllText(orphan));
        Assert.Single(fixture.Store.Load().Entries);
    }

    [Fact]
    public void FailedAtomicRenamePreservesOldIndexAndCompleteTemp()
    {
        using var fixture = new ArchiveTestFixture();
        fixture.WriteIndex(ArchiveTestFixture.ValidIndex());
        var original = File.ReadAllBytes(fixture.Store.IndexPath);
        var lockType = ArchiveTestFixture.CoreType("ArchiveLock");
        using var lease = (IDisposable)ArchiveTestFixture.Invoke(null, lockType, "Acquire", fixture.Context)!;
        ArchiveTestFixture.Invoke(lease, lockType, "EnableWrites");
        // A deterministic sharing violation at rename, after the complete temporary JSON was flushed and closed.
        using var indexReader = new FileStream(fixture.Store.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => ArchiveTestFixture.Invoke(fixture.Store, typeof(ArchiveMasterIndexStore), "Publish",
            new ArchiveMasterIndex(fixture.Context.Id, [Entry(ArchiveTestFixture.NimroelAsset)]), lease));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal(original, File.ReadAllBytes(fixture.Store.IndexPath));
        var temp = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(fixture.Store.IndexPath)!, "master_index.*.tmp"));
        Assert.Matches(@"master_index\.[0-9a-f]{32}\.tmp$", temp);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(temp));
        Assert.Single(doc.RootElement.GetProperty("entries").EnumerateArray());
    }

    private static ArchiveMasterIndexEntry Entry(string id) => new(id, "portrait", new Sha256Digest(new string('a', 64)), 80, "originals/" + id, true);
}
