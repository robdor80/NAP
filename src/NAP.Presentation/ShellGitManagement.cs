using NAP.Core;

namespace NAP.Presentation;

public sealed partial class ShellViewModel
{
    public string GitRepositoryLabel => GitRepositoryIdentity.Label(Context?.Id, Context?.Storage.ProductionRoot);
    public UiAsyncCommand ProposeGitAuthorization { get; private set; } = null!;
    public UiAsyncCommand SaveGitAuthorization { get; private set; } = null!;
    public UiAsyncCommand RevokeGitAuthorization { get; private set; } = null!;
    public UiAsyncCommand ViewOutboxReceipt { get; private set; } = null!;
    public UiAsyncCommand RetryBlockedGit { get; private set; } = null!;
    public UiAsyncCommand RetryPublicationGit { get; private set; } = null!;
    private IGitManagementUiService Management => _service as IGitManagementUiService ?? throw new InvalidOperationException("Git management unavailable.");
    private bool GitWritable() => Writable(Git) && _service is IGitManagementUiService && Git.HostAccess is { MutationsAllowed: true };
    private void InitializeGitCommands()
    {
        ProposeGitAuthorization = Command(() => ActionAsync(async (c, ct) =>
        {
            var generation = _generation; var input = Git.Input(); Git.Review = null;
            var review = await Management.ProposeGitAuthorizationAsync(c, input, ct);
            if (!Active(c) || generation != _generation || ct.IsCancellationRequested || !SameEditor(input)) return;
            Scope(review.Configuration.UniverseId, c);
            if (await Confirm(c, "Revisar autorización Git", "Comprueba identidad, vínculos y permisos. Esta revisión todavía no guarda ni autoriza operaciones.", review.Details, ct)
                && generation == _generation && SameEditor(input)) Git.Review = review;
        }), () => GitWritable() && Git.ValidEditor);
        SaveGitAuthorization = Command(() => ActionAsync(async (c, ct) =>
        {
            var review = Git.Review!; var generation = _generation;
            if (!await Confirm(c, "Autorizar y guardar Git", "Autoriza solo los permisos indicados para este universo. Los modos automáticos permiten futuros turnos del host; no reactivan operaciones antiguas ni arrancan el host. Commit y push manual conservan confirmaciones independientes.", review.Details, ct)
                || generation != _generation || Git.Review != review) return;
            Git.Review = null;
            await Management.SaveGitAuthorizationAsync(c, review, ct);
            if (Active(c) && generation == _generation) Record(c, "Autorización Git guardada mediante CAS", UiTone.Warning);
        }), () => GitWritable() && Git.Review is not null);
        RevokeGitAuthorization = Command(() => ActionAsync(async (c, ct) =>
        {
            var p = Git.Configuration!; var actor = Git.Actor.Trim(); var generation = _generation;
            if (!await Confirm(c, "Revocar autorización Git", "Desactiva commit y push en la revisión siguiente, conserva Outbox y bloquea pendientes. Un turno que ya posee el lock no se revoca anticipadamente. Busy o conflicto no acreditan revocación.",
                [c.Id.Value, p.Summary, "Actor: " + actor, "AuthorizationId: " + p.AuthorizationId], ct) || generation != _generation) return;
            Git.Review = null; await Management.RevokeGitAuthorizationAsync(c, p.Revision, actor, ct);
            if (Active(c) && generation == _generation) Record(c, "Autorización Git revocada", UiTone.Warning);
        }), () => Context is not null && !IsExecuting && _service is IGitManagementUiService &&
            Git.HostAccess is { MutationsAllowed: true } && Git.Configuration is { Revision: > 0, Revoked: false } && !string.IsNullOrWhiteSpace(Git.Actor));
        ViewOutboxReceipt = Command(() =>
        {
            Git.SelectedReceipt = Git.Receipts.FirstOrDefault(r => r.OperationId.Value == Git.SelectedOutbox?.OperationId);
            return Task.CompletedTask;
        }, () => Writable(Git) && Git.SelectedOutbox?.OperationId is { } id && Git.Receipts.Any(r => r.OperationId.Value == id));
        RetryBlockedGit = Command(() => RetryOutbox(false), () => CanRetryOutbox(false));
        RetryPublicationGit = Command(() => RetryOutbox(true), () => CanRetryOutbox(true));
    }
    private bool SameEditor(GitAuthorizationInput input)
    {
        if (!Git.ValidEditor) return false;
        var now = Git.Input(); return input.Mode == now.Mode && input.Actor == now.Actor && input.MaximumJobs == now.MaximumJobs &&
            input.AllowCommit == now.AllowCommit && input.AllowPush == now.AllowPush && input.Prefixes.SequenceEqual(now.Prefixes);
    }
    private bool CanRetryOutbox(bool publication) => GitWritable() && Git.CanCommit && Git.HostAccess?.Available == true &&
        Git.Configuration!.Mode is GitPublicationMode.AutoCommitBatch or GitPublicationMode.AutoCommitAndPushBatch &&
        Git.SelectedOutbox is { } row && row.IsBatch == publication &&
        row.State == (publication ? GitOutboxState.Committed : GitOutboxState.Blocked) &&
        row.Revision == Git.Configuration.Revision && row.AuthorizationId == Git.Configuration.AuthorizationId;
    private Task RetryOutbox(bool publication) => ActionAsync(async (c, ct) =>
    {
        var row = Git.SelectedOutbox!; var p = Git.Configuration!; var generation = _generation;
        if (!await Confirm(c, publication ? "Reintentar publicación Outbox" : "Reintentar operación bloqueada", "El owner legítimo revalidará la autorización original y los límites del motor. No se cambian IDs, evidencias ni Job/Queue Completed. Puede conceder otro presupuesto explícito de retry.",
            [row.Id, row.Summary, "Operación: " + row.OperationId, "Commit: " + row.CommitSha, "Diagnóstico: " + row.Diagnostic], ct) || generation != _generation) return;
        var result = await Management.RetryGitOutboxAsync(c, new(row.Id, p.Revision, p.AuthorizationId!, publication), ct);
        if (Active(c) && generation == _generation) Record(c, "Resultado Outbox: " + result, UiTone.Neutral);
    });
    private string? GitDisabledReason(UiAsyncCommand command)
    {
        if (command == ProposeGitAuthorization || command == SaveGitAuthorization || command == RevokeGitAuthorization ||
            command == CommitGit || command == RecoverGit || command == RetryGit || command == PushGit ||
            command == RetryBlockedGit || command == RetryPublicationGit)
        {
            if (_service is not IGitManagementUiService) return "El adaptador no ofrece gestión segura Git.";
            if (Git.HostAccess is null) return "Actualiza la configuración y la disponibilidad del host.";
            if (!Git.HostAccess.MutationsAllowed) return Git.HostAccess.Reason;
            if (command == ProposeGitAuthorization) return "Introduce actor local, prefijos exactos y máximo de 1 a 20 Jobs.";
            if (command == SaveGitAuthorization) return "Genera y confirma primero una revisión inmutable; editar el formulario la invalida.";
            if (command == RevokeGitAuthorization) return "Se requiere política válida no revocada y actor local explícito.";
            if (!Git.CanCommit) return "Git no tiene autorización vigente de commit. Revisa la configuración.";
            if (command == PushGit && !Git.CanPush) return "Falta autorización independiente AllowPush.";
            if (command == RetryBlockedGit || command == RetryPublicationGit)
                return Git.HostAccess.Available ? "Selecciona un intent Blocked o lote Committed de la autorización actual en modo automático. Las autorizaciones antiguas requieren revisión; no se migran." : Git.HostAccess.Reason;
        }
        if (command == ViewOutboxReceipt) return "Selecciona una fila Outbox con recibo nativo existente.";
        return null;
    }
}
