using System.Collections.ObjectModel;
using NAP.Core;
namespace NAP.Presentation;

public sealed record HistoricalGroupChoice(string? Value, string Label);
public sealed record HistoricalStatusChoice(HistoricalSimulationStatus? Value, string Label);
public sealed class HistoricalSimulationViewModel : ObservableObject
{
    private readonly IReadOnlyList<HistoricalSimulationRow> _reference = HistoricalSimulation.CreateReference();
    private HistoricalGroupChoice? _group;
    private HistoricalStatusChoice? _status;
    private string _search = "";
    private HistoricalSimulationRow? _selected;
    private HistoricalSimulationIssue? _issue;
    public HistoricalSimulationViewModel()
    {
        Groups = new[] { new HistoricalGroupChoice(null, "Todos los grupos · 39") }.Concat(_reference.GroupBy(r => r.HistoricalGroup)
            .Select(g => new HistoricalGroupChoice(g.Key, $"{g.Key} · {g.Count()}"))).ToArray();
        ContextChanged();
    }
    public string Scope => HistoricalSimulation.Scope;
    public string DocumentarySource => HistoricalSimulation.DocumentarySource;
    public string HistoricalFiles => HistoricalSimulation.HistoricalFiles;
    public string ContextNotice => "Referencia fija de Treskal, independiente del universo operativo de la cabecera. Solo metadatos sintéticos.";
    public IReadOnlyList<HistoricalGroupChoice> Groups { get; }
    public IReadOnlyList<HistoricalStatusChoice> Statuses { get; } = [new(null, "Todos los estados"),
        new(HistoricalSimulationStatus.Conforming, "Conforme en simulación"),
        new(HistoricalSimulationStatus.DocumentationReview, "Necesita revisión documental"),
        new(HistoricalSimulationStatus.Unverifiable, "No verificable sin originales")];
    public HistoricalGroupChoice? Group { get => _group; set { if (Set(ref _group, value)) Apply(); } }
    public HistoricalStatusChoice? Status { get => _status; set { if (Set(ref _status, value)) Apply(); } }
    public string Search { get => _search; set { if (Set(ref _search, value.Length <= 80 ? value : value[..80])) Apply(); } }
    public ObservableCollection<HistoricalSimulationRow> Rows { get; } = [];
    public HistoricalSimulationRow? Selected { get => _selected; set { if (Set(ref _selected, value)) { SelectedIssue = value?.Issues.FirstOrDefault(); Notify(nameof(HasSelection)); } } }
    public HistoricalSimulationIssue? SelectedIssue { get => _issue; set => Set(ref _issue, value); }
    public bool HasSelection => Selected is not null;
    public string Result => Rows.Count == 0 ? "Sin coincidencias. Cambia los filtros o restablece la referencia." : $"{Rows.Count} de 39 referencias sintéticas · ocho grupos documentales";
    public string Summary => $"{_reference.Count(r => r.Status == HistoricalSimulationStatus.Conforming)} conformes en simulación · " +
        $"{_reference.Count(r => r.Status == HistoricalSimulationStatus.DocumentationReview)} necesitan revisión documental · " +
        $"{_reference.Count(r => r.Status == HistoricalSimulationStatus.Unverifiable)} no verificable sin originales";
    public string Report => HistoricalSimulation.Report();
    public RelayCommand ResetFilters => new(_ => ContextChanged());
    public RelayCommand CloseDetail => new(_ => Selected = null);
    public void ContextChanged()
    {
        _group = Groups[0]; _status = Statuses[0]; _search = "";
        Notify(nameof(Group)); Notify(nameof(Status)); Notify(nameof(Search)); Apply();
    }
    private void Apply()
    {
        Selected = null; Rows.Clear();
        foreach (var row in _reference.Where(r => (Group?.Value is null || r.HistoricalGroup == Group.Value) &&
            (Status?.Value is null || r.Status == Status.Value) &&
            (Search.Length == 0 || r.Reference.Contains(Search, StringComparison.OrdinalIgnoreCase) || r.Identity.Contains(Search, StringComparison.OrdinalIgnoreCase)))) Rows.Add(row);
        Notify(nameof(Result));
    }
}
