-- Schema v1: exact DDL from CatalogSchema.cs. Documentation only; UniverseId metadata is inserted with a bound parameter by the runtime.
PRAGMA foreign_keys=ON;
PRAGMA journal_mode=DELETE;
PRAGMA synchronous=FULL;

CREATE TABLE catalog_metadata (singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL, universe_id TEXT COLLATE BINARY NOT NULL UNIQUE);

CREATE TABLE assets (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, asset_type TEXT COLLATE BINARY NOT NULL, production_profile TEXT COLLATE BINARY NOT NULL, lifecycle TEXT NOT NULL CHECK(lifecycle='physically_verified'), audit_state TEXT CHECK(audit_state IS NULL OR audit_state='PASS'), master_sha256 TEXT NOT NULL CHECK(length(master_sha256)=64), master_size INTEGER NOT NULL CHECK(master_size>0), production_sha256 TEXT NOT NULL CHECK(length(production_sha256)=64), production_size INTEGER NOT NULL CHECK(production_size>0), archive_directory TEXT COLLATE BINARY NOT NULL, production_directory TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,asset_id), UNIQUE(universe_id,production_directory), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id));

CREATE TABLE classifications (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, dimension TEXT COLLATE BINARY NOT NULL, value TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,asset_id,dimension), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE);

CREATE TABLE files (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, location TEXT COLLATE BINARY NOT NULL CHECK(location IN ('Archive','Production')), role TEXT COLLATE BINARY NOT NULL, kind TEXT NOT NULL, relative_path TEXT COLLATE BINARY NOT NULL, sha256 TEXT NOT NULL CHECK(length(sha256)=64), size INTEGER NOT NULL CHECK(size>=0), verified INTEGER NOT NULL CHECK(verified=1), PRIMARY KEY(universe_id,asset_id,location,role), UNIQUE(universe_id,location,relative_path), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE);

CREATE TABLE documents (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, location TEXT NOT NULL CHECK(location='Production'), role TEXT COLLATE BINARY NOT NULL, content BLOB NOT NULL, PRIMARY KEY(universe_id,asset_id,location,role), FOREIGN KEY(universe_id,asset_id,location,role) REFERENCES files(universe_id,asset_id,location,role) ON DELETE CASCADE);

CREATE TABLE visual_traits (universe_id TEXT COLLATE BINARY NOT NULL, asset_id TEXT COLLATE BINARY NOT NULL, trait_key TEXT COLLATE BINARY NOT NULL, trait_value TEXT COLLATE BINARY NOT NULL, scalar_type TEXT NOT NULL CHECK(scalar_type IN ('String','Number','Boolean')), PRIMARY KEY(universe_id,asset_id,trait_key), FOREIGN KEY(universe_id,asset_id) REFERENCES assets(universe_id,asset_id) ON DELETE CASCADE);

CREATE TABLE objectives (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, name TEXT NOT NULL, target_count INTEGER NOT NULL CHECK(target_count>0), asset_id TEXT COLLATE BINARY, asset_type TEXT COLLATE BINARY, production_profile TEXT COLLATE BINARY, PRIMARY KEY(universe_id,objective_id), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id));

CREATE TABLE objective_classifications (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, dimension TEXT COLLATE BINARY NOT NULL, value TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,objective_id,dimension), FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id) ON DELETE CASCADE);

CREATE TABLE objective_traits (universe_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, trait_key TEXT COLLATE BINARY NOT NULL, trait_value TEXT COLLATE BINARY NOT NULL, scalar_type TEXT NOT NULL CHECK(scalar_type IN ('String','Number','Boolean')), PRIMARY KEY(universe_id,objective_id,trait_key,trait_value,scalar_type), FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id) ON DELETE CASCADE);

CREATE TABLE campaigns (universe_id TEXT COLLATE BINARY NOT NULL, campaign_id TEXT COLLATE BINARY NOT NULL, name TEXT NOT NULL, PRIMARY KEY(universe_id,campaign_id), FOREIGN KEY(universe_id) REFERENCES catalog_metadata(universe_id));

CREATE TABLE campaign_objectives (universe_id TEXT COLLATE BINARY NOT NULL, campaign_id TEXT COLLATE BINARY NOT NULL, objective_id TEXT COLLATE BINARY NOT NULL, PRIMARY KEY(universe_id,campaign_id,objective_id), FOREIGN KEY(universe_id,campaign_id) REFERENCES campaigns(universe_id,campaign_id) ON DELETE CASCADE, FOREIGN KEY(universe_id,objective_id) REFERENCES objectives(universe_id,objective_id));

CREATE INDEX ix_assets_type_profile ON assets(universe_id,asset_type,production_profile,asset_id);

CREATE INDEX ix_assets_profile ON assets(universe_id,production_profile,asset_id);

CREATE INDEX ix_classification_selection ON classifications(universe_id,dimension,value,asset_id);

CREATE INDEX ix_trait_selection ON visual_traits(universe_id,trait_key,trait_value,scalar_type,asset_id);

CREATE INDEX ix_campaign_objective ON campaign_objectives(universe_id,objective_id,campaign_id);

PRAGMA user_version=1;
