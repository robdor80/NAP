using System.Text.Json;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class LocalUniverseSettingsTests
{
    [Fact]
    public void MultipleUniversesRoundTripWithExplicitContextsAndNoSecrets()
    {
        using var first = new CatalogTestFixture(universe: "one_world"); using var second = new CatalogTestFixture(universe: "two_world");
        var store = new LocalUniverseSettingsStore(Path.Combine(first.Root, "settings", "settings.json"));
        store.Save([second.Context.Storage, first.Context.Storage]);
        var rows = new LocalUniverseSettingsStore(store.SettingsPath).Load(); Assert.Equal(2, rows.Count);
        Assert.Equal(first.Context.Storage, rows[0]); Assert.Equal(second.Context.Storage, rows[1]);
        Assert.Equal(first.Context, new UniverseContext(first.Context.Profile, rows[0]));
        using var doc = JsonDocument.Parse(File.ReadAllText(store.SettingsPath));
        Assert.Equal(new[] { "schema_version", "universes" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "universe_id", "workspace_root", "production_root", "archive_root" }, doc.RootElement.GetProperty("universes")[0].EnumerateObject().Select(p => p.Name));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.SettingsPath)!, "*.tmp"));
    }
    [Fact] public void MissingSettingsAreEmptyAndCreateNothing()
    { using var f = new CatalogTestFixture(); var path = Path.Combine(f.Root, "absent", "settings.json"); Assert.Empty(new LocalUniverseSettingsStore(path).Load()); Assert.False(Directory.Exists(Path.GetDirectoryName(path))); }
    [Fact] public void DuplicateUniverseRejectedBeforeAnyPublication()
    { using var f = new CatalogTestFixture(); var store = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); Assert.Throws<ArgumentException>(() => store.Save([f.Context.Storage, f.Context.Storage])); Assert.False(File.Exists(store.SettingsPath)); }
    [Fact] public void CrossUniverseOverlapRejected()
    { using var f = new CatalogTestFixture(); var s = f.Context.Storage; var other = new UniverseStorageConfig(new("other"), s.WorkspaceRoot, s.ProductionRoot, s.ArchiveRoot); Assert.Throws<InvalidDataException>(() => LocalUniverseSettingsStore.Validate([s, other])); }
    [Theory] [InlineData("Workspace")] [InlineData("Production")] [InlineData("Archive")]
    public void RelativeRootsUseExistingInvariant(string root)
    { using var f = new CatalogTestFixture(); var s = f.Context.Storage; Assert.Throws<ArgumentException>(() => new UniverseStorageConfig(s.UniverseId, root == "Workspace" ? "relative" : s.WorkspaceRoot, root == "Production" ? "relative" : s.ProductionRoot, root == "Archive" ? "relative" : s.ArchiveRoot)); }
    [Theory] [InlineData("Workspace")] [InlineData("Production")] [InlineData("Archive")]
    public void MissingRootsNeverCreated(string root)
    {
        using var f = new CatalogTestFixture(); var s = f.Context.Storage; var absent = Path.Combine(f.Root, "absent");
        var config = new UniverseStorageConfig(s.UniverseId, root == "Workspace" ? absent : s.WorkspaceRoot, root == "Production" ? absent : s.ProductionRoot, root == "Archive" ? absent : s.ArchiveRoot);
        var error = Record.Exception(() => LocalUniverseSettingsStore.Validate([config]));
        Assert.NotNull(error); Assert.True(error is ProductionStorageException or ArchiveStorageException or InvalidDataException); Assert.False(Directory.Exists(absent));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schema_version\":2,\"universes\":[]}")]
    [InlineData("{\"schema_version\":1,\"universes\":[],\"api_key\":\"secret\"}")]
    [InlineData("{\"schema_version\":1,\"schema_version\":1,\"universes\":[]}")]
    [InlineData("{\"schema_version\":1,\"universes\":[{}]}")]
    public void CorruptUnknownOrSecretFieldsStopWithoutOverwriting(string json)
    {
        using var f = new CatalogTestFixture(); var path = Path.Combine(f.Root, "settings.json"); File.WriteAllText(path, json);
        var store = new LocalUniverseSettingsStore(path); Assert.ThrowsAny<Exception>(() => store.Load()); Assert.ThrowsAny<Exception>(() => store.Save([f.Context.Storage])); Assert.Equal(json, File.ReadAllText(path));
    }
    [Fact] public void UnavailableRememberedRootsCanBeLoadedForExplicitEditing()
    {
        using var f = new CatalogTestFixture(); var path = Path.Combine(f.Root, "settings.json"); var store = new LocalUniverseSettingsStore(path); store.Save([f.Context.Storage]);
        Directory.Delete(f.Context.Storage.ArchiveRoot); Assert.Single(store.Load()); Assert.False(Directory.Exists(f.Context.Storage.ArchiveRoot));
    }
    [Fact] public void SettingsCannotBeWrittenInsideUniverseRoots()
    { using var f = new CatalogTestFixture(); var store = new LocalUniverseSettingsStore(Path.Combine(f.Context.Storage.CacheRoot, "settings.json")); Assert.Throws<InvalidDataException>(() => store.Save([f.Context.Storage])); Assert.False(File.Exists(store.SettingsPath)); }
    [Fact] public void GitCheckoutSettingsRejected()
    { using var f = new CatalogTestFixture(); var root = Directory.CreateDirectory(Path.Combine(f.Root, "checkout")).FullName; File.WriteAllText(Path.Combine(root, ".git"), "gitdir: elsewhere"); Assert.Throws<InvalidDataException>(() => new LocalUniverseSettingsStore(Path.Combine(root, "settings.json")).Save([])); }
    [Fact] public void FailedPublicationPreservesPriorFile()
    {
        using var f = new CatalogTestFixture(); var store = new LocalUniverseSettingsStore(Path.Combine(f.Root, "settings.json")); store.Save([f.Context.Storage]); var prior = File.ReadAllBytes(store.SettingsPath);
        var invalid = new UniverseStorageConfig(f.Context.Id, f.Context.Storage.WorkspaceRoot, f.Context.Storage.WorkspaceRoot, f.Context.Storage.ArchiveRoot);
        Assert.Throws<ProductionStorageException>(() => store.Save([invalid])); Assert.Equal(prior, File.ReadAllBytes(store.SettingsPath));
    }
    [Fact] public void SettingsPathMustBeAbsolute() => Assert.Throws<ArgumentException>(() => new LocalUniverseSettingsStore("relative.json"));
    [Fact] public void SettingsParentReparseRejected()
    {
        using var f = new CatalogTestFixture(); var link = Path.Combine(f.Root, "link"); var target = Directory.CreateDirectory(Path.Combine(f.Root, "target")).FullName;
        ArchiveTestFixture.Junction(link, target);
        try { Assert.Throws<ProductionStorageException>(() => new LocalUniverseSettingsStore(Path.Combine(link, "settings.json")).Save([])); Assert.Empty(Directory.GetFiles(target)); }
        finally { Directory.Delete(link); }
    }
}
