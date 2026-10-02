using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class UniverseStorageIsolationValidatorTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "nap-isolation-tests");

    [Fact]
    public void SiblingsAndSimilarPrefixes_DoNotOverlap()
    {
        Assert.True(UniverseStorageIsolationValidator.Validate([Config("nimroel"), Config("other")]).IsClean);
        Assert.True(UniverseStorageIsolationValidator.Validate([Config("nimroel"), Config("nimroel_extra")]).CanContinue);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void AllNineRootPairs_DetectEqualityAndBothContainmentDirections(int firstIndex, int secondIndex)
    {
        foreach (var relation in new[] { "equal", "second_child", "first_child" })
        {
            var first = Paths("nimroel");
            var second = Paths("other");
            if (relation == "first_child")
                first[firstIndex] = Path.Combine(second[secondIndex], "child");
            else
                second[secondIndex] = relation == "equal" ? first[firstIndex] : Path.Combine(first[firstIndex], "child");

            var report = UniverseStorageIsolationValidator.Validate([Config("nimroel", first), Config("other", second)]);
            var issue = Assert.Single(report.Issues);
            Assert.Equal(NapIssueCodes.UniverseStorageOverlap, issue.Code);
            Assert.Equal(NapIssueSeverity.Error, issue.Severity);
            Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
            Assert.True(report.HasErrors);
            Assert.True(report.ShouldStop);
            Assert.False(report.CanContinue);
            Assert.Equal(first[firstIndex], issue.SubjectPath);
            Assert.DoesNotContain(first[firstIndex], issue.Message);
            Assert.Contains(first[firstIndex], issue.Detail!);
            Assert.Contains(second[secondIndex], issue.Detail!);
        }
    }

    [Fact]
    public void Casing_FollowsPlatformComparison()
    {
        var first = Paths("mixed_case");
        var second = Paths("other");
        second[0] = first[0].ToUpperInvariant();
        var report = UniverseStorageIsolationValidator.Validate([Config("first", first), Config("second", second)]);
        Assert.Equal(OperatingSystem.IsWindows(), report.ShouldStop);
    }

    [Fact]
    public void TrailingSeparatorsAndDotSegments_DoNotHideOverlap()
    {
        var first = Paths("first");
        var second = Paths("second");
        second[2] = Path.Combine(first[0], "unused", "..") + Path.DirectorySeparatorChar;
        Assert.True(UniverseStorageIsolationValidator.Validate([Config("first", first), Config("second", second)]).ShouldStop);
    }

    [Fact]
    public void VolumeRoot_IsAnAncestor()
    {
        var first = Paths("first");
        first[0] = Path.GetPathRoot(Root)!;
        Assert.True(UniverseStorageIsolationValidator.Validate([Config("first", first), Config("second")]).ShouldStop);
    }

    [Fact]
    public void SameUniverseRoots_AreNotCompared()
    {
        var path = Path.Combine(Root, "single");
        var config = Config("single", [path, path, Path.Combine(path, "child")]);
        Assert.True(UniverseStorageIsolationValidator.Validate([config]).IsClean);
        Assert.True(UniverseStorageIsolationValidator.Validate([]).IsClean);
    }

    [Fact]
    public void DuplicateUniverseOrNullInput_IsAnArgumentError()
    {
        Assert.Throws<ArgumentException>(() => UniverseStorageIsolationValidator.Validate([Config("same"), Config("same", Paths("separate"))]));
        Assert.Throws<ArgumentNullException>(() => UniverseStorageIsolationValidator.Validate(null!));
        Assert.Throws<ArgumentException>(() => UniverseStorageIsolationValidator.Validate([null!]));
    }

    [Fact]
    public void MultipleConflicts_AreReportedInInputAndRootOrder()
    {
        var path = Path.Combine(Root, "shared");
        var first = Config("first", [path, path, path]);
        var second = Config("second", [path, path, path]);
        var report = UniverseStorageIsolationValidator.Validate([first, second]);
        Assert.Equal(9, report.Issues.Count);
        Assert.Contains("WorkspaceRoot", report.Issues[0].Detail!);
        Assert.Contains("ArchiveRoot", report.Issues[^1].Detail!);
    }

    private static string[] Paths(string name) =>
    [Path.Combine(Root, name, "workspace"), Path.Combine(Root, name, "production"), Path.Combine(Root, name, "archive")];
    private static UniverseStorageConfig Config(string id, string[]? paths = null)
    {
        paths ??= Paths(id);
        return new UniverseStorageConfig(new UniverseId(id), paths[0], paths[1], paths[2]);
    }
}
