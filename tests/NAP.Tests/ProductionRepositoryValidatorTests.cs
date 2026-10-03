using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionRepositoryValidatorTests
{
    [Fact]
    public void NullContextIsAProgrammingError()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ProductionRepositoryValidator().Validate(null!));
        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void NormalDirectoryProducesCleanResultWithoutRequiringGitOrOtherStorageRoots()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var context = fixture.Context();

        var result = fixture.ValidateReadOnly(context);

        Assert.True(result.IsValid);
        var repository = Assert.IsType<ValidatedProductionRepository>(result.Repository);
        Assert.Same(context.Id, repository.UniverseId);
        Assert.NotSame(context.Storage.UniverseId, repository.UniverseId);
        Assert.Equal(context.Storage.ProductionRoot, repository.RootPath);
        Assert.True(result.Issues.IsClean);
        Assert.Empty(result.Issues.Issues);
        Assert.False(result.Issues.HasErrors);
        Assert.False(result.Issues.ShouldStop);
        Assert.True(result.Issues.CanContinue);
        Assert.False(Directory.Exists(Path.Combine(repository.RootPath, ".git")));
        Assert.False(Directory.Exists(context.Storage.WorkspaceRoot));
        Assert.False(Directory.Exists(context.Storage.ArchiveRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootPathPreservesExactNormalizedAbsoluteStorageRoot(bool trailingSeparator)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var input = Path.Combine(fixture.Root, "unused", "..", ".", "production");
        if (trailingSeparator) input += Path.DirectorySeparatorChar;
        var context = fixture.Context(input);

        var repository = fixture.ValidateReadOnly(context).Repository!;

        Assert.Equal(Path.GetFullPath(input), context.Storage.ProductionRoot);
        Assert.Same(context.Storage.ProductionRoot, repository.RootPath);
        Assert.True(Path.IsPathFullyQualified(repository.RootPath));
        Assert.Same(context.Id, repository.UniverseId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingRootOrParentProducesMissingIssueWithoutCreatingAnything(bool missingParent)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var root = missingParent ? Path.Combine(fixture.Root, "absent", "production") : fixture.ProductionRoot;
        var context = fixture.Context(root);

        Failure(fixture.ValidateReadOnly(context), NapIssueCodes.ProductionRootMissing, context.Storage.ProductionRoot);

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(root));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "absent")));
    }

    [Fact]
    public void FileRootProducesInvalidIssueWithoutReadingOrChangingItsContent()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        File.WriteAllText(fixture.ProductionRoot, "not a directory, not asset metadata");
        // Exclusive sharing proves the validator only queries attributes, without opening the content.
        var before = fixture.Snapshot();
        ProductionRepositoryValidationResult result;
        using (var locked = new FileStream(fixture.ProductionRoot, FileMode.Open, FileAccess.Read, FileShare.None))
            result = new ProductionRepositoryValidator().Validate(fixture.Context());
        fixture.AssertSnapshot(before);

        Failure(result, NapIssueCodes.ProductionRootInvalid, fixture.ProductionRoot);
    }

    [Fact]
    public void ArbitraryNestedContentAndSpecialNamesAreIgnoredAndUnchanged()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, ".git"), "not a Git repository");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "bad_manifest.json"), "malformed JSON");
        File.WriteAllText(Path.Combine(fixture.ProductionRoot, "profile.json"), "arbitrary configuration");
        File.WriteAllBytes(Path.Combine(fixture.ProductionRoot, "unexpected.png"), [1, 2, 3]);
        var nested = Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "new", "unrecognized")).FullName;
        File.WriteAllText(Path.Combine(nested, "asset.txt"), "no canonical layout imposed");

        Assert.True(fixture.ValidateReadOnly(fixture.Context()).IsValid);
    }

    [Fact]
    public void ExclusiveLockedInternalFileIsNotOpened()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var path = Path.Combine(fixture.ProductionRoot, "portrait_example_001_manifest.json");
        File.WriteAllText(path, "must not read me");
        var before = fixture.Snapshot();
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(new ProductionRepositoryValidator().Validate(fixture.Context()).IsValid);
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void IndependentUniversesKeepExactIdentityAndRootsAcrossRepeatedValidation()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var otherRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "other-production")).FullName;
        var first = fixture.Context();
        var second = fixture.Context(otherRoot, "other_universe");
        var firstRepository = fixture.ValidateReadOnly(first).Repository!;
        var secondRepository = fixture.ValidateReadOnly(second).Repository!;
        var repeated = fixture.ValidateReadOnly(first).Repository!;

        Assert.Same(first.Id, firstRepository.UniverseId);
        Assert.Same(second.Id, secondRepository.UniverseId);
        Assert.Equal(first.Storage.ProductionRoot, firstRepository.RootPath);
        Assert.Equal(second.Storage.ProductionRoot, secondRepository.RootPath);
        Assert.NotEqual(firstRepository.RootPath, secondRepository.RootPath);
        Assert.NotEqual(firstRepository.UniverseId, secondRepository.UniverseId);
        Assert.Equal(firstRepository.RootPath, repeated.RootPath);
        Assert.Same(first.Id, repeated.UniverseId);
    }

    [Fact]
    public void DirectoryReparseRootIsRejectedWithoutChangingItsTarget()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "target sentinel");
        ProductionRepositoryTestFixture.CreateDirectoryLink(fixture.ProductionRoot, target);
        try
        {
            Assert.True((File.GetAttributes(fixture.ProductionRoot) & FileAttributes.ReparsePoint) != 0);
            Failure(fixture.ValidateReadOnly(fixture.Context()), NapIssueCodes.ProductionRootReparse, fixture.ProductionRoot);
        }
        finally { Directory.Delete(fixture.ProductionRoot); } // Remove only the link, never its target.
    }

    [Fact]
    public void InternalDirectoryLinkIsIgnoredEvenWhenItLoopsBackToTheRoot()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var link = Path.Combine(fixture.ProductionRoot, "loop");
        ProductionRepositoryTestFixture.CreateDirectoryLink(link, fixture.ProductionRoot);
        try { Assert.True(fixture.ValidateReadOnly(fixture.Context()).IsValid); }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void ReparseAncestorIsNotInspectedOrResolved()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var target = Directory.CreateDirectory(Path.Combine(fixture.Root, "target")).FullName;
        Directory.CreateDirectory(Path.Combine(target, "production"));
        var link = Path.Combine(fixture.Root, "ancestor-link");
        ProductionRepositoryTestFixture.CreateDirectoryLink(link, target);
        try
        {
            var context = fixture.Context(Path.Combine(link, "production"));
            var repository = fixture.ValidateReadOnly(context).Repository!;
            Assert.NotNull(repository);
            Assert.Same(context.Storage.ProductionRoot, repository.RootPath);
            Assert.NotEqual(Path.Combine(target, "production"), repository.RootPath);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void WindowsRootWithoutListPermissionIsValidWithoutEnumeratingEntries()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new ProductionRepositoryTestFixture();
        var directory = Directory.CreateDirectory(fixture.ProductionRoot);
        File.WriteAllText(Path.Combine(directory.FullName, "private.txt"), "must not enumerate");
        var context = fixture.Context();
        var before = fixture.Snapshot();
        using var identity = WindowsIdentity.GetCurrent();
        var security = directory.GetAccessControl();
        var deny = new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        security.AddAccessRule(deny);
        directory.SetAccessControl(security);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(directory.FullName).ToArray());
            Assert.True(new ProductionRepositoryValidator().Validate(context).IsValid);
        }
        finally
        {
            security.RemoveAccessRuleSpecific(deny);
            directory.SetAccessControl(security);
        }
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void WindowsInvalidNameIoErrorPropagatesWithoutBecomingMissingIssue()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new ProductionRepositoryTestFixture();
        // GetFullPath accepts a fully qualified wildcard path, but Windows attribute I/O rejects it.
        var root = Path.Combine(fixture.Root, "*production*");
        var context = fixture.Context(root);
        var before = fixture.Snapshot();
        Assert.Throws<IOException>(() => File.GetAttributes(root));
        Assert.Throws<IOException>(() => new ProductionRepositoryValidator().Validate(context));
        fixture.AssertSnapshot(before);
    }

    [Fact]
    public void ValidatedRepositoryIsSealedReadOnlyAndHasOnlyAnInternalContextConstructor()
    {
        var type = typeof(ValidatedProductionRepository);
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(constructor.IsAssembly);
        Assert.Equal(typeof(UniverseContext), Assert.Single(constructor.GetParameters()).ParameterType);
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(ProductionRepositoryValidationResult).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void InternalConstructorCopiesContextWithoutFilesystemIO()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        var context = fixture.Context(); // Deliberately nonexistent; the constructor must not probe or create it.
        var before = fixture.Snapshot();
        var constructor = Assert.Single(typeof(ValidatedProductionRepository).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        var repository = Assert.IsType<ValidatedProductionRepository>(constructor.Invoke([context]));

        Assert.Same(context.Id, repository.UniverseId);
        Assert.Same(context.Storage.ProductionRoot, repository.RootPath);
        fixture.AssertSnapshot(before);
        Assert.False(Directory.Exists(repository.RootPath));
    }

    [Fact]
    public void ResultConstructorRejectsNullReportAndCleanReportWithoutRepository()
    {
        Assert.Throws<ArgumentNullException>(() => new ProductionRepositoryValidationResult(null!, null));
        Assert.Throws<ArgumentException>(() => new ProductionRepositoryValidationResult(new NapIssueReport([]), null));
    }

    [Theory]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Info, NapIssueDisposition.Stop)]
    [InlineData(NapIssueSeverity.Warning, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Warning, NapIssueDisposition.Stop)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Continue)]
    [InlineData(NapIssueSeverity.Error, NapIssueDisposition.Stop)]
    public void AnyIssuePreventsRepositoryRegardlessOfSeverityOrDisposition(NapIssueSeverity severity, NapIssueDisposition disposition)
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var repository = fixture.ValidateReadOnly(fixture.Context()).Repository!;
        var issues = new NapIssueReport([new NapIssue("test_issue", severity, disposition, "Issue.")]);

        Assert.Throws<ArgumentException>(() => new ProductionRepositoryValidationResult(issues, repository));
        var invalid = new ProductionRepositoryValidationResult(issues, null);
        Assert.Same(issues, invalid.Issues);
        Assert.False(invalid.IsValid);
        Assert.Null(invalid.Repository);
    }

    [Fact]
    public void CleanReportAndRepositoryCanBeCombinedWithoutRevalidation()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var repository = fixture.ValidateReadOnly(fixture.Context()).Repository!;
        Directory.Delete(fixture.ProductionRoot);
        var issues = new NapIssueReport([]);

        var result = new ProductionRepositoryValidationResult(issues, repository);

        Assert.True(result.IsValid);
        Assert.Same(issues, result.Issues);
        Assert.Same(repository, result.Repository);
        Assert.False(Directory.Exists(repository.RootPath)); // Validation is a point-in-time check, not a filesystem lock.
    }

    private static void Failure(ProductionRepositoryValidationResult result, string code, string root)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Repository);
        Assert.False(result.Issues.IsClean);
        Assert.True(result.Issues.HasErrors);
        Assert.True(result.Issues.ShouldStop);
        Assert.False(result.Issues.CanContinue);
        var issue = Assert.Single(result.Issues.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(NapIssueSeverity.Error, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.Equal(root, issue.SubjectPath);
        Assert.True(Path.IsPathFullyQualified(issue.SubjectPath!));
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
    }
}
