# NAP 2.0 — Fase 4/7: ejecución y reconciliación idempotente

Inicio: 2026-10-12. Rama: `codex/nap-2-fase-4-execution-recovery`.
Repositorio: `robdor80/NAP`. Base: `main` posterior a la PR #45 (Fase 3 fusionada).

> **Estado:** Fase 4 iniciada y alcance delimitado. Este documento es un contrato de trabajo; **no acredita implementación, compilación ni pruebas**. La automatización productiva debe permanecer inactiva hasta que exista código revisado, ensayos satisfactorios y autorización real explícita.

## 1. Objetivo y frontera

Ejecutar **un** QueueItem admitido, preparado y auditado con PASS auténtico hasta `COMPLETED` usando el **mismo** `QueueItemId / WorkflowAttemptId / JobId`, y reconciliar cortes únicamente cuando las evidencias y el ownership de ese mismo workflow lo permitan. No reescribir motores productivos ya existentes.

Integrar:
- `AutomationQueueStore` de Fases 1/2: estado, claims, CAS, reserva AssetKey y eventos.
- `DurablePreparationService`, `PreparationStateStore`, `PreparationSnapshot`, `DurableAuditReceipt` y bundles de Fase 3: contexto, originales, derivación, perfil completo congelado y PASS ligado.
- `AssetExecutionCoordinator`, `ArchiveMasterPlanner/Executor`, `ProductionAssetPlanner/Executor`, `JobStateStore` y `AssetCatalog`: ejecución y verificación existentes.

**No** introducir todavía watcher/host continuo, scheduler de lotes, bandeja, UI nueva, Git automático, borrado/retención, cambios de canon, creación/adopción de perfiles, aprobación humana falsa ni paso automático desde el universo visible.

## 2. Invariantes obligatorios

1. Default `Disabled`. `PreparationOnly` autoriza exclusivamente preparación, normalización y auditoría preproductiva. No conceder ejecución productiva por compatibilidad, por presencia de una política, por un PASS aislado o por una bandera en memoria. Proponer y probar expresamente la autorización productiva versionada `Enabled` antes de permitirla, con `Admit|Audit|Archive|Produce|Register` (y `Normalize` cuando corresponda), raíces/operaciones/límites congelados, autorización del mismo universo/política, CAS, pausa y revocación. **No habilitar ningún universo real ni configurar políticas reales desde esta rama.**
2. Un `DurableAuditReceipt` PASS solo es válido después de verificar físicamente su `PreparationBundle`, decisión, petición, `SnapshotHash`, `RequestHash`, `GrantHash`, actor/política autorizada, proveedor/modelo, universo, intento y Job. Un test fixture puede simular el proveedor, nunca conceder autoridad a producción real.
3. Revalidar ZIP y ficheros preservados, candidato, lineage, manifiesto, perfil efectivo completo, reglas de conversión, dimensiones, rutas, hashes, límites, warnings y destino. STOP/FAIL/WARNING jamás abren escritura. Un perfil/motor/root alterado, o un `Job` extraño, se aparca.
4. Mantener el mismo `JobId`; nunca usar de nuevo `ProfessionalUiService.Prepare` para recovery ni crear Job alternativo. La cola es progreso, no sustituto del journal. Los originales recibidos se conservan fuera de `ArchiveRoot`, junto con procedencia verificable hasta la derivación efectiva.
5. ArchiveRoot y ProductionRoot usan no-overwrite. Ni igualdad de bytes ni basename implican propiedad. `IndexExisting` sin prueba del mismo workflow debe ir a revisión; el final físico publicado entre rename y registro de ownership que carezca de recibo propio sigue siendo ambiguo y **no se adopta**.
6. `COMPLETED` implica archivo y producción verificados **más** registro del catálogo del mismo asset/Job. Al verificar uno ya completado, comprobar también el catálogo (el coordinador actual solo verifica los outputs físicos). Un `COMPLETED` corrupto queda a revisión, sin rebuild ni regeneración automática.
7. Ledger/receipts/intents antes de efectos externos, flush/lectura y hashes después de publicación, CAS y orden de locks. No efectuar HTTP o escrituras productivas bajo una transacción SQLite. No sostener un lock DB mientras corre `AssetExecutionCoordinator`, que adquiere su propio mutex Job.
8. Errores individuales se aíslan en `NeedsReview` con código diagnóstico y evidencia; corrupción de cola/ledger/roots compartidos bloquea el universo. No ocultar el fallo con `Completed`. No borrar ZIP, assets, temporales ambiguos, otros Jobs ni archivos ajenos.

## 3. Implementación en entregas revisables

### P4.1 — Contratos, gate productivo y transición segura

- Auditar `AutomationPolicyContracts.cs`, `AutomationQueueLedger.cs`, `AutomationContracts.cs`, `AutomationQueuePreparation.cs` y el estado histórico de la política: Fase 3 **rechaza Enabled** y `StoredAutomationPolicy.IsExecutionAuthorized` devuelve false.
- Diseñar la activación productiva explícita, fail-closed y retrocompatible; registrar autorización exacta, transición CAS y verificación de permisos vigentes en cada efecto irreversible. `PreparationOnly` y `Disabled` siguen inertes frente a escritura; prohibir inferir `Enabled` de operaciones.
- Definir modelo/sidecar durable de ejecución si el contrato de Fases 1–3 no basta, sin alterar el journal v1 ni el catálogo existente.

### P4.2 — Adaptador de ejecución por item

- API explícita para **un item** con `UniverseContext` fijo y `AutomationQueueOwner`, sin scheduler ni estado de UI.
- Handoff verificable desde recepción/preparación y PASS con snapshot actual. Plan nuevo vs recuperación parcial diferenciados: `RequireFreshDestination` solo cuando corresponde, y ownership exacto para retomar outputs.
- Claims de cola transaccionales, guardas de reservas, revalidación antes del coordinador, `QueueStage.Execution/Verification/Catalog`, mismo Job, checkpoint tras cada evidencia y cierre `QueueState.Completed` únicamente después de comprobar los tres subsistemas y el catálogo.

### P4.3 — Reconciliación por checkpoints

- Interrupción antes/depués de claim, intención, llamada al coordinador, archivo, conversión/WebP, publicación, recibo de producción, verificación, commit SQLite y journal `COMPLETED`.
- Reabrir y reconstruir solo planes y evidencia exactos del mismo intento. Reconciliar `AUDITED/EXECUTED/VERIFIED` cuando la implementación existente lo admite. Recibo final sin ownership -> revisión.
- Estado Queue `Running` vs Job `Completed`: verificar todo y cerrar cola sin repetir efectos. `Completed` físico/catálogo discrepante -> incidencia. No borrar ni reparar automáticamente.

### P4.4 — Pruebas, CI y certificación de la fase

Usar solo PNG/ZIP/manifest y mundos **sintéticos**, directorios temporales; sentinels y snapshots SHA de ArchiveRoot, ProductionRoot, Inbox y universos ajenos. Nada de claves reales ni assets canónicos.

Matriz mínima:
1. Recorrido 1 ZIP: Admitted -> Planned -> PASS simulado y ligado -> archivos/WebP/catálogo -> Job y cola `COMPLETED`, sin confirmaciones por asset; autorización productiva sintética explícita.
2. `Disabled`, `PreparationOnly`, Paused, Revoked, política no current, grants incompatibles, PASS con receipt o bundle alterado, WARNING/FAIL, perfil/motor cambiados: **cero escrituras productivas**.
3. Fallos inyectados antes/después de intenciones y recepciones: reapertura del mismo Job sin segunda producción, ownership válido, bytes equivalentes, sin side effects duplicados.
4. Rename físico final sin receipt, `IndexExisting` ajeno, mismo AssetKey distinto contenido, ruta ocupada con bytes iguales, `COMPLETED` con catálogo borrado, fuente mutada, raíz reparse/symlink, corrupción DB: STOP/revisión, no adopción ni reparación.
5. Variantes sin normalización y con derivación autorizada; maestro recibido exacto y lineage íntegro, WebP reproducible según motor; dos universos con mismo identificador siguen aislados.
6. Fault injection de SQLite committed/journal no actualizado; registros y verificación reconciliables por identidad. Asserts anti-overwrite y no cambio en roots ajenas.
7. Release build, suite de regresión, tests focalizados, GitHub Actions Linux y **cuatro smoke WPF Windows** existentes. Documentar resultados medidos y fallos, no inventar cifras.

## 4. Exclusiones, entregables y control de calidad

**Exclusiones:** automatizar el monitor o los lotes (Fase 5), interfaz RobStyle/acciones de incidencia (Fase 6), Git productivo (Fase 7). No modificar `profile.json`, reglas visuales Nimroel, catálogos o activos reales; no alterar semantics de `AiAuditReport` o la publicación manual salvo para preservar compatibilidad.

**Entregables requeridos antes de PR:** código Core y pruebas aisladas, `docs/NAP_2_0_FASE_4_EXECUTION_RECOVERY.md` actualizado con decisiones definitivas, matriz crash/recovery, resultados de compilación/tests/CI, riesgos abiertos, estado de la autorización productiva (apagada para reales), diff limpio y sin archivos inesperados. Publicar PR **en borrador** para auditoría; no fusionar a `main` sin revisión y luz verde del responsable.

## 5. Orden recomendado para Codex

1. Leer este documento y `docs/NAP_2_0_FASE_0_AUDITORIA.md` secciones 6, 7, 9, 10, 11, más `docs/NAP_2_0_FASE_3_PREPARATION_AUDIT.md`.
2. Inspeccionar código real y tests; registrar desajustes antes de cambiar contratos. Conservar fronteras de Fases 1–3.
3. Implementar P4.1 → P4.2 → P4.3 → P4.4 sobre esta rama. Commits temáticos. No emitir un resultado `COMPLETED` sintético sin haber verificado bytes y catálogo.
4. Ejecutar pruebas, tratar incidencias, repetir matriz crítica y generar informe final. No proclamar que el escenario Windows está validado por pasar Linux.
5. Crear PR en borrador; esperar revisión para merge. Si surge una decisión de autor o una relajación de seguridad, **detener esa modificación** y documentar alternativas.
