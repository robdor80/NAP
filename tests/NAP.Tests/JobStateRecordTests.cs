using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobStateRecordTests
{
    private static readonly JobId Id = new("job_00112233445566778899aabbccddeeff");
    private static readonly UniverseId Universe = new("nimroel");

    [Fact]
    public void ContractIsAnImmutablePublicSealedRecordWithOnlyIdentityUniverseAndState()
    {
        var type = typeof(JobStateRecord);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "jobId", "universeId", "state" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(JobId), typeof(UniverseId), typeof(JobState) }, parameters.Select(p => p.ParameterType));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "JobId", "State", "UniverseId" }, properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(JobId), typeof(JobState), typeof(UniverseId) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    public static IEnumerable<object[]> States() => Enum.GetValues<JobState>().Select(state => new object[] { state });

    [Theory]
    [MemberData(nameof(States))]
    public void DefinedStatesRetainImmutableInputReferences(JobState state)
    {
        var record = new JobStateRecord(Id, Universe, state);
        Assert.Same(Id, record.JobId);
        Assert.Same(Universe, record.UniverseId);
        Assert.Equal(state, record.State);
    }

    [Theory]
    [InlineData(true, true, "jobId")]
    [InlineData(true, false, "jobId")]
    [InlineData(false, true, "universeId")]
    public void NullInputsAreRejectedInIdentityThenUniverseOrder(bool nullJob, bool nullUniverse, string parameter) =>
        Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(() =>
            new JobStateRecord(nullJob ? null! : Id, nullUniverse ? null! : Universe, (JobState)(-1))).ParamName);

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void UndefinedStatesAreRejected(int state) =>
        Assert.Equal("state", Assert.Throws<ArgumentOutOfRangeException>(() => new JobStateRecord(Id, Universe, (JobState)state)).ParamName);

    [Fact]
    public void EqualityOperatorsAndHashCodesUseAllThreeValues()
    {
        var first = new JobStateRecord(Id, Universe, JobState.Planned);
        var same = new JobStateRecord(new JobId(Id.Value), new UniverseId(Universe.Value), JobState.Planned);
        Assert.NotSame(first.JobId, same.JobId);
        Assert.NotSame(first.UniverseId, same.UniverseId);
        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.True(first == same);
        Assert.False(first != same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        var differences = new[]
        {
            new JobStateRecord(new JobId("job_00000000000000000000000000000001"), Universe, JobState.Planned),
            new JobStateRecord(Id, new UniverseId("test_universe"), JobState.Planned),
            new JobStateRecord(Id, Universe, JobState.Failed)
        };
        Assert.All(differences, different =>
        {
            Assert.False(first.Equals(different));
            Assert.False(first == different);
            Assert.True(first != different);
        });
        Assert.False(first.Equals(null));
        Assert.Equal(4, new HashSet<JobStateRecord>(differences) { first, same }.Count);
    }
}
