using System.Text;
using NAP.Core;
using NAP.Presentation;
using Xunit;
namespace NAP.Tests;

// Closed, in-memory metadata. Only the explicit export tests use disposable text files.
public sealed class Phase6HistoricalTests
{
    [Fact]
    public void DocumentaryCountsAreEightGroupsAndThirtyNineSyntheticReferences()
    {
        var rows = HistoricalSimulation.CreateReference();
        Assert.Equal(39, rows.Count); Assert.Equal(39, rows.Select(r => r.Reference).Distinct().Count());
        Assert.Equal(new[] { 10, 5, 5, 3, 6, 6, 2, 2 }, rows.GroupBy(r => r.HistoricalGroup).Select(g => g.Count()));
        Assert.Equal(new[] { "farmer", "carpenter", "blacksmith", "healer", "children", "elder", "fisher", "tavernkeeper" }, rows.GroupBy(r => r.HistoricalGroup).Select(g => g.Key));
        Assert.All(rows, r => { Assert.Contains("sintético", r.Provenance); Assert.StartsWith("portrait_p67_reference_", r.Reference); });
    }
    [Fact]
    public void ChildrenUseExplicitMetadataAndKeepIdentityDespiteHistoricalBoyGirlSegments()
    {
        var rows = HistoricalSimulation.CreateReference().Where(r => r.HistoricalGroup == "children").ToArray();
        Assert.Equal(2, rows.Count(r => r.Classification["role"] == "farmer"));
        Assert.Equal(4, rows.Count(r => r.Classification["role"] == "village_child"));
        Assert.Equal(3, rows.Count(r => r.Classification["sex"] == "male"));
        Assert.All(rows, r =>
        {
            Assert.Contains(r.Classification["sex"] == "male" ? "/children/boy/" : "/children/girl/", r.HistoricalPath);
            Assert.Equal($"portraits/norgard/treskal/{r.Classification["role"]}/{r.Classification["sex"]}/{r.Reference}", r.ProposedPath);
            Assert.DoesNotContain("/boy/", r.ProposedPath!); Assert.DoesNotContain("/girl/", r.ProposedPath!);
        });
    }
    [Fact]
    public void ElderIsHistoricalGroupingAndIncompatibleRoleHasNoProposal()
    {
        var rows = HistoricalSimulation.CreateReference().Where(r => r.HistoricalGroup == "elder").ToArray();
        Assert.Equal(5, rows.Count(r => r.ProposedPath?.Contains("/village_elder/") == true));
        var incompatible = Assert.Single(rows, r => r.Classification["role"] == "elder");
        Assert.Null(incompatible.ProposedPath); Assert.Contains(incompatible.Issues, i => i.Code == "historical_role");
    }
    [Theory]
    [InlineData("003", "classification_incomplete")]
    [InlineData("004", "ambiguous")]
    [InlineData("005", "documentation_pending")]
    [InlineData("007", "unknown_value")]
    [InlineData("011", "historical_sex")]
    [InlineData("016", "classification_incomplete")]
    [InlineData("021", "metadata_invalid")]
    public void MissingUnknownAmbiguousAndIncompatibleMetadataRemainReviewable(string suffix, string code)
    {
        var row = HistoricalSimulation.CreateReference().Single(r => r.Reference.EndsWith(suffix));
        Assert.Equal(HistoricalSimulationStatus.DocumentationReview, row.Status);
        Assert.Contains(row.Issues, i => i.Code == code && i.Proposal.Length > 0);
        if (code != "documentation_pending") Assert.Null(row.ProposedPath);
    }
    [Fact]
    public void PossibleDuplicatesAreSyntheticIdentityWarningsWithoutMergingOrChoosingOne()
    {
        var rows = HistoricalSimulation.CreateReference();
        var pair = rows.Where(r => r.Issues.Any(i => i.Code == "possible_duplicate")).ToArray();
        Assert.Equal(2, pair.Length); Assert.NotEqual(pair[0].Reference, pair[1].Reference);
        Assert.All(pair, r => { Assert.Equal(HistoricalSimulationStatus.DocumentationReview, r.Status); Assert.NotNull(r.ProposedPath); });
        Assert.Equal(39, HistoricalSimulation.CreateReference().Count);
    }
    [Fact]
    public void SyntheticConformityNeverClaimsRealHomologationOrVerifiedFiles()
    {
        var rows = HistoricalSimulation.CreateReference();
        Assert.NotEmpty(rows.Where(r => r.Status == HistoricalSimulationStatus.Conforming));
        Assert.All(rows, r => Assert.Contains("sin homologación para producción", r.OriginalStatus));
        var unknown = Assert.Single(rows, r => r.Status == HistoricalSimulationStatus.Unverifiable);
        Assert.Equal("No verificable", unknown.Identity); Assert.Empty(unknown.Classification); Assert.Null(unknown.ProposedPath);
        var report = HistoricalSimulation.Report();
        Assert.Contains("No es inventario nominal histórico", report); Assert.Contains("03/10/2026", report);
        Assert.Contains("no es un ZIP nuevo", report); Assert.True(Encoding.UTF8.GetByteCount(report) < 256 * 1024);
    }
    [Fact]
    public void FiltersSelectionEmptyStatesAndContextChangesNeverRetainStaleDetails()
    {
        var vm = new HistoricalSimulationViewModel();
        vm.Group = vm.Groups.Single(g => g.Value == "children"); Assert.Equal(6, vm.Rows.Count);
        vm.Selected = vm.Rows[0]; Assert.True(vm.HasSelection);
        vm.Status = vm.Statuses.Single(s => s.Value == HistoricalSimulationStatus.Unverifiable);
        Assert.Empty(vm.Rows); Assert.False(vm.HasSelection); Assert.Null(vm.SelectedIssue); Assert.Contains("Sin coincidencias", vm.Result);
        vm.ResetFilters.Execute(null); Assert.Equal(39, vm.Rows.Count);
        vm.Search = "Identidad sintética 001"; Assert.Single(vm.Rows); vm.Selected = vm.Rows[0];
        Assert.NotNull(vm.SelectedIssue); vm.SelectedIssue = vm.Selected.Issues.Last();
        vm.CloseDetail.Execute(null); Assert.Null(vm.Selected); Assert.Null(vm.SelectedIssue);
        vm.Search = new string('z', 100); Assert.Equal(80, vm.Search.Length);
        vm.ContextChanged(); Assert.Equal(39, vm.Rows.Count); Assert.Equal("", vm.Search); Assert.Null(vm.Selected);
        Assert.Equal(HistoricalSimulation.Report(), vm.Report); Assert.Contains("independiente", vm.ContextNotice);
    }
    [Fact]
    public void ExportUsesP65BoundaryAndNeverWritesConfiguredProductionOrOverwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "nap-p67-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var id = new UniverseId("synthetic");
            var storage = new UniverseStorageConfig(id, Path.Combine(root, "workspace"), Path.Combine(root, "production"), Path.Combine(root, "archive"));
            var admin = Path.Combine(root, "admin");
            foreach (var protectedRoot in new[] { storage.WorkspaceRoot, storage.ProductionRoot, storage.ArchiveRoot, admin })
            {
                Assert.Throws<InvalidDataException>(() => DiagnosticReportWriter.Export(Path.Combine(protectedRoot, "report.txt"), HistoricalSimulation.Report(), [storage], admin));
                Assert.False(Directory.Exists(protectedRoot));
            }
            var path = Path.Combine(root, "simulation.txt");
            DiagnosticReportWriter.Export(path, HistoricalSimulation.Report(), [storage], admin);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(HistoricalSimulation.Report(), File.ReadAllText(path));
            Assert.Throws<IOException>(() => DiagnosticReportWriter.Export(path, "replace", [storage], admin));
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            Assert.Throws<InvalidDataException>(() => DiagnosticReportWriter.Export(Path.Combine(root, "report.json"), "x", [storage], admin));
        }
        finally { Directory.Delete(root, true); }
    }
}
