using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class RepositorySnapshotBackupTests
{
    [Fact]
    public void ExactVisibleTreeIncludesUnicodeCasingIgnoredAndEmptyDirectoriesButExcludesOnlyRootGit()
    {
        using var f = new BackupRepositoryFixture(); var root = f.Context.Storage.ProductionRoot;
        foreach (var d in new[] { ".git", ".github/workflows", "empty", "docs/未来", "nested/.git" }) Directory.CreateDirectory(Path.Combine(root, d));
        foreach (var (name, bytes) in new[] { (".git/secret", "git metadata"), (".gitignore", "untracked.txt"), ("untracked.txt", "untracked"),
            ("README.md", "dirty"), (".github/workflows/ci.yml", "ci"), ("docs/未来/Árbol.txt", "Unicode"), ("Case.txt", "Case"), ("nested/.git/visible", "nested content") })
            File.WriteAllText(Path.Combine(root, name), bytes);
        var before = ArchiveTestFixture.Snapshot(root); var e = new RepositorySnapshotService().Create(f.Context);
        ArchiveTestFixture.AssertSnapshot(before, root); Assert.Contains("empty", e.Manifest.Directories);
        Assert.Contains(e.Manifest.Files, file => file.RelativePath == "docs/未来/Árbol.txt");
        Assert.DoesNotContain(e.Manifest.Files, file => file.RelativePath.StartsWith(".git/", StringComparison.Ordinal));
        Assert.Contains(e.Manifest.Files, file => file.RelativePath == "nested/.git/visible");
        foreach (var file in e.Manifest.Files)
        {
            var dest = Path.Combine(Artifact(f.Context, e), file.RelativePath);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root, file.RelativePath)), File.ReadAllBytes(dest));
            Assert.Equal(new Sha256Hasher().Compute(dest), file.Sha256); Assert.Equal(new FileInfo(dest).Length, file.Size);
        }
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
    }
    [Theory] [InlineData("source_captured")] [InlineData("copied")]
    public void ConcurrentSourceChangeCannotPublishMixedSnapshot(string stage)
    {
        using var f = new BackupRepositoryFixture(); var s = new RepositorySnapshotService();
        Set(s, "Observer", (Action<string>)(actual => { if (actual == stage) File.WriteAllText(Path.Combine(f.Context.Storage.ProductionRoot, "new.txt"), "changed"); }));
        Stop(() => s.Create(f.Context), NapIssueCodes.RepositorySnapshotChanged); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.RepositorySnapshot));
    }
    [Fact]
    public void CollisionPreservesOriginalSnapshot()
    {
        using var f = new BackupRepositoryFixture(); var s = new RepositorySnapshotService(); var id = BackupId.New(); Set(s, "NewId", (Func<BackupId>)(() => id));
        var e = s.Create(f.Context); var before = ArchiveTestFixture.Snapshot(DirectoryPath(f.Context, e));
        Stop(() => s.Create(f.Context), NapIssueCodes.BackupCollision); ArchiveTestFixture.AssertSnapshot(before, DirectoryPath(f.Context, e));
    }
    [Theory] [InlineData("Production")] [InlineData("Archive")]
    public void ExistingProductionOrArchiveWriterStopsSnapshot(string scope)
    {
        using var f = new BackupRepositoryFixture(); using var lease = scope == "Archive" ? (IDisposable)ArchiveLock(f.Context) : Lock(f.Context, scope);
        Stop(() => new RepositorySnapshotService().Create(f.Context), NapIssueCodes.BackupBusy);
        Assert.Empty(RecognizedDirectories(f.Context, BackupKind.RepositorySnapshot));
    }
    [Fact]
    public void SameLayoutAcrossUniversesNeverReadsOrWritesOtherRoot()
    {
        using var a = new BackupRepositoryFixture("alpha"); using var b = new BackupRepositoryFixture("beta");
        var before = ArchiveTestFixture.Snapshot(b.Root); var e = new RepositorySnapshotService().Create(a.Context);
        ArchiveTestFixture.AssertSnapshot(before, b.Root); Assert.Equal(a.Context.Id, e.UniverseId);
        var bb = new RepositorySnapshotService().Create(b.Context); Assert.Equal(e.Sha256, bb.Sha256); Assert.NotEqual(e.BackupId, bb.BackupId);
    }
    [Theory] [InlineData("artifact")] [InlineData("unknown")] [InlineData("empty_directory")] [InlineData("traversal")]
    public void SnapshotVerificationRejectsAnyChangedTreeOrPath(string mode)
    {
        using var f = new BackupRepositoryFixture(); Directory.CreateDirectory(Path.Combine(f.Context.Storage.ProductionRoot, "empty"));
        var e = new RepositorySnapshotService().Create(f.Context);
        switch (mode)
        {
            case "artifact": File.WriteAllText(Path.Combine(Artifact(f.Context, e), "README.md"), "changed"); break;
            case "unknown": File.WriteAllText(Path.Combine(Artifact(f.Context, e), "foreign"), "foreign"); break;
            case "empty_directory": Directory.Delete(Path.Combine(Artifact(f.Context, e), "empty")); break;
            case "traversal": Mutate(f.Context, e, j => j["files"]![0]!["relative_path"] = "../outside"); break;
        }
        Stop(() => new BackupHistoryReader().Read(f.Context));
    }
}
