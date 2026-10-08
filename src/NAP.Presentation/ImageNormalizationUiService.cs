using NAP.Core;

namespace NAP.Presentation;

public interface IImageNormalizationUiService
{
    Task<ImageNormalizationProposal> PrepareAsync(UniverseContext context, InboxPackageCandidate candidate, string? targetProfile, ImageNormalizationPolicy policy, CancellationToken cancellation);
    Task<ImageNormalizationResult> DecideAsync(UniverseContext context, ImageNormalizationProposal proposal, ImageNormalizationAuthorization authorization, CancellationToken cancellation);
    Task<ImageNormalizationRecoverySnapshot> ReadAsync(UniverseContext context, CancellationToken cancellation);
}

public sealed class ImageNormalizationUiService : IImageNormalizationUiService
{
    public Task<ImageNormalizationProposal> PrepareAsync(UniverseContext context, InboxPackageCandidate candidate, string? targetProfile,
        ImageNormalizationPolicy policy, CancellationToken cancellation) => Task.Run(async () =>
    {
        var path = Path.GetFullPath(candidate.FullPath);
        if (!string.Equals(Path.GetDirectoryName(path), context.Storage.InboxRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            Path.GetFileName(path) != candidate.FileName)
            throw new InvalidDataException("Normalization must use the selected ordinary ZIP in this universe's Inbox.");
        return await new ImageNormalizationService(context, policy).PreparePackageAsync(path, targetProfile, cancellation).ConfigureAwait(false);
    }, cancellation);

    public Task<ImageNormalizationResult> DecideAsync(UniverseContext context, ImageNormalizationProposal proposal,
        ImageNormalizationAuthorization authorization, CancellationToken cancellation) => Task.Run(() =>
        new ImageNormalizationService(context, proposal.Policy).Decide(proposal, authorization, cancellation), cancellation);
    public Task<ImageNormalizationRecoverySnapshot> ReadAsync(UniverseContext context, CancellationToken cancellation) => Task.Run(() =>
    { cancellation.ThrowIfCancellationRequested(); var result = new ImageNormalizationService(context).Scan(); cancellation.ThrowIfCancellationRequested(); return result; }, cancellation);
}

public sealed record ImageNormalizationProfileChoice(string AssetType, string ProductionProfile)
{ public string Label => AssetType + " / " + ProductionProfile; }

public sealed class ImageNormalizationReview
{
    public ImageNormalizationReview(ImageNormalizationProposal proposal)
    { Proposal = proposal; OriginalPng = proposal.Preview.GetOriginalPng(); CandidatePng = proposal.Preview.GetCandidatePng(); }
    public ImageNormalizationProposal Proposal { get; }
    public byte[] OriginalPng { get; }
    public byte[] CandidatePng { get; }
    public string OriginalDimensions => $"{Proposal.Preview.Geometry.OriginalWidth} × {Proposal.Preview.Geometry.OriginalHeight}";
    public string CandidateDimensions => $"{Proposal.Preview.Geometry.CanvasWidth} × {Proposal.Preview.Geometry.CanvasHeight}";
    public string Summary => FormattableString.Invariant($"{OriginalDimensions} → {CandidateDimensions} · +{Proposal.Preview.Geometry.AddedPercent:0.000}% de área");
    public string Margins => $"Anchura +{Proposal.Preview.Geometry.Left + Proposal.Preview.Geometry.Right}; altura +{Proposal.Preview.Geometry.Top + Proposal.Preview.Geometry.Bottom}. Márgenes: izquierda {Proposal.Preview.Geometry.Left}, derecha {Proposal.Preview.Geometry.Right}, arriba {Proposal.Preview.Geometry.Top}, abajo {Proposal.Preview.Geometry.Bottom}.";
    public string Method => Proposal.Preview.Geometry.HasChanges ? "Extensión del píxel de borde más cercano; sin recorte, escalado ni deformación." : "Proporción exacta: el PNG se conserva byte-for-byte, sin normalizar.";
    public string Safety => "PNG estático y decode verificados. Píxeles y bordes comprobados; crecimiento dentro de límites. SHA-256 vincula original y candidato. El ZIP completo cumple el perfil seleccionado.";
    public string ProfileChange => Proposal.RequiresMetadataApproval ? $"Corrección separada pendiente de aprobación: {Proposal.OriginalProfile} → {Proposal.TargetProfile}." : $"Perfil: {Proposal.TargetProfile}; manifest sin cambios.";
    public string OriginalHash => "SHA-256 original: " + Proposal.Preview.OriginalSha256;
    public string CandidateHash => "SHA-256 candidato: " + Proposal.Preview.CandidateSha256;
}

public sealed record ImageNormalizationRecordedCandidate(ImageNormalizationResult Result)
{
    public string Summary => Result.Receipt.AssetId + " · " + (Result.Receipt.Decision switch { "Approved" => "Aprobado", "Rejected" => "Rechazado", "Failed" => "Fallido", "Cancelled" => "Cancelado", _ => "Sin cambios" });
    public string Location => Result.CandidatePath ?? "Sin candidato definitivo.";
}
