using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class NapIssueMapperTests
{
    [Theory]
    [InlineData(InboxPackageReadinessStatus.Ready, null)]
    [InlineData(InboxPackageReadinessStatus.Missing, NapIssueCodes.InboxMissing)]
    [InlineData(InboxPackageReadinessStatus.Changing, NapIssueCodes.InboxChanging)]
    [InlineData(InboxPackageReadinessStatus.InUse, NapIssueCodes.InboxInUse)]
    public void Readiness_MapsKnownOutcomes(InboxPackageReadinessStatus status, string? code)
    {
        var result = new InboxPackageReadinessResult(status);
        AssertMapping(NapIssueMapper.Map(result, "inbox/asset.zip"), code,
            NapIssueSeverity.Warning, "inbox/asset.zip", null);
        if (code is not null)
            Assert.Null(NapIssueMapper.Map(result)!.SubjectPath);
    }

    [Theory]
    [InlineData(InboxPackageStagingStatus.Staged, null, NapIssueSeverity.Info, null)]
    [InlineData(InboxPackageStagingStatus.NotReady, NapIssueCodes.StagingNotReady, NapIssueSeverity.Warning, "inbox/asset.zip")]
    [InlineData(InboxPackageStagingStatus.Collision, NapIssueCodes.StagingCollision, NapIssueSeverity.Error, "staging/asset.zip")]
    public void Staging_MapsKnownOutcomes(InboxPackageStagingStatus status, string? code,
        NapIssueSeverity severity, string? path)
    {
        var result = new InboxPackageStagingResult(status, "inbox/asset.zip", "staging/asset.zip",
            InboxPackageReadinessStatus.Changing);
        AssertMapping(NapIssueMapper.Map(result), code, severity, path, null);
    }

    [Theory]
    [InlineData(StagedPackageExtractionStatus.Extracted, null)]
    [InlineData(StagedPackageExtractionStatus.Collision, NapIssueCodes.ZipCollision)]
    [InlineData(StagedPackageExtractionStatus.InvalidArchive, NapIssueCodes.ZipInvalidArchive)]
    [InlineData(StagedPackageExtractionStatus.Rejected, NapIssueCodes.ZipRejected)]
    public void Extraction_MapsKnownOutcomesAndPreservesReason(StagedPackageExtractionStatus status, string? code)
    {
        var result = new StagedPackageExtractionResult(status, "staging/asset.zip", Reason: "Technical reason.");
        AssertMapping(NapIssueMapper.Map(result), code, NapIssueSeverity.Error,
            "staging/asset.zip", "Technical reason.");
        if (code is not null)
        {
            Assert.Null(NapIssueMapper.Map(result with { Reason = null })!.Detail);
            Assert.Equal("published/asset", NapIssueMapper.Map(result with { FinalPath = "published/asset" })!.SubjectPath);
        }
    }

    [Theory]
    [InlineData(PngValidationStatus.Valid, null)]
    [InlineData(PngValidationStatus.Invalid, NapIssueCodes.PngInvalid)]
    [InlineData(PngValidationStatus.UnsupportedFeature, NapIssueCodes.PngUnsupportedFeature)]
    public void Png_MapsKnownOutcomesAndPreservesContext(PngValidationStatus status, string? code)
    {
        var result = new PngValidationResult(status, Reason: "Technical reason.");
        const string path = "relative/../asset.png";
        AssertMapping(NapIssueMapper.Map(result, path), code, NapIssueSeverity.Error, path, "Technical reason.");
        if (code is not null)
        {
            Assert.Null(NapIssueMapper.Map(result)!.SubjectPath);
            Assert.Null(NapIssueMapper.Map(result with { Reason = null })!.Detail);
        }
    }

    [Fact]
    public void NullResults_AreProgrammingErrors()
    {
        Assert.Throws<ArgumentNullException>(() => NapIssueMapper.Map((InboxPackageReadinessResult)null!));
        Assert.Throws<ArgumentNullException>(() => NapIssueMapper.Map((InboxPackageStagingResult)null!));
        Assert.Throws<ArgumentNullException>(() => NapIssueMapper.Map((StagedPackageExtractionResult)null!));
        Assert.Throws<ArgumentNullException>(() => NapIssueMapper.Map((PngValidationResult)null!));
    }

    [Fact]
    public void UnknownStatuses_AreProgrammingErrorsRatherThanSuccess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NapIssueMapper.Map(new InboxPackageReadinessResult((InboxPackageReadinessStatus)99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NapIssueMapper.Map(new InboxPackageStagingResult((InboxPackageStagingStatus)99, "source", "destination")));
        Assert.Throws<ArgumentOutOfRangeException>(() => NapIssueMapper.Map(new StagedPackageExtractionResult((StagedPackageExtractionStatus)99, "source")));
        Assert.Throws<ArgumentOutOfRangeException>(() => NapIssueMapper.Map(new PngValidationResult((PngValidationStatus)99)));
    }

    private static void AssertMapping(NapIssue? issue, string? code, NapIssueSeverity severity,
        string? path, string? detail)
    {
        if (code is null)
        {
            Assert.Null(issue);
            return;
        }
        Assert.NotNull(issue);
        Assert.Equal(code, issue.Code);
        Assert.Equal(severity, issue.Severity);
        Assert.Equal(NapIssueDisposition.Stop, issue.Disposition);
        Assert.True(issue.StopsProcessing);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        Assert.Equal(path, issue.SubjectPath);
        Assert.Equal(detail, issue.Detail);
    }
}
