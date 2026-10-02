namespace NAP.Core;

/// <summary>Stable machine identities; human messages may change independently.</summary>
public static class NapIssueCodes
{
    public const string InboxMissing = "inbox_missing";
    public const string InboxChanging = "inbox_changing";
    public const string InboxInUse = "inbox_in_use";
    public const string StagingNotReady = "staging_not_ready";
    public const string StagingCollision = "staging_collision";
    public const string ZipCollision = "zip_collision";
    public const string ZipInvalidArchive = "zip_invalid_archive";
    public const string ZipRejected = "zip_rejected";
    public const string PngInvalid = "png_invalid";
    public const string PngUnsupportedFeature = "png_unsupported_feature";
}
