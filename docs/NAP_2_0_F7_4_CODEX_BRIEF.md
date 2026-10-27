# NAP 2.0 — F7.4 · Encargo vinculante para Codex

**Fecha:** 27-10-2026.
**Repositorio:** robdor80/NAP. **Rama exclusiva:** chatgpt/nap-2-f7-4-git-ui.
**Base de creación comprobada:** main = c4a053226f6f27c0cf109ef89cf1a92be1117f36, merge real de PR #55.
**Estado:** PREPARADA POR CHATGPT; IMPLEMENTACIÓN NO INICIADA. PR Draft F7.4 abierta para desarrollo y posterior auditoría independiente.
**Responsables:** ChatGPT dirección/auditoría; Codex implementación, pruebas selectivas y documentación.

## 0. Orden inicial de comprobación

Antes de escribir código:
1. Comprueba directamente main, SHA actual, base de esta rama y PR Draft F7.4; no des por válidos commits copiados sin verificar. Si main cambió, documenta divergencia y detén cambios potencialmente conflictivos antes de decidir cómo proceder, sin hacer merge/rebase indiscriminado.
2. Lee íntegramente docs/NAP_2_0_F7_4_ARQUITECTURA_Y_AUDITORIA.md y docs/NAP_2_0_F7_4_MATRIZ_DE_ACEPTACION.md.
3. Relee docs/NAP_2_0_F7_1_GIT_ARCHITECTURE.md, docs/NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md, docs/NAP_2_0_F7_2_CODEX_BRIEF.md, docs/NAP_2_0_F7_3_CODEX_BRIEF.md, docs/NAP_2_0_F7_3_IMPLEMENTACION.md, docs/NAP_2_0_F7_3_F73_A1.md, docs/NAP_2_0_F7_3_F73_A2.md y docs/GIT_PRODUCTION.md.
4. Inspecciona implementación actual y tests antes de decidir nombres/contratos: src/NAP.App/Views/GitView.xaml y .xaml.cs; src/NAP.Presentation/{ProfessionalPages,ShellViewModel,ProfessionalUiServices,ProfessionalUiModels,UniverseOperationDispatcher,AutomationDesktopBridge,UiAsyncCommand}.cs; src/NAP.Core/{GitPublicationModels,GitPublicationPolicyStore,GitOutboxStore,GitPublicationManualService,GitBatchPublicationService,GitBatchPublicationPush,GitProductionInspector,GitProductionService,GitProductionSafety,AutomationUniverseRuntime,AutomationProtocol,AutomationHost}.cs; tests focalizados de Git/UI/IPC.
5. El trabajo se ejecuta exclusivamente en la rama indicada. Mantén la PR Draft, con nuevos commits informativos marcados [skip ci] cuando proceda. No toques main. No ejecutes Actions.

## 1. Objetivo

Construye la **interfaz gráfica definitiva de gestión Git** de NAP 2.0, RobStyle, sin duplicar GitView ni reemplazar el motor. El usuario debe poder **consultar**, **configurar conscientemente**, **revocar** y **supervisar** el sistema Git F7.1–F7.3, realizar operaciones manuales ya permitidas y gestionar reintentos/revisión de Outbox cuando exista autoridad legítima. Mostrar razones humanas y datos técnicos ampliables, sin exposición de secretos.

Implementa en subetapas internas continuas (no nuevas fases): A lectura/proyección/contratos, B editor/autorización/revocación, C panel Outbox/recovery, D WPF+IPC+pruebas+evidencias. Cada subetapa debe dejar contratos coherentes; una sola PR Draft F7.4.

## 2. Lo que existe y debes reutilizar

- GitView ya tiene inspección, commit manual, receipts nativos, RecoverGit, RetryGit, PushGit y confirmaciones separadas. Conserva la pantalla y la vía segura de estas llamadas.
- GitPublicationPolicyStore: Load() retorna null cuando política ausente; Propose() captura los vínculos del Git actual y retorna permisos false; Save(policy, expectedRevision) persiste la nueva revisión mediante CAS, HMAC y ancla Outbox; nunca escribas policy.json directamente.
- GitPublicationModels: los cuatro modos; los cuatro estados Outbox; todos los campos de PolicyV1, Intent, Batch, Snapshot. MaxJobsPerBatch 1–20 y prefijos 1–64, ordenados y únicos, sin paths especiales; el store es fuente de validación.
- GitOutboxStore.Read() devuelve ledger o snapshot vacío sin configuración; no editar el JSON para reintentar o «reparar» estados.
- GitPublicationManualService: PrepareCommit/Commit/RetryCommit/Recover/Push, con AllowCommit/AllowPush, bindings, locks y reconciliación Outbox para push nativo; no bypass.
- GitBatchPublicationService.RetryBlocked(AutomationQueueOwner, intentId) y RetryPublication(AutomationQueueOwner,batchId): sólo bajo owner real y permisos/estado compatibles. GitBatchPublicationPush incluye recuperación F73-A1/A2 y límites durables. No modificar estas semánticas.
- AutomationUniverseRuntime/AutomationUniverseScope mantienen autoridad por hilo; AutomationHost.RunManualAsync requiere paused/stopped y ausencia de trabajos Running. AutomationProtocol/AutomationDesktopBridge exponen interfaz remota autenticada con deduplicación durable de mutaciones; vía IPC no debe aceptar objetos arbitrarios ni inventar owner.

## 3. Contrato funcional detallado

### A. Estado Git y configuración

- En la pantalla Git, mostrar universo y binding actual, modo efectivo (Disabled por defecto y no un estado «sin definir» permisivo), repositorio anclado al ProductionRoot, rama, upstream, remoto (nombre, NO URL), sincronización factual, status y revisión actual. Estado Unknown/inaccesible explícito, no fingir «sincronizado».
- Inspección read-only disponible **sin tener AllowCommit** y sin crear grant. En el código actual GitPublicationManualService.Inspect exige autorización: proporcionar API read-only de configuración y una inspección nativa segura separadas de operaciones mutantes. La inspección remota sólo tras acción informativa del usuario, sin actividad Git automática en Disabled.
- Política ausente = Disabled; política corrupta, root cambiado, HMAC/ancla inconsistente o archivo parcial = STOP. No crear ficheros mientras se hace una lectura, no transformar errores en Disabled.
- Mantener selector provisional de Disabled, Manual, AutoCommitBatch, AutoCommitAndPushBatch. Una selección de modo **no persiste ni autoriza ni inicia un host**.

### B. Autorizaciones explícitas y revocación

- Editor con actor local explícito, prefijos permitidos y máximo Jobs 1..20; checkboxes AllowCommit y AllowPush independientes, con Push únicamente disponible si Commit y el modo Manual o AutoCommitAndPushBatch. Disabled desactiva ambas; AutoCommitBatch impide AllowPush.
- Prefijos relativos a ProductionRoot, estrictos y sin wildcard, traversal, .git, reparse, absolutos, duplicados ni scope universal implícito; no tratar prefijo como prueba de ownership ni sustituir ProductionAssetResult.
- Propuesta creada por **GitPublicationPolicyStore.Propose**, no por ensamblado arbitrario de hashes desde WPF; capturar revisión, AuthorizationId, identidad Git efectiva/fingerprint, rama, upstream, remoto, remote fingerprint y config fingerprint, además de roots/binding. **Revisión visible antes de aceptar** y firma/consentimiento local mediante confirmación propia. Mostrar fingerprints completos en vista técnica local si es apropiado, y no exponer URL con credenciales.
- Confirmación final inequívoca «Autorizar y guardar» con universo, modo, permisos y consecuencias. Si la revisión o alguno de los bindings cambió entre propuesta y guardado: caducar, fallar cerrado y solicitar nueva propuesta. Save únicamente por el store original con CAS expectedRevision y nuevo AuthorizationId; jamás modificar manualmente policy.json, Outbox o sus claves HMAC. La interfaz no concede permisos desde la existencia de tokens/GCM ni desde consentimiento de procesamiento ZIP.
- Revocación mediante botón separado Danger y confirmación específica, accesible desde una política válida incluso si el remoto no puede inspeccionarse. Debe construir revisión siguiente sobre política autenticada anterior, con ID nuevo, actor/fecha de revocación, Mode Disabled, AllowCommit=false, AllowPush=false, Revoked=true; guardar mediante Save+CAS, conservando historial y evidencia. No depende de Propose/Inspección remota. Si configuración corrupta, CAS falla o writer Busy, mostrar STOP y no afirmar que fue revocado.
- Una revisión nueva no resucita Pending/Committed de autorizaciones antiguas ni migra su AuthorizationId; no «arreglar» batches viejos cambiando policy revision o intent hashes. Mostrar los pendientes antiguos y la intervención requerida.

### C. Supervisión Outbox y recuperación

- Panel bajo GitView con contadores y filas humanas por estado: Pending («Pendiente de commit»), Committed («Commit local; remoto pendiente»), Pushed («Publicación acreditada según receipt»), Blocked («Revisión necesaria»). Las etiquetas no alteran estado durable.
- Separar **intenciones por Job** y **lotes**: número exacto de Jobs en lote, IDs, diagnóstico, revisión/autorización, OperationId, PlanHash y CommitSha si existen. Expander «Detalle técnico» con copiado opcional de identificadores saneados, sin roots absolutos innecesarios ni URL Git. Tablas virtualizadas o lista acotada, y refresco asincrónico con preservación de selección por ID.
- Mostrar compatibilidad con receipts nativos, no equiparar receipt Pushed con tip remoto actual. Una operación Blocked indica razón legible y el siguiente paso: inspección, revisar configuración/identidad, conceder autorización nueva para acciones futuras, publicar manualmente cuando proceda o revisión administrativa.
- Exponer «Actualizar», «Ver recibo», «Recuperar» (read-only nativo cuando proceda), «Reintentar commit», «Publicar manualmente» y, cuando haya autoridad, «Reintentar operación bloqueada» / «Reintentar publicación». Confirmación específica antes de **cada mutación**. Reintentos de lotes sólo usando los métodos existentes, con owner legítimo y presupuestos exactos. La ausencia de owner es un bloqueo, NO una razón para usar un runner paralelo.
- Para host: preferir una mediación mínima en AutomationDesktopBridge/AutomationProtocol con payloads tipados acotados (universo, ID, referencia/revisión, intención exacta), IPC autenticado y deduplicación durable. Dispatch hacia AutomationUniverseScope.Owner mediante operación legítima y quiescente. No aceptar policy.json, paths arbitrarios ni GitOperationId inventados de clientes como permiso suficiente; volver a consultar store/autorización.
- No realizar acciones mutantes cuando un host está ejecutando trabajo o la cola no está quiescente. El código actual de host limita el owner a **laboratorio**; mantener reserva explícita y no abrir el host productivo en F7.4. Si una acción no puede exponerse seguramente en despliegue real, mostrar disabled con mensaje e informar de la limitación técnica exacta en la entrega; nunca simular éxito ni degradar seguridades.
- AutoCommitBatch: explicar RequireNoPending: un nuevo lote puede quedar esperando mientras haya receipt Committed anterior. No cambiar ese invariante, la política, ni la máquina de estados para ocultarlo.
- Recuperación F73-A1/A2: no revalidar/remotamente si el mecanismo de reconocimiento local durable Pushed lo prohíbe o si agotó presupuesto, no conceder cuarto push ni consultas remotas extras; no replanear, rehacer commits, borrar finales u orfandades ni resetear refs.

### D. RobStyle y MVVM

- Ampliar src/NAP.App/Views/GitView.xaml (no segunda pantalla) con RobTitledFrame y estilos existentes; configuración, inspección, Outbox y operaciones agrupadas con jerarquía visual clara. Las operaciones peligrosas no deben parecer una opción habitual.
- ViewModels tipados en src/NAP.Presentation, sin lógica de Git CLI, sin acceso directo a archivos de policy/outbox, sin almacenar credenciales. Si hay un modelo nuevo GitUiStatus/GitOutboxRow/GitAuthorizationDraft, usar nombres/documentación consistentes y proteger invariantes.
- Integrar en ShellViewModel e IProfessionalUiService métodos async no bloqueantes, CancellationToken, detección de cambio de universo y generaciones para descartar resultados tardíos, Clear/Reset de UI, refresco incremental tras acciones y estados Busy/Stopped/ReadOnly. Mostrar razones DisabledReason precisas, no ocultar el fallo real bajo un tooltip genérico.
- Retener ConfirmAsync vigente para commit y push por separado. La previsualización y consentimiento Git son independientes también del consentimiento ZIP. No marcar UI «operación realizada» antes de recibir resultado del Core.
- Compatibilidad con WPF en Windows 11 y escalados típicos del MSI Raider; no romper tamaños mínimos, alineación, tabulación de teclado ni lectura de TextBlock largos.

## 4. Límite de cambios permitido

Puedes modificar GitView.xaml(.cs solo si imprescindible), ProfessionalPages/GitViewModel, ShellViewModel, ProfessionalUiServices, modelos UI necesarios y mínimos adaptadores read-only/autoridad Core, AutomationProtocol/AutomationDesktopBridge para IPC estrictamente necesario, y tests focalizados. Si detectas que hay que modificar un contrato sensible de GitProductionService, GitPublicationPolicyStore.Save/Validate, GitOutboxStore.Validate, GitBatchPublicationService, concurrencia o HMAC, **detén ese cambio**: justifica necesidad, impacto, tests y consulta a ChatGPT antes de alterar la frontera. No rediseñar el motor ni UI global.

NO cambiar GitProductionReceiptStore, no borrar/readoptar intents/receipts, no ampliar retry budgets, no bypass de OriginalAuthorization, no modificar RequireNoPending, no ejecutar Git CLI mutante desde WPF, no duplicar policy/Outbox caches autoritativos ni permitir host IPC una escritura fuera de su autoridad. Job/Queue Completed independientes de Git.

## 5. Pruebas focalizadas autorizadas

Usa docs/NAP_2_0_F7_4_MATRIZ_DE_ACEPTACION.md. Prioriza **pocos tests nuevos y controles mínimos relevantes**: política por defecto Disabled; cuatro modos; confirmaciones y CAS/TOCTOU; AllowCommit/AllowPush independiente; revocación incluso sin remoto; bloqueo por ancla corrupta; mapeos Outbox y NO mutación por lectura; selección por ID; disabled reasons; invalidación de universo; IPC no bypass; un caso de retry autorizado con bare local y uno de rechazo original/revocación. No es necesario repetir la suite F7.3.

Usar fake services para UI y remotos bare locales sólo donde necesites demostrar comportamiento Git. Máximo 1–2 ZIP sintéticos por escenario salvo necesidad justificada (jamás estrés/volumen). NO ejecutar dotnet test sin --filter; NO ejecutar NAP CI, workflows, suite general, tests Git sobre GitHub/Nimroel/NAP reales o credenciales del MSI. Puedes compilar componentes pertinentes sin automatizar CI. Linux/Debug no certifica compilación visual WPF/Windows; reporta explícitamente lo no ejecutable.

Para cada prueba: nombre/filtro, SHA código probado, entorno, resultado Passed/Failed/Skipped, duración si confiable, fakes o bare temporal, números reales de ZIP, precondiciones y ausencia de writes indebidas en negativos; TRX/logs saneados e incidencias. No ocultar warnings NU1902/NU1903 ImageSharp ni reservas de Release/licencia.

## 6. Entregables antes de devolver control a ChatGPT

1. Código implementado y commits sólo en chatgpt/nap-2-f7-4-git-ui; PR Draft actualizada sin fusionar.
2. docs/NAP_2_0_F7_4_IMPLEMENTACION.md: interfaces/cambios por fichero, APIs, concesión/revocación, revocation race, read-only, permisos, límites de owner/IPC y comportamiento de fallback ausente.
3. docs/NAP_2_0_F7_4_PRUEBAS.md + evidencias trazables sanas: filtros, resultados, hashes, entorno y reservas. Si falta Windows/WPF, indicarlo, no suponerlo aprobado.
4. Resumen de casos de aceptación y negativos, tabla con protección F7.1/F7.2/F7.3 conservada y riesgos abiertos.
5. Entrega final a ChatGPT con enlace PR, HEAD entregado verificable, SHA exacto de código probado, decisiones no triviales, TODOs e incidencias. No declarar F7.4 completa/aceptada antes de auditoría independiente.

## 7. Restricciones absolutas

**NO NAP CI ni otros workflows. NO cambios directos en main, merge, CUTOVER, instalación/configuración Git productiva, acciones Git contra repos reales, credenciales/licencias/permisos MSI, grandes baterías ni avance a otra fase.** La PR queda Draft hasta autorización expresa.

Las incidencias #47 y #49 y los avisos ImageSharp/Release siguen abiertos. No equiparar Linux/Debug ni fixtures con validación Windows 11/WPF/NTFS/ACL/MSI/producción.
