using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class JobStateTests
{
    [Fact]
    public void PublicEnumHasExactlyNineCanonicalNamesAndExplicitValues()
    {
        Assert.True(typeof(JobState).IsPublic);
        Assert.True(typeof(JobState).IsEnum);
        Assert.Equal(new[] { "Detected", "Staged", "Validated", "Planned", "Audited", "Executed", "Verified", "Completed", "Failed" },
            Enum.GetNames<JobState>());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, Enum.GetValues<JobState>().Select(state => (int)state));
    }
}
