using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionRepositoryScannerTests
{
    [Fact]
    public void NullRepositoryIsRejectedAndPublicApiRequiresTheValidatedBoundary()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ProductionRepositoryScanner().Scan(null!));
        Assert.Equal("repository", exception.ParamName);
        var method = Assert.Single(typeof(ProductionRepositoryScanner).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Scan", method.Name);
        Assert.Equal(typeof(ProductionRepositoryScanResult), method.ReturnType);
        Assert.Equal(typeof(ValidatedProductionRepository), Assert.Single(method.GetParameters()).ParameterType);
        Assert.Equal(new[] { "Directory", "File" }, Enum.GetNames<ProductionRepositoryEntryKind>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRootProducesEmptySnapshotWithExactIdentityAndRoot(bool trailingSeparator)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var root = fixture.ProductionRoot + (trailingSeparator ? Path.DirectorySeparatorChar.ToString() : "");
        var repository = Validate(fixture, root);

        var result = ScanReadOnly(fixture, repository);

        Assert.True(result.IsValid);
        Assert.True(result.Issues.IsClean);
        Assert.Empty(result.Issues.Issues);
        Assert.False(result.Issues.ShouldStop);
        Assert.True(result.Issues.CanContinue);
        Assert.Empty(result.Snapshot!.Entries);
        Assert.Same(repository.UniverseId, result.Snapshot.UniverseId);
        Assert.Same(repository.RootPath, result.Snapshot.RootPath);
        Assert.False(Directory.Exists(Path.Combine(root, ".git")));
    }

    [Fact]
    public void RootFileHasExactRelativePathAbsoluteNativePathAndFileKind()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var file = Path.Combine(fixture.ProductionRoot, "README.md");
        File.WriteAllText(file, "arbitrary bytes");

        var entry = Assert.Single(ScanReadOnly(fixture, Validate(fixture)).Snapshot!.Entries);

        Assert.Equal("README.md", entry.RelativePath);
        Assert.Equal(file, entry.FullPath);
        Assert.True(Path.IsPathFullyQualified(entry.FullPath));
        Assert.Equal(ProductionRepositoryEntryKind.File, entry.Kind);
    }

    [Fact]
    public void EmptyDirectoryAndAllDeepDescendantsAreIncludedWithoutIncludingRoot()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var deep = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "a", "b")).FullName;
        Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "empty"));
        File.WriteAllText(Path.Combine(deep, "file.txt"), "content");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "root.txt"), "content");

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(new[] { "a", "a/b", "a/b/file.txt", "empty", "root.txt" }, snapshot.Entries.Select(entry => entry.RelativePath));
        Assert.Equal(new[] { ProductionRepositoryEntryKind.Directory, ProductionRepositoryEntryKind.Directory,
            ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.Directory, ProductionRepositoryEntryKind.File },
            snapshot.Entries.Select(entry => entry.Kind));
        Assert.All(snapshot.Entries, entry =>
        {
            Assert.NotEqual(snapshot.RootPath, entry.FullPath);
            Assert.False(string.IsNullOrEmpty(entry.RelativePath));
            Assert.False(entry.RelativePath.StartsWith('/'));
            Assert.False(entry.RelativePath.EndsWith('/'));
            Assert.DoesNotContain('\\', entry.RelativePath);
            Assert.Equal(Path.Combine(snapshot.RootPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)), entry.FullPath);
            var relative = Path.GetRelativePath(snapshot.RootPath, entry.FullPath);
            Assert.False(Path.IsPathRooted(relative));
            Assert.False(relative.StartsWith("..", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntriesAreOrdinallySortedIndependentlyOfCreationOrder(bool reverseCreation)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        string[] expected = ["A First", "Z_Last", "a legacy", "a legacy/child.txt", "z Next", "Élite"];
        string[] names = ["Élite", "z Next", "a legacy", "A First", "Z_Last"];
        foreach (var name in reverseCreation ? names.Reverse() : names)
            Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, name));
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "a legacy", "child.txt"), "content");

        var repository = Validate(fixture);
        var first = ScanReadOnly(fixture, repository).Snapshot!;
        var second = ScanReadOnly(fixture, repository).Snapshot!;

        Assert.Equal(expected, first.Entries.Select(entry => entry.RelativePath));
        Assert.Equal(expected, second.Entries.Select(entry => entry.RelativePath));
    }

    [Theory]
    [InlineData("Treskal Viejo")]
    [InlineData("Campesinos")]
    [InlineData("Élite")]
    [InlineData("portrait OLD")]
    [InlineData("House Aethros")]
    [InlineData("日本語 🐉")]
    [InlineData("e\u0301")]
    [InlineData(" leading space")]
    public void HistoricalNamesPreserveExactCasingSpacesAndUnicode(string name)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var directory = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, name)).FullName;
        File.WriteAllText(Path.Combine(directory, "Portrait OLD.txt"), "content");

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(new[] { name, name + "/Portrait OLD.txt" }, snapshot.Entries.Select(entry => entry.RelativePath));
        Assert.Equal(directory, snapshot.Entries[0].FullPath);
        Assert.Equal(Path.Combine(directory, "Portrait OLD.txt"), snapshot.Entries[1].FullPath);
    }

    [Fact]
    public void UnixBackslashInARealNameIsPreservedRatherThanReplaced()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ProductionRepositoryTestFixture();
        var directory = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "legacy\\folder")).FullName;
        File.WriteAllText(Path.Combine(directory, "old\\file.txt"), "content");

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(new[] { "legacy\\folder", "legacy\\folder/old\\file.txt" }, snapshot.Entries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public void CorruptJsonManifestsAndPngArePhotographedWithoutInterpretation()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        foreach (var name in new[] { "manifest_v1.json", "manifest_v2.json", "profile.json", "portrait OLD_manifest.json" })
            File.WriteAllText(Path.Combine(fixture.ProductionRoot, name), "{ not valid JSON or Naming v1");
        File.WriteAllBytes(Path.Combine(fixture.ProductionRoot, "invalid.png"), [1, 2, 3]);

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(5, snapshot.Entries.Count);
        Assert.All(snapshot.Entries, entry => Assert.Equal(ProductionRepositoryEntryKind.File, entry.Kind));
    }

    [Fact]
    public void ExclusivelyLockedContentCanStillBeIncludedUsingOnlyMetadata()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var path = Path.Combine(fixture.ProductionRoot, "broken_manifest.json");
        File.WriteAllText(path, "do not read");
        var repository = Validate(fixture);
        var before = fixture.Snapshot();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = new ProductionRepositoryScanner().Scan(repository);
            Assert.True(result.IsValid);
            Assert.Equal(path, Assert.Single(result.Snapshot!.Entries).FullPath);
        }
        fixture.AssertSnapshot(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GitAndInfrastructureAreOrdinaryEntriesWithoutIgnores(bool gitDirectory)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        if (gitDirectory)
        {
            var git = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, ".git")).FullName;
            File.WriteAllText(Path.Combine(git, "config"), "broken git config");
        }
        else File.WriteAllText(Path.Combine(fixture.ProductionRoot, ".git"), "not a Git repository");
        var workflows = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, ".github", "workflows")).FullName;
        File.WriteAllText(Path.Combine(workflows, "test.yml"), "invalid YAML");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, ".gitignore"), "README.md\n.github\n");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "README.md"), "still present");

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        string[] expected = gitDirectory
            ? [".git", ".git/config", ".github", ".github/workflows", ".github/workflows/test.yml", ".gitignore", "README.md"]
            : [".git", ".github", ".github/workflows", ".github/workflows/test.yml", ".gitignore", "README.md"];
        Assert.Equal(expected, snapshot.Entries.Select(entry => entry.RelativePath));
        Assert.Equal(gitDirectory ? ProductionRepositoryEntryKind.Directory : ProductionRepositoryEntryKind.File, snapshot.Entries[0].Kind);
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("outside")]
    [InlineData("circular")]
    public void DirectoryLinksStopWithoutTraversalAndWithoutChangingAnything(string mode)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var target = mode == "circular" ? fixture.ProductionRoot
            : Directory.CreateDirectory(Path.Combine(mode == "internal" ? fixture.ProductionRoot : fixture.Root, "target")).FullName;
        if (mode != "circular") File.WriteAllText(Path.Combine(target, "must-not-read.txt"), "target sentinel");
        var link = Path.Combine(fixture.ProductionRoot, "link");
        ProductionRepositoryTestFixture.CreateDirectoryLink(link, target);
        try
        {
            var result = ScanReadOnly(fixture, Validate(fixture));
            var issue = Failure(result, NapIssueCodes.RepositoryEntryReparse, link);
            Assert.Equal("link", issue.Detail);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void HiddenAndSystemEntriesAreIncludedWithoutAttributeBasedIgnores()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var directory = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, ".hidden")).FullName;
        var path = Path.Combine(directory, "system.txt");
        File.WriteAllText(path, "sentinel");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
        }

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(new[] { ".hidden", ".hidden/system.txt" }, snapshot.Entries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public void MultipleReparseIssuesAreOrdinallySortedAcrossNormalBranches()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var nested = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "A Branch")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "outside-target")).FullName;
        var first = Path.Combine(nested, "Élite link");
        var second = Path.Combine(fixture.ProductionRoot, "z link");
        // A link in the target must remain invisible: scanning it would create an extra issue.
        var hidden = Path.Combine(target, "must-not-discover");
        ProductionRepositoryTestFixture.CreateDirectoryLink(hidden, fixture.ProductionRoot);
        ProductionRepositoryTestFixture.CreateDirectoryLink(second, target);
        ProductionRepositoryTestFixture.CreateDirectoryLink(first, target);
        try
        {
            var result = ScanReadOnly(fixture, Validate(fixture));
            Assert.False(result.IsValid);
            Assert.Null(result.Snapshot);
            Assert.Equal(new[] { "A Branch/Élite link", "z link" }, result.Issues.Issues.Select(issue => issue.Detail));
            Assert.Equal(new[] { first, second }, result.Issues.Issues.Select(issue => issue.SubjectPath));
            Assert.All(result.Issues.Issues, issue => AssertFailureIssue(issue, NapIssueCodes.RepositoryEntryReparse));
            Assert.Equal(result.Issues.Issues.Select(issue => issue.SubjectPath),
                ScanReadOnly(fixture, Validate(fixture)).Issues.Issues.Select(issue => issue.SubjectPath));
        }
        finally
        {
            Directory.Delete(first);
            Directory.Delete(second);
            Directory.Delete(hidden);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnixFileSymlinksIncludingDanglingLinksStopWithoutReadingTarget(bool dangling)
    {
        if (OperatingSystem.IsWindows()) return; // Windows file symlinks require privileges; directory junctions are tested above.
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var target = Path.Combine(fixture.Root, "external.txt");
        if (!dangling) File.WriteAllText(target, "outside sentinel");
        var link = Path.Combine(fixture.ProductionRoot, "file-link");
        File.CreateSymbolicLink(link, target);
        try { Failure(ScanReadOnly(fixture, Validate(fixture)), NapIssueCodes.RepositoryEntryReparse, link); }
        finally { File.Delete(link); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("file")]
    [InlineData("reparse")]
    public void ChangedRootAfterValidationReusesRootCodesAndNeverCreatesSnapshot(string mode)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var repository = Validate(fixture);
        Directory.Delete(fixture.ProductionRoot);
        var code = NapIssueCodes.ProductionRootMissing;
        if (mode == "file")
        {
            File.WriteAllText(fixture.ProductionRoot, "replacement file");
            code = NapIssueCodes.ProductionRootInvalid;
        }
        if (mode == "reparse")
        {
            var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "target")).FullName;
            ProductionRepositoryTestFixture.CreateDirectoryLink(fixture.ProductionRoot, target);
            code = NapIssueCodes.ProductionRootReparse;
        }
        try { Failure(ScanReadOnly(fixture, repository), code, repository.RootPath); }
        finally { if (mode == "reparse") Directory.Delete(fixture.ProductionRoot); }
        if (mode == "missing") Assert.False(Directory.Exists(fixture.ProductionRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsListingAccessErrorsPropagateForRootAndDescendants(bool descendant)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var restricted = new DirectoryInfo(descendant
            ? Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "restricted")).FullName : fixture.ProductionRoot);
        File.WriteAllText(Path.Combine(restricted.FullName, "private.txt"), "sentinel");
        var repository = Validate(fixture);
        var before = fixture.Snapshot();
        using var identity = WindowsIdentity.GetCurrent();
        var security = restricted.GetAccessControl();
        var deny = new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        security.AddAccessRule(deny);
        restricted.SetAccessControl(security);
        try { Assert.Throws<UnauthorizedAccessException>(() => new ProductionRepositoryScanner().Scan(repository)); }
        finally
        {
            security.RemoveAccessRuleSpecific(deny);
            restricted.SetAccessControl(security);
        }
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void ManyLevelsAreVisitedUsingAnExplicitWorklist()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var directory = fixture.ProductionRoot;
        for (var level = 0; level < 50; level++) directory = Directory.CreateDirectory(Path.Combine(directory, "d")).FullName;
        File.WriteAllText(Path.Combine(directory, "end.txt"), "sentinel");

        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;

        Assert.Equal(51, snapshot.Entries.Count);
        Assert.Equal(50, snapshot.Entries.Count(entry => entry.Kind == ProductionRepositoryEntryKind.Directory));
        Assert.Equal(string.Join('/', Enumerable.Repeat("d", 50)) + "/end.txt", snapshot.Entries[^1].RelativePath);
    }

    [Fact]
    public void IndependentUniversesRetainTheirExactIdentityAndRoot()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other-production")).FullName;
        File.WriteAllText(Path.Combine(other, "other.txt"), "sentinel");
        var first = Validate(fixture);
        var second = fixture.ValidateReadOnly(fixture.Context(other, "other_universe")).Repository!;

        var a = ScanReadOnly(fixture, first).Snapshot!;
        var b = ScanReadOnly(fixture, second).Snapshot!;

        Assert.Same(first.UniverseId, a.UniverseId);
        Assert.Same(second.UniverseId, b.UniverseId);
        Assert.Same(first.RootPath, a.RootPath);
        Assert.Same(second.RootPath, b.RootPath);
        Assert.Empty(a.Entries);
        Assert.Equal("other.txt", Assert.Single(b.Entries).RelativePath);
    }

    [Fact]
    public void ModelsAreSealedImmutableAndHaveOnlyInternalConstructors()
    {
        foreach (var type in new[] { typeof(ProductionRepositoryEntry), typeof(ProductionRepositorySnapshot) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors());
            Assert.True(Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).IsAssembly);
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
        Assert.All(typeof(ProductionRepositoryScanResult).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void SnapshotDefensivelyCopiesSortsAndExposesReadOnlyEntriesWithoutFurtherIO()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "Z.txt"), "sentinel");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "a.txt"), "sentinel");
        var repository = Validate(fixture);
        var scanned = ScanReadOnly(fixture, repository).Snapshot!;
        var source = scanned.Entries.Reverse().ToList();
        Directory.Delete(fixture.ProductionRoot, recursive: true);
        var constructor = Assert.Single(typeof(ProductionRepositorySnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        var copy = Assert.IsType<ProductionRepositorySnapshot>(constructor.Invoke([repository, source]));
        source.Clear();

        Assert.Equal(new[] { "Z.txt", "a.txt" }, copy.Entries.Select(entry => entry.RelativePath));
        Assert.Same(repository.UniverseId, copy.UniverseId);
        Assert.Same(repository.RootPath, copy.RootPath);
        var list = Assert.IsAssignableFrom<IList<ProductionRepositoryEntry>>(copy.Entries);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = scanned.Entries[1]);
        Assert.Equal(2, scanned.Entries.Count);
        Assert.False(Directory.Exists(copy.RootPath));
    }

    [Fact]
    public void EntryConstructorStoresValuesWithoutFilesystemIO()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var before = fixture.Snapshot();
        var constructor = Assert.Single(typeof(ProductionRepositoryEntry).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        var fullPath = Path.Combine(fixture.ProductionRoot, "Élite");

        var entry = Assert.IsType<ProductionRepositoryEntry>(constructor.Invoke(["Élite", fullPath, ProductionRepositoryEntryKind.File]));

        Assert.Equal("Élite", entry.RelativePath);
        Assert.Equal(fullPath, entry.FullPath);
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void ScanResultRejectsNullReportAndCleanReportWithoutSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => new ProductionRepositoryScanResult(null!, null));
        Assert.Throws<ArgumentException>(() => new ProductionRepositoryScanResult(new NapIssueReport([]), null));
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Stop)]
    [InlineData(NapIssueSeverity.Warning, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Warning, NapIssueDisposition.Stop)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Stop)]
    public void AnyIssuePreventsSnapshotAndConsistentStatesAreAccepted(NapIssueSeverity severity, NapIssueDisposition disposition)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var snapshot = ScanReadOnly(fixture, Validate(fixture)).Snapshot!;
        var issues = new NapIssueReport([new NapIssue("test_issue", severity, disposition, "Issue.")]);

        Assert.Throws<ArgumentException>(() => new ProductionRepositoryScanResult(issues, snapshot));
        var invalid = new ProductionRepositoryScanResult(issues, null);
        Assert.False(invalid.IsValid);
        Assert.Null(invalid.Snapshot);
        Assert.Same(issues, invalid.Issues);
        var clean = new NapIssueReport([]);
        var valid = new ProductionRepositoryScanResult(clean, snapshot);
        Assert.True(valid.IsValid);
        Assert.Same(snapshot, valid.Snapshot);
        Assert.Same(clean, valid.Issues);
    }

    private static ValidatedProductionRepository Validate(ProductionRepositoryTestFixture fixture, string? root = null) =>
        fixture.ValidateReadOnly(fixture.Context(root)).Repository!;

    private static ProductionRepositoryScanResult ScanReadOnly(ProductionRepositoryTestFixture fixture, ValidatedProductionRepository repository)
    {
        var before = fixture.Snapshot();
        try { return new ProductionRepositoryScanner().Scan(repository); }
        finally { fixture.AssertSnapshot(before); }
    }

    private static NapIssue Failure(ProductionRepositoryScanResult result, string code, string fullPath)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Snapshot);
        Assert.False(result.Issues.IsClean);
        Assert.True(result.Issues.ShouldStop);
        Assert.False(result.Issues.CanContinue);
        var issue = Assert.Single(result.Issues.Issues);
        Assert.Equal(fullPath, issue.SubjectPath);
        AssertFailureIssue(issue, code);
        return issue;
    }

    private static void AssertFailureIssue(NapIssue issue, string code)
    {
        Assert.Equal(code, issue.Code);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.True(Path.IsPathFullyQualified(issue.SubjectPath!));
    }
}
