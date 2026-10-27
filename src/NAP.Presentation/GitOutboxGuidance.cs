using NAP.Core;

namespace NAP.Presentation;

public enum GitOutboxIntervention { ExplicitReview, ConfigurationReview, AdministrativeReview }
public sealed record GitOutboxGuidance(string Reason, string RecommendedAction, GitOutboxIntervention Intervention)
{
    public string InterventionLabel => Intervention switch
    {
        GitOutboxIntervention.ExplicitReview => "Acción explícita tras revisión",
        GitOutboxIntervention.ConfigurationReview => "Revisar configuración y permisos",
        _ => "Revisión administrativa; no reintentar a ciegas"
    };

    /// <summary>Pure, closed projection of existing codes. Neither reads evidence nor grants an action.</summary>
    public static GitOutboxGuidance For(GitOutboxState state, string? code, GitConfigurationView? configuration = null,
        long revision = 0, string? authorization = null)
    {
        if (code is null && state == GitOutboxState.Committed && configuration is { AllowCommit: true, AllowPush: false, Revoked: false })
            return Configuration("La autorización actual permite commit, pero no concede push.", "Revisa AllowPush por separado. AutoCommitBatch no publica automáticamente; un permiso nuevo no reactiva el lote y toda publicación manual requiere confirmación independiente.");
        if (code is "git_policy_changed" or "git_publication_blocked" or "git-authorization-required" or "git-disabled")
        {
            if (configuration?.Revoked == true)
                return Configuration("La autorización Git está revocada.", "Conserva la operación y revisa la política. Una autorización nueva sirve para trabajos futuros; no reactiva esta operación.");
            if (configuration is { Revision: > 0 } && revision > 0 &&
                (revision != configuration.Revision || authorization != configuration.AuthorizationId))
                return Configuration("La operación conserva una autorización de una revisión anterior.", "Revisa la revisión y el recibo originales. No migres el lote; una publicación manual, si procede, requiere permiso vigente y confirmación independiente.");
            if (configuration is { AllowCommit: false } || configuration?.Mode == GitPublicationMode.Disabled)
                return Configuration("No hay permiso vigente de commit para esta operación.", "Revisa el modo y AllowCommit. No se deduce autorización de las credenciales; un grant nuevo no adopta operaciones antiguas.");
        }
        return code switch
        {
            null when state == GitOutboxState.Pushed => Explicit("Publicación acreditada por evidencia durable histórica.", "Evidencia durable histórica; no garantiza el tip remoto actual. Consulta el recibo; no se necesita otra publicación de esta operación."),
            null when state == GitOutboxState.Committed => Explicit("El commit es local; la publicación remota está pendiente.", "Revisa el recibo y AllowPush antes de una publicación manual confirmada. AutoCommitBatch no concede push automático."),
            null when state == GitOutboxState.Pending => Explicit("La intención está pendiente de commit.", "Espera al turno autorizado del host o revisa el recibo anterior si el progreso está detenido. RequireNoPending sigue vigente."),
            "git_policy_changed" => Configuration("La política de esta operación fue sustituida o revocada.", "Consulta la revisión original y la política actual. El lote no se reactiva con una autorización nueva; puede necesitar revisión administrativa."),
            "git-authorization-required" or "git-disabled" => Configuration("La autorización necesaria no está vigente o no corresponde a esta operación.", "Revisa modo, revisión y permisos independientes de commit y push. No adoptes la operación con un grant nuevo."),
            NapIssueCodes.GitIdentityMissing => Configuration("No se puede acreditar una identidad Git explícita válida.", "Revisa user.name, user.email y las identidades efectivas. Si cambiaron los vínculos, revisa una autorización para trabajos futuros; conserva el lote original."),
            NapIssueCodes.GitPlanStale => Configuration("Cambió la fotografía Git del plan: configuración, identidad, refs o contenido.", "Inspecciona y compara los vínculos con el plan original. No regeneres el commit ni reautorices este lote automáticamente."),
            NapIssueCodes.GitRemoteInvalid or NapIssueCodes.GitUpstreamMissing => Configuration("El remoto o upstream no cumple el vínculo Git admitido.", "Revisa rama, upstream y configuración fuera de NAP. Conserva el recibo y sus fingerprints; no cambies su destino desde esta operación."),
            NapIssueCodes.GitRemoteChanged or NapIssueCodes.GitNotSynchronized => Configuration("El remoto cambió o no coincide con la base requerida; puede haber divergencia.", "Inspecciona local y remoto y compara SHA, rama y fingerprints. Resuelve la discrepancia mediante revisión externa antes de intentar publicar."),
            NapIssueCodes.GitReceiptInvalid => Administrative("El recibo no permite continuar, o una operación Prepared/Committed anterior activa RequireNoPending.", "Selecciona el recibo anterior y revisa recuperación o publicación explícita según su estado. Si hay incoherencia, conserva la evidencia para revisión administrativa; no inicies otro lote."),
            "native_evidence_unavailable" or NapIssueCodes.GitOwnedFileChanged or NapIssueCodes.GitUnownedChange => Administrative("No se puede acreditar la evidencia nativa o la propiedad y contenido de los outputs.", "Conserva Job/Queue Completed y sus originales. Solicita revisión administrativa de hashes, ownership y recibos; no reconstruyas evidencia desde la carpeta."),
            NapIssueCodes.GitRepositoryInvalid or NapIssueCodes.GitCommitVerificationFailed or NapIssueCodes.GitStagingMismatch or NapIssueCodes.GitStagedContentMismatch or NapIssueCodes.GitRollbackFailed or
                "git_ui_outbox_integrity" or "git_ui_configuration_integrity" => Administrative("La integridad del commit, índice, recibo o almacén requiere revisión.", "Detén los reintentos y conserva todas las evidencias. Solicita revisión administrativa; la pantalla no puede reparar ni adoptar esos datos."),
            NapIssueCodes.GitIndexNotClean or NapIssueCodes.GitChangesConflict or NapIssueCodes.GitIgnoredOutput => Configuration("Hay staging previo, cambios incompatibles u outputs ignorados.", "Revisa el índice y los cambios fuera de NAP. No se incluirán cambios ajenos ni se saltará la selección exacta; reintenta solo tras revisión y confirmación."),
            NapIssueCodes.GitOperationInProgress or NapIssueCodes.GitBusy => Explicit("Git o el escritor del universo está ocupado.", "Espera a que termine y pausa o detén el host. Resuelve cualquier Running antes de actualizar y solicitar otra acción explícita."),
            NapIssueCodes.GitDetachedHead or NapIssueCodes.GitUnbornBranch => Configuration("HEAD no es una rama normal con commits válida para publicar.", "Revisa el repositorio fuera de NAP. Vuelve a inspeccionar los vínculos antes de considerar una operación autorizada."),
            NapIssueCodes.GitRemoteUnavailable or NapIssueCodes.GitTimeout or NapIssueCodes.GitPushRejected or
                "git_push_retry_1" or "git_push_retry_2" or "git_verify_retry_1" or "git_verify_retry_2" => Explicit("El remoto no pudo consultarse o rechazó la publicación; no hay confirmación suficiente.", "Revisa disponibilidad, autenticación y reglas del remoto sin compartir credenciales. Después, solo el owner y la autorización original pueden solicitar un retry confirmado dentro del motor."),
            "git_push_attempt_1" or "git_push_attempt_2" or "git_verify_attempt_1" or "git_verify_attempt_2" => Explicit("Existe una reserva durable de intento; el resultado puede estar pendiente de comprobación.", "Consulta el recibo y el estado durable antes de decidir otro intento. No presupongas que la publicación falló ni repitas el commit."),
            "git_push_attempt_3" or "git_push_retry_3" or "git-push-retry-exhausted" => Explicit("Se agotó el presupuesto automático de publicación.", "Revisa el recibo y el remoto. Pueden quedar comprobaciones nativas limitadas; renovar intentos exige decisión explícita, owner legítimo y autorización original. No se concede un cuarto push automático."),
            "git_push_exhausted_verify_1" or "git_push_exhausted_verify_2" => Explicit("El presupuesto de publicación está agotado y hay una comprobación remota reservada.", "Consulta el recibo antes de actuar. Solo quedan las comprobaciones acotadas que admita el motor; una lectura de esta fila no consume ni renueva reservas."),
            "git_push_exhausted_verify_3" or "git_verify_retry_3" or "git_verify_attempt_3" => Explicit("Se agotaron las reservas automáticas de comprobación remota.", "Conserva el commit y revisa el recibo. Un receipt Pushed durable solo se concilia por la vía local nativa; para otros casos se requiere revisión y una decisión explícita autorizada, sin consultas extra desde la UI."),
            NapIssueCodes.GitPushVerificationFailed => Administrative("No se ha podido acreditar el SHA remoto tras la publicación.", "Conserva el commit y el recibo. Revisa la evidencia antes de cualquier otro intento; no interpretes el exit code como prueba de publicación."),
            NapIssueCodes.GitIoFailed or NapIssueCodes.GitCommitFailed => Administrative("La operación de escritura o commit no terminó con un resultado acreditado.", "Conserva HEAD, índice y recibos para revisar qué ocurrió antes de otra acción. No repitas la escritura hasta verificar la evidencia nativa."),
            NapIssueCodes.GitOutputLimit => Administrative("La salida Git superó el límite seguro de inspección.", "Solicita revisión administrativa del repositorio y conserva el diagnóstico. Esta pantalla no ampliará límites ni repetirá la operación automáticamente."),
            NapIssueCodes.GitMessageInvalid => Configuration("El asunto del commit no cumple el formato admitido.", "Revisa el mensaje antes de preparar una operación manual nueva. No modifiques ni replanees una operación ya reservada."),
            NapIssueCodes.GitUnavailable => Configuration("El ejecutable Git no está disponible para la operación.", "Revisa la instalación del entorno fuera de NAP y vuelve a inspeccionar antes de cualquier acción autorizada."),
            _ => Administrative("La causa no está identificada con suficiente precisión.", "Conserva la operación y el diagnóstico técnico para revisión administrativa. No reintentes ni cambies evidencias a ciegas.")
        };
    }
    private static GitOutboxGuidance Explicit(string reason, string action) => new(reason, action, GitOutboxIntervention.ExplicitReview);
    private static GitOutboxGuidance Configuration(string reason, string action) => new(reason, action, GitOutboxIntervention.ConfigurationReview);
    private static GitOutboxGuidance Administrative(string reason, string action) => new(reason, action, GitOutboxIntervention.AdministrativeReview);
}
