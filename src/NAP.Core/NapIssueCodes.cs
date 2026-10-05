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
    public const string UniverseStorageOverlap = "universe_storage_overlap";
    public const string PackageRootInvalid = "package_root_invalid";
    public const string PackageStructureInvalid = "package_structure_invalid";
    public const string PackageManifestMissing = "package_manifest_missing";
    public const string PackageManifestAmbiguous = "package_manifest_ambiguous";
    public const string PackageManifestInvalid = "package_manifest_invalid";
    public const string PackageManifestFilenameMismatch = "package_manifest_filename_mismatch";
    public const string PackageRootNameMismatch = "package_root_name_mismatch";
    public const string PackageUniverseMismatch = "package_universe_mismatch";
    public const string PackageRuleNotFound = "package_rule_not_found";
    public const string PackageClassificationInvalid = "package_classification_invalid";
    public const string PackageRequiredFileMissing = "package_required_file_missing";
    public const string PackageUnexpectedFile = "package_unexpected_file";
    public const string PackageContentValidatorUnsupported = "package_content_validator_unsupported";
    public const string ProductionRootMissing = "production_root_missing";
    public const string ProductionRootInvalid = "production_root_invalid";
    public const string ProductionRootReparse = "production_root_reparse";
    public const string RepositoryEntryReparse = "repository_entry_reparse";
    public const string PlanDestinationCasingConflict = "plan_destination_casing_conflict";
    public const string PlanDestinationBlocked = "plan_destination_blocked";
    public const string PlanDestinationExists = "plan_destination_exists";
    public const string PortraitInputTooLarge = "portrait_input_too_large";
    public const string PortraitAspectRatioMismatch = "portrait_aspect_ratio_mismatch";
    public const string PortraitDecodeFailed = "portrait_decode_failed";
    public const string PortraitOutputMetadataMismatch = "portrait_output_metadata_mismatch";
    public const string PortraitOutputInvalidWebp = "portrait_output_invalid_webp";
    public const string PortraitOutputDimensionsMismatch = "portrait_output_dimensions_mismatch";
    public const string SceneInputTooLarge = "scene_input_too_large";
    public const string SceneAspectRatioMismatch = "scene_aspect_ratio_mismatch";
    public const string SceneDecodeFailed = "scene_decode_failed";
    public const string SceneOutputMetadataMismatch = "scene_output_metadata_mismatch";
    public const string SceneOutputInvalidWebp = "scene_output_invalid_webp";
    public const string SceneOutputDimensionsMismatch = "scene_output_dimensions_mismatch";
    public const string ImageConversionAspectRatioMismatch = "image_conversion_aspect_ratio_mismatch";
}
