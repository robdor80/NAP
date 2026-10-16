using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NAP.Core;

/// <summary>Authenticated host read projections are display data, never native execution evidence.
/// Validated packages/plans are not deserialized. Mutations use server-owned opaque handles.</summary>
public static class AutomationReadViews
{
    private static readonly ConditionalWeakTable<object, object> Projections = new();
    private static T Mark<T>(T value) where T : class { Projections.Add(value, new()); return value; }
    public static void RequireNative(object value)
    { if (Projections.TryGetValue(value, out _)) throw new InvalidOperationException("A remote read projection cannot authorize a native mutation."); }
    private sealed class View<T>(Func<JsonElement, JsonSerializerOptions, T> read, Func<T, object>? write = null) : JsonConverter<T> where T : class
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        { using var doc = JsonDocument.ParseValue(ref reader); return Mark(read(doc.RootElement, options)); }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // Serialization only: read public getters. No reflection deserialization or nonpublic constructor discovery.
            var projection = write?.Invoke(value) ?? value.GetType().GetProperties().Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)
                .ToDictionary(p => p.Name, p => p.GetValue(value));
            JsonSerializer.Serialize(writer, projection, projection.GetType(), options);
        }
    }
    private static T P<T>(JsonElement value, string name, JsonSerializerOptions options) => value.GetProperty(name).Deserialize<T>(options)!;

    public static JsonSerializerOptions Json(UniverseContext context)
    {
        var options = new JsonSerializerOptions(AutomationProtocol.Json);
        options.Converters.Add(new View<AiAuditReport>((e, o) => new(P<AiAuditDecision>(e, "Decision", o), P<string>(e, "Summary", o), P<AiAuditFinding[]>(e, "Findings", o))));
        options.Converters.Add(new View<NapIssueReport>((e, o) => new(P<NapIssue[]>(e, "Issues", o))));
        options.Converters.Add(new View<CatalogFilter>((e, o) => new(P<string?>(e, "AssetId", o), P<string?>(e, "AssetType", o),
            P<string?>(e, "ProductionProfile", o), P<Dictionary<string, string>>(e, "Classification", o), P<CatalogVisualTrait[]>(e, "Traits", o))));
        options.Converters.Add(new View<CatalogCampaign>((e, o) => new(P<UniverseId>(e, "UniverseId", o), P<string>(e, "Id", o), P<string>(e, "Name", o), P<string[]>(e, "ObjectiveIds", o))));
        options.Converters.Add(new View<CatalogDocument>((e, o) => new(P<string>(e, "Role", o), P<byte[]>(e, "Bytes", o)), v => new { v.Role, Bytes = v.ToArray() }));
        options.Converters.Add(new View<CatalogAssetSnapshot>((e, o) => new(P<UniverseAssetKey>(e, "AssetKey", o), P<string>(e, "AssetType", o), P<string>(e, "ProductionProfile", o),
            P<string>(e, "ArchiveRelativeDirectory", o), P<Sha256Digest>(e, "MasterDigest", o), P<long>(e, "MasterSizeBytes", o), P<Sha256Digest>(e, "ProductionDigest", o),
            P<long>(e, "ProductionSizeBytes", o), P<Dictionary<string, string>>(e, "Classification", o), P<CatalogFile[]>(e, "Files", o), P<CatalogDocument[]>(e, "Documents", o),
            P<CatalogVisualTrait[]>(e, "Traits", o), P<string?>(e, "AuditState", o))));
        options.Converters.Add(new View<BackupManifest>((e, o) => new(P<BackupId>(e, "BackupId", o), P<UniverseId>(e, "UniverseId", o), P<BackupKind>(e, "Kind", o),
            P<DateTimeOffset>(e, "CreatedUtc", o), P<long>(e, "Size", o), P<Sha256Digest>(e, "Sha256", o), P<int?>(e, "CatalogSchemaVersion", o),
            P<DatabaseBackupPurpose?>(e, "DatabasePurpose", o), P<BackupFile[]>(e, "Files", o), P<string[]>(e, "Directories", o), P<string?>(e, "Head", o), P<bool?>(e, "Dirty", o))));
        options.Converters.Add(new View<BackupHistoryEntry>((e, o) => new(P<BackupManifest>(e, "Manifest", o), P<Sha256Digest>(e, "ManifestSha256", o)), v => new { v.Manifest, v.ManifestSha256 }));
        options.Converters.Add(new View<DatabaseRestorePlan>((e, o) => new(context, P<BackupHistoryEntry>(e, "Source", o), P<ActiveCatalogState>(e, "ActiveState", o), null, null)));
        options.Converters.Add(new View<BackupRetentionPlan>((e, o) => new(context, P<BackupRetentionPolicy>(e, "Policy", o), P<Sha256Digest>(e, "Inventory", o), P<BackupRetentionDecision[]>(e, "Decisions", o)),
            v => new { v.UniverseId, v.Policy, v.Decisions, v.Inventory }));
        options.Converters.Add(new View<CatalogRecoveryArtifact>((e, o) => new(P<CatalogRecoveryId>(e, "RecoveryId", o), P<UniverseId>(e, "UniverseId", o), P<DateTimeOffset>(e, "CreatedUtc", o), P<long>(e, "Size", o), P<Sha256Digest>(e, "Sha256", o))));
        options.Converters.Add(new View<GitChangeEntry>((e, o) => new(P<string>(e, "RelativePath", o), P<string?>(e, "OriginalRelativePath", o), P<char>(e, "IndexStatus", o),
            P<char>(e, "WorktreeStatus", o), P<GitChangeKind>(e, "ChangeKind", o), P<bool>(e, "IsSubmodule", o))));
        options.Converters.Add(new View<GitRepositoryStatus>((e, o) => new(P<UniverseId>(e, "UniverseId", o), P<string>(e, "RepositoryBinding", o), P<string>(e, "Head", o),
            P<string>(e, "Branch", o), P<string>(e, "Upstream", o), P<string>(e, "RemoteName", o), P<string>(e, "RemoteBranch", o), P<string>(e, "RemoteFingerprint", o),
            P<string?>(e, "RemoteTip", o), P<GitSynchronization>(e, "Synchronization", o), "", "", P<GitChangeEntry[]>(e, "Changes", o))));
        options.Converters.Add(new View<GitOwnedFile>((e, o) => new(P<string>(e, "RelativePath", o), P<Sha256Digest>(e, "Digest", o), P<long>(e, "SizeBytes", o))));
        options.Converters.Add(new View<GitCommitSelection>((e, o) => new(P<GitOwnedFile[]>(e, "OwnedFiles", o), P<string[]>(e, "Paths", o))));
        options.Converters.Add(new View<GitCommitPlan>((e, o) => new(P<GitRepositoryStatus>(e, "Status", o), P<GitCommitSelection>(e, "Selection", o), P<string>(e, "Message", o))));
        options.Converters.Add(new View<GitOperationReceipt>((e, o) => new(P<GitOperationId>(e, "OperationId", o), P<GitCommitPlan>(e, "Plan", o), P<GitReceiptState>(e, "State", o),
            commit: P<string?>(e, "CommitSha", o)), v => new { v.OperationId, v.Plan, v.State, v.CommitSha }));
        options.Converters.Add(new View<GitCommitResult>((e, o) => new(e, o)));
        options.Converters.Add(new View<ProductionAssetFile>((e, o) => new(e, o)));
        options.Converters.Add(new View<ProductionAssetResult>((e, o) => new(e, o)));
        options.Converters.Add(new View<ImageNormalizationPreview>((e, o) => new(P<byte[]>(e, "Original", o), P<byte[]>(e, "Candidate", o), P<ImageNormalizationGeometry>(e, "Geometry", o)),
            v => new { Original = v.GetOriginalPng(), Candidate = v.GetCandidatePng(), v.Geometry }));
        options.Converters.Add(new View<ImageNormalizationProposal>((e, o) => new(context, e, o)));
        options.Converters.Add(new View<ImageNormalizationResult>((e, o) => new(P<ImageNormalizationReceipt>(e, "Receipt", o), P<string>(e, "DirectoryPath", o), P<bool>(e, "AlreadyRecorded", o))));
        return options;
    }
}
