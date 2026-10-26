# NAP 2.0 — F7.1 · Contrato definitivo para Git configurable
**Fecha:** 2026-10-26. **Base auditada:** `main` = `a6dc6fe81815db93f744d00f6ecd17e315c54e67`. **Rama:** `chatgpt/nap-2-f7-git-contracts`.
**Naturaleza:** decisión arquitectónica y aceptación documental; F7.2–F7.4 son implementación futura, NO comportamiento existente.

## 1. Principio de compatibilidad
Se conserva `GitProductionService` y el contrato conservador documentado en `docs/GIT_PRODUCTION.md`. No se sustituye el motor Git de Fase 13, ni se crea un segundo runner Git con permisos más amplios. Usar `GitProductionInspector`, `GitProductionReceiptStore`, `GitProductionSafety` y sus controles como frontera de seguridad. Adaptador y Outbox añaden autorización, selección durable y orquestación; no rebajan validaciones.

La actual implementación exige:
- Antes de preparar y comprometer: `RemoteTip == HEAD == BaseHead`. El upstream remoto existe, tiene el mismo nombre de rama; índice sin staging ajeno.
- Después del commit, `HEAD == CommitSha`; para push, `RemoteTip == BaseHead` o, si ya fue publicado, `RemoteTip == CommitSha`. Esta es la **interpretación existente y válida**, no una excepción nueva. El borrador anterior que sugería una incompatibilidad genérica era impreciso.
- `RequireNoPending`: no admite otra operación mientras haya receipt `Prepared` o `Committed` no publicado. **No modificar ni rodear silenciosamente esta barrera**. En `AutoCommitBatch` un segundo lote aguardará o quedará bloqueado hasta resolver/push explícito del anterior. UX debe explicarlo.
- `PrepareCommit` admite exclusivamente `ProductionAssetResult` nativo con `FilesVerified`, y exige que **todos** los dirty paths se expliquen por los resultados proporcionados. Si hay otros cambios, BLOQUEAR, no hacer staging parcial ni apropiarse de ellos.
- Excluye symlinks/reparse, repos worktree/bare, renames, deletes, submodules, filtros/atributos de contenido, staging previo y remotos/config ambiguos. No forzar su soporte en F7.

## 2. Política configurable (nueva, independiente)
Política `GitPublicationPolicyV1` por `UniverseId` y binding exacto `ProductionRoot + StateRoot`. No existe universo/repo predeterminado global. Solo se admite el `ProductionRoot` validado como toplevel Git exacto; nunca aceptar ruta de Git arbitraria independiente.

Modos cerrados:
| Modo | Lectura | Commit | Push |
| --- | --- | --- | --- |
| Disabled | Estado local de política, sin operación Git remota automática | No | No |
| Manual | Inspección y acciones expresamente confirmadas | Solo tras confirmación específica | Solo tras confirmación independiente |
| AutoCommitBatch | Inspección necesaria para operación autorizada | Sí, por lote verificado y autorizado | No |
| AutoCommitAndPushBatch | Inspección necesaria para operación autorizada | Sí, por lote verificado y autorizado | Sí, con permiso adicional e independiente |

**Disabled es el valor por defecto incluso en migración desde NAP previo**. Política ausente -> Disabled; política corrupta, versión desconocida, root divergente o revocación -> STOP y bloqueo, nunca regeneración permisiva ni sobrescritura silenciosa. El consentimiento para ejecutar una política de procesamiento de ZIP **no** autoriza Git. Las credenciales almacenadas en Git/GCM **no** autorizan NAP.

Propuesta de archivo: configuración cerrada y versionada en área de configuración local de NAP, separada de `settings.json` v1 (el parser actual rechaza campos desconocidos) y fuera de cualquier checkout/universe root. Historial/revocaciones y Outbox en `StateRoot` protegido y vinculado al universe/repo. No copiar tokens o licencias al repositorio. Si no se puede garantizar ACL/escritura exclusiva, STOP (reservas #47 permanecen).

Campos obligatorios para autorizar operación mutante: `schema_version=1`, `universe_id`, `repository_binding`, `canonical_production_root` (solo en configuración local; NO en recibos/exportaciones), `mode`, `policy_revision`, `authorization_id`, `actor`, `authorized_utc`, `git_identity_fingerprint`, `local_branch`, `upstream_ref`, `remote_name`, `remote_branch`, `remote_fingerprint`, `config_fingerprint`, `allowed_relative_prefixes`, `max_jobs_per_batch`, `allow_commit`, `allow_push`, `revoked`. La identidad real `user.name/email` se lee y compara en cada operación; el contrato Git base la exige explícita. **No** se registran credenciales ni URL remota en receipts/logs. `allow_push=true` requiere `allow_commit=true` y modo compatible, pero no es inferible del nombre del modo.

Los prefijos de rutas son una restricción *adicional*: la selección debe seguir probándose con `ProductionAssetResult` + ownership/hash. Un prefijo no prueba propiedad. Versionar política y autorización, usar escritura atómica y mutex; cambios de remoto/branch/config/identidad/root/prefijo obligan a nueva autorización. Revocación tiene efecto antes de cada operación; no hay grandfathering de intents antiguos. Al reiniciar, la política no cambia a auto por recordar sesiones anteriores.

## 3. Git Outbox durable nueva (no existe en main)
`GitOutbox` es un subsistema F7 nuevo y distinto de `GitProductionReceiptStore` v1. Separar:
- **Job**: `JobState.Completed` + `QueueState.Completed`, independientes de Git.
- **Outbox**: intención de publicación con ID determinista e idempotente, keyed por `UniverseId + repository_binding + JobId + production_receipt_snapshot_hash` y `policy_revision`. `QueueItemId`, `WorkflowAttemptId`, `BatchId` se conservan para correlación, no para inferir propiedad.
- **Operación Git**: `GitOperationId` y receipt v1 nativo (`Prepared/Committed/Pushed`) no se reinterpretan ni se alteran mediante escritura manual.

Estados UI/Outbox: `Pending` (sin commit), `Committed` (commit local), `Pushed` (remoto confirmado), `Blocked` (falta evidencia, autorización, inconsistencia o conflicto). `Disabled` describe la política, NO modifica Job ni receipt. Registro de bloqueos con razón codificada; reintento explícito/automático limitado y condicionado a política vigente. No borrar intents ni recuperar por adopción de temp huérfanos.

**Origen de datos:** `DurableExecutionService.Execute` solo devuelve IDs y outcome, no `ProductionAssetResult`. `JobStateRecord` contiene solo JobId/universe/state; `ProductionExecutionEvidence` guarda por Job outputs publicados y snapshot, pero no expone directamente un `ProductionAssetResult` público. Se requiere una **proyección read-only de output nativo**: partir de `QueueState.Completed`, intento/Job durables, planes/evidencia de ejecución nativa, receipts de producción, catálogo PASS y revalidación física por ruta/tamaño/SHA-256. Reconstruir `ProductionAssetResult` mediante servicio/Core nativo o adaptador interno que conserve todos los invariantes; **prohibido fabricar objetos falsos, usar solo carpeta/JSON o confiar en la lista `_verified` de ShellViewModel**.

## 4. Frontera COMPLETED → Outbox y recuperación
1. Registrar `JobState.Completed` y `QueueState.Completed` por la secuencia de ejecución nativa; nunca alterar esa secuencia por Git.
2. Después de la confirmación durable COMPLETED, registrar intención Outbox con idempotencia y sin realizar commit en la misma transacción.
3. Ventana de caída entre COMPLETED y Outbox: al iniciar/reconciliar, recorrer solo Completed de forma **paginada, read-only e incremental**, y recrear únicamente las intenciones que faltan por clave estable; nunca reejecutar producción o volver a generar el ZIP. No leer indiscriminadamente cientos/miles de assets como prueba.
4. Validar receipts/outbox al reiniciar: si hay `Prepared` con HEAD base, recuperación explícita según motor; si HEAD es commit esperado, `Recover` nativo; si `Committed`, no rehacer commit; si push previo fue exitoso, `Push` reconoce `AlreadyPushed`.
5. Si cambia hash, path, política, Git metadata, index o remoto, bloquear intent sin borrar nada. Los fallos Git **no llaman** `ReportCompletedInconsistent`, ni mueven `QueueState.Completed` a `NeedsReview`. La excepción ya existente `ReportCompletedInconsistent` es únicamente para inconsistencias de integridad física independientes.
6. Varios Jobs se agrupan únicamente si pertenecen al mismo repo, autorización y snapshot inmutable, con todos los outputs exactos verificables. No mezclar universos, revisiones ni lotes conflictivos.

## 5. Aislamiento y concurrencia
- Mutex `Production` en `ProductionRoot` ya existe; toda orquestación automática adquiere además/coordina la autoridad `AutomationOwner` con orden fijo y sin invertir locks. Se impide ejecución simultánea manual/host sobre el mismo repo. No asumir que el mutex por universo equivale al repo si las rutas cambian.
- Nunca cruzar raíz de almacenamiento de universos ni tocar rutas realistas fuera de fixtures. No modificar permisos, identidades de Windows, servicios ni permisos Git productivos.
- El engine v1 usa `git add -- path` exacto sobre el índice normal y exige ausencia de index ajeno. **No es un índice temporal.** Mantener este contrato conservador en F7; prohibido cambiarlo por un índice temporal no auditado. Si aparecen cambios ajenos en índice/worktree, bloquear lote.
- Git mutations: sin `git add .`, `-A`, `commit -a`, force, reset, clean, pull/rebase, amend, branch creation, auto-merge, auto-reconcile o cambio de upstream/remoto.
- Receipts existentes congelan `HEAD`, parent, tree, commit, hashes y remote fingerprint. No ampliar automáticamente la autorización para resolver 'remote ahead'.

## 6. Push F7.3 (preparación, no implementado)
Separar commit local de push. Revalidar política y `allow_push` justo antes de `Push(context, operationId)`; `RemoteTip` debe ser `BaseHead` o `CommitSha` para idempotencia. Push normal a ref ya existente, verificación `ls-remote` post-push; red/auth/branch avanzada -> `Committed` o `Blocked` con commit intacto. Solo push de fixtures remotos locales en tests.

## 7. Compatibilidad UX y pruebas
La UI existente dispone de Git manual (`GitView.xaml`, `ShellViewModel`, `ProfessionalUiServices`), receipts y confirmaciones; la futura F7.4 añadirá autorización/modos/outbox y NO duplicará páginas.
Pruebas F7.2/7.3 en Git init + bare locales *ficticios* y 1/5/10/20 ZIP sintéticos como máximo por escenario. Solo pruebas focalizadas pequeñas, sin volumen ni regresiones generales. No ejecutar `NAP CI` ni CUTOVER. No ejecutar pruebas por el mero hecho de redactar F7.1. Reserva #47 y #49 conservadas.

**Criterio de entrega F7.1:** documentación de decisiones, auditoría de código, matriz de seguridad y encargo F7.2 publicados en rama sin modificar `main`; no equiparar documental con funcionalidades implementadas.
