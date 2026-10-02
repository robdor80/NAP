namespace NAP.Core;

/// <summary>Adapts local outcomes without invoking components or performing I/O.</summary>
public static class NapIssueMapper
{
    public static NapIssue? Map(InboxPackageReadinessResult result, string? subjectPath = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status switch
        {
            InboxPackageReadinessStatus.Ready => null,
            InboxPackageReadinessStatus.Missing => Stop(NapIssueCodes.InboxMissing,
                NapIssueSeverity.Warning, "The Inbox package is missing.", subjectPath),
            InboxPackageReadinessStatus.Changing => Stop(NapIssueCodes.InboxChanging,
                NapIssueSeverity.Warning, "The Inbox package is still changing.", subjectPath),
            InboxPackageReadinessStatus.InUse => Stop(NapIssueCodes.InboxInUse,
                NapIssueSeverity.Warning, "The Inbox package is in use.", subjectPath),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown readiness status.")
        };
    }

    public static NapIssue? Map(InboxPackageStagingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status switch
        {
            InboxPackageStagingStatus.Staged => null,
            InboxPackageStagingStatus.NotReady => Stop(NapIssueCodes.StagingNotReady,
                NapIssueSeverity.Warning, "The Inbox package is not ready for staging.", result.SourcePath),
            InboxPackageStagingStatus.Collision => Stop(NapIssueCodes.StagingCollision,
                NapIssueSeverity.Error, "The staging destination already exists.", result.FinalStagedPath),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown staging status.")
        };
    }

    public static NapIssue? Map(StagedPackageExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var path = result.FinalPath ?? result.StagedZipPath;
        return result.Status switch
        {
            StagedPackageExtractionStatus.Extracted => null,
            StagedPackageExtractionStatus.Collision => Stop(NapIssueCodes.ZipCollision,
                NapIssueSeverity.Error, "The extraction destination already exists.", path, result.Reason),
            StagedPackageExtractionStatus.InvalidArchive => Stop(NapIssueCodes.ZipInvalidArchive,
                NapIssueSeverity.Error, "The ZIP archive is invalid.", path, result.Reason),
            StagedPackageExtractionStatus.Rejected => Stop(NapIssueCodes.ZipRejected,
                NapIssueSeverity.Error, "The ZIP archive was rejected by extraction policy.", path, result.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown extraction status.")
        };
    }

    public static NapIssue? Map(PngValidationResult result, string? subjectPath = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status switch
        {
            PngValidationStatus.Valid => null,
            PngValidationStatus.Invalid => Stop(NapIssueCodes.PngInvalid,
                NapIssueSeverity.Error, "The PNG structure is invalid.", subjectPath, result.Reason),
            PngValidationStatus.UnsupportedFeature => Stop(NapIssueCodes.PngUnsupportedFeature,
                NapIssueSeverity.Error, "The PNG uses an unsupported feature.", subjectPath, result.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unknown PNG status.")
        };
    }

    private static NapIssue Stop(string code, NapIssueSeverity severity, string message,
        string? path, string? detail = null) =>
        new(code, severity, NapIssueDisposition.Stop, message, path, detail);
}
