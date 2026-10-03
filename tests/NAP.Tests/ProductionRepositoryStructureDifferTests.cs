using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class ProductionRepositoryStructureDifferTests
{
    private static string TestRoot => Path.Combine(Path.GetTempPath(), "nap-structure-tests", "production");

    [Fact]
    public void NullInputsAreRejectedAndApiOnlyAcceptsSnapshots()
    {
        var snapshot = Snapshot();
        var differ = new ProductionRepositoryStructureDiffer();
        Assert.Equal("before", Assert.Throws<ArgumentNullException>(() => differ.Compare(null!, snapshot)).ParamName);
        Assert.Equal("after", Assert.Throws<ArgumentNullException>(() => differ.Compare(snapshot, null!)).ParamName);
        var method = Assert.Single(typeof(ProductionRepositoryStructureDiffer).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Compare", method.Name);
        Assert.Equal(typeof(ProductionRepositoryStructureDiff), method.ReturnType);
        Assert.Equal(new[] { typeof(ProductionRepositorySnapshot), typeof(ProductionRepositorySnapshot) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(new[] { "Added", "Removed", "KindChanged" }, Enum.GetNames<RepositoryStructuralChangeKind>());
    }

    [Fact]
    public void EmptySnapshotsAreEmptyAndKeepExactBeforeIdentityAndRoot()
    {
        var before = Snapshot();
        var after = Snapshot();
        Assert.NotSame(before.UniverseId, after.UniverseId);
        var diff = new ProductionRepositoryStructureDiffer().Compare(before, after);
        Assert.True(diff.IsEmpty);
        Assert.Empty(diff.Changes);
        Assert.Same(before.UniverseId, diff.UniverseId);
        Assert.Same(before.RootPath, diff.RootPath);
    }

    [Fact]
    public void IdenticalStructureProducesNoChangesRegardlessOfEntryOrder()
    {
        var first = Snapshot(("a", ProductionRepositoryEntryKind.Directory), ("a/b.txt", ProductionRepositoryEntryKind.File));
        var second = Snapshot(("a/b.txt", ProductionRepositoryEntryKind.File), ("a", ProductionRepositoryEntryKind.Directory));
        Assert.True(new ProductionRepositoryStructureDiffer().Compare(first, second).IsEmpty);
        Assert.True(new ProductionRepositoryStructureDiffer().Compare(first, first).IsEmpty);
    }

    [Theory]
    [InlineData(ProductionRepositoryEntryKind.File)]
    [InlineData(ProductionRepositoryEntryKind.Directory)]
    public void AddedEntryHasOnlyAfterKind(ProductionRepositoryEntryKind kind)
    {
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(), Snapshot(("new", kind)));
        Assert.False(diff.IsEmpty);
        var change = Assert.Single(diff.Changes);
        AssertChange(change, "new", RepositoryStructuralChangeKind.Added, null, kind);
    }

    [Theory]
    [InlineData(ProductionRepositoryEntryKind.File)]
    [InlineData(ProductionRepositoryEntryKind.Directory)]
    public void RemovedEntryHasOnlyBeforeKind(ProductionRepositoryEntryKind kind)
    {
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(("old", kind)), Snapshot());
        AssertChange(Assert.Single(diff.Changes), "old", RepositoryStructuralChangeKind.Removed, kind, null);
    }

    [Theory]
    [InlineData(ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.Directory)]
    [InlineData(ProductionRepositoryEntryKind.Directory, ProductionRepositoryEntryKind.File)]
    public void KindReplacementIsExactlyOneKindChanged(ProductionRepositoryEntryKind before, ProductionRepositoryEntryKind after)
    {
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(("same", before)), Snapshot(("same", after)));
        AssertChange(Assert.Single(diff.Changes), "same", RepositoryStructuralChangeKind.KindChanged, before, after);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedChangesAreOrdinallySortedAndIndependentOfInputOrder(bool reverse)
    {
        (string, ProductionRepositoryEntryKind)[] before = [("z old", ProductionRepositoryEntryKind.File),
            ("Élite", ProductionRepositoryEntryKind.File), ("A replacement", ProductionRepositoryEntryKind.Directory),
            ("unchanged", ProductionRepositoryEntryKind.File)];
        (string, ProductionRepositoryEntryKind)[] after = [("a new", ProductionRepositoryEntryKind.Directory),
            ("Élite", ProductionRepositoryEntryKind.Directory), ("A replacement", ProductionRepositoryEntryKind.File),
            ("unchanged", ProductionRepositoryEntryKind.File)];
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(reverse ? before.Reverse().ToArray() : before),
            Snapshot(reverse ? after.Reverse().ToArray() : after));
        Assert.Equal(new[] { "A replacement", "a new", "z old", "Élite" }, diff.Changes.Select(change => change.RelativePath));
        Assert.Equal(new[] { RepositoryStructuralChangeKind.KindChanged, RepositoryStructuralChangeKind.Added,
            RepositoryStructuralChangeKind.Removed, RepositoryStructuralChangeKind.KindChanged }, diff.Changes.Select(change => change.Kind));
        Assert.Equal(diff.Changes.Count, diff.Changes.Select(change => change.RelativePath).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("Foo", "foo")]
    [InlineData("Treskal/Farmer", "treskal/Farmer")]
    [InlineData("Élite", "E\u0301lite")]
    [InlineData(" name", "name")]
    [InlineData("name ", "name")]
    public void ExactNameDifferencesProduceRemovedAndAddedWithoutNormalization(string beforeName, string afterName)
    {
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot((beforeName, ProductionRepositoryEntryKind.File)),
            Snapshot((afterName, ProductionRepositoryEntryKind.File)));
        Assert.Equal(2, diff.Changes.Count);
        AssertChange(Assert.Single(diff.Changes, change => change.Kind == RepositoryStructuralChangeKind.Removed),
            beforeName, RepositoryStructuralChangeKind.Removed, ProductionRepositoryEntryKind.File, null);
        AssertChange(Assert.Single(diff.Changes, change => change.Kind == RepositoryStructuralChangeKind.Added),
            afterName, RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.File);
        Assert.Equal(new[] { beforeName, afterName }.OrderBy(path => path, StringComparer.Ordinal), diff.Changes.Select(change => change.RelativePath));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".github/workflows/test.yml")]
    [InlineData("README.md")]
    [InlineData("Treskal Viejo")]
    [InlineData("Élite")]
    [InlineData("House Aethros")]
    [InlineData("portrait OLD")]
    [InlineData("日本語 🐉")]
    public void InfrastructureAndHistoricalNamesAreOrdinaryExactEntries(string name)
    {
        var before = Snapshot();
        var after = Snapshot((name, ProductionRepositoryEntryKind.Directory));
        var change = Assert.Single(new ProductionRepositoryStructureDiffer().Compare(before, after).Changes);
        Assert.Same(after.Entries[0].RelativePath, change.RelativePath);
        AssertChange(change, name, RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.Directory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntireTreesProduceOneChangePerObservedEntryWithoutCollapsing(bool added)
    {
        var tree = Snapshot(("a", ProductionRepositoryEntryKind.Directory), ("a/b", ProductionRepositoryEntryKind.Directory),
            ("a/b/file.txt", ProductionRepositoryEntryKind.File));
        var diff = new ProductionRepositoryStructureDiffer().Compare(added ? Snapshot() : tree, added ? tree : Snapshot());
        Assert.Equal(new[] { "a", "a/b", "a/b/file.txt" }, diff.Changes.Select(change => change.RelativePath));
        Assert.All(diff.Changes, change => Assert.Equal(added ? RepositoryStructuralChangeKind.Added : RepositoryStructuralChangeKind.Removed, change.Kind));
    }

    [Fact]
    public void ApparentMoveRemainsRemovedAndAdded()
    {
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(("a/file.txt", ProductionRepositoryEntryKind.File)),
            Snapshot(("b/file.txt", ProductionRepositoryEntryKind.File)));
        Assert.Equal(2, diff.Changes.Count);
        AssertChange(diff.Changes[0], "a/file.txt", RepositoryStructuralChangeKind.Removed, ProductionRepositoryEntryKind.File, null);
        AssertChange(diff.Changes[1], "b/file.txt", RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.File);
    }

    [Fact]
    public void FullPathIsNotUsedAsEntryIdentity()
    {
        var first = CreateSnapshot(TestRoot, "test_universe", [Entry("same.txt", ProductionRepositoryEntryKind.File, Path.Combine(TestRoot, "first.txt"))]);
        var second = CreateSnapshot(TestRoot, "test_universe", [Entry("same.txt", ProductionRepositoryEntryKind.File, Path.Combine(TestRoot, "other.txt"))]);
        Assert.True(new ProductionRepositoryStructureDiffer().Compare(first, second).IsEmpty);
    }

    [Fact]
    public void UniverseMismatchIsRejectedAndEqualValueInstancesAreAccepted()
    {
        var first = CreateSnapshot(TestRoot, "first_universe", []);
        var mismatch = CreateSnapshot(TestRoot, "other_universe", []);
        Assert.Equal("after", Assert.Throws<ArgumentException>(() => new ProductionRepositoryStructureDiffer().Compare(first, mismatch)).ParamName);
        var equal = CreateSnapshot(TestRoot, "first_universe", []);
        Assert.NotSame(first.UniverseId, equal.UniverseId);
        Assert.Equal(first.UniverseId, equal.UniverseId);
        Assert.True(new ProductionRepositoryStructureDiffer().Compare(first, equal).IsEmpty);
    }

    [Fact]
    public void DifferentRootsAreRejected()
    {
        var first = Snapshot();
        var second = CreateSnapshot(Path.Combine(TestRoot, "different"), "test_universe", []);
        Assert.Equal("after", Assert.Throws<ArgumentException>(() => new ProductionRepositoryStructureDiffer().Compare(first, second)).ParamName);
    }

    [Fact]
    public void RootCasingUsesPlatformComparisonWhileRelativeCasingAlwaysRemainsVisible()
    {
        var before = Snapshot(("Foo", ProductionRepositoryEntryKind.File));
        var after = Snapshot(("foo", ProductionRepositoryEntryKind.File));
        SetRoot(after, before.RootPath.ToUpperInvariant());
        if (OperatingSystem.IsWindows())
        {
            var diff = new ProductionRepositoryStructureDiffer().Compare(before, after);
            Assert.Equal(2, diff.Changes.Count);
            Assert.Same(before.RootPath, diff.RootPath);
        }
        else Assert.Throws<ArgumentException>(() => new ProductionRepositoryStructureDiffer().Compare(before, after));
    }

    [Fact]
    public void RootIsComparedLiterallyWithoutNormalizationOrFilesystemLookup()
    {
        var before = Snapshot();
        var after = Snapshot();
        var raw = before.RootPath + Path.DirectorySeparatorChar + "unused" + Path.DirectorySeparatorChar + "..";
        SetRoot(before, raw);
        SetRoot(after, raw);
        var diff = new ProductionRepositoryStructureDiffer().Compare(before, after);
        Assert.Same(raw, diff.RootPath);
        SetRoot(after, TestRoot);
        Assert.Throws<ArgumentException>(() => new ProductionRepositoryStructureDiffer().Compare(before, after));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRelativePathInEitherSnapshotIsRejectedRatherThanOverwritten(bool inAfter)
    {
        var duplicate = Snapshot(("duplicate", ProductionRepositoryEntryKind.File), ("duplicate", ProductionRepositoryEntryKind.Directory));
        var empty = Snapshot();
        var exception = Assert.Throws<ArgumentException>(() => new ProductionRepositoryStructureDiffer().Compare(inAfter ? empty : duplicate, inAfter ? duplicate : empty));
        Assert.Equal(inAfter ? "after" : "before", exception.ParamName);
    }

    [Fact]
    public void CaseDistinctPathsAreNotDuplicatesEvenOnWindows()
    {
        var snapshot = Snapshot(("Foo", ProductionRepositoryEntryKind.File), ("foo", ProductionRepositoryEntryKind.File));
        var diff = new ProductionRepositoryStructureDiffer().Compare(Snapshot(), snapshot);
        Assert.Equal(new[] { "Foo", "foo" }, diff.Changes.Select(change => change.RelativePath));
    }

    [Fact]
    public void ScanSnapshotsRemainComparableAfterRootIsDeletedAndChangedBytesAreNotStructuralChanges()
    {
        using var fixture = new ProductionRepositoryTestFixture();
        Directory.CreateDirectory(fixture.ProductionRoot);
        var path = Path.Combine(fixture.ProductionRoot, "same.txt");
        File.WriteAllText(path, "before bytes");
        var repository = fixture.ValidateReadOnly(fixture.Context()).Repository!;
        var scanner = new ProductionRepositoryScanner();
        var before = scanner.Scan(repository).Snapshot!;
        File.WriteAllText(path, "completely different bytes and length");
        var contentAfter = scanner.Scan(repository).Snapshot!;
        Directory.CreateDirectory(Path.Combine(fixture.ProductionRoot, "added directory"));
        var structureAfter = scanner.Scan(repository).Snapshot!;
        Directory.Delete(fixture.ProductionRoot, recursive: true);
        var filesystemBeforeCompare = fixture.Snapshot();

        var differ = new ProductionRepositoryStructureDiffer();
        Assert.True(differ.Compare(before, contentAfter).IsEmpty);
        AssertChange(Assert.Single(differ.Compare(before, structureAfter).Changes), "added directory",
            RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.Directory);
        fixture.AssertSnapshot(filesystemBeforeCompare);
        Assert.False(Directory.Exists(fixture.ProductionRoot));
    }

    [Fact]
    public void IndependentComparisonsDoNotMixUniversesOrRoots()
    {
        var differ = new ProductionRepositoryStructureDiffer();
        var firstBefore = Snapshot();
        var firstAfter = Snapshot(("first", ProductionRepositoryEntryKind.File));
        var otherRoot = Path.Combine(TestRoot, "other");
        var secondBefore = CreateSnapshot(otherRoot, "other_universe", []);
        var secondAfter = CreateSnapshot(otherRoot, "other_universe", [Entry("second", ProductionRepositoryEntryKind.Directory)]);
        var first = differ.Compare(firstBefore, firstAfter);
        var second = differ.Compare(secondBefore, secondAfter);
        Assert.Equal("first", Assert.Single(first.Changes).RelativePath);
        Assert.Equal("second", Assert.Single(second.Changes).RelativePath);
        Assert.Same(firstBefore.UniverseId, first.UniverseId);
        Assert.Same(secondBefore.UniverseId, second.UniverseId);
        Assert.Same(firstBefore.RootPath, first.RootPath);
        Assert.Same(secondBefore.RootPath, second.RootPath);
        Assert.Equal("first", Assert.Single(differ.Compare(firstBefore, firstAfter).Changes).RelativePath);
    }

    [Fact]
    public void DiffDefensivelyCopiesSortsAndExposesReadOnlyChanges()
    {
        var before = Snapshot();
        var changes = new ProductionRepositoryStructureDiffer().Compare(before,
            Snapshot(("z", ProductionRepositoryEntryKind.File), ("A", ProductionRepositoryEntryKind.Directory))).Changes.Reverse().ToList();
        var diff = Construct<ProductionRepositoryStructureDiff>(before, changes);
        changes.Clear();
        Assert.Equal(new[] { "A", "z" }, diff.Changes.Select(change => change.RelativePath));
        var collection = Assert.IsAssignableFrom<IList<ProductionRepositoryStructuralChange>>(diff.Changes);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection[0] = diff.Changes[1]);
        Assert.False(diff.IsEmpty);
        Assert.Empty(Construct<ProductionRepositoryStructureDiff>(before, Array.Empty<ProductionRepositoryStructuralChange>()).Changes);
    }

    [Fact]
    public void DiffConstructorRejectsNullItemsAndDuplicateChangePaths()
    {
        var before = Snapshot();
        var change = Assert.Single(new ProductionRepositoryStructureDiffer().Compare(before, Snapshot(("path", ProductionRepositoryEntryKind.File))).Changes);
        AssertConstructionError<ProductionRepositoryStructureDiff>(before, new ProductionRepositoryStructuralChange[] { null! });
        AssertConstructionError<ProductionRepositoryStructureDiff>(before, new[] { change, change });
        AssertConstructionError<ProductionRepositoryStructureDiff>(before, null);
        AssertConstructionError<ProductionRepositoryStructureDiff>(null, new[] { change });
    }

    [Theory]
    [InlineData(RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.File)]
    [InlineData(RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.Directory)]
    [InlineData(RepositoryStructuralChangeKind.Removed, ProductionRepositoryEntryKind.File, null)]
    [InlineData(RepositoryStructuralChangeKind.Removed, ProductionRepositoryEntryKind.Directory, null)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.Directory)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, ProductionRepositoryEntryKind.Directory, ProductionRepositoryEntryKind.File)]
    public void ChangeConstructorAcceptsExactlyConsistentCombinations(RepositoryStructuralChangeKind kind,
        ProductionRepositoryEntryKind? before, ProductionRepositoryEntryKind? after)
    {
        const string name = " Élite / legacy name ";
        var change = Construct<ProductionRepositoryStructuralChange>(name, kind, before, after);
        AssertChange(change, name, kind, before, after);
        Assert.Same(name, change.RelativePath);
    }

    [Theory]
    [InlineData(RepositoryStructuralChangeKind.Added, null, null)]
    [InlineData(RepositoryStructuralChangeKind.Added, ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.File)]
    [InlineData(RepositoryStructuralChangeKind.Added, ProductionRepositoryEntryKind.File, null)]
    [InlineData(RepositoryStructuralChangeKind.Removed, null, null)]
    [InlineData(RepositoryStructuralChangeKind.Removed, ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.Directory)]
    [InlineData(RepositoryStructuralChangeKind.Removed, null, ProductionRepositoryEntryKind.File)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, null, ProductionRepositoryEntryKind.File)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, ProductionRepositoryEntryKind.File, null)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, ProductionRepositoryEntryKind.File, ProductionRepositoryEntryKind.File)]
    [InlineData(RepositoryStructuralChangeKind.KindChanged, ProductionRepositoryEntryKind.Directory, ProductionRepositoryEntryKind.Directory)]
    public void ChangeConstructorRejectsIncoherentStates(RepositoryStructuralChangeKind kind,
        ProductionRepositoryEntryKind? before, ProductionRepositoryEntryKind? after) =>
        AssertConstructionError<ProductionRepositoryStructuralChange>("path", kind, before, after);

    [Fact]
    public void ChangeConstructorRejectsNullPathAndUndefinedEnums()
    {
        AssertConstructionError<ProductionRepositoryStructuralChange>(null, RepositoryStructuralChangeKind.Added, null, ProductionRepositoryEntryKind.File);
        AssertConstructionError<ProductionRepositoryStructuralChange>("path", (RepositoryStructuralChangeKind)99, null, ProductionRepositoryEntryKind.File);
        AssertConstructionError<ProductionRepositoryStructuralChange>("path", RepositoryStructuralChangeKind.Added, null, (ProductionRepositoryEntryKind)99);
        AssertConstructionError<ProductionRepositoryStructuralChange>("path", RepositoryStructuralChangeKind.Removed, (ProductionRepositoryEntryKind)99, null);
    }

    [Fact]
    public void ModelsAreSealedReadOnlyAndHaveNoPublicConstructorOrRuntimeSemanticDependencies()
    {
        Type[] forbidden = [typeof(AssetRoutingRule), typeof(AssetRouteSegment), typeof(UniverseAssetRule), typeof(UniverseProfile),
            typeof(ValidatedAssetPackage), typeof(NapIssue), typeof(NapIssueReport)];
        foreach (var type in new[] { typeof(ProductionRepositoryStructuralChange), typeof(ProductionRepositoryStructureDiff) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors());
            Assert.True(Assert.Single(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).IsAssembly);
            Assert.All(type.GetProperties(), property =>
            {
                Assert.Null(property.SetMethod);
                Assert.DoesNotContain(property.PropertyType, forbidden);
                Assert.NotEqual("FullPath", property.Name);
            });
        }
    }

    // Reflection exercises internal constructors without weakening the 3.1/3.2 public boundaries.
    private static ProductionRepositorySnapshot Snapshot(params (string Path, ProductionRepositoryEntryKind Kind)[] entries) =>
        CreateSnapshot(TestRoot, "test_universe", entries.Select(entry => Entry(entry.Path, entry.Kind)));

    private static ProductionRepositorySnapshot CreateSnapshot(string root, string universe, IEnumerable<ProductionRepositoryEntry> entries)
    {
        var id = new UniverseId(universe);
        var context = new UniverseContext(new UniverseProfile(id, "Synthetic structure test"),
            new UniverseStorageConfig(id, root, root, root));
        return Construct<ProductionRepositorySnapshot>(Construct<ValidatedProductionRepository>(context), entries);
    }

    private static ProductionRepositoryEntry Entry(string path, ProductionRepositoryEntryKind kind, string? fullPath = null) =>
        Construct<ProductionRepositoryEntry>(path, fullPath ?? Path.Combine(TestRoot, path.Replace('/', Path.DirectorySeparatorChar)), kind);

    private static T Construct<T>(params object?[] arguments) =>
        Assert.IsType<T>(Assert.Single(typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)).Invoke(arguments));

    private static void AssertConstructionError<T>(params object?[] arguments)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => Construct<T>(arguments));
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    private static void SetRoot(ProductionRepositorySnapshot snapshot, string root) =>
        typeof(ProductionRepositorySnapshot).GetField("<RootPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(snapshot, root);

    private static void AssertChange(ProductionRepositoryStructuralChange change, string path, RepositoryStructuralChangeKind kind,
        ProductionRepositoryEntryKind? before, ProductionRepositoryEntryKind? after)
    {
        Assert.Equal(path, change.RelativePath);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(before, change.BeforeKind);
        Assert.Equal(after, change.AfterKind);
    }
}
