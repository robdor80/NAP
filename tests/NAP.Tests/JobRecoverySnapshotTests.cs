using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobRecoverySnapshotTests
{
    private static readonly UniverseId Universe = new("nimroel");
    private static readonly NapIssueReport Clean = new([]);
    private static JobId Id(int number) => new("job_" + number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture));
    private static JobStateRecord Record(int number, JobState state) => new(Id(number), Universe, state);
    private static JobRecoveryTempArtifact Temp(int number, char suffix) => new(Id(number), Id(number).Value + "." + new string(suffix, 32) + ".tmp", true);

    [Fact]
    public void PublicContractIsSealedClassWithExactConstructorAndGetOnlyProperties()
    {
        var type = typeof(JobRecoverySnapshot);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Null(type.GetMethod("<Clone>$"));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "recoverableJobs", "completedJobs", "failedJobs", "orphanTemps", "issues" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(IEnumerable<JobStateRecord>), typeof(IEnumerable<JobStateRecord>), typeof(IEnumerable<JobStateRecord>),
            typeof(IEnumerable<JobRecoveryTempArtifact>), typeof(NapIssueReport) }, parameters.Select(p => p.ParameterType));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "CompletedJobs", "FailedJobs", "Issues", "OrphanTemps", "RecoverableJobs" }, properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(IReadOnlyList<JobStateRecord>), typeof(IReadOnlyList<JobStateRecord>), typeof(NapIssueReport),
            typeof(IReadOnlyList<JobRecoveryTempArtifact>), typeof(IReadOnlyList<JobStateRecord>) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    [Theory]
    [InlineData(0, "recoverableJobs")]
    [InlineData(1, "completedJobs")]
    [InlineData(2, "failedJobs")]
    [InlineData(3, "orphanTemps")]
    [InlineData(4, "issues")]
    public void NullArgumentsAreRejected(int index, string parameter) => Assert.Equal(parameter,
        Assert.Throws<ArgumentNullException>(() => new JobRecoverySnapshot(index == 0 ? null! : [], index == 1 ? null! : [],
            index == 2 ? null! : [], index == 3 ? null! : [], index == 4 ? null! : Clean)).ParamName);

    [Theory]
    [InlineData(0, "recoverableJobs")]
    [InlineData(1, "completedJobs")]
    [InlineData(2, "failedJobs")]
    [InlineData(3, "orphanTemps")]
    public void NullElementsAreRejected(int index, string parameter) => Assert.Equal(parameter,
        Assert.Throws<ArgumentException>(() => new JobRecoverySnapshot(index == 0 ? [null!] : [], index == 1 ? [null!] : [],
            index == 2 ? [null!] : [], index == 3 ? [null!] : [], Clean)).ParamName);

    public static IEnumerable<object[]> Categories() => from state in Enum.GetValues<JobState>() from category in Enumerable.Range(0, 3)
        select new object[] { state, category, category == 0 ? state is not (JobState.Completed or JobState.Failed)
            : category == 1 ? state == JobState.Completed : state == JobState.Failed };

    [Theory]
    [MemberData(nameof(Categories))]
    public void AllStatesMustBeInTheirExactCategory(JobState state, int category, bool valid)
    {
        JobRecoverySnapshot Create() => new(category == 0 ? [Record(1, state)] : [], category == 1 ? [Record(1, state)] : [],
            category == 2 ? [Record(1, state)] : [], [], Clean);
        if (valid) Assert.Single(new[] { Create().RecoverableJobs, Create().CompletedJobs, Create().FailedJobs }[category]);
        else Assert.Throws<ArgumentException>(Create);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 2)]
    [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(1, 2)]
    public void DuplicateValueIdentitiesWithinOrAcrossCategoriesAreRejected(int first, int second)
    {
        var groups = new[] { new List<JobStateRecord>(), new List<JobStateRecord>(), new List<JobStateRecord>() };
        var states = new[] { JobState.Planned, JobState.Completed, JobState.Failed };
        groups[first].Add(Record(1, states[first]));
        groups[second].Add(new JobStateRecord(new JobId(Id(1).Value), new UniverseId(Universe.Value), states[second]));
        Assert.Throws<ArgumentException>(() => new JobRecoverySnapshot(groups[0], groups[1], groups[2], [], Clean));
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 2)]
    [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(1, 2)]
    public void MixedUniversesWithinOrAcrossCategoriesAreRejected(int first, int second)
    {
        var groups = new[] { new List<JobStateRecord>(), new List<JobStateRecord>(), new List<JobStateRecord>() };
        var states = new[] { JobState.Planned, JobState.Completed, JobState.Failed };
        groups[first].Add(Record(1, states[first]));
        groups[second].Add(new JobStateRecord(Id(2), new UniverseId("other"), states[second]));
        Assert.Throws<ArgumentException>(() => new JobRecoverySnapshot(groups[0], groups[1], groups[2], [], Clean));
    }

    [Fact]
    public void CopiesCollectionsSortsOrdinalAndRetainsRecordsTempsAndImmutableReport()
    {
        var recoverable = new[] { Record(4, JobState.Planned), Record(1, JobState.Detected) };
        var completed = new List<JobStateRecord> { Record(6, JobState.Completed), Record(2, JobState.Completed) };
        var failed = new List<JobStateRecord> { Record(7, JobState.Failed), Record(3, JobState.Failed) };
        var temps = new List<JobRecoveryTempArtifact> { Temp(4, 'f'), Temp(4, 'a'), Temp(1, 'b') };
        var issueList = new List<NapIssue> { new(NapIssueCodes.JobRecoveryOrphanTemp, NapIssueSeverity.Warning, NapIssueDisposition.Continue, "temp") };
        var report = new NapIssueReport(issueList);
        var snapshot = new JobRecoverySnapshot(recoverable, completed, failed, temps, report);
        Assert.Same(recoverable[0], snapshot.RecoverableJobs[1]);
        Assert.Same(temps[1], snapshot.OrphanTemps[1]);
        recoverable[0] = Record(9, JobState.Validated);
        completed.Clear(); failed.Clear(); temps.Clear(); issueList.Clear();
        Assert.Equal(new[] { Id(1), Id(4) }, snapshot.RecoverableJobs.Select(r => r.JobId));
        Assert.Equal(new[] { Id(2), Id(6) }, snapshot.CompletedJobs.Select(r => r.JobId));
        Assert.Equal(new[] { Id(3), Id(7) }, snapshot.FailedJobs.Select(r => r.JobId));
        Assert.Equal(new[] { Temp(1, 'b'), Temp(4, 'a'), Temp(4, 'f') }, snapshot.OrphanTemps);
        Assert.Same(report, snapshot.Issues);
        Assert.Single(snapshot.Issues.Issues);
        foreach (var group in new[] { snapshot.RecoverableJobs, snapshot.CompletedJobs, snapshot.FailedJobs })
        {
            var collection = Assert.IsAssignableFrom<IList<JobStateRecord>>(group);
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Add(Record(8, JobState.Detected)));
            Assert.Throws<NotSupportedException>(() => collection[0] = Record(8, JobState.Detected));
            Assert.Throws<NotSupportedException>(() => collection.Clear());
        }
        var tempCollection = Assert.IsAssignableFrom<IList<JobRecoveryTempArtifact>>(snapshot.OrphanTemps);
        Assert.True(tempCollection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => tempCollection.Add(Temp(8, 'a')));
        Assert.Throws<NotSupportedException>(() => tempCollection[0] = Temp(8, 'a'));
        Assert.Throws<NotSupportedException>(() => tempCollection.Clear());
    }
}
