using System.Globalization;
using System.Reflection;
using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobStateMachineTests
{
    internal static readonly (JobState State, string Token)[] Vocabulary =
    [
        (JobState.Detected, "DETECTED"), (JobState.Staged, "STAGED"), (JobState.Validated, "VALIDATED"),
        (JobState.Planned, "PLANNED"), (JobState.Audited, "AUDITED"), (JobState.Executed, "EXECUTED"),
        (JobState.Verified, "VERIFIED"), (JobState.Completed, "COMPLETED"), (JobState.Failed, "FAILED")
    ];
    private readonly JobStateMachine _machine = new();

    [Fact]
    public void PublicSealedStatelessContractHasExactlyThreeApis()
    {
        var type = typeof(JobStateMachine);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Empty(Assert.Single(type.GetConstructors()).GetParameters());
        Assert.Empty(type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Empty(type.GetProperties());
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "CanTransition", "Create", "Transition" }, methods.Select(method => method.Name));
        Assert.Equal(new[] { typeof(bool), typeof(JobStateRecord), typeof(JobStateRecord) }, methods.Select(method => method.ReturnType));
        Assert.Equal(new[] { typeof(JobState), typeof(JobState) }, methods[0].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "from", "to" }, methods[0].GetParameters().Select(p => p.Name));
        Assert.Equal(new[] { typeof(JobId), typeof(UniverseId) }, methods[1].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "jobId", "universeId" }, methods[1].GetParameters().Select(p => p.Name));
        Assert.Equal(new[] { typeof(JobStateRecord), typeof(JobState) }, methods[2].GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "current", "nextState" }, methods[2].GetParameters().Select(p => p.Name));
    }

    public static IEnumerable<object[]> Matrix()
    {
        var normal = new HashSet<(JobState, JobState)>
        {
            (JobState.Detected, JobState.Staged), (JobState.Staged, JobState.Validated),
            (JobState.Validated, JobState.Planned), (JobState.Planned, JobState.Audited),
            (JobState.Audited, JobState.Executed), (JobState.Executed, JobState.Verified),
            (JobState.Verified, JobState.Completed)
        };
        foreach (var (from, fromToken) in Vocabulary)
            foreach (var (to, toToken) in Vocabulary)
                yield return [from, to, normal.Contains((from, to)) ||
                    (from is not (JobState.Completed or JobState.Failed) && to == JobState.Failed), fromToken, toToken];
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void All81TransitionsEnforceNormalEdgesFailureAndTerminality(JobState from, JobState to, bool allowed, string fromToken, string toToken)
    {
        var current = new JobStateRecord(new JobId("job_00112233445566778899aabbccddeeff"), new UniverseId("nimroel"), from);
        Assert.Equal(allowed, _machine.CanTransition(from, to));
        if (allowed)
        {
            var next = _machine.Transition(current, to);
            Assert.NotSame(current, next);
            Assert.Same(current.JobId, next.JobId);
            Assert.Same(current.UniverseId, next.UniverseId);
            Assert.Equal(to, next.State);
        }
        else Assert.Equal($"Invalid Job state transition from '{fromToken}' to '{toToken}'.",
            Assert.Throws<InvalidOperationException>(() => _machine.Transition(current, to)).Message);
        Assert.Equal(from, current.State);
    }

    [Fact]
    public void CreateStartsDetectedAndRetainsExactIdentities()
    {
        var job = new JobId("job_00112233445566778899aabbccddeeff");
        var universe = new UniverseId("nimroel");
        var result = _machine.Create(job, universe);
        Assert.Equal(JobState.Detected, result.State);
        Assert.Same(job, result.JobId);
        Assert.Same(universe, result.UniverseId);
    }

    [Fact]
    public void NullValidationPrecedesEnumAndUsesExactParameters()
    {
        var job = JobId.Create();
        var universe = new UniverseId("nimroel");
        Assert.Equal("jobId", Assert.Throws<ArgumentNullException>(() => _machine.Create(null!, null!)).ParamName);
        Assert.Equal("jobId", Assert.Throws<ArgumentNullException>(() => _machine.Create(null!, universe)).ParamName);
        Assert.Equal("universeId", Assert.Throws<ArgumentNullException>(() => _machine.Create(job, null!)).ParamName);
        Assert.Equal("current", Assert.Throws<ArgumentNullException>(() => _machine.Transition(null!, (JobState)(-1))).ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void UndefinedEnumsAreRejectedBeforeClassifyingTransitions(int value)
    {
        var bad = (JobState)value;
        Assert.Equal("from", Assert.Throws<ArgumentOutOfRangeException>(() => _machine.CanTransition(bad, bad)).ParamName);
        Assert.Equal("to", Assert.Throws<ArgumentOutOfRangeException>(() => _machine.CanTransition(JobState.Completed, bad)).ParamName);
        var current = _machine.Create(JobId.Create(), new UniverseId("nimroel"));
        Assert.Equal("nextState", Assert.Throws<ArgumentOutOfRangeException>(() => _machine.Transition(current, bad)).ParamName);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void FullMatrixAndMessagesAreCultureIndependent(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var row in Matrix())
                All81TransitionsEnforceNormalEdgesFailureAndTerminality((JobState)row[0], (JobState)row[1], (bool)row[2], (string)row[3], (string)row[4]);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
