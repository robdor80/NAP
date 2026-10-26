# NAP 2.0 — F7.1 · Auditoría de código y GitHub (lectura estática)
**26-10-2026** · `robdor80/NAP` · `main` commit `a6dc6fe81815db93f744d00f6ecd17e315c54e67` · Rama documental `chatgpt/nap-2-f7-git-contracts`.

## Evidencia de GitHub
- [PR #53](https://github.com/robdor80/NAP/pull/53): merged=true, fecha 2026-10-26T11:21:48Z, merge SHA exacto `a6dc6fe81815db93f744d00f6ecd17e315c54e67`.
- Rama `main`: head SHA anterior, `protected=true`. La rama documental nace de `main`; no se ha realizado commit directo ni merge a `main`.
- [#47](https://github.com/robdor80/NAP/issues/47) abierta, CUTOVER y uso productivo BLOQUEADOS; [#49](https://github.com/robdor80/NAP/issues/49) abierta, reservas IPC, StorageUnavailable e ImageSharp.
- Acciones **solo inspeccionadas**, no ejecutadas ni reactivadas. La configuración del workflow `.github/workflows/ci.yml` contiene `on: workflow_dispatch`, con varias pruebas potencialmente amplias; por tanto **está prohibido lanzarlo**. No se ha comprobado mediante API el bit administrativo «workflow deshabilitado» en el servidor: la decisión del responsable es que siga desactivado.
- `cutover-laboratory.yml`: `workflow_dispatch` únicamente (no ejecutar).
- `phase6-review.yml`: `workflow_dispatch` únicamente, permisos `contents:write` para evidencias en rama autorizada de Fase 6 (no ejecutar).
- `phase6-msi-preparation.yml`: **mantiene un disparador `push`** con paths acotados y branch `codex/nap-2-fase-6-ux`, además de `workflow_dispatch`; job también filtra esa rama. **No se activa por commits documentales en la rama F7**. Dejarlo como riesgo histórico a revisar si se reutiliza esa rama; no se ha modificado el workflow sin autorización ni se ha ejecutado.

## Matriz estática de contratos vigentes
| Contrato / ruta | Evidencia real en `main` | Implicación F7 |
| --- | --- | --- |
| `src/NAP.Core/GitProductionService.cs` | `PrepareCommit`, `Commit`, `ListOperations`, `ReadReceipt`, `Recover`, `RetryCommit`, `Push`; comentario declara explícitamente que coordinadores y UI no lo disparan automáticamente | Reutilizar; construir orquestador F7 independiente |
| `src/NAP.Core/GitProductionInspector.cs` | Upstream real, misma rama local/remota, SHA remoto via `ls-remote`, fingerprint de configuración; clasifica Ahead/Behind/Diverged/Unknown | Política Git autoriza contra snapshot inspeccionado, no presupone sincronía |
| `src/NAP.Core/GitProductionSafety.cs` | `Acquire` mutex `Production` por raíz, binding hash universo+ProductionRoot+StateRoot; exige árbol Git standalone, rechaza config/redirecciones inseguras y comprueba hashes | Orquestador F7 no debe sustituir seguridad ni asumir mutex por Job basta |
| `src/NAP.Core/GitProductionProcess.cs` | Comandos Git tipados; `git add -- path`, `git commit`, `git push` normal, `ls-remote`, timeouts; no force | No inventar runner shell libre; no relajar allowlist |
| `src/NAP.Core/GitProductionReceiptStore.cs` | Schema v1 cerrado en `StateRoot/git-production`; `Prepared/Committed/Pushed`; temp+flush+move; validación estricta y bloqueo de orphans | Outbox es almacén **nuevo** distinto de los receipts existentes |
| `src/NAP.Core/GitProductionModels.cs` | `GitCommitPlan` y `GitCommitSelection` internos; `GitOwnedFile` con Digest/Size; `GitReceiptState` carece de Pending/Blocked | Estados Outbox independientes; nunca mutar receipt base |
| `src/NAP.Core/ProductionExecutionEvidence.cs` | Receipt por Job `StateRoot/production-executions`, snapshot, archivos `published`, ownership tras publicación física | Fuente necesaria, NO suficiente por sí sola para probar `ProductionAssetResult` completo |
| `src/NAP.Core/AssetExecutionCoordinator.cs` | Transiciones de Job a `Completed` tras verificación de producción y catálogo | El Git opcional se orquesta DESPUÉS, sin alterar Job |
| `src/NAP.Core/DurableExecutionService.cs` | Verifica catálogo y registra `QueueState.Completed` tras ejecución; retorno `DurableExecutionResult` con IDs, no `ProductionAssetResult`; recuperación de Completed existe | Proyección read-only de outputs; reconciliación tras crash `Completed -> Outbox` |
| `src/NAP.Core/AutomationQueueStore.cs` y `AutomationQueueSchema.cs` | Ledger SQLite v1 cerrado: batches/items/attempts/events/reservations/lineage/decisions; NO tabla Outbox | No alterar tabla ni estado Completed por error Git; almacén nuevo versionado/aislado |
| `src/NAP.Core/LocalUniverseSettingsStore.cs` | `settings.json` esquema v1 estricto, con raíces solamente, publicación atómica, no secretos | Config Git separada, Disabled por defecto |
| `src/NAP.Presentation/ShellViewModel.cs` | `_verified` es lista de sesión, UI Git pide confirmación separada de commit y push; receipt UI ya existe | No recuperar outputs a partir de `_verified`; preservar acciones manuales con nueva política |
| `src/NAP.App/Views/GitView.xaml` | UI Git manual actual | F7.4 extiende componente, no introduce duplicado |

## Hallazgos críticos y decisiones
**H1 · SIN OUTBOX.** No hay un archivo denominado `GitOutbox` ni modelo/lógica equivalente en árbol de `main`. `GitProductionReceiptStore` no es Outbox. Nuevo diseño imprescindible.

**H2 · BARRERA DE PROGRESIÓN.** `GitProductionService.RequireNoPending` rechaza nuevos commits si cualquier receipt no es Pushed. En AutoCommitBatch (sin push automático) se permite como máximo un commit local pendiente antes de que las operaciones siguientes aguarden. No se debe modificar esta garantía sin diseño y autorización separados. No promete throughput de commits offline sin límites.

**H3 · SIN STAGING PARCIAL DE DIRTY PATHS.** `PrepareCommit` exige que cada cambio del working tree pertenezca a los outputs suministrados. Los cambios ajenos bloquean. No se usará `git add .` ni se creará un segundo mecanismo de commit de alcance mayor. El índice usado hoy es el ordinario con staging exacto y guardas previas/posteriores; no afirmar que ya usa índice temporal.

**H4 · COMMIT Y PUSH YA SON DIFERENTES.** `RemoteTip == HEAD` se exige **antes de commit**, no después. En Push se exige remoto en BaseHead o CommitSha para reintentos idempotentes. NO es necesaria una «excepción» adicional para push normal del commit recién creado.

**H5 · PROYECCIÓN DE OUTPUTS.** La ejecución durable persiste evidencias físicas, pero su retorno público no trae `ProductionAssetResult`. Codex deberá definir una reconstrucción read-only estricta, respaldada por Job, Queue, ownership, hash, tamaño y catálogo; no fabricaciones, no re-ejecución.

**H6 · AUTORIZACIÓN.** La política de automatización ZIP existente es independiente de Git. El Git manual de WPF hoy no tiene nuevos modos; incorporar los modos puede requerir gatear puntos mutantes `ProfessionalUiServices` y `ShellViewModel` sin romper compatibilidad y confirmaciones explícitas.

**H7 · RECOVERY.** El motor Git v1 ya puede recuperar Prepared/Committed/Pushed sin reset; el Outbox debe relacionar JobId/BatchId/GitOperationId y reconciliar la ventana de caída antes de Outbox. En ningún caso fallo Git cambia COMPLETED.

## Riesgos y límites no certificados
- Auditoría de lectura estática y de declaraciones de workflow, NO prueba de ejecución, WPF, Git local, red, CLI ni comportamiento en MSI. Los tests previos en `docs/PHASE13_AUDIT.md` son antecedentes y no se han repetido.
- No se ha confirmado desde la API el estado administrativo de habilitación/deshabilitación de cada workflow; verificación exacta requiere acceso adecuado y **no autoriza** ejecutar nada.
- En F7 la publicación productiva sigue prohibida por #47. Seguridad de escritores/ACL reales, runtime de instalación y CUTOVER son asuntos ajenos a F7.1.
- `GitProductionService` conserva límite de una operación pendiente y dependencia de remoto consultable. No prometer commits offline sin modificar expresamente el contrato.
- La lectura de la PR #53 incluye las evidencias de aceptación Fase 6; F7.1 no las reproduce ni las declara repetidas.

## Evidencias de cierre
- Solo lectura de GitHub de `main`, PR #53, issues y cuatro YAML.
- Commits exclusivamente documentales en rama F7, sin modificar `main`, sin ejecutar workflows, sin tocar repos productivos ni assets.
- La especificación detallada está en `NAP_2_0_F7_1_GIT_ARCHITECTURE.md` y la tarea focalizada de Codex en `NAP_2_0_F7_2_CODEX_BRIEF.md`.
