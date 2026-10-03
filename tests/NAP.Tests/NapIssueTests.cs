using NAP.Core;
using Xunit;

namespace NAP.Tests;

public sealed class NapIssueTests
{
    [Fact]
    public void CentralCodes_AreStableUniqueNamingV1Identifiers()
    {
        (string Actual, string Expected)[] codes =
        [
            (NapIssueCodes.InboxMissing, "inbox_missing"),
            (NapIssueCodes.InboxChanging, "inbox_changing"),
            (NapIssueCodes.InboxInUse, "inbox_in_use"),
            (NapIssueCodes.StagingNotReady, "staging_not_ready"),
            (NapIssueCodes.StagingCollision, "staging_collision"),
            (NapIssueCodes.ZipCollision, "zip_collision"),
            (NapIssueCodes.ZipInvalidArchive, "zip_invalid_archive"),
            (NapIssueCodes.ZipRejected, "zip_rejected"),
            (NapIssueCodes.PngInvalid, "png_invalid"),
            (NapIssueCodes.PngUnsupportedFeature, "png_unsupported_feature"),
            (NapIssueCodes.UniverseStorageOverlap, "universe_storage_overlap"),
            (NapIssueCodes.PackageRootInvalid, "package_root_invalid"),
            (NapIssueCodes.PackageStructureInvalid, "package_structure_invalid"),
            (NapIssueCodes.PackageManifestMissing, "package_manifest_missing"),
            (NapIssueCodes.PackageManifestAmbiguous, "package_manifest_ambiguous"),
            (NapIssueCodes.PackageManifestInvalid, "package_manifest_invalid"),
            (NapIssueCodes.PackageManifestFilenameMismatch, "package_manifest_filename_mismatch"),
            (NapIssueCodes.PackageRootNameMismatch, "package_root_name_mismatch"),
            (NapIssueCodes.PackageUniverseMismatch, "package_universe_mismatch"),
            (NapIssueCodes.PackageRuleNotFound, "package_rule_not_found"),
            (NapIssueCodes.PackageClassificationInvalid, "package_classification_invalid"),
            (NapIssueCodes.PackageRequiredFileMissing, "package_required_file_missing"),
            (NapIssueCodes.PackageUnexpectedFile, "package_unexpected_file"),
            (NapIssueCodes.PackageContentValidatorUnsupported, "package_content_validator_unsupported"),
            (NapIssueCodes.ProductionRootMissing, "production_root_missing"),
            (NapIssueCodes.ProductionRootInvalid, "production_root_invalid"),
            (NapIssueCodes.ProductionRootReparse, "production_root_reparse")
        ];
        Assert.Equal(codes.Length, codes.Select(pair => pair.Actual).Distinct(StringComparer.Ordinal).Count());
        foreach (var (actual, expected) in codes)
        {
            Assert.Equal(expected, actual);
            Assert.True(AssetNamingRules.IsValidMachineIdentifier(actual));
            Assert.DoesNotContain(' ', actual);
        }
    }

    [Fact]
    public void Context_IsOptionalAndPreservedVerbatim()
    {
        var issue = new NapIssue("future_issue", NapIssueSeverity.Info, NapIssueDisposition.Continue, "Fallback.");
        Assert.Null(issue.SubjectPath);
        Assert.Null(issue.Detail);
        const string path = "  relative/../missing.png  ";
        const string detail = "Technical detail.\nSecond line.";
        var contextual = new NapIssue(issue.Code, issue.Severity, issue.Disposition, issue.Message, path, detail);
        Assert.Equal(path, contextual.SubjectPath);
        Assert.Equal(detail, contextual.Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Png_Invalid")]
    [InlineData("png-invalid")]
    [InlineData("png__invalid")]
    [InlineData("png_invalid\n")]
    public void InvalidCode_IsRejected(string? code) =>
        Assert.Throws<ArgumentException>(() => new NapIssue(code!, NapIssueSeverity.Error, NapIssueDisposition.Stop, "Message."));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingFallbackMessage_IsRejected(string? message) =>
        Assert.ThrowsAny<ArgumentException>(() => new NapIssue(NapIssueCodes.PngInvalid, NapIssueSeverity.Error, NapIssueDisposition.Stop, message!));

    [Fact]
    public void UndefinedSeverityOrDisposition_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NapIssue(NapIssueCodes.PngInvalid, (NapIssueSeverity)99, NapIssueDisposition.Stop, "Message."));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NapIssue(NapIssueCodes.PngInvalid, NapIssueSeverity.Error, (NapIssueDisposition)99, "Message."));
    }
}
