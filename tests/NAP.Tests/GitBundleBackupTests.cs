using System.Reflection;
using NAP.Core;
using Xunit;
using static NAP.Tests.BackupTestSupport;

namespace NAP.Tests;

public sealed class GitBundleBackupTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void VerifiedBundlePreservesCommitsRefsAndAllRepositoryBytesIncludingDirtyIndexAndConfiguration(bool dirty)
    {
        using var f = new BackupRepositoryFixture(git: true); var root = f.Context.Storage.ProductionRoot;
        if (dirty)
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "staged bytes"); Git(root, "add", "README.md");
            File.WriteAllText(Path.Combine(root, "README.md"), "dirty working bytes"); File.WriteAllText(Path.Combine(root, "untracked.txt"), "untracked bytes");
        }
        Git(root, "config", "remote.origin.url", "https://example.invalid/no-network");
        var before = ArchiveTestFixture.Snapshot(root); var head = Git(root, "rev-parse", "HEAD"); var refs = Git(root, "show-ref");
        var e = new GitBundleService().Create(f.Context); ArchiveTestFixture.AssertSnapshot(before, root);
        Assert.Equal(head, e.Manifest.Head); Assert.Equal(dirty, e.Manifest.Dirty); Assert.Equal(head, Git(root, "rev-parse", "HEAD")); Assert.Equal(refs, Git(root, "show-ref"));
        Assert.Equal(new Sha256Hasher().Compute(Artifact(f.Context, e)), e.Sha256); Assert.Equal(new FileInfo(Artifact(f.Context, e)).Length, e.Size);
        Git(root, "bundle", "verify", Artifact(f.Context, e));
        var heads = Git(root, "bundle", "list-heads", Artifact(f.Context, e)); Assert.Contains("refs/heads/history_branch", heads); Assert.Contains("refs/heads/main", heads);
        // Test-only local recovery into a new bare repo proves the bundle is self-contained and contains committed bytes only.
        var recovered = Path.Combine(f.Root, "recovered.git"); Git(root, "clone", "--bare", Artifact(f.Context, e), recovered);
        Assert.Equal("2", Git(recovered, "rev-list", "--count", "HEAD")); Assert.Equal("second", Git(recovered, "show", "HEAD:README.md"));
        Assert.DoesNotContain("untracked.txt", Git(recovered, "ls-tree", "-r", "--name-only", "HEAD"));
        var snapshot = new RepositorySnapshotService().Create(f.Context);
        if (dirty)
        {
            Assert.Equal("dirty working bytes", File.ReadAllText(Path.Combine(Artifact(f.Context, snapshot), "README.md")));
            Assert.Equal("untracked bytes", File.ReadAllText(Path.Combine(Artifact(f.Context, snapshot), "untracked.txt")));
        }
        ArchiveTestFixture.AssertSnapshot(before, root);
    }
    [Fact]
    public void ProductionSubdirectoryNeverBacksUpParentRepository()
    {
        using var f = new BackupRepositoryFixture(git: true); var sub = Path.Combine(f.Context.Storage.ProductionRoot, "sub"); Directory.CreateDirectory(sub);
        var c = new UniverseContext(f.Context.Profile, new(f.Context.Id, f.Context.Storage.WorkspaceRoot, sub, f.Context.Storage.ArchiveRoot));
        Stop(() => new GitBundleService().Create(c), NapIssueCodes.GitRepositoryInvalid); Assert.Empty(RecognizedDirectories(c, BackupKind.GitBundle));
    }
    [Fact]
    public void NonRepositoryIsRejected()
    { using var f = new BackupRepositoryFixture(); Stop(() => new GitBundleService().Create(f.Context), NapIssueCodes.GitRepositoryInvalid); }
    [Fact]
    public void GitUnavailableIsExplicitAndDoesNotAffectDatabaseBackups()
    {
        using var f = new BackupRepositoryFixture(git: true); var s = new GitBundleService(); Set(Get(s, "Git"), "Executable", Path.Combine(f.Root, "missing-git"));
        Stop(() => s.Create(f.Context), NapIssueCodes.GitUnavailable); new AssetCatalog(f.Context).Initialize();
        Assert.Equal(BackupKind.Database, new DatabaseBackupService().Create(f.Context).Kind);
    }
    [Fact]
    public void RealProcessTimeoutIsBoundedAndRepositoryIsUnchanged()
    {
        using var f = new BackupRepositoryFixture(git: true); var before = ArchiveTestFixture.Snapshot(f.Context.Storage.ProductionRoot);
        var s = new GitBundleService(); Set(Get(s, "Git"), "Timeout", TimeSpan.Zero);
        Stop(() => s.Create(f.Context), NapIssueCodes.GitTimeout); ArchiveTestFixture.AssertSnapshot(before, f.Context.Storage.ProductionRoot);
    }
    [Fact]
    public void OutputReaderEnforcesHardLimit()
    {
        var type = typeof(AssetCatalog).Assembly.GetType("NAP.Core.GitBackupProcess")!;
        using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('a', 1024 * 1024 + 1)));
        using var reader = new StreamReader(bytes);
        var task = (Task<string>)type.GetMethod("ReadBounded", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [reader, CancellationToken.None])!;
        Stop(() => task.GetAwaiter().GetResult(), NapIssueCodes.GitOutputLimit);
    }
    [Fact]
    public void GitErrorExitCannotPublishAValidLookingBundle()
    {
        using var f = new BackupRepositoryFixture(); Git(f.Context.Storage.ProductionRoot, "init", "--initial-branch=main");
        Stop(() => new GitBundleService().Create(f.Context), NapIssueCodes.GitBundleFailed); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.GitBundle));
    }
    [Fact]
    public void CollisionNeverOverwritesBundleOrManifest()
    {
        using var f = new BackupRepositoryFixture(git: true); var s = new GitBundleService(); var id = BackupId.New(); Set(s, "NewId", (Func<BackupId>)(() => id));
        var e = s.Create(f.Context); var before = ArchiveTestFixture.Snapshot(DirectoryPath(f.Context, e));
        Stop(() => s.Create(f.Context), NapIssueCodes.BackupCollision); ArchiveTestFixture.AssertSnapshot(before, DirectoryPath(f.Context, e));
    }
    [Fact]
    public void ExternalWorkingTreeChangeAfterBundleVerificationStopsPublication()
    {
        using var f = new BackupRepositoryFixture(git: true); var s = new GitBundleService();
        Set(s, "Observer", (Action<string>)(_ => File.WriteAllText(Path.Combine(f.Context.Storage.ProductionRoot, "README.md"), "external change")));
        Stop(() => s.Create(f.Context), NapIssueCodes.BackupChanged); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.GitBundle));
    }
    [Fact]
    public void ChangedVerifiedBundleCannotBePublishedWithNewlyAdoptedHash()
    {
        using var f = new BackupRepositoryFixture(git: true); var s = new GitBundleService();
        Set(s, "Observer", (Action<string>)(_ => File.AppendAllText(Directory.GetFiles(Path.Combine(f.Context.Storage.ArchiveRoot, "NAP_GIT_BUNDLES"), "history.bundle", SearchOption.AllDirectories).Single(), "changed")));
        Stop(() => s.Create(f.Context), NapIssueCodes.BackupIntegrityFailed); Assert.Empty(RecognizedDirectories(f.Context, BackupKind.GitBundle));
    }
    [Fact]
    public void OfflineHistoryRejectsCorruptPackEvenIfManifestHashAndSizeAreUpdated()
    {
        using var f = new BackupRepositoryFixture(git: true); var e = new GitBundleService().Create(f.Context); var path = Artifact(f.Context, e);
        var bytes = File.ReadAllBytes(path); bytes[^25] ^= 1; File.WriteAllBytes(path, bytes);
        Mutate(f.Context, e, j => j["sha256"] = new Sha256Hasher().Compute(path).Hex);
        Stop(() => new BackupHistoryReader().Read(f.Context), NapIssueCodes.BackupIntegrityFailed);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DetachedHeadAndBothGitObjectFormatsAreVerified(bool sha256)
    {
        using var f = new BackupRepositoryFixture(git: true, sha256: sha256);
        Git(f.Context.Storage.ProductionRoot, "checkout", "--detach", "HEAD~1");
        var e = new GitBundleService().Create(f.Context); Assert.Equal(sha256 ? 64 : 40, e.Manifest.Head!.Length);
        Assert.Single(new BackupHistoryReader().Read(f.Context).Entries);
    }
    [Theory] [InlineData("Production")] [InlineData("Archive")]
    public void GitBundleHonorsWriterCoordination(string scope)
    {
        using var f = new BackupRepositoryFixture(git: true); using var lease = scope == "Archive" ? (IDisposable)ArchiveLock(f.Context) : Lock(f.Context, scope);
        Stop(() => new GitBundleService().Create(f.Context), NapIssueCodes.BackupBusy);
    }
    [Fact]
    public void IdenticalRepositoryLayoutsRemainUniverseScoped()
    {
        using var a = new BackupRepositoryFixture("alpha", git: true); using var b = new BackupRepositoryFixture("beta", git: true);
        var before = ArchiveTestFixture.Snapshot(b.Root); var aa = new GitBundleService().Create(a.Context); ArchiveTestFixture.AssertSnapshot(before, b.Root);
        var bb = new GitBundleService().Create(b.Context); Assert.NotEqual(aa.BackupId, bb.BackupId); Assert.NotEqual(aa.UniverseId, bb.UniverseId);
        Assert.Equal(aa.Manifest.Head, Git(a.Context.Storage.ProductionRoot, "rev-parse", "HEAD")); Assert.Equal(bb.Manifest.Head, Git(b.Context.Storage.ProductionRoot, "rev-parse", "HEAD"));
    }
    [Theory] [InlineData("alternates")] [InlineData("promisor")] [InlineData("include")]
    public void OfflineBoundaryRejectsExternalObjectOrConfigurationSources(string mode)
    {
        using var f = new BackupRepositoryFixture(git: true); var metadata = Path.Combine(f.Context.Storage.ProductionRoot, ".git");
        if (mode == "include") File.AppendAllText(Path.Combine(metadata, "config"), "\n[include]\npath = ../external-config\n");
        else File.WriteAllText(Path.Combine(metadata, mode == "alternates" ? "objects/info/alternates" : "objects/pack/external.promisor"), "");
        Stop(() => new GitBundleService().Create(f.Context), NapIssueCodes.GitRepositoryInvalid);
    }
}
