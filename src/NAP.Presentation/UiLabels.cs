using System.Globalization;

namespace NAP.Presentation;

/// <summary>Display vocabulary only. Stored keys, IDs and provider evidence are never translated.</summary>
public static class UiLabels
{
    public static string Dimension(string key) => key switch
    {
        "culture" or "cultura" => "Cultura", "location" or "localidad" => "Ubicación",
        "role" or "profession" or "profesion" => "Profesión", "sex" or "sexo" => "Sexo",
        "/age/apparent_age/value" or "apparent_age" => "Edad aparente", "asset_type" => "Tipo de recurso",
        "production_profile" => "Perfil", _ => Words(key)
    };
    public static string Value(string key) => key.ToLowerInvariant() switch
    {
        "portrait" or "portrait_npc" => "Retrato", "scene" => "Escena", "male" => "Hombre", "female" => "Mujer",
        "fisher" or "fisherman" => "Pescador", "farmer" => "Agricultor", "merchant" => "Comerciante",
        "guard" => "Guardia", "blacksmith" => "Herrero", "carpenter" => "Carpintero", "hunter" => "Cazador",
        "sailor" => "Marinero", "priest" => "Sacerdote", "innkeeper" => "Posadero", "healer" => "Sanador",
        "adult" => "Adulto", "young_adult" => "Adulto joven", "elderly" or "elder" => "Mayor",
        "child" => "Infancia", "unknown" => "Sin datos", _ => Words(key)
    };
    public static string Words(string value) => CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(value.Replace('_', ' ').Replace('-', ' '));
    public static string AssetName(string id)
    {
        var stem = Path.GetFileNameWithoutExtension(id);
        if (stem.StartsWith("portrait ", StringComparison.Ordinal) || stem.StartsWith("scene ", StringComparison.Ordinal)) stem = stem.Replace(' ', '_');
        var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] is not ("portrait" or "scene")) return Words(stem);
        var words = parts.Skip(2).Where(p => p is not ("male" or "female" or "npc")).ToArray();
        var suffix = words.LastOrDefault() is { } number && number.All(char.IsDigit) ? number : null;
        var subject = words.Where(p => p != suffix).Select(Value).ToArray();
        if (parts.Contains("female") && subject.Length > 0)
            subject[0] = subject[0] switch { "Pescador" => "Pescadora", "Agricultor" => "Agricultora", "Herrero" => "Herrera", "Cazador" => "Cazadora", "Marinero" => "Marinera", "Carpintero" => "Carpintera", _ => subject[0] };
        return string.Join(" ", subject.DefaultIfEmpty(Value(parts[0]))) + " · " + Words(parts[1]) + (suffix is null ? "" : " · " + suffix);
    }
}
