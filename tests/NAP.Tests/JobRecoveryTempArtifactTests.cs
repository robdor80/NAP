using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobRecoveryTempArtifactTests
{
    private static readonly JobId Id = new("job_00112233445566778899aabbccddeeff");
    private static readonly string Name = Id.Value + ".0123456789abcdef0123456789abcdef.tmp";

    [Fact]
    public void PublicContractIsSealedRecordWithExactlyThreeGetOnlyProperties()
    {
        var type = typeof(JobRecoveryTempArtifact);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.NotNull(type.GetMethod("<Clone>$"));
        var parameters = Assert.Single(type.GetConstructors()).GetParameters();
        Assert.Equal(new[] { "jobId", "fileName", "hasFinalJournal" }, parameters.Select(p => p.Name));
        Assert.Equal(new[] { typeof(JobId), typeof(string), typeof(bool) }, parameters.Select(p => p.ParameterType));
        var properties = type.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "FileName", "HasFinalJournal", "JobId" }, properties.Select(p => p.Name));
        Assert.Equal(new[] { typeof(string), typeof(bool), typeof(JobId) }, properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.Null(p.SetMethod));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanonicalNameRetainsInputsAndUsesValueEquality(bool hasFinal)
    {
        var artifact = new JobRecoveryTempArtifact(Id, Name, hasFinal);
        Assert.Same(Id, artifact.JobId);
        Assert.Same(Name, artifact.FileName);
        Assert.Equal(hasFinal, artifact.HasFinalJournal);
        var same = new JobRecoveryTempArtifact(new JobId(Id.Value), Name, hasFinal);
        Assert.Equal(artifact, same);
        Assert.True(artifact == same);
        Assert.Equal(artifact.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(artifact, new JobRecoveryTempArtifact(Id, Name, !hasFinal));
        Assert.NotEqual(artifact, new JobRecoveryTempArtifact(Id, Id.Value + "." + new string('0', 32) + ".tmp", hasFinal));
    }

    [Fact]
    public void NullJobIsRejectedFirst() => Assert.Equal("jobId",
        Assert.Throws<ArgumentNullException>(() => new JobRecoveryTempArtifact(null!, null!, false)).ParamName);

    public static IEnumerable<object?[]> InvalidNames()
    {
        foreach (var name in new string?[] { null, "", " ", "\t\r\n", "C:\\state\\" + Name,
            "/state/" + Name, "../" + Name, "..\\" + Name, "nested/" + Name, "nested\\" + Name,
            Name.ToUpperInvariant(), Name.Replace(".tmp", ".TMP"), Name.Replace("job_", "JOB_"),
            Id.Value + ".01234567-89ab-cdef-0123-456789abcdef.tmp", Id.Value + "." + new string('a', 31) + ".tmp",
            Id.Value + "." + new string('a', 33) + ".tmp", Id.Value + "." + new string('A', 32) + ".tmp",
            Id.Value + "." + new string('g', 32) + ".tmp", Id.Value + "." + new string('١', 32) + ".tmp",
            Id.Value + "/" + new string('a', 32) + ".tmp", "job_11112233445566778899aabbccddeeff.0123456789abcdef0123456789abcdef.tmp" })
            yield return new object?[] { name };
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void RejectsNonCanonicalNamesAndPaths(string? name) => Assert.Equal("fileName",
        Assert.Throws<ArgumentException>(() => new JobRecoveryTempArtifact(Id, name!, true)).ParamName);

    [Fact]
    public void TempSuffixIsHexPatternIncludingZeroNotAnAdditionalGuidSemanticRule() =>
        Assert.Equal(Id.Value + "." + new string('0', 32) + ".tmp",
            new JobRecoveryTempArtifact(Id, Id.Value + "." + new string('0', 32) + ".tmp", false).FileName);
}
