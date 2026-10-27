# NAP 2.0 — F7.3 · Encargo de implementación para Codex y contrato de auditoría

**Fecha:** 26-10-2026. **Estado:** PREPARADO; F7.3 NO IMPLEMENTADA NI PROBADA.
**Repositorio:** robdor80/NAP. **Rama de trabajo:** chatgpt/nap-2-f7-3-auto-push.
**Base comprobada:** main = 809fe00b5397c38ff6031987fbc7058f9b238d5b.
**PR #54 F7.2:** merged 2026-10-26T17:14:03Z. No trabajar desde el HEAD viejo ni reabrir una rama F7.2 apilada. #47 y #49 siguen abiertas.

## 1. Encargo — ejecutar solo F7.3

Codex: tienes autorización para implementar la **publicación Git automática condicionada** de los commits locales ya generados por F7.2, con recuperación e idempotencia, en la rama F7.3. Trabaja autónomamente en esa rama y entrega código, pruebas sintéticas focalizadas, evidencias, documentación y PR Draft para auditoría de ChatGPT; no fusiones. No avances F7.4.

Antes de editar, verifica HEAD de main, rama y PR vigente, y lee íntegramente:
1. docs/NAP_2_0_F7_1_CIERRE.md
2. docs/NAP_2_0_F7_1_GIT_ARCHITECTURE.md
3. docs/NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md
4. docs/NAP_2_0_F7_2_CODEX_BRIEF.md
5. docs/NAP_2_0_F7_2_PREIMPLEMENTACION_Y_AUDITORIA.md
6. docs/NAP_2_0_F7_2_IMPLEMENTACION.md
7. docs/NAP_2_0_F7_2_PRUEBAS.md
8. docs/GIT_PRODUCTION.md
9. docs/NAP_2_0_F7_1_CODIGO_Y_ACTIONS_AUDITORIA.md y docs/PHASE13_AUDIT.md como apoyo.

Inspecciona directamente GitProductionService, GitProductionInspector, GitProductionSafety, GitProductionReceiptStore, GitPublicationPolicyStore, GitPublicationManualService, GitBatchPublicationService, GitOutboxStore, GitPublicationModels, GitProductionModels, AutomationHost, ProfessionalUiServices y GitOutboxF72Tests, además de las fronteras Queue/Job/AutomationOwner.

## 2. Hechos de código verificados por ChatGPT

- GitBatchPublicationService.RunOnce concilia Completed por páginas, recupera lotes Pending y genera commits locales, pero devuelve git-idle cuando únicamente quedan lotes Committed: NO publica ni concilia esos SHA remotos.
- GitBatchPublicationService.ExecuteBatch llama a CommitBound / Recover / RetryCommitBound nativos, conserva GitOperationId reservado y PlanHash: preservarlos.
- GitPublicationPolicyStore.Require incluye una guarda push && (!AllowPush || automatic), por lo que **prohíbe expresamente todo push automático** incluso en AutoCommitAndPushBatch con AllowPush=true. Abrir solo el caso exacto autorizado, no ampliar Manual ni AutoCommitBatch.
- GitProductionService.Push(context, operationId) y PushLocked ya implementan: receipt Committed/Pushed, comprobación del remoto real, AlreadyPushed al coincidir SHA, push normal sin force, ls-remote posterior, inspección local y persistencia de receipt Pushed. REUTILIZAR ESTAS RUTAS; no inventar motor ni ejecutar CLI ajeno.
- GitPublicationManualService.Push llama al Push nativo tras permisos y confirmación independiente en UI, pero hoy no actualiza la Outbox al completar un push manual. Integrar reconciliación del lote correspondiente, sin apropiarse de commits manuales ajenos.
- GitOutboxStore.Validate exige que los intents de un batch tengan su mismo estado y que un batch Committed/Pushed tenga CommitSha; no inventar transiciones que rompan estas invariantes.
- AutomationHost.RunOnce dispara Git desde el owner-thread al quedar ocioso, con cadencia mínima actual de cinco segundos; errores Git no cambian Queue/Job Completed.
- F7.2: 19/19 pruebas focalizadas en Linux x64 Debug, no Windows/MSI; ninguna prueba acreditó push automático. No se afirma certificación productiva.

## 3. Comportamiento exigido por modo

- Disabled (ausente/configuración deshabilitada): cero ejecución Git automática; no crear autorización ni inferir nada de credenciales/ZIP. Política corrupta: STOP, sin reparación permisiva.
- Manual: commit y push solo tras autorizaciones Git explícitas y confirmaciones independientes en la UI; sin actividad automática de publicación. Al publicar manualmente una operación previamente inscrita en Outbox, conciliar Outbox con el receipt/tip remoto verificados.
- AutoCommitBatch: commits locales F7.2, sin push automático. Sigue vigente RequireNoPending: otro lote queda pendiente/bloqueado hasta resolver publicación del previo.
- AutoCommitAndPushBatch: commit por lote y push posterior únicamente si **allow_commit y allow_push están explícitamente activos**, autorización vigente, modo exacto, mismo universo, mismo repo, revisión, AuthorizationId, todos los fingerprints, identidad Git, branch, upstream y remoto. El mero modo no concede push. No activar al instalar ni durante la implementación.

## 4. Diseño técnico obligatorio

### 4.1 Extender, no duplicar, el motor nativo
Usa el GitProductionService.Push existente por operationId exacto. Si hace falta asegurar una autorización adicional **justo antes de la instrucción irreversible de red**, propón una seam interna con callback dentro del mismo PushLocked, bajo el mutex Production, equivalente a CommitBound, manteniendo la API y validaciones nativas. No dupliques PushLocked ni crees runner/subproceso Git independiente. Mantén el mutex de política durante el turno atómico de publicación para impedir revocación concurrente entre última comprobación y push; documenta límites frente a actores externos. Revalida también antes de escribir receipt Pushed en la rama AlreadyPushed.

### 4.2 Orden del turno y máquina de estados
Tras cargar política e Outbox en la autoridad existente, inspecciona primero lotes Committed **antes de formar nuevos commits** (RequireNoPending). Revisa identidad del lote, GitOperationId, PlanHash, CommitSha y receipt nativo; usa la API de lectura/recuperación nativa, no crea otro commit. Para un lote elegible, publica y verifica SHA remoto exacto; marca atómicamente en Outbox batch e intents Pushed en un mismo final. Los intents retienen JobId, QueueItemId, WorkflowAttemptId, BatchId, PolicyRevision, AuthorizationId y GitOperationId en batch. Nunca escribas un Pushed basándote solo en exit code o en un tracking ref local.

Si hubo push nativo exitoso pero cayó antes de actualizar Outbox, la recuperación debe reconocer receipt Pushed / SHA remoto y completar Outbox sin repetir el push. Si el usuario ya publicó a mano exactamente ese SHA en el remoto autorizado y el receipt sigue Committed, invoca recuperación segura que produzca AlreadyPushed y después reconcilia Outbox. El reconocimiento nunca debe adoptarse por heurística de mensaje/autor/fecha ni por coincidencia parcial.

### 4.3 Permisos, revisiones y revocación
Autoriza commit y push **por separado**. No revivir batches Committed de revisión/AuthorizationId anteriores porque el usuario otorgó una política nueva; la recuperación de su publicación requiere decisión humana explícita, salvo la reconciliación puramente comprobatoria de un estado Pushed ya acreditado. Si se revoca política o no se dispone de evidencias, no hacer ninguna nueva mutación remota. Evita que el segundo proceso ejecute publicaciones tras revocación. Las diferencias de modo Manual/AutoCommitBatch/AutoCommitAndPushBatch deben ser inequívocas.

La política nueva que supere una revisión anterior no debe otorgar permisos implícitos a lotes antiguos. Una operación que fue publicada legítimamente antes de revocación puede reconocerse si existe evidencia ya durable y se puede comprobar sin saltarse la política, pero nunca utilizar esa reconciliación para ejecutar un push nuevo.

### 4.4 Fallos, recuperación y retry
Distingue red no disponible/autenticación fallida (conserva Committed, permite retry limitado tras nueva verificación), remote ahead/branch divergente/fingerprint/identidad/config changed (Blocked, revisión humana), recibos corruptos/huérfanos (Blocked sin adopción), permiso revocado (sin push), publicación ambigua (primero comprobar tip remoto antes de cualquier segundo intento). Los diagnósticos deben ser códigos saneados sin URL de remoto, tokens, credenciales ni texto crudo de procesos.

Cubre checkpoints: antes del push, antes del comando Git.Push, tras retorno del remoto antes del receipt Pushed, tras receipt Pushed antes de Outbox Pushed, y reinicio. Un cierre con Outbox Committed debe poder resolverse por la misma OperationId, sin reejecutar assets ni nuevos commits. Conserva Completed de Job/Queue. No uses ReportCompletedInconsistent para errores de red/Git.

### 4.5 Push manual y control de concurrencia
Actualiza GitPublicationManualService.Push, o punto de composición equivalente, para conciliar Outbox únicamente con receipt/tip verificados y mismo OperationId/plan/CommitSha. Respeta confirmaciones actuales en ProfessionalUiServices/ShellViewModel. Si la publicación manual es externa a NAP, el posterior turno autorizado puede detectar el SHA exacto y reconocer AlreadyPushed. No bloquear ni retocar otro receipt/manual sin vínculo.

Mantén AutomationOwner, mutex de política y Production en orden consistente. No mantengas SQLite abierto durante red ni inviertas locks; ante Busy, detener el turno sin cambiar Completed. Verifica que el owner-thread del host y las operaciones manuales no pueden publicar simultáneamente en el mismo repo.

### 4.6 No ampliar F7.2
No alterar RequireNoPending, staging exacto, selección ownership, proyección nativa, schema SQLite, límites Outbox v1, claves HMAC, ni root/ACL/identidad MSI. Una revisión de política y ancla Outbox entre dos archivos puede bloquear y necesita revisión administrativa: documentar, no automatizar una reparación insegura. Mantener necesidad de evidencia original para reconstrucción, sin reejecutar producción.

## 5. Pruebas autorizadas, pequeñas y trazables

Tests nuevos focalizados sugeridos en tests/NAP.Tests/GitOutboxF73Tests.cs, usando fixtures temporales ficticios de F7.2 y transporte local **exclusivo de test**. Remoto bare local; absolutamente ningún push a GitHub ni repositorio productivo. Límite 20 ZIP sintéticos por escenario y preferencia 1 o 2; no multiplicar ejecuciones para emular carga. No ejecutar suites generales ni NAP CI.

Matriz prioritaria:
1. AutoCommitAndPushBatch con allow_push=true: 1 Job, commit+push a bare local, SHA remoto = CommitSha, receipt y Outbox Pushed, Job/Queue Completed.
2. Mismo modo con allow_push=false: commit Committed sin push, aunque credenciales Git estén disponibles; modo AutoCommitBatch también jamás publica.
3. Crash antes de push y posterior reinicio: un commit, un push como máximo.
4. Push remoto correcto y crash antes de receipt/Outbox: reintento AlreadyPushed (ninguna escritura remota redundante) y Outbox Pushed.
5. Offline/auth failure: Completed y Committed intactos; retry posterior autorizado; sin reprocesamiento ZIP.
6. Remote advanced/diverged o remoto/upstream/identidad/fingerprint alterado: Blocked, cero force/pull/rebase/reset.
7. Revocación tras commit y antes de push: no publicación. Nueva autorización no activa por sí sola el lote antiguo.
8. Push manual realizado por NAP y push manual externo equivalente: mismo OperationId+SHA verificado conduce a Pushed; commits ajenos no adoptados.
9. Concurrencia manual/host: una sola autoridad, sin doble push/deadlock; simular checkpoints sin suites de carga.
10. Cinco Jobs de mismo repo si aporta cobertura: un commit/un push, sin mezcla de universos ni archivos ajenos; no es obligatorio repetir pruebas antiguas.

Registrar por escenario: SHA de código probado, comando y filtro exacto, cantidad real de tests y ZIP, identidad ficticia y aislamiento, HEAD/base/CommitSha, tip remoto consultado, receipt nativo, Outbox, Job/Queue, comandos Push efectivamente invocados en el fixture, fallos previos y correcciones. Los tests negativos han de demostrar ausencia de mutaciones, no solo excepción. Recopilar TRX/logs saneados; incluir build de Core/Tests si es necesario sin disparar tests generales. No ocultar warnings ImageSharp existentes.

## 6. Restricciones absolutas

- NO ejecutar/activar NAP CI ni ningún workflow, ni baterías generales, estrés o volumen.
- NO realizar push reales contra GitHub en pruebas. Los pushes de commits de implementación hechos por Codex a su rama de trabajo son gestión de código, **no** pruebas del motor NAP.
- NO CUTOVER, producción, MSI real, credenciales, permisos/ACL/licencias, instalaciones, bundles/releases ni assets Nimroel/Star Trek reales.
- NO cambios directos a main, merge o auto-merge sin autorización; NO cierre de issues #47/#49.
- NO F7.4, ni nuevo configurador WPF de autorización salvo mínimos indispensables que se justifiquen (la experiencia de activación corresponde a F7.4).
- NO force push, pull, rebase, reset, changes de remoto/branch, ajustes de credenciales ni reparaciones destructivas.

## 7. Entrega y aceptación

Codex debe crear commits **solo** en chatgpt/nap-2-f7-3-auto-push, actualizar esta PR Draft sin fusionar, adjuntar:
- docs/NAP_2_0_F7_3_IMPLEMENTACION.md (API, invariantes, transiciones, seguridad/revocation, recovery, limitaciones).
- docs/NAP_2_0_F7_3_PRUEBAS.md y evidencia compacta sintética filtrada.
- Resumen final con commit SHA, rutas cambiadas, comandos y resultados, advertencias, riesgos y temas pendientes.

ChatGPT hará una auditoría independiente posterior de código, diff, estado de PR y evidencias antes de decidir si F7.3 puede aceptarse. La PR Draft de preparación documental NO representa funcionalidad implementada. F7.3 tampoco equivale a certificación Windows/MSI/productiva, que continúa sujeta a #47.

**STOP obligatorio**: si implementar push exige saltarse RequireNoPending, eliminar guardas, permitir transportes no autorizados, modificar producción o lanzar CI, documenta bloqueo y pide autorización nueva; no reduzcas seguridad silenciosamente.
