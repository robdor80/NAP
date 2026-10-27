# F7.4 — Implementación para auditoría independiente

27-10-2026. Repositorio `robdor80/NAP`, rama exclusiva `chatgpt/nap-2-f7-4-git-ui`, PR **#56 Draft**. **No se declara F7.4 aceptada, cerrada ni certificada para producción.**

Código final probado: **`9fb4f1703f5b7fa8c62bac0822898834d02d4e46`**. Los commits posteriores solo añaden documentación/evidencia. El HEAD entregado es el commit que contiene este informe y se comunica con su SHA completo en la entrega y PR, evitando autorreferencia dentro del propio commit.

## Comprobaciones de partida

GitHub y `git ls-remote origin refs/heads/main refs/heads/chatgpt/nap-2-f7-4-git-ui` confirmaron antes de editar: main `c4a053226f6f27c0cf109ef89cf1a92be1117f36`, F7.4 `ca73d7677ddc4878f6ec2f34dc3548ac778b58f8`, PR #56 OPEN/Draft. PR #55 CLOSED/MERGED con merge coincidente con main; su HEAD `e3138f5f5531e750a95ba31898755f1002c26f33` es padre de ese merge y antecesor comprobado de main. No había divergencia contractual. Ambos refs se comprobaron otra vez antes de publicar; #47/#49 siguen OPEN.

Se leyeron íntegramente los tres documentos F7.4 y las referencias obligatorias F7.1, briefs F7.2/F7.3, implementación F7.3, F73-A1/A2 y GIT_PRODUCTION. No se encontraron AGENTS.md en el workspace. No se modificó main ni se lanzó workflow alguno.

## Cambios por fichero

| Fichero | Cambio |
| --- | --- |
| `src/NAP.App/Views/GitView.xaml` | Amplía la única pantalla Git: RobTitledFrame, Primary/Secondary/Danger, ScrollViewer, wrapping, cuatro modos provisionales, permisos separados, revisión, revocación, historial, listas virtualizadas de lotes/intents y operaciones manuales. Code-behind intacto. |
| `src/NAP.Core/GitPublicationManagementService.cs` | Adaptador ReadConfiguration/InspectForSetup/ReadOutbox/Propose/Save/Revoke sobre stores e inspector existentes. Ningún runner ni contrato durable nuevo. |
| `GitManagementUiModels.cs` | DTOs tipados sin roots ni URL: configuración, entrada/revisión, disponibilidad de host, Outbox/filas y retry. Contadores por intent, número exacto de Jobs por lote. |
| `GitManagementUiService.cs`, `ProfessionalUiServices.cs` | IGitManagementUiService async; factory opcional de policy para composición/tests. CWT de propuestas nativas retenidas por identidad del objeto; mutaciones manuales por GitPublicationManualService. |
| `GitManagementViewModel.cs`, `ProfessionalPages.cs` | Estado efectivo separado del editor, dependencia de flags, invalidación de propuesta al editar, selección por ID, reset completo y aviso RequireNoPending. |
| `ShellGitManagement.cs`, `ShellViewModel.cs` | Revisión y guardado con dos decisiones; confirmaciones propias para revocación, Recover y cada retry. Gates por estado/permisos/owner, tokens y generaciones para descartar respuestas tardías. |
| `UniverseOperationDispatcher.cs` | Disponibilidad factual del host y RunOwnedAsync → RunManualAsync existente; host pausado/detenido y cola sin Running. |
| `AutomationProtocol.cs`, `AutomationDesktopBridge.cs`, `RemoteDesktopService.cs` | Siete operaciones añadidas al final del enum, sin renumerar las anteriores. Handles por universo y deduplicación durable de mutaciones sobre IPC autenticado; sin fallback local. |
| `GitUiDiagnostics.cs`, `ProfessionalUiModels.cs` | Códigos cerrados de integridad, entrada, guardado/revocación no acreditados y Busy, sin stderr ni error crudo de filesystem. |
| `tests/NAP.Tests/GitUiF74Tests.cs`, `ProfessionalUiTests.cs` | Casos nuevos focalizados y regresiones UI actualizadas para grant explícito, sin overrides que evadan autorización. |

Salvo rutas completas, los ficheros de la tabla viven en `src/NAP.Presentation`.

## Lectura, revisión y guardado

`ReadGitConfigurationAsync` no inspecciona Git ni consulta remoto. Ausencia muestra Disabled sin crear grant; política/ancla parcial o corrupta produce STOP distinto. `InspectGitAsync` comprueba integridad y llama al inspector seguro sin requerir AllowCommit. Solo la acción explícita Inspeccionar y la propuesta/guardado solicitados consultan el remoto; refrescar Git no activa Git automático.

`ReadGitOutboxAsync` autentica el ledger y cruza OperationId/PlanHash/CommitSha/estado con receipts nativos existentes. Committed/Pushed sin receipt o discordante produce STOP, nunca adopción. Una reserva previa a Prepared puede carecer legítimamente de receipt. La lectura no cambia estados. Una inconsistencia limpia las filas visibles; un fallo de política invalida también configuración/propuesta. Receipts e intents/batches siguen separados.

Las filas conservan JobId, QueueItemId, AttemptId, BatchId, OperationId, revisión/AuthorizationId, PlanHash y CommitSha. Los diagnósticos se limitan a códigos acotados; texto ajeno se proyecta como `git_review_required`. Pushed acredita un hecho histórico, no el tip remoto actual. No hay binding directo a JSON ni acceso del ViewModel a archivos.

El editor solo cambia memoria. Disabled fuerza ambos permisos false; AutoCommitBatch no permite push. AllowPush exige AllowCommit y modo Manual/AutoCommitAndPushBatch. El adaptador valida actor explícito, máximo 1–20, 1–64 prefijos exactos y distintos, sin wildcard/traversal/.git/_nap/absolutos/reparse existentes; reutiliza ProductionPaths y el store sin modificar sus validadores. Un prefijo nunca demuestra ownership.

Propose original captura ID/revisión y vínculos sin guardar; se congela una copia read-only de prefijos y se comprueba coherencia local de identidad/configuración/rama. Se muestran autor/committer efectivos sin fechas, fingerprint de identidad, upstream, remoto por nombre y fingerprints completos. Nunca URL. La primera confirmación deja la revisión inmutable visible; **Autorizar y guardar Git** exige una segunda decisión. Editar invalida; cancelar no concede.

El servicio solo acepta la revisión retenida en su CWT, no un clone/DTO fabricado. Antes de Save vuelve a proponer por el motor y compara scope, revisión, identidad, refs/remoto y fingerprints. Consume la propuesta y no la refresca silenciosamente si falla. Save original aplica CAS expectedRevision y crea ancla/historial/AuthorizationId nuevo.

Se conserva AutomationOwner durante revalidación y Save, y cada API mantiene su orden de locks. No se añade un nuevo CAS sobre configuración Git ni se modifica el lock del store. Un Git externo puede competir fuera de los mutex; runtime vuelve a verificar bindings/identidad antes de cualquier efecto. No se promete exclusión externa absoluta.

## Revocación, Outbox histórica y operaciones

Revoke carga política íntegra, exige expectedRevision y construye revisión siguiente sobre esa política: nuevo ID, actor/fecha, Disabled, permisos false y Revoked=true. Guarda solo mediante Save/CAS original. **No llama Propose ni inspector**, por lo que funciona sin remoto. Conserva historia/Outbox; Save bloquea Pending por cambio de política.

Un turno que ya tenga el lock no se revoca anticipadamente. Busy, CAS o corrupción significan **revocación no acreditada**. Política/ancla siguen siendo dos finales; caída intermedia requiere revisión administrativa, sin reparación automática.

Retries UI requieren revisión/AuthorizationId originales de la fila, modo automático vigente y owner real disponible. No migran grants antiguos. La UI restringe conservadoramente también retry de un lote histórico cuyo receipt ya sea Pushed: la conciliación local histórica F73-A2 permanece en el turno nativo del host bajo sus controles, sin vía nueva de adopción UI. Un receipt Committed antiguo puede publicarse **manualmente** con grant actual compatible y confirmación independiente del ID/SHA seleccionado; el servicio original conserva las autorizaciones históricas de Outbox.

Commit manual solo usa outputs verificados de sesión, nunca `_verified` como evidencia durable de Outbox. Commit y push tienen confirmaciones independientes. Recover ahora confirma que puede actualizar el receipt; Prepared no crea commit sin retry separado. Tras cada operación se refresca el universo activo y se conserva selección por ID. AutoCommitBatch muestra permanentemente que otro lote puede esperar por RequireNoPending.

## Autoridad e IPC

- RetryBlocked/RetryPublication se invocan únicamente desde HostedUniverseOperationDispatcher → AutomationHost.RunManualAsync → AutomationUniverseScope.Owner/Queue. El request contiene ID hex acotado, revisión, AuthorizationId e intención exacta; el Core revalida política original y budgets. No se crea owner ni se ejecuta RunOnce desde WPF.
- Sin host/owner, botones Outbox bloqueados con motivo. Gestión/manual local conserva AutomationManualAccess y no compite con un host propietario. Cliente conectado nunca degrada a escritor local si falla RPC.
- Host activo o QueueState.Running bloquea mutaciones en UI y nuevamente en RunManualAsync. Configuración/Outbox se leen por QueryAsync; proponer exige quiescencia aunque no conceda permisos.
- IPC admite DTOs cerrados o referencias opacas del servidor, no policy.json, paths arbitrarios ni objetos Core del cliente. Proponer es lectura sin registro durable de solicitud: evita guardar autor/email en el ledger de replay. Save/revoke/retry mantienen deduplicación durable, autenticación y scope.
- Los handles expiran al reiniciar. Se conservan límites existentes de 128 handles/4096 comandos; su agotamiento exige revisión/reinicio, sin limpieza/ampliación silenciosa.
- Host sigue limitado a laboratorio TEMP; seleccionar modo o guardar no lo inicia ni abre autoridad productiva.

## Invariantes conservadas

| Protección | Evidencia |
| --- | --- |
| F7.1: único motor, staging exacto, identidad/config/remoto | GitProductionService, Inspector y Safety sin diff; solo APIs tipadas existentes |
| F7.2: HMAC/CAS/anclas, ownership, Completed | PolicyStore/OutboxStore/ReceiptStore y proyección durable sin diff; negativos verifican preservación |
| RequireNoPending | Sin diff, aviso persistente y regresión de dos ZIP Passed |
| F7.3: autorización original/revocación | Batch/Push sin diff; regresión grant nuevo/revocación Passed |
| F73-A1/A2: límites y reconocimiento local durable | Ningún budget nuevo; regresión A2 Passed, cero queries/push tras reinicio |
| Autoridad legítima | AutomationHost/UniverseRuntime sin diff; prueba IPC con owner real, universo ajeno, host activo y ausente |

[source-manifest.json](f7-4-evidence/source-manifest.json) registra blobs base/probados de diez archivos críticos. No se necesitó alterar contratos sensibles de recuperación, autorización o persistencia.

## Reservas

Compilación cruzada de NAP.App WPF/Debug correcta en Linux. **No ejecución visual Windows 11**, ni validación real de 2560×1600/125–200 %, teclado/accesibilidad, NTFS/ACL/MSI/Release. Wrapping/scroll/virtualización no sustituyen revisión visual. Sin host productivo, CUTOVER, autenticación HTTPS/SSH/GCM real, kill eléctrico ni durabilidad física. No nueva certificación multiproceso.

Cinco advisories NU1902/NU1903 ImageSharp 3.2.0 y avisos de licencia Debug/Release conservados, sin supresión. #47/#49 siguen abiertas, incluidos IPC/StorageUnavailable históricos. No reparación administrativa, force/reset, borrado/readopción de receipts ni migración de grants viejos. La limitación UI de retry histórico Pushed se describe arriba.

No se ejecutó NAP CI, otro workflow, suite general/estrés, pruebas Git contra repos reales, producción ni fases posteriores. Los pushes de esta rama son gestión de código autorizada. La auditoría independiente y aceptación corresponden a ChatGPT.
