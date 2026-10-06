using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogOperations
{
    internal static void SaveObjective(SqliteConnection c, CatalogObjective objective)
    {
        using var t = c.BeginTransaction(); var u = objective.UniverseId.Value; var id = objective.Id; var f = objective.Filter;
        var exists = CatalogSql.Scalar(c, t, "SELECT 1 FROM objectives WHERE universe_id=$u AND objective_id=$id", "$u", u, "$id", id) is not null;
        CatalogSql.Execute(c, t, exists
            ? "UPDATE objectives SET name=$n,target_count=$count,asset_id=$a,asset_type=$type,production_profile=$p WHERE universe_id=$u AND objective_id=$id"
            : "INSERT INTO objectives VALUES($u,$id,$n,$count,$a,$type,$p)",
            "$u", u, "$id", id, "$n", objective.Name, "$count", objective.TargetCount, "$a", f.AssetId, "$type", f.AssetType, "$p", f.ProductionProfile);
        CatalogSql.Execute(c, t, "DELETE FROM objective_classifications WHERE universe_id=$u AND objective_id=$id", "$u", u, "$id", id);
        CatalogSql.Execute(c, t, "DELETE FROM objective_traits WHERE universe_id=$u AND objective_id=$id", "$u", u, "$id", id);
        foreach (var (key, value) in f.Classification)
            CatalogSql.Execute(c, t, "INSERT INTO objective_classifications VALUES($u,$id,$k,$v)", "$u", u, "$id", id, "$k", key, "$v", value);
        foreach (var trait in f.Traits.Distinct())
            CatalogSql.Execute(c, t, "INSERT INTO objective_traits VALUES($u,$id,$k,$v,$t)", "$u", u, "$id", id, "$k", trait.Key, "$v", trait.Value, "$t", trait.Type.ToString());
        t.Commit();
    }
    internal static void SaveCampaign(SqliteConnection c, CatalogCampaign campaign)
    {
        using var t = c.BeginTransaction(); var u = campaign.UniverseId.Value; var id = campaign.Id;
        var exists = CatalogSql.Scalar(c, t, "SELECT 1 FROM campaigns WHERE universe_id=$u AND campaign_id=$id", "$u", u, "$id", id) is not null;
        CatalogSql.Execute(c, t, exists ? "UPDATE campaigns SET name=$n WHERE universe_id=$u AND campaign_id=$id" : "INSERT INTO campaigns VALUES($u,$id,$n)", "$u", u, "$id", id, "$n", campaign.Name);
        CatalogSql.Execute(c, t, "DELETE FROM campaign_objectives WHERE universe_id=$u AND campaign_id=$id", "$u", u, "$id", id);
        foreach (var objective in campaign.ObjectiveIds)
            CatalogSql.Execute(c, t, "INSERT INTO campaign_objectives VALUES($u,$id,$o)", "$u", u, "$id", id, "$o", objective);
        t.Commit();
    }
    internal static IReadOnlyList<CatalogObjective> Objectives(SqliteConnection c, UniverseId universe)
    {
        var objectives = new List<(string Id, string Name, long Target, string? Asset, string? Type, string? Profile)>();
        using (var command = CatalogSql.Command(c, null, "SELECT objective_id,name,target_count,asset_id,asset_type,production_profile FROM objectives WHERE universe_id=$u", "$u", universe.Value))
        using (var reader = command.ExecuteReader()) while (reader.Read()) objectives.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        var result = new List<CatalogObjective>();
        foreach (var o in objectives.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            var classification = new Dictionary<string, string>(StringComparer.Ordinal); var traits = new List<CatalogVisualTrait>();
            using (var command = CatalogSql.Command(c, null, "SELECT dimension,value FROM objective_classifications WHERE universe_id=$u AND objective_id=$id", "$u", universe.Value, "$id", o.Id))
            using (var reader = command.ExecuteReader()) while (reader.Read()) classification.Add(reader.GetString(0), reader.GetString(1));
            using (var command = CatalogSql.Command(c, null, "SELECT trait_key,trait_value,scalar_type FROM objective_traits WHERE universe_id=$u AND objective_id=$id", "$u", universe.Value, "$id", o.Id))
            using (var reader = command.ExecuteReader()) while (reader.Read()) traits.Add(new(reader.GetString(0), reader.GetString(1), Enum.Parse<CatalogTraitType>(reader.GetString(2))));
            result.Add(new(universe, o.Id, o.Name, new CatalogFilter(o.Asset, o.Type, o.Profile, classification, traits), o.Target));
        }
        return result.AsReadOnly();
    }
    internal static IReadOnlyList<CatalogCampaign> Campaigns(SqliteConnection c, UniverseId universe)
    {
        var campaigns = new List<(string Id, string Name)>();
        using (var command = CatalogSql.Command(c, null, "SELECT campaign_id,name FROM campaigns WHERE universe_id=$u", "$u", universe.Value))
        using (var reader = command.ExecuteReader()) while (reader.Read()) campaigns.Add((reader.GetString(0), reader.GetString(1)));
        var result = new List<CatalogCampaign>();
        foreach (var campaign in campaigns.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            var ids = new List<string>();
            using (var command = CatalogSql.Command(c, null, "SELECT objective_id FROM campaign_objectives WHERE universe_id=$u AND campaign_id=$id", "$u", universe.Value, "$id", campaign.Id))
            using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetString(0));
            result.Add(new(universe, campaign.Id, campaign.Name, ids));
        }
        return result.AsReadOnly();
    }
    internal static CatalogPlanningReadModel Planning(SqliteConnection c, UniverseId universe)
    {
        var objectives = Objectives(c, universe).Select(o =>
        { var facts = CatalogQueries.Statistics(CatalogQueries.Select(c, universe, o.Filter)); return new CatalogObjectiveProgress(o, new(facts.TotalAssets, o.TargetCount), facts); }).ToArray();
        var lookup = objectives.ToDictionary(o => o.Objective.Id, StringComparer.Ordinal);
        var campaigns = Campaigns(c, universe).Select(campaign => new CatalogCampaignProgress(campaign,
            Array.AsReadOnly(campaign.ObjectiveIds.Select(id => lookup[id]).ToArray()))).ToArray();
        return new(universe, Array.AsReadOnly(objectives), Array.AsReadOnly(campaigns));
    }
}
