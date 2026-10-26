# NAP 2.0 — F7.2 · Encargo técnico específico a Codex
**Estado:** LISTO PARA ENTREGAR, NO INICIADO. **Fecha:** 2026-10-26.
**Repositorio:** `robdor80/NAP`, rama de trabajo F7 `chatgpt/nap-2-f7-git-contracts`. **No usar ni tocar main directamente**.

## Contexto vinculante — LEER ANTES DE CODIFICAR
1. `docs/NAP_2_0_F7_1_GIT_ARCHITECTURE.md` (contrato aceptado).
2. `docs/NAP_2_0_F7_1_CODIGO_Y_ACTIONS_AUDITORIA.md` (hallazgos verificados).
3. `docs/NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md` (pruebas autorizables, no ejecutarlas todas).
4. `docs/GIT_PRODUCTION.md` y `docs/PHASE13_AUDIT.md` (motor Git existente).
5. `docs/NAP_2_0_FASE_0_AUDITORIA.md`, `docs/NAP_2_0_FASE_5_IMPLEMENTATION.md`, `docs/NAP_MASTER_SPEC.md`.
6. Código `GitProductionService`, `GitProductionInspector`, `GitProductionSafety`, `GitProductionReceiptStore`, `ProductionExecutionEvidence`, `DurableExecutionService`, `DurablePreparationService`, `AssetExecutionCoordinator`, `AutomationQueueStore`, `JobStateStore`, `LocalUniverseSettingsStore`, `ProfessionalUiServices` y `ShellViewModel`.

## Objetivo F7.2 (sin implementar push nuevo)
**Implementar política Git segura y commits automáticos recuperables** que conviertan trabajos COMPLETED de NAP en intents durables por repo y, cuando exista autorización, commits exactos de outputs propios. No introducir cambios funcionales WPF extensos hasta F7.4. No implementar activación de push automático hasta F7.3.

### Entregable A — Configuración y autorización
- Nuevo store local separado de `LocalUniverseSettingsStore.settings.json` esquema v1 (no admite campos Git), versionado, cerrado, atómico y fail-closed. Scope universo/binding de roots y repo.
- Modos Disabled (default), Manual, AutoCommitBatch, AutoCommitAndPushBatch (este último definible pero **sin push automático operativo en F7.2**).
- Autorización separada e inequívoca de commit/push. Bindings de identidad, rama/upstream/remoto, URL fingerprint y config fingerprint; prefijos relativos autorizados y policy revision.
- Revocación inmediata antes de cada mutación; migración sin activar Git; secretos y credenciales fuera del store/receipts/logs.

### Entregable B — Proyección read-only nativa
- El `DurableExecutionResult` existente solo trae IDs. Crear un recuperador de `ProductionAssetResult` NAP **válido y nativo** desde `QueueState.Completed` + Job journal + workflow Attempt/Plans + `ProductionExecutionEvidence` + receipt de publicación + catálogo PASS y verificación byte/hashes. Reutilizar métodos de Core; si la API nativa no permite reconstrucción sin efectos, crear método Core read-only focalizado y documentar prueba de integridad.
- Nunca sintetizar `FilesVerified` a partir de un directory listing, de manifests no confiables, ni usar `ShellViewModel._verified` como fuente durable. No repetir conversión/auditoría ni generar nuevos Jobs.

### Entregable C — Git Outbox v1
- Store Outbox dedicado, versionado y durable en StateRoot, distinto de receipts existentes en `StateRoot/git-production`, sin nuevas tablas en SQLite v1 a menos que se documente migración estricta y se justifique (preferible almacén aislado).
- Intents idempotentes keyed por UniverseId+RepoBinding+JobId+snapshot de receipts verificados (policy revision asociada).
- Estados Pending, Committed, Blocked en F7.2; Pushed queda reservado para F7.3. Conservar GitOperationId exacto y referenciar Git receipt v1, nunca alterar ese receipt fuera del servicio nativo.
- Creación DESPUÉS de `JobState.Completed` y `QueueState.Completed`; reconciliación incremental ante caída Completed→Outbox, sin reejecutar procesamiento. Git failure no altera Completed, no `ReportCompletedInconsistent` por error Git.

### Entregable D — Commit y coordinación
- Orquestador conserva estrictamente `GitProductionService.PrepareCommit/Commit/Recover/RetryCommit`, `Production` mutex por root y `AutomationOwner` con orden de locks explícito.
- Rechazar index previo, dirty paths ajenos, cambios del remoto, config/identidad/branch/upstream/fingerprint nuevos, falta de hash/ownership o receipt corrupto. Stage exacto nativo; nunca `git add .`, `-A`, commit -a, force/reset/clean/rebase/pull.
- **No sortear `RequireNoPending`**: si hay receipt Prepared o Committed previo, gestionar bloqueo/espera explicable para otro lote. En modo AutoCommitBatch no puede haber varios commits pendientes sin publicarse según motor v1.
- Un lote solo incluye Jobs de mismo repo, misma autorización y evidencia verificada, con selección determinista. No duplicar commit ni fusionar cambios ajenos.
- Mantener Manual disponible únicamente tras configuración/consentimiento nuevo. Reusar UI/servicio existente, sin duplicar su funcionalidad.

### Entregable E — Recuperación
- Crash antes de registrar Outbox: idempotente por reconciliación.
- Crash tras Prepared, antes y después de mover HEAD: enlazar y usar `Recover`/`RetryCommit` nativo, sin regenerar commit con distinto autor/hora.
- Revocación y policy change bloquean nuevos efectos incluso para intents antiguos. Recuperación NO adopta temp/orphan ni modifica HEAD; en conflicto deja error accionable.

## Pruebas permitidas / evidencia
- Únicamente fixtures **temporales ficticios** y remotos Git bare locales ficticios, nunca repositorios productivos. Usar filtros precisos de tests nuevos, ejecutar solo los estrictamente necesarios; máximo 20 ZIP por escenario y **no realizar baterías masivas de tests**, ni dividirlas artificialmente.
- No ejecutar/activar `NAP CI`, ningún workflow con suites generales, pruebas de volumen/estrés, CUTOVER, assets reales de Nimroel, red productiva ni acciones de publicación/release. La Fase 7.1 se cerró sin ejecutar test alguno: no atribuirle verde CI.
- Pruebas de seguridad focalizadas sugeridas: Disabled de origen/migración, dirty path ajeno, configuraciones alteradas, payload tampered, crash Completed→Outbox, Prepared/Committed recovery y AutoCommitBatch 2º lote bloqueado.
- Por cada prueba: caso, comando exacto, filtro, número de tests/ZIP, fixtures, resultados, SHA, logs saneados; no reejecutar tests históricos sin motivación.

## Entrega esperada
1. Commits exclusivamente en rama F7; no tocar `main`.
2. Informe F7.2 con rutas, APIs nuevas/adaptadas, alcance, autorizaciones, invariant checks, estados/recovery, fixtures y hallazgos.
3. Evidencias sintéticas mínimas, listando lo no verificado y riesgos.
4. Sin PR fusionada, sin cerrar #47 ni #49, sin activar workflows.
5. Parar y documentar bloqueo si algo exige rebajar protecciones, modificar política productiva o requerir privilegios del MSI.

**No continuar por tu cuenta con F7.3/F7.4**: entregar F7.2 para auditoría de ChatGPT. El objetivo es seguridad e implementación acotada, sin repetir fases antiguas.
