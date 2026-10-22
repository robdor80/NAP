using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NAP.Core;

public enum HistoricalSimulationStatus { Conforming, DocumentationReview, Unverifiable }
public sealed record HistoricalSimulationIssue(string Code, string Title, string Evidence, string Proposal);
public sealed record HistoricalSimulationRow(string Reference, string HistoricalGroup, string Identity,
    IReadOnlyDictionary<string, string> Classification, string HistoricalPath, string? ProposedPath,
    HistoricalSimulationStatus Status, IReadOnlyList<HistoricalSimulationIssue> Issues)
{
    public string StatusLabel => Status switch
    {
        HistoricalSimulationStatus.Conforming => "Conforme en simulación",
        HistoricalSimulationStatus.DocumentationReview => "Necesita revisión documental",
        _ => "No verificable sin originales"
    };
    public string ClassificationLabel => Classification.Count == 0 ? "No verificable" :
        string.Join(" · ", Classification.Select(p => $"{p.Key}: {p.Value}"));
    public string ProposedPathLabel => ProposedPath ?? "Sin propuesta: metadata insuficiente o incompatible";
    public string Provenance => "Registro sintético · no corresponde a un retrato real";
    public string IssueSummary => Issues.Count == 0 ? "Sin incidencias de metadata sintética; originales pendientes de comprobar." : $"{Issues.Count} incidencias sintéticas · selecciona una para examinar evidencia y propuesta";
    public string OriginalStatus => "No verificable sin originales · sin homologación para producción";
}

/// <summary>Closed embedded metadata reference. No paths, caller data, host, images or production capabilities accepted.</summary>
public static class HistoricalSimulation
{
    public const string Scope = "SIMULACIÓN SINTÉTICA · Treskal · 39 referencias · sin homologación para producción";
    public const string DocumentarySource = "Corte documentado 03/10/2026: 39 retratos, ocho grupos y cuatro archivos históricos. No certifica su estado actual.";
    public const string HistoricalFiles = "<asset_id>.webp · <asset_id>_prompt.md · <asset_id>_info.md · <asset_id>_visual_identity.json (estructura histórica; no es un ZIP nuevo)";
    public static IReadOnlyList<HistoricalSimulationRow> CreateReference()
    {
        using var inventory = Resource("NAP.TreskalSyntheticInventory.json");
        var fixture = JsonSerializer.Deserialize<Fixture>(inventory) ?? throw new InvalidDataException("Missing synthetic reference.");
        if (fixture.Version != 1 || fixture.Provenance != "SYNTHETIC_ONLY" || fixture.Records.Length != 39)
            throw new InvalidDataException("Invalid synthetic reference.");
        using var profileStream = Resource("NAP.HistoricalProfile.json");
        var profile = UniverseProfileLoader.Load(profileStream);
        if (!profile.TryGetAssetRule("portrait", "portrait_npc", out var rule))
            throw new InvalidDataException("Missing reference portrait rule.");
        var duplicates = fixture.Records.Where(r => r.IdentityKey is not null).GroupBy(r => r.IdentityKey)
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key!, g => g.Select(r => r.Reference).ToArray());
        return Array.AsReadOnly(fixture.Records.Select(r => Evaluate(r, rule, duplicates)).ToArray());
    }

    private static HistoricalSimulationRow Evaluate(SyntheticReference r, UniverseAssetRule rule, Dictionary<string, string[]> duplicates)
    {
        var issues = new List<HistoricalSimulationIssue>();
        void Add(string code, string title, string evidence, string proposal) => issues.Add(new(code, title, evidence, proposal));
        string? proposed = null;
        AssetManifestV2? manifest = null;
        // Reuse the strict existing manifest loader in memory. This is not package validation.
        try
        {
            using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new AssetManifestV2
            {
                SchemaVersion = 2, UniverseId = "nimroel", AssetId = r.Reference, AssetType = "portrait",
                ProductionProfile = "portrait_npc", Classification = r.Classification
            }));
            manifest = AssetManifestV2Loader.Load(stream);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { Add("metadata_invalid", "Metadata incompatible con Naming v1", "El fixture contiene valores no canónicos; Core no los normaliza.", "Contrastar valores con documentación antes de redactar una corrección."); }
        var classification = rule.ValidateClassification(r.Classification);
        if (!classification.IsValid)
            Add("classification_incomplete", "Clasificación incompleta", "Dimensiones pendientes: " + string.Join(", ", classification.MissingRequired), "Obtener metadata documental; no inferir desde identificador ni carpeta.");
        if (r.Candidates.Length > 0)
            Add("ambiguous", "Clasificación ambigua", "Alternativas sintéticas sin resolver: " + string.Join(" / ", r.Candidates), "Contrastar identidad y metadata; no elegir automáticamente una alternativa.");
        if (!r.DocumentationComplete)
            Add("documentation_pending", "Documentación pendiente", "Falta documentación en este escenario sintético; no describe ausencias en el corte histórico.", "Solicitar inventario documental actualizado y cotejar los cuatro archivos históricos.");
        if (r.Classification.TryGetValue("role", out var role))
        {
            // Historical semantics are documentary warnings, not a new profile value validator.
            if (role == "elder" || role == "children" || (r.Group == "elder" && role != "village_elder") ||
                (r.Group == "children" && role is not ("farmer" or "village_child")))
                Add("historical_role", "Rol incompatible con la semántica documentada", "children agrupa farmer/village_child; elder corresponde a village_elder, según metadata.", "Documentar el rol canónico desde metadata verificada; conservar conceptualmente asset_id.");
            else if (role is not ("farmer" or "carpenter" or "blacksmith" or "healer" or "village_child" or "village_elder" or "fisher" or "tavernkeeper"))
                Add("unknown_value", "Rol sin respaldo documental", "Naming v1 admite el valor; el perfil no certifica un vocabulario de roles.", "Revisar documentación de clasificación; no presentar el valor como profesión real.");
        }
        if (r.Classification.TryGetValue("sex", out var sex) && sex is not ("male" or "female"))
            Add("historical_sex", "Segmento histórico usado como clasificación", "boy/girl no sustituyen automáticamente male/female.", "Consultar metadata para documentar sex canónico; no deducirlo desde la carpeta.");
        if (r.IdentityKey is not null && duplicates.TryGetValue(r.IdentityKey, out var pair))
            Add("possible_duplicate", "Posible duplicado sintético", "Clave de identidad de fixture compartida con: " + string.Join(", ", pair.Where(id => id != r.Reference)), "Comparar identidad, metadata y hashes de originales en una futura revisión autorizada. No fusionar ni borrar.");
        if (r.Identity is null)
            Add("identity_unverifiable", "Identidad no verificable", "El escenario no incluye identidad individual respaldada.", "Cotejar registry y originales bajo autorización futura; no inventar identidad.");
        if (manifest is not null && classification.IsValid && !issues.Any(i => i.Code is "ambiguous" or "historical_role" or "historical_sex" or "unknown_value"))
        {
            try { proposed = ProductionDestinationResolver.ResolveRelative(rule, manifest.Classification, manifest.AssetId); }
            catch (InvalidOperationException)
            { Add("route_invalid", "Ruta rechazada por Core", "El cálculo compartido rechaza segmentos inseguros.", "Revisar metadata documental; no adaptar ni crear una ruta física."); }
        }
        var status = r.Scenario == "unverifiable" ? HistoricalSimulationStatus.Unverifiable :
            issues.Count == 0 ? HistoricalSimulationStatus.Conforming : HistoricalSimulationStatus.DocumentationReview;
        return new(r.Reference, r.Group, r.Identity ?? "No verificable", new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(r.Classification, StringComparer.Ordinal)),
            $"portraits/Norgard/Treskal/{r.Group}/{r.HistoricalSegment}/{r.Reference}", proposed, status, Array.AsReadOnly(issues.ToArray()));
    }

    // Export only this closed reference, never caller-controlled metadata or real asset content.
    public static string Report()
    {
        var rows = CreateReference(); var report = new StringBuilder();
        report.AppendLine(Scope).AppendLine(DocumentarySource).AppendLine(HistoricalFiles)
            .AppendLine("Todos los identificadores, identidades, clasificaciones individuales, rutas e incidencias siguientes son sintéticos. No es inventario nominal histórico.")
            .AppendLine("Conforme en simulación = metadata sintética válida; originales no comprobados. Propuestas documentales informativas, no ejecutables.")
            .AppendLine("Contratos: Universe Profile v4; Manifest v2; Routing v1. Sin validación de imágenes, paquete, integridad o producción.");
        foreach (var group in rows.GroupBy(r => r.HistoricalGroup)) report.AppendLine($"Grupo documental {group.Key}: {group.Count()}");
        foreach (var state in rows.GroupBy(r => r.StatusLabel)) report.AppendLine($"{state.Key}: {state.Count()}");
        foreach (var row in rows)
        {
            report.AppendLine().AppendLine(row.Reference + " | " + row.StatusLabel).AppendLine(row.Provenance)
                .AppendLine(row.Identity).AppendLine(row.ClassificationLabel).AppendLine("Histórica (modelo sintético): " + row.HistoricalPath)
                .AppendLine("Canónica (propuesta sintética): " + row.ProposedPathLabel).AppendLine(row.OriginalStatus);
            foreach (var issue in row.Issues) report.AppendLine($"{issue.Code}: {issue.Title}. {issue.Evidence} Propuesta documental: {issue.Proposal}");
        }
        return report.ToString();
    }
    private static Stream Resource(string name) => typeof(HistoricalSimulation).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Missing embedded synthetic reference.");
    private sealed record Fixture([property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("provenance")] string Provenance,
        [property: JsonPropertyName("records")] SyntheticReference[] Records);
    private sealed record SyntheticReference(
        [property: JsonPropertyName("reference")] string ReferenceId,
        [property: JsonPropertyName("group")] string Group,
        [property: JsonPropertyName("historical_segment")] string HistoricalSegment,
        [property: JsonPropertyName("identity")] string? Identity,
        [property: JsonPropertyName("identity_key")] string? IdentityKey,
        [property: JsonPropertyName("classification")] Dictionary<string, string> Classification,
        [property: JsonPropertyName("candidates")] string[] Candidates,
        [property: JsonPropertyName("documentation_complete")] bool DocumentationComplete,
        [property: JsonPropertyName("scenario")] string Scenario)
    { public string Reference => ReferenceId; }
}
