using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogSchema
{
    internal const int Version = 1;
    internal static readonly IReadOnlyDictionary<string, string> Objects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["catalog_metadata"] = "CREATE TABLE catalog_metadata (singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL, universe_id TEXT COLLATE BINARY NOT NULL UNIQUE)",
        ["assets"] = "CREATE TABLE assets (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, asset_type TEXT COLLATE BINARY NOT NULL, production_profile TEXT COLLATE BINARY NOT NULL, lifecycle TEXT NOT NULL CHECK(lifecycle='physically_verified'), audit_state TEXT CHECK(audit_state IS NULL OR audit_state='PASS'), master_sha256 TEXT NOT NULL CHECK(length(master_sha256)=64), master_size INTEGER NOT NULL CHECK(master_size>0), production_sha256 TEXT NOT NULL CHECK(length(production_sha256)=64), production_size INTEGER NOT NULL CHECK(production_size>0), archive_directory TEXT COLLATE BINARY NOT NULL, production_directory TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,asset_id), UNIQUE(universe_id,production_directory), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id))",
        ["classifications"] = "CREATE TABLE classifications (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, dimension TEXT COLLATE BINARY NOT NULL, value TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,asset_id,dimension), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE)",
        ["files"] = "CREATE TABLE files (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, location TEXT COLLATE BINARY NOT NULL CHECK(location IN ('Archive','Production')), role TEXT COLLATE BINARY NOT NULL, kind TEXT NOT NULL, relative_path TEXT COLLATE BINARY NOT NULL, sha256 TEXT NOT NULL CHECK(length(sha256)=64), size INTEGER NOT NULL CHECK(size>=0), verified INTEGER NOT NULL CHECK(verified=1), PRIMARY KEY(universe_id,asset_id,location,role), UNIQUE(universe_id,location,relative_path), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE)",
        ["documents"] = "CREATE TABLE documents (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, location TEXT NOT NULL CHECK(location='Production'), role TEXT COLLATE BINARY NOT NULL, content BLOB NOT NULL, PRIMARY KEY(universe_id,asset_id,location,role), FOREIGN KEY(universe_id,asset_id,location,role) REFERENCES files(universe_id,asset_id,location,role) ON DELETE CASCADE)",
        ["visual_traits"] = "CREATE TABLE visual_traits (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, trait_key TEXT COLLATE BINARY NOT NULL, trait_value TEXT COLLATE BINARY NOT NULL, scalar_type TEXT NOT NULL CHECK(scalar_type IN ('String','Number','Boolean')), PRIMARY KEY(universe_id,asset_id,trait_key), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE)",
        ["objectives"] = "CREATE TABLE objectives (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, name TEXT NOT NULL, target_count INTEGER NOT NULL CHECK(target_count>0), asset_id TEXT COLLATE BINARY, asset_type TEXT COLLATE BINARY, production_profile TEXT COLLATE BINARY, PRIMARY KEY(universe_id,objective_id), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id))",
        ["objective_classifications"] = "CREATE TABLE objective_classifications (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, dimension TEXT COLLATE BINARY NOT NULL, value TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,objective_id,dimension), FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id) ON DELETE CASCADE)",
        ["objective_traits"] = "CREATE TABLE objective_traits (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, trait_key TEXT COLLATE BINARY NOT NULL, trait_value TEXT COLLATE BINARY NOT NULL, scalar_type TEXT NOT NULL CHECK(scalar_type IN ('String','Number','Boolean')), PRIMARY KEY(universe_id,objective_id,trait_key,trait_value,scalar_type), FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id) ON DELETE CASCADE)",
        ["campaigns"] = "CREATE TABLE campaigns (universe_id TEXT COLLATE BINARY NOT NULL, campaign_id TEXT COLLATE BINARY NOT NULL, name TEXT NOT NULL, PRIMARY KEY(universe_id,campaign_id), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id))",
        ["campaign_objectives"] = "CREATE TABLE campaign_objectives (universe_id TEXT COLLATE BINARY NOT NULL, campaign_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,campaign_id,objective_id), FOREIGN KEY(universe_id,campaign_id) REFERENCES campaigns(universe_id,campaign_id) ON DELETE CASCADE, FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id))",
        ["ix_assets_type_profile"] = "CREATE INDEX ix_assets_type_profile ON assets(universe_id,asset_type,production_profile,asset_id)",
        ["ix_assets_profile"] = "CREATE INDEX ix_assets_profile ON assets(universe_id,production_profile,asset_id)",
        ["ix_classification_selection"] = "CREATE INDEX ix_classification_selection ON classifications(universe_id,dimension,value,asset_id)",
        ["ix_trait_selection"] = "CREATE INDEX ix_trait_selection ON visual_traits(universe_id,trait_key,trait_value,scalar_type,asset_id)",
        ["ix_campaign_objective"] = "CREATE INDEX ix_campaign_objective ON campaign_objectives(universe_id,objective_id,campaign_id)"
    };

    internal static void Create(SqliteConnection connection, UniverseId universe)
    {
        using var transaction = connection.BeginTransaction();
        foreach (var sql in Objects.Values) CatalogSql.Execute(connection, transaction, sql);
        CatalogSql.Execute(connection, transaction, "INSERT INTO catalog_metadata VALUES(1,$v,$u)", "$v", Version, "$u", universe.Value);
        CatalogSql.Execute(connection, transaction, "PRAGMA user_version=1");
        transaction.Commit();
    }

    internal static void Validate(SqliteConnection connection, UniverseId universe)
    {
        ValidateContract(connection, universe);
        Integrity(connection);
    }

    internal static void ValidateContract(SqliteConnection connection, UniverseId universe)
    {
        using (var command = CatalogSql.Command(connection, null, "SELECT schema_version,universe_id FROM catalog_metadata WHERE singleton=1"))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Catalog metadata is missing.");
            if (reader.GetInt64(0) != Version) throw CatalogException.Stop(NapIssueCodes.CatalogSchemaUnsupported, "Unknown catalog schema version.");
            if (reader.GetString(1) != universe.Value) throw CatalogException.Stop(NapIssueCodes.CatalogWrongUniverse, "The catalog belongs to another universe.");
            if (reader.Read()) throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "Ambiguous catalog metadata.");
        }
        if (Convert.ToInt64(CatalogSql.Scalar(connection, null, "PRAGMA user_version")) != Version)
            throw CatalogException.Stop(NapIssueCodes.CatalogSchemaUnsupported, "The SQLite schema version is unsupported.");
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var command = CatalogSql.Command(connection, null, "SELECT name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'"))
        using (var reader = command.ExecuteReader()) while (reader.Read()) found.Add(reader.GetString(0), reader.GetString(1));
        if (found.Count != Objects.Count || Objects.Any(o => !found.TryGetValue(o.Key, out var sql) || sql != o.Value))
            throw CatalogException.Stop(NapIssueCodes.CatalogInvalid, "The required schema, constraints or indexes differ from schema v1.");
    }

    internal static void Integrity(SqliteConnection connection)
    {
        using (var command = CatalogSql.Command(connection, null, "PRAGMA integrity_check"))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "SQLite integrity_check failed.");
        }
        using var foreign = CatalogSql.Command(connection, null, "PRAGMA foreign_key_check");
        using var violations = foreign.ExecuteReader();
        if (violations.Read()) throw CatalogException.Stop(NapIssueCodes.CatalogIntegrityFailed, "SQLite foreign_key_check failed.");
    }
}
