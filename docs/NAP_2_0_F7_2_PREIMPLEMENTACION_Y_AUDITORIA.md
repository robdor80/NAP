# NAP 2.0 — F7.2 · Verificación previa de integración y criterios de auditoría

**Fecha:** 2026-10-26. **Estado:** dirección técnica previa a implementación; **NO prueba funcional ni cierre F7.2**.
**Repositorio:** `robdor80/NAP` · rama `chatgpt/nap-2-f7-git-contracts` · HEAD inspeccionado `02f067cf3ed0291c719ad24808dac8b1ce1cb9a0`.
**Base:** `main` `a6dc6fe81815db93f744d00f6ecd17e315c54e67`; comparación GitHub: rama +7 commits, -0. PR #53 merged. Issues #47/#49 abiertos.

## Documentación vinculante
No reemplaza `NAP_2_0_F7_1_GIT_ARCHITECTURE.md`, `NAP_2_0_F7_1_CODIGO_Y_ACTIONS_AUDITORIA.md`,
`NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md`, `NAP_2_0_F7_2_CODEX_BRIEF.md` ni `GIT_PRODUCTION.md`.
Sirve para concretar **puntos de acoplamiento y riesgos de recuperación** descubiertos al reexaminar código en la rama.

## Comprobaciones estáticas adicionales
1. En `DurableExecutionService.Execute` (líneas 88-98), el registro `ExecutionRecord.Completed` precede a `QueueState.Completed`: la frontera Git se sitúa **tras** la transición durable de cola, no cuando aparece solo un Job `Completed`.
2. `DurableExecutionResult` contiene solo IDs y estado. No aporta `ProductionAssetResult`; usar su retorno como ownership del fichero sería erróneo.
3. `DurablePreparationService.VerifyExecution` es un handoff nativo read-only, pero también comprueba política de ejecución **actual**, fuentes ZIP y auditoría. No asumir que siempre podrá recuperarse después de revocaciones, limpieza legítima de staging o cambios de perfil: ante falta de evidencia, Outbox `Blocked`, sin volver a ejecutar NAP.
4. `AssetExecutionCoordinator.VerifyCompleted` (privado) utiliza el plan nativo de archivo y producción ya completados; `ProductionAssetExecutor.Verify`/`VerifyCoordinated` producen `ProductionAssetResult` verificado sin escribir assets. Se recomienda reutilizar esta vía desde una proyección Core **estrictamente read-only** que compruebe Job + Queue + intento + snapshot + provenance/ownership + catálogo PASS, sin usar la UI.
5. `AutomationHost.WorkflowStep` ya revisita `QueueState.Completed` en recuperación inicial y llama a ejecución verificadora. **No** mezclar ese comportamiento con una nueva repetición de producción para Git; la reconciliación de Outbox debe poder enumerar Completed por páginas sin cambiar su estado ni reejecutar conversión.
6. `GitProductionService.PrepareCommit` requiere todo dirty path cubierto por `ProductionAssetResult.FilesVerified`. Por tanto, lote = exactamente el conjunto verificable de cambios: si hay un fichero manual/untracked/staged ajeno, bloquear completo; no staging parcial, no silenciar errores.
7. `GitProductionService.Commit` crea internamente `GitOperationId` y escribe receipt `Prepared` **antes** de mover HEAD; devuelve el ID al caller solo al finalizar. Este es un riesgo real para el enlace durable con Outbox si se corta el proceso antes del retorno. Hay que demostrar cómo se vincula inequívocamente operación ↔ lote en **cada** ventana de crash.
8. El motor existente usa `AutomationManualAccess.EnterLegacy` antes del mutex Production y `AutomationUniverseRuntime` adquiere el `AutomationOwner`; coordinar la entrada automática en el owner-thread o por una autoridad equivalente sin inversión de locks. No mantener una transacción SQLite/queue I/O abierta durante Git.
9. `GitProductionService.RequireNoPending` impide nuevos commits mientras haya receipt `Prepared` o `Committed`, incluida configuración `AutoCommitBatch`. El segundo lote debe quedar pendiente/bloqueado de forma explicable; esta fase **no** crea múltiples commits offline consecutivos.
10. `AutoCommitAndPushBatch` puede existir en políticas y autorizaciones, pero en F7.2 no puede ejecutar push automático. `GitReceiptState.Pushed` y Outbox `Pushed` se documentan como compatibilidad futura o recuperación de operaciones manuales, no como autorización implícita de F7.3.

## Resolución obligatoria del enlace Outbox ↔ operación Git
- Antes de producir efectos Git, existe intent durable con key estable de universo, repository_binding, Job, snapshot verificado y revisión de política.
- Debe quedar resuelta la caída **después de crear el receipt Git Prepared y antes de persistir el OperationId en Outbox**, incluso cuando Git ya movió HEAD. No adoptar por heurística un commit remoto/ajeno solo por mensaje, autor o SHA cercano.
- Alternativa preferente para evaluar por Codex: una pequeña **seam interna** en el servicio Git existente que permita preparar/reservar el ID vinculado al intent, **sin** exponer APIs públicas peligrosas ni saltar validaciones de `PrepareCommit/Commit/Recover/RetryCommit`; otra solución es admisible solo con prueba de unicidad, correspondencia de plan/hashes y crash seguro. No desarrollar un motor Git alternativo.
- Si no se consigue este enlace con garantías, STOP y documentar bloqueo; no declarar idempotencia.
- Recuperación `Prepared`: utilizar `Recover` nativo; solo `RetryCommit` bajo autorización actual y fotografía íntegra. Recuperación `Committed`: no volver a ejecutar `Commit`. Ningún fallo de Git debe llamar a `ReportCompletedInconsistent` por sí mismo.
- En reinicio se reconcilian Completed → intents mediante recorrido incremental limitado. La política Git nueva se revalida al ejecutar, nunca se infiere de autorización para ZIP ni de configuración antigua.

## Criterios mínimos para aceptar la implementación
| Control | Evidencia exigible |
| --- | --- |
| Disabled / migración / ausencia o corrupción de política | Sin operaciones automáticas, sin consentimientos implícitos ni reparación permisiva |
| Pertenencia + tamaño + SHA-256 de outputs | Proyección Core con checks nativos y rechazo de evidencia incompleta |
| Crash Completed→Outbox | Exactamente un intent, cero repetición de producción |
| Crash Prepared→HEAD→Committed/Outbox | OperationId identificable sin heurística; un commit como máximo |
| Receipts previos / segundo lote | Respeto a `RequireNoPending`, bloqueo persistente explicable |
| Revocación, identidad, branch, upstream, remoto, fingerprints o dirty ajeno | Bloqueo antes de mutar; nunca dar permisos por defecto |
| Solo commit local en F7.2 | No `Push` automático, ni en modo `AutoCommitAndPushBatch` |
| Concurrencia UI/manual y host | Exclusión efectiva sin deadlocks y sin transacciones DB alrededor de Git |
| Estado de Job / Queue | `Completed` permanece independiente de Git |
| Tests | Casos mínimos filtrados, fixtures Git/ZIP ficticios y bare locales; máximo 20 ZIP por escenario, sin suites generales |

## Límites administrativos
No activar ni ejecutar `NAP CI` ni otros workflows; no CI general; sin CUTOVER ni pruebas en MSI productivo, credenciales, repos reales o assets de Nimroel. No modificar `main` directamente ni cerrar #47/#49. Una PR **Draft** de F7.2 podrá prepararse cuando existan código y evidencias auditables, pero **no se fusionará** sin autorización expresa y no adelanta F7.3/F7.4. El cierre de F7.2 exige revisión posterior de ChatGPT, no basta la declaración de Codex.

## Protocolo de traspaso
Codex implementará sobre esta rama a partir de los contratos, registrará archivos cambiados, SHA, decisiones técnicas especialmente sobre la seam Git/Outbox, pruebas focalizadas exactas y límites no verificados. ChatGPT auditará después la PR Draft y evidencias.
