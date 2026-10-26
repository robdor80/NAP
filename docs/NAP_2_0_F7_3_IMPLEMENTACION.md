# NAP 2.0 — F7.3 · Implementación entregada para auditoría

Fecha: 2026-10-26. Repositorio `robdor80/NAP`. Rama exclusiva `chatgpt/nap-2-f7-3-auto-push`, PR Draft #55.
Base comprobada `main`: `809fe00b5397c38ff6031987fbc7058f9b238d5b`; preparación F7.3: `670966e1b0be82fb10983a64d0bfa7fe4a70f1b1`.
Esta entrega requiere auditoría independiente de ChatGPT. **No declara aceptada ni cerrada F7.3.**

Adenda posterior: [corrección focalizada F73-A1](NAP_2_0_F7_3_F73_A1.md), sobre la reconciliación nativa sin escritura remota después de agotar los tres intentos de publicación. Sus SHA y pruebas se registran separadamente; las evidencias iniciales se conservan.

## Cambios y API

| Archivo | Cambio |
| --- | --- |
| `GitPublicationPolicyStore.cs` | `Require` permite push automático solamente con ambos permisos explícitos, modo exacto `AutoCommitAndPushBatch` y política vigente íntegra |
| `GitProductionService.cs` | Seam interna `PushBound(context, operationId, Action<GitRepositoryStatus>?, verificationOnly, requirePushedReceipt)` llama al mismo `PushLocked`; API pública conservada |
| `GitBatchPublicationService.cs` | Examina Committed antes de reconciliar nuevos Completed/formar commits; frontera durable Committed previa al push; recuperación F7.2 conservada |
| `GitBatchPublicationPush.cs` | Orquestación parcial del mismo servicio: autorización original, publicación, reconocimiento, presupuesto durable de retry y `RetryPublication(owner, batchId)` explícito |
| `GitOutboxStore.cs` | `RequireReceipt` une OperationId, PlanHash y CommitSha; `RecordPushedLocked` publica batch e intents conjuntamente en el mismo final autenticado |
| `GitPublicationManualService.cs` | Push explícito usa la seam y concilia solamente el batch del mismo OperationId/plan/SHA; una operación manual ajena no crea intents |
| `GitProductionProcess.cs` | Observer interno `CommandStarting`, usado exclusivamente por tests para contar también comandos rechazados antes de su ejecución; runner, argv y transportes conservados |
| `GitOutboxF72Tests.cs` | Fixture compartido internamente con F7.3; caso histórico de cinco Jobs pasa a AutoCommitBatch para conservar su comprobación de commit local sin push |
| `GitOutboxF73Tests.cs` | Casos sintéticos focalizados y evidencia JSON saneada por escenario |

No hay un segundo motor Git, un runner shell nuevo ni nuevos comandos productivos. No se modifica SQLite, schema Outbox v1, receipt nativo v1, HMAC, claves, ACL, configuración/credenciales Git reales, MSI, dependencias ni licencias. No se añade UI de autorización F7.4. `ProfessionalUiServices` continúa pasando por `GitPublicationManualService`; `ShellViewModel` conserva las confirmaciones separadas de commit y push.

## Autorización y orden del turno

1. Exigir el owner del universo en su hilo, entrar en `AutomationManualAccess`, adquirir mutex de política y cargar política + ancla Outbox cerradas.
2. Ausencia/Disabled/revocación/sin AllowCommit: devolver `git-disabled`, sin comandos Git automáticos. Manual: `git-manual`, sin actividad automática. Corrupción: STOP, sin reparación.
3. Examinar primero batches Committed. Para un receipt aún Committed, reconocer un push externo por comprobación sin escritura remota solo con autorización original vigente; publicar únicamente con **AllowCommit + AllowPush**, modo exacto y todos los intents en la **revisión y AuthorizationId originales**, además de OperationId/PlanHash/CommitSha exactos.
4. Si hay receipt Pushed durable, permitir solamente comprobación nativa `verificationOnly`: para autorizaciones distintas exige ese estado bajo Production y jamás puede ejecutar `Git.Push`, incluso si el remoto volvió a BaseHead. Se conserva la autorización actual de inspección, fingerprints, identidad y prefijos. No se utiliza el permiso nuevo para republicar un lote antiguo.
5. Solo cuando el turno no resuelve una publicación previa, continúa la reconciliación paginada F7.2 y la reserva/commit nativos. Tras un commit elegible, se guarda primero Committed y luego se entra en la fase independiente de push.

La seam recibe el `GitRepositoryStatus` recién inspeccionado por el motor. Bajo el mutex Production, `Require` vuelve a cargar política/ancla y valida universo, binding de repositorio/roots, modo, revisión/autorización íntegra, identidad author/committer efectiva sin fechas, rama, upstream, remoto, fingerprints y prefijos. No se infiere permiso del modo, credenciales o consentimiento ZIP.

La comprobación adicional se ejecuta inmediatamente antes de `Git.Push`, antes de escribir receipt Pushed en AlreadyPushed y antes del receipt posterior al push. La composición retiene **el mismo mutex de política durante todo el turno**, incluida la red. Una revocación aceptada impide el siguiente turno; otro escritor concurrente recibe Busy y debe volver a solicitar su operación. No se declara aceptada una revocación cuyo escritor recibió Busy.

Una nueva política no migra ni reautoriza batches Committed anteriores: se conserva Committed y se devuelve `git-authorization-required`. `RetryPublication` tampoco los reactiva. Un push manual con permiso actual y confirmación independiente constituye una decisión humana explícita sobre el OperationId seleccionado; mantiene los IDs/revisión/autorización originales en Outbox, sin reescribirlos.

## Evidencia remota y estados

Se conserva la secuencia nativa: receipt Committed/Pushed válido, repo/HEAD/parent/árbol/objetos/outputs/index limpio exactos, fingerprints congelados y tip obtenido con `ls-remote` del ref concreto. BaseHead admite el push normal; CommitSha produce AlreadyPushed sin otro push; otro tip bloquea. Después del comando se exigen `ls-remote == CommitSha` e inspección local/remota final antes del receipt Pushed.

En la ruta exclusivamente comprobatoria, si HEAD sigue siendo CommitSha y el remoto sigue en BaseHead, el motor devuelve `null` (todavía pendiente) tras validar configuración/binding/autorización. No afirma publicación ni escribe receipt, Outbox o remoto. Esa observación permite que nuevos outputs Completed sigan encontrando la barrera `RequireNoPending` original, sin bloquear erróneamente el primer lote al observarlos. **Todo push y todo reconocimiento AlreadyPushed conservan la verificación completa** de commit/árbol/bytes y limpieza nativa; la salida pendiente no sirve como prueba de Pushed.

`RecordPushedLocked` solo acepta receipt Pushed de la operación vinculada y escribe **batch y todos sus intents Pushed juntos**. Conserva JobId, QueueItemId, WorkflowAttemptId, SourceBatchId, BatchId, PolicyRevision, AuthorizationId, OperationId y PlanHash. Nunca usa exit code o tracking ref como prueba suficiente.

| Situación | Estado/acción |
| --- | --- |
| AutoCommitBatch o AllowPush=false | Commit local Committed; ningún push automático. Puede reconocer el SHA externo exacto bajo autorización original vigente; `RequireNoPending` continúa bloqueando commits posteriores mientras siga pendiente |
| AutoCommitAndPushBatch con permisos originales vigentes | Commit y publicación por lote; receipt y Outbox Pushed con SHA remoto exacto |
| Caída antes de push/comando | Outbox Committed, mismo ID y SHA; reinicio comprueba remoto antes de publicar |
| Remoto aceptó pero receipt aún Committed | PushBound reconoce CommitSha remoto, devuelve AlreadyPushed y genera receipt Pushed sin repetir escritura remota |
| Receipt Pushed, Outbox Committed | Turno de verificación, sin otro push, seguido del final Outbox Pushed |
| Push manual NAP | Conciliación inmediata del batch enlazado tras la verificación nativa |
| Push manual externo del SHA exacto | Siguiente turno con autorización original vigente usa AlreadyPushed; sin AllowPush se usa exclusivamente la ruta sin escritura remota; sin adopción por mensaje/autor/fecha |
| Política revocada/Disabled | Ningún nuevo comando automático; tampoco se intenta conciliar por red mientras esté deshabilitada |
| Nueva autorización + receipt Pushed previo | Solo comprobación bajo autorización actual compatible; ninguna publicación remota nueva |
| Remote ahead/divergente, config/identidad/upstream diferentes, receipt huérfano/corrupto, SHA/plan incompatibles | Blocked con código saneado, sin force/pull/rebase/reset ni adopción |

## Fallos y reintentos acotados

`git_remote_unavailable`, `git_push_rejected` y `git_timeout` conservan Committed. No se inspecciona texto de stdout/stderr para distinguir autenticación, red, rechazo del servidor o protección de rama. El código nativo de push rechazado no hace esa distinción; todos pasan por el mismo presupuesto conservador y una nueva verificación completa.

Cada intento reserva su presupuesto durable **antes** de entrar al motor, en el diagnóstico codificado del batch y sus intents, sin ampliar schema v1. Máximo tres intentos automáticos; reiniciar el servicio no lo restablece. Códigos `git_push_attempt_1..3` / `git_push_retry_1..3`; una caída puede consumir un intento sin arrancar el comando. `git-push-retry-exhausted` conserva el commit. El host mantiene su cadencia mínima previa de cinco segundos; no hay bucle interno de retry.

La ruta sin escritura remota tiene presupuesto separado de hasta tres fallos de comprobación (`git_verify_retry_1..3`); ninguna comprobación puede publicar. Un resultado BaseHead devuelve pendiente sin conceder push ni gastar un intento de escritura. `RetryPublication` renueva explícitamente el presupuesto de un batch Committed tras comprobar de nuevo política y vínculo. Para receipt aún Committed exige autorización original; si no hay AllowPush solo comprueba. Para Pushed admite únicamente comprobación bajo política automática actual válida. `RetryBlocked` conserva ID/plan/SHA y usa esta ruta cuando el lote ya tiene commit. Busy detiene el turno; si ya se había reservado presupuesto pero el motor no pudo adquirir autoridad, restaura esa reserva sin tocar Completed.

Una publicación ambigua no autoriza una segunda escritura a ciegas: toda entrada a PushLocked consulta el tip real antes del comando. Errores durables de receipt/Outbox pueden exigir revisión explícita y quedan conservados; no se borran finales/temporales para desbloquearlos.

## Coordinación y protecciones F7.2

Orden: AutomationOwner → mutex de política → mutex Production nativo. La callback no vuelve a adquirir owner ni política. Las lecturas/transactions SQLite terminan antes de Git/red. No se altera el dispatcher ni la pausa manual del host; el hilo ajeno recibe Busy y el owner conserva su autoridad. Production sigue serializando otros escritores del mismo repo incluso si hay otro contexto/StateRoot.

`RequireNoPending`, staging exacto, selección de ownership, proyección read-only nativa, hash completo de plan y reserva de OperationId previa a Prepared permanecen. No se reejecutan ZIP, auditoría, conversión, archive ni catálogo al recuperar un commit. Job/Queue Completed no cambian y no se llama `ReportCompletedInconsistent` por fallos Git.

## Límites que la auditoría debe conservar

- Evidencia de Linux x64 / Debug; no certifica Windows, WPF, NTFS/ACL, MSI, Release ni producción. #47 y #49 permanecen abiertas.
- Checkpoints con excepciones y reconstrucción de servicios; no equivalen a kill real, pérdida eléctrica, fsync portable de directorios ni disco averiado. La exclusión se ejercita con hilos concretos y el mutex existente; no se acredita aquí una prueba nueva de dos procesos independientes.
- Los tests de red usan solo bare temporales locales. Se prueba indisponibilidad y rechazo real del receptor ficticio; no se cambian credenciales ni se acredita autenticación HTTPS/SSH/GCM real.
- El native engine exige HEAD y working tree/index limpios del commit original. Si se producen otros outputs antes de resolver el push pendiente, puede bloquear hasta revisión; no se amplía el staging ni se adopta contenido nuevo para resolverlo.
- Actores Git/editores externos no sujetos a los mutex pueden competir. Un push normal no ofrece CAS adicional contra eliminación/recreación de la rama por el administrador remoto. Los controles y la comprobación posterior detectan muchos conflictos, sin prometer exclusión externa absoluta.
- Se conservan límites Outbox v1 (2 MiB, 4096 intents/lotes), receipts, salida Git y timeouts nativos. No se acredita capacidad ni rendimiento de volumen.
- Política/ancla siguen siendo dos finales distintos: caída entre ambos requiere revisión administrativa, sin reparación automática ni nueva clave HMAC. Las premisas de confianza local, conservación de fuentes/evidencia original y límites antirollback F7.2 permanecen.
- No se activa política Git alguna en instalación o producción. No workflows, NAP CI, CUTOVER, releases, merge, main ni F7.4. Los pushes a la rama de implementación son gestión de código autorizada; el motor NAP se prueba exclusivamente contra remotos locales ficticios.

Los SHA, comandos exactos, resultados y evidencias se registran en `NAP_2_0_F7_3_PRUEBAS.md` y `docs/f7-3-evidence/`.
