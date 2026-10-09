using NAP.Core;
using NAP.Presentation;
using Xunit;

namespace NAP.Tests;

public sealed class ImageNormalizationUiTests
{
    [Theory][InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public async Task ProfileCorrectionAndGeometryRequireTwoIndependentHumanDecisions(bool metadataAccepted, bool geometryAccepted)
    {
        using var f = new ImageNormalizationTestFixture(narrative: true); var original = File.ReadAllBytes(f.ZipPath);
        var confirm = new Confirmation(metadataAccepted, geometryAccepted); using var shell = Create(f, confirm);
        await Ready(shell); Assert.False(shell.ApproveNormalization.CanExecute(null)); Assert.False(shell.PrepareNormalizedCandidate.CanExecute(null));
        await shell.PrepareNormalization.ExecuteAsync(); Assert.Equal("normalization_profile_unknown", shell.Pipeline.Notice!.Code);
        Assert.Null(shell.Pipeline.Normalization);
        shell.Pipeline.NormalizationTarget = shell.Pipeline.NormalizationProfiles.Single(p => p.ProductionProfile == "scene_cartography");
        Assert.True(shell.PrepareNormalization.CanExecute(null)); await shell.PrepareNormalization.ExecuteAsync();
        Assert.NotNull(shell.Pipeline.Normalization); Assert.Contains("corrección", shell.Pipeline.Normalization!.ProfileChange, StringComparison.OrdinalIgnoreCase);
        Assert.False(shell.ExecutePackage.CanExecute(null)); await shell.ApproveNormalization.ExecuteAsync();
        var result = Assert.Single(shell.Pipeline.NormalizationHistory).Result;
        Assert.Equal(metadataAccepted && geometryAccepted ? "Approved" : "Rejected", result.Receipt.Decision);
        Assert.Equal(metadataAccepted ? 2 : 1, confirm.Titles.Count);
        Assert.Equal("Autorizar corrección del perfil", confirm.Titles[0]);
        if (metadataAccepted) Assert.Equal("Aprobar normalización técnica", confirm.Titles[1]);
        Assert.Null(shell.Pipeline.Prepared); Assert.Null(shell.Pipeline.Audit);
        Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ProductionRoot)); Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ArchiveRoot));
        Assert.Equal(original, File.ReadAllBytes(f.ZipPath));
    }

    [Fact]
    public async Task CandidateApprovalCannotReplaceAuditOrPublicationAuthorization()
    {
        using var f = new ImageNormalizationTestFixture(); var original = File.ReadAllBytes(f.ZipPath);
        var confirm = new Confirmation(true, false, true); var auditor = new OfflineAuditor(); using var shell = Create(f, confirm, new ProfessionalUiService(auditor));
        await Ready(shell); await shell.PreparePackage.ExecuteAsync();
        Assert.Equal(UiPageState.Stopped, shell.Pipeline.State); Assert.Equal(NapIssueCodes.ImageConversionAspectRatioMismatch, shell.Pipeline.Notice!.Code);
        Assert.True(shell.PrepareNormalization.CanExecute(null)); await shell.PrepareNormalization.ExecuteAsync(); await shell.ApproveNormalization.ExecuteAsync();
        Assert.Equal(0, auditor.Calls); Assert.False(shell.ExecutePackage.CanExecute(null));
        Assert.True(shell.PrepareNormalizedCandidate.CanExecute(null)); await shell.PrepareNormalizedCandidate.ExecuteAsync();
        Assert.NotNull(shell.Pipeline.Prepared); Assert.Null(shell.Pipeline.Audit); Assert.False(shell.ExecutePackage.CanExecute(null));
        await shell.AuditPackage.ExecuteAsync(); Assert.Equal(1, auditor.Calls); Assert.True(shell.ExecutePackage.CanExecute(null));
        await shell.ExecutePackage.ExecuteAsync(); Assert.Empty(Directory.GetFileSystemEntries(f.Context.Storage.ProductionRoot));
        await shell.ExecutePackage.ExecuteAsync(); Assert.Single(new AssetCatalog(f.Context).Query());
        Assert.Equal(original, File.ReadAllBytes(f.ZipPath));
    }

    [Fact]
    public async Task RejectButtonRecordsDecisionWithoutCreatingCandidateOrRequestingPublication()
    {
        using var f = new ImageNormalizationTestFixture(); var confirm = new Confirmation(); using var shell = Create(f, confirm);
        await Ready(shell); await shell.PrepareNormalization.ExecuteAsync(); await shell.RejectNormalization.ExecuteAsync();
        Assert.Null(shell.Pipeline.Normalization); Assert.Equal("Rejected", Assert.Single(shell.Pipeline.NormalizationHistory).Result.Receipt.Decision);
        Assert.False(shell.PrepareNormalizedCandidate.CanExecute(null)); Assert.Empty(confirm.Titles);
    }

    [Theory][InlineData("-1")][InlineData("5.01")][InlineData("6")][InlineData("Infinity")]
    public async Task InvalidGrowthCannotEnablePreparation(string value)
    {
        using var f = new ImageNormalizationTestFixture(); using var shell = Create(f, new Confirmation()); await Ready(shell);
        shell.Pipeline.NormalizationAreaLimit = value; Assert.False(shell.PrepareNormalization.CanExecute(null));
    }

    [Fact]
    public async Task ChangingLimitsOrSelectedSourceInvalidatesTheVisibleApproval()
    {
        using var f = new ImageNormalizationTestFixture(); using var shell = Create(f, new Confirmation()); await Ready(shell);
        await shell.PrepareNormalization.ExecuteAsync(); Assert.True(shell.ApproveNormalization.CanExecute(null));
        shell.Pipeline.NormalizationAreaLimit = "0,75"; Assert.Null(shell.Pipeline.Normalization); Assert.False(shell.ApproveNormalization.CanExecute(null));
        await shell.PrepareNormalization.ExecuteAsync(); Assert.NotNull(shell.Pipeline.Normalization);
        shell.Pipeline.SelectedCandidate = null; Assert.Null(shell.Pipeline.Normalization); Assert.False(shell.ApproveNormalization.CanExecute(null));
    }

    [Fact]
    public async Task OriginalMutationDuringConfirmationInvalidatesTheApprovedPreview()
    {
        using var f = new ImageNormalizationTestFixture(); var confirm = new Confirmation(true) { BeforeAnswer = () => File.AppendAllText(f.ZipPath, "external mutation") };
        using var shell = Create(f, confirm); await Ready(shell); await shell.PrepareNormalization.ExecuteAsync(); await shell.ApproveNormalization.ExecuteAsync();
        Assert.Equal("normalization_source_changed", shell.Pipeline.Notice!.Code); Assert.False(shell.ExecutePackage.CanExecute(null));
        Assert.Equal("Failed", Assert.Single(f.Service.Scan().Recorded).Receipt.Decision);
    }

    [Fact]
    public async Task CancelCommandStopsPendingPreviewWithoutManufacturingApproval()
    {
        using var f = new ImageNormalizationTestFixture(); using var shell = Create(f, new Confirmation(), normalization: new CancelledPreviewService());
        await Ready(shell); var pending = shell.PrepareNormalization.ExecuteAsync(); Assert.True(shell.CancelNormalization.CanExecute(null));
        shell.CancelNormalization.Execute(null); await pending;
        Assert.Null(shell.Pipeline.Normalization); Assert.False(shell.ApproveNormalization.CanExecute(null)); Assert.False(Directory.Exists(f.Service.Root));
    }

    [Fact]
    public async Task RestartShowsVerifiedCandidateWithoutAutomaticallyStagingAuditingOrPublishing()
    {
        using var f = new ImageNormalizationTestFixture(); var proposal = await f.Service.PreparePackageAsync(f.ZipPath);
        f.Service.Decide(proposal, ImageNormalizationTestFixture.Authorize(proposal));
        using var shell = Create(f, new Confirmation()); await Ready(shell);
        Assert.Single(shell.Pipeline.NormalizationHistory); Assert.Null(shell.Pipeline.SelectedNormalizedCandidate); Assert.Null(shell.Pipeline.Prepared);
        Assert.False(shell.PrepareNormalizedCandidate.CanExecute(null)); Assert.False(shell.ExecutePackage.CanExecute(null));
        shell.Pipeline.SelectedNormalizedCandidate = shell.Pipeline.NormalizationHistory[0]; Assert.True(shell.PrepareNormalizedCandidate.CanExecute(null));
    }

    private static ShellViewModel Create(ImageNormalizationTestFixture f, IUserConfirmation confirmation, IProfessionalUiService? service = null, IImageNormalizationUiService? normalization = null)
    {
        var store = new LocalUniverseSettingsStore(Path.Combine(f.Root, "test-settings.json")); store.Save([f.Context.Storage]);
        return new(new ExplorerViewModel([f.Context.Profile], store, new ExplorerService(), new Picker()), service ?? new ProfessionalUiService(), confirmation, normalization: normalization);
    }
    private static async Task Ready(ShellViewModel shell)
    { await shell.InitializeAsync(); shell.NavigateTo("Producción"); await shell.LastRefresh; shell.Pipeline.SelectedCandidate = Assert.Single(shell.Pipeline.Candidates); }
    private sealed class Picker : IFolderPicker { public string? Pick(string title, string current) => null; }
    private sealed class Confirmation(params bool[] answers) : IUserConfirmation
    {
        private readonly Queue<bool> _answers = new(answers);
        public List<string> Titles { get; } = [];
        public Action? BeforeAnswer { get; init; }
        public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation)
        { Titles.Add(confirmation.Title); BeforeAnswer?.Invoke(); return Task.FromResult(_answers.Count != 0 && _answers.Dequeue()); }
    }
    private sealed class OfflineAuditor : IAiAuditClient
    {
        public int Calls { get; private set; }
        public Task<AiAuditReport> AuditAsync(AiAuditRequest request, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(ProductionTestFixture.Pass); }
    }
    private sealed class CancelledPreviewService : IImageNormalizationUiService
    {
        public async Task<ImageNormalizationProposal> PrepareAsync(UniverseContext context, InboxPackageCandidate candidate, string? targetProfile, ImageNormalizationPolicy policy, CancellationToken cancellation)
        { await Task.Delay(Timeout.Infinite, cancellation); throw new InvalidOperationException("No preview exists."); }
        public Task<ImageNormalizationResult> DecideAsync(UniverseContext context, ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, CancellationToken cancellation) => throw new InvalidOperationException("Must not approve a cancelled preview.");
        public Task<ImageNormalizationRecoverySnapshot> ReadAsync(UniverseContext context, CancellationToken cancellation) => Task.FromResult(new ImageNormalizationRecoverySnapshot([], [], []));
    }
}
