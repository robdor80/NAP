using NAP.Core;

namespace NAP.Presentation;

public enum GitUiFailure { ConfigurationIntegrity, OutboxIntegrity, AuthorizationInput, AuthorizationSave, Revocation }

/// <summary>Closed, non-sensitive diagnostics. Never forwards filesystem errors or Git stderr.</summary>
public sealed class GitUiStoppedException(GitUiFailure failure) : InvalidOperationException("Git UI operation stopped.")
{
    public UiNotice Notice => NoticeFor(failure);
    public static UiNotice NoticeFor(GitUiFailure failure) => failure switch
    {
        GitUiFailure.ConfigurationIntegrity => new("STOP · la política o su ancla no es íntegra. No equivale a Git desactivado; requiere revisión.", "git_ui_configuration_integrity", UiTone.Error),
        GitUiFailure.OutboxIntegrity => new("STOP · Outbox y sus recibos requieren revisión de integridad. No se adoptará ni reparará evidencia.", "git_ui_outbox_integrity", UiTone.Error),
        GitUiFailure.AuthorizationInput => new("Propuesta detenida. Revisa actor, permisos, prefijos exactos sin duplicados y máximo de 1 a 20 Jobs.", "git_ui_authorization_input", UiTone.Error),
        GitUiFailure.AuthorizationSave => new("Guardado no acreditado: revisión caducada o almacén inconsistente. Actualiza la política y genera una propuesta nueva.", "git_ui_authorization_save", UiTone.Error),
        _ => new("Revocación no acreditada: revisión cambiada o política inconsistente. Actualiza y revisa antes de volver a solicitarla.", "git_ui_revocation", UiTone.Error)
    };
    internal static UiNotice? FromCode(string code) => Enum.GetValues<GitUiFailure>().Select(NoticeFor).FirstOrDefault(n => n.Code == code);
    internal static T Guard<T>(GitUiFailure failure, Func<T> operation)
    {
        try { return operation(); }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { throw new GitUiStoppedException(failure); }
    }
}
