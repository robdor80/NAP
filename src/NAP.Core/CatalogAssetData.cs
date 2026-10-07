using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogAssetData
{
    internal static CatalogAssetSnapshot WithAudit(CatalogAssetSnapshot asset, string? audit) => new(asset.AssetKey, asset.AssetType, asset.ProductionProfile,
        asset.ProductionRelativeDirectory, asset.MasterDigest, asset.MasterSizeBytes, asset.ProductionDigest, asset.ProductionSizeBytes,
        asset.Classification, asset.Files, asset.Documents, asset.Traits, audit);
    internal static bool Equivalent(IReadOnlyList<CatalogAssetSnapshot> first, IReadOnlyList<CatalogAssetSnapshot> second) =>
        first.Count == second.Count && first.Zip(second).All(p => Equivalent(p.First, p.Second));
    // Compare reconstructible facts. Audit knowledge may be absent in an imported snapshot without contradicting a real PASS.
    internal static bool Equivalent(CatalogAssetSnapshot a, CatalogAssetSnapshot b) => a.AssetKey == b.AssetKey && a.AssetType == b.AssetType &&
        a.ProductionProfile == b.ProductionProfile && a.ArchiveRelativeDirectory == b.ArchiveRelativeDirectory && a.ProductionRelativeDirectory == b.ProductionRelativeDirectory &&
        a.MasterDigest == b.MasterDigest && a.MasterSizeBytes == b.MasterSizeBytes && a.ProductionDigest == b.ProductionDigest && a.ProductionSizeBytes == b.ProductionSizeBytes &&
        a.Classification.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(b.Classification.OrderBy(p => p.Key, StringComparer.Ordinal)) &&
        a.Files.SequenceEqual(b.Files) && a.Traits.SequenceEqual(b.Traits) && a.Documents.Count == b.Documents.Count &&
        a.Documents.Zip(b.Documents).All(p => p.First.Role == p.Second.Role && p.First.ToArray().AsSpan().SequenceEqual(p.Second.ToArray()));

    internal static bool InsertOrVerify(SqliteConnection connection, SqliteTransaction transaction, CatalogAssetSnapshot asset)
    {
        var existing = Read(connection, transaction, asset.AssetKey.UniverseId, asset.AssetKey.AssetId);
        if (existing is not null)
        {
            if (!Equivalent(existing, asset)) throw CatalogException.Stop(NapIssueCodes.CatalogAssetConflict, "The same asset identity already has incompatible catalog facts.");
            // Unknown imported audit metadata is not a conflicting decision. Only a supplied validated PASS can enrich it.
            if (existing.AuditState is null && asset.AuditState == "PASS")
                CatalogSql.Execute(connection, transaction, "UPDATE assets SET audit_state=$s WHERE universe_id=$u AND asset_id=$a", "$s", asset.AuditState, "$u", asset.AssetKey.UniverseId.Value, "$a", asset.AssetKey.AssetId);
            return false;
        }
        var u = asset.AssetKey.UniverseId.Value; var id = asset.AssetKey.AssetId;
        CatalogSql.Execute(connection, transaction, "INSERT INTO assets VALUES($u,$a,$t,$p,'physically_verified',$audit,$mh,$ms,$ph,$ps,$ad,$pd)",
            "$u", u, "$a", id, "$t", asset.AssetType, "$p", asset.ProductionProfile, "$mh", asset.MasterDigest.Hex, "$ms", asset.MasterSizeBytes,
            "$ph", asset.ProductionDigest.Hex, "$ps", asset.ProductionSizeBytes, "$ad", asset.ArchiveRelativeDirectory, "$pd", asset.ProductionRelativeDirectory, "$audit", asset.AuditState);
        foreach (var (dimension, value) in asset.Classification)
            CatalogSql.Execute(connection, transaction, "INSERT INTO classifications VALUES($u,$a,$d,$v)", "$u", u, "$a", id, "$d", dimension, "$v", value);
        foreach (var f in asset.Files)
            CatalogSql.Execute(connection, transaction, "INSERT INTO files VALUES($u,$a,$l,$r,$k,$p,$h,$s,1)", "$u", u, "$a", id, "$l", f.Location.ToString(), "$r", f.Role,
                "$k", f.Kind, "$p", f.RelativePath, "$h", f.Digest.Hex, "$s", f.SizeBytes);
        foreach (var d in asset.Documents)
            CatalogSql.Execute(connection, transaction, "INSERT INTO documents VALUES($u,$a,'Production',$r,$b)", "$u", u, "$a", id, "$r", d.Role, "$b", d.ToArray());
        foreach (var t in asset.Traits)
            CatalogSql.Execute(connection, transaction, "INSERT INTO visual_traits VALUES($u,$a,$k,$v,$t)", "$u", u, "$a", id, "$k", t.Key, "$v", t.Value, "$t", t.Type.ToString());
        return true;
    }

    internal static CatalogAssetSnapshot? Read(SqliteConnection connection, SqliteTransaction? transaction, UniverseId universe, string id)
    {
        string type, profile, relative; string? audit; Sha256Digest master, production; long masterSize, productionSize;
        using (var command = CatalogSql.Command(connection, transaction, "SELECT asset_type,production_profile,archive_directory,production_directory,master_sha256,master_size,production_sha256,production_size,lifecycle,audit_state FROM assets WHERE universe_id=$u AND asset_id=$a", "$u", universe.Value, "$a", id))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            type = reader.GetString(0); profile = reader.GetString(1); relative = reader.GetString(2);
            audit = reader.IsDBNull(9) ? null : reader.GetString(9);
            if (reader.GetString(3) != relative || reader.GetString(8) != "physically_verified" || (audit is not null && audit != "PASS")) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Incompatible asset-derived catalog invariants.");
            master = new(reader.GetString(4)); masterSize = reader.GetInt64(5); production = new(reader.GetString(6)); productionSize = reader.GetInt64(7);
        }
        var classification = new Dictionary<string, string>(StringComparer.Ordinal); var files = new List<CatalogFile>(); var documents = new List<CatalogDocument>(); var traits = new List<CatalogVisualTrait>();
        using (var command = CatalogSql.Command(connection, transaction, "SELECT dimension,value FROM classifications WHERE universe_id=$u AND asset_id=$a", "$u", universe.Value, "$a", id))
        using (var reader = command.ExecuteReader()) while (reader.Read()) classification.Add(reader.GetString(0), reader.GetString(1));
        using (var command = CatalogSql.Command(connection, transaction, "SELECT location,role,kind,relative_path,sha256,size,verified FROM files WHERE universe_id=$u AND asset_id=$a", "$u", universe.Value, "$a", id))
        using (var reader = command.ExecuteReader()) while (reader.Read()) files.Add(new(Enum.Parse<CatalogFileLocation>(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), new(reader.GetString(4)), reader.GetInt64(5), reader.GetInt64(6) == 1));
        using (var command = CatalogSql.Command(connection, transaction, "SELECT role,content FROM documents WHERE universe_id=$u AND asset_id=$a", "$u", universe.Value, "$a", id))
        using (var reader = command.ExecuteReader()) while (reader.Read()) documents.Add(new(reader.GetString(0), (byte[])reader.GetValue(1)));
        using (var command = CatalogSql.Command(connection, transaction, "SELECT trait_key,trait_value,scalar_type FROM visual_traits WHERE universe_id=$u AND asset_id=$a", "$u", universe.Value, "$a", id))
        using (var reader = command.ExecuteReader()) while (reader.Read()) traits.Add(new(reader.GetString(0), reader.GetString(1), Enum.Parse<CatalogTraitType>(reader.GetString(2))));
        return new(new UniverseAssetKey(universe, id), type, profile, relative, master, masterSize, production, productionSize, classification, files, documents, traits, audit);
    }

    internal static void ValidateLogical(SqliteConnection connection, UniverseContext context)
    {
        var ids = new List<string>();
        using (var command = CatalogSql.Command(connection, null, "SELECT asset_id FROM assets WHERE universe_id=$u", "$u", context.Id.Value))
        using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0));
        foreach (var id in ids)
        {
            var asset = Read(connection, null, context.Id, id)!;
            ValidateAsset(asset, context);
        }
    }

    internal static void ValidateAsset(CatalogAssetSnapshot asset, UniverseContext context)
    {
        var id = asset.AssetKey.AssetId;
        ProductionPaths.Resolve(context.Storage.ProductionRoot, asset.ProductionRelativeDirectory);
        ProductionPaths.Resolve(context.Storage.ArchiveRoot, asset.ArchiveRelativeDirectory);
        if (!AssetNamingRules.MatchesAssetType(id, asset.AssetType) || !AssetNamingRules.IsValidMachineIdentifier(asset.ProductionProfile)) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Invalid catalog asset identifiers.");
        foreach (var file in asset.Files)
        {
            ProductionPaths.Resolve(file.Location == CatalogFileLocation.Production ? context.Storage.ProductionRoot : context.Storage.ArchiveRoot, file.RelativePath);
            if (!file.Verified || !file.RelativePath.StartsWith(asset.ProductionRelativeDirectory + "/", StringComparison.Ordinal) ||
                file.RelativePath[(asset.ProductionRelativeDirectory.Length + 1)..].Contains('/')) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Invalid logical file location.");
        }
        var master = asset.Files.SingleOrDefault(f => f.Location == CatalogFileLocation.Archive && f.Kind == "master");
        var output = asset.Files.SingleOrDefault(f => f.Location == CatalogFileLocation.Production && f.Kind == "generated_webp");
        if (master is null || output is null || master.Digest != asset.MasterDigest || master.SizeBytes != asset.MasterSizeBytes ||
            output.Digest != asset.ProductionDigest || output.SizeBytes != asset.ProductionSizeBytes || asset.Documents.Count != asset.Files.Count(f => f.Location == CatalogFileLocation.Production && f.Kind == "document"))
            throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "Asset/file/document fingerprints are inconsistent.");
        foreach (var document in asset.Documents)
        {
            var file = asset.Files.Single(f => f.Location == CatalogFileLocation.Production && f.Role == document.Role);
            using var bytes = new MemoryStream(document.ToArray(), writable: false);
            if (bytes.Length != file.SizeBytes || new Sha256Hasher().Compute(bytes) != file.Digest) throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "A document no longer matches its preserved bytes fingerprint.");
        }
    }
}
