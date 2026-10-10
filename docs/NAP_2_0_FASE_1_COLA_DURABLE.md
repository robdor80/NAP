# NAP 2.0 — Fase 1: contratos y cola durable

Implementación sobre `main` tras PR #42. Documento rector: [Fase 0 y adenda 0B](NAP_2_0_FASE_0_AUDITORIA.md). No se modifica su auditoría histórica ni las decisiones aprobadas.

## Alcance real

Se incorpora un **ledger pasivo** portable .NET 8 en NAP.Core: cola SQLite por universo, batches/observaciones, intentos con JobId preasignado, reservas, claims, eventos, políticas/versiones/autorizaciones, procedencia y decisiones excepcionales. No se integra en App ni en ShellViewModel. No hay watcher, admisión de ZIP, scheduler, host, IA remota, transformación, conversión, ejecución productiva, Git automático, limpieza ni perfiles nuevos.

`Observe` registra metadata suministrada por el llamador (identidad SHA-256 y tamaño del ZIP), sin abrir/hash/copia/admisión del archivo. La Fase 2 debe calcular y comprobar esa identidad mediante su admisión segura. Un registro de observación no declara validez del contenido ni conservación física. No hay deduplicación por PNG, adopción de assets ni recovery de producción.

## Contratos e interfaces

- `AutomationContracts.cs`: estados, códigos tipados, QueueItemId/WorkflowAttemptId/QueueBatchId, QueueItem, WorkflowAttempt, AssetReservation, QueueEvent y Batch. Reutiliza UniverseId, UniverseStorageConfig, UniverseAssetKey, JobId y Sha256Digest.
- `AutomationPolicyContracts.cs`: AutomationPolicyDefinition/StoredAutomationPolicy, PolicyReference, autorización global, presupuestos, políticas por perfil, RationalProportion, EffectiveRuleSnapshot, NormalizationAuthority/Lineage y ExceptionalConfigurationDecision.
- `AutomationQueueStore` implementa `IAutomationQueueStore`, `IAutomationPolicyStore` e `IAutomationEvidenceStore`. Todas las APIs mutantes requieren `AutomationQueueOwner`; las lecturas no requieren propietario ni crean base de datos.
- `AutomationQueueLedger.cs` persiste políticas, batches, intentos/reservas; `AutomationQueueEvidence.cs` persiste evidencia y verifica consistencia lógica; `AutomationQueueSchema.cs` define schema y boundary; `AutomationValidation.cs` comparte validaciones/codificación.

## Base de datos y versionado

Ubicación calculada exclusivamente desde settings: `StateRoot/automation/AutomationQueue.db`. No usa nombres de universos para derivar rutas. Schema independiente **v1** (`PRAGMA user_version=1` + metadata), sin cambios al journal Job v1 ni AssetCatalog.db. No migración automática de schemas desconocidos.

| Tabla | Contrato persistido / restricción |
| --- | --- |
| queue_metadata | Universo, raíces congeladas, versión, epoch de propietario y policy actual; una fila. |
| policies / policy_history | Hash canónico, ID/version únicos; definiciones inmutables, estado/autorización/revisión y snapshots de cada cambio. |
| items | ZIP hash único dentro de esa DB/universo, estado, revisión, batch/policy FK y payload completo. |
| observations | Alias de rutas/cohortes al item; unique item+ruta+batch. Repetir scan no añade observaciones idénticas. |
| batches | Cohortes explícitas, fechas de apertura/cierre y lectura de resumen; no crea lote automáticamente. |
| attempts | JobId único, secuencia por item, parent attempt, profile snapshot opcional, paths relativos/evidence; un solo intento Active por item. |
| reservations | AssetId único en universo, propietario item/attempt/Job y hash semántico declarado. Un asset por item/attempt. |
| events | Secuencia contigua por item, etapa/código/resultado/estado/incidencia/attempt. |
| lineage / decisions | Identidades y versiones append-only, FK a item/attempt; relaciones y decisiones inmutables salvo transiciones versionadas previstas. |

Los campos indexados están normalizados para constraints/queries; los payloads JSON canónicos conservan el contrato tipado completo. Al abrir se comprueban schema exacto (incluidos índices y ausencia de triggers ajenos), quick_check, foreign_key_check, scope/raíces, enums, hashes/referencias, JSON estricto sin duplicados/campos desconocidos, relaciones y coherencia de eventos/historial. No se promueven ni reparan registros. Un JSON/perfil/evidencia incongruente falla con error tipado.

SQLite usa foreign_keys ON, DELETE journal, synchronous FULL, temp_store MEMORY, timeout limitado y Pooling=false. Creación inicial en temporal propio, transacción completa, flush y rename no-overwrite. Temporales de inicialización fallida quedan sin adoptar; se revisarán explícitamente, no se limpian archivos ajenos. WAL/SHM inesperados, DB/directorios/ancestros reparse, casing incompatible, roots solapadas o workspace de red se rechazan. Solo se crean StateRoot, automation y DB propia; no se inspeccionan/mutan assets de ArchiveRoot/ProductionRoot.

La validación lógica completa al abrir prima fail-closed sobre coste: aún no se certifica rendimiento de ingesta continua de 1.000 paquetes. La optimización posterior debe preservar esas invariantes. Las lecturas/escrituras cooperantes usan exclusión breve de I/O; Busy es diagnosticable, no un bloqueo indefinido.

## Transiciones, eventos y ownership

| Desde | Destinos permitidos |
| --- | --- |
| Observed | WaitingStable, Queued, NeedsReview, Rejected, Duplicate, MissingSource |
| WaitingStable | Queued, NeedsReview, Rejected, MissingSource, Duplicate |
| Queued | Running exclusivamente mediante Claim; NeedsReview, Rejected, Duplicate, MissingSource |
| Running | RetryScheduled, NeedsReview, Rejected, Completed, MissingSource; requiere claim exacto |
| RetryScheduled | Queued cuando llega fecha; NeedsReview, Rejected, MissingSource |
| NeedsReview | Queued, Rejected, Duplicate, MissingSource |
| MissingSource | WaitingStable, NeedsReview, Rejected |
| Completed / Rejected / Duplicate | Terminales; no reset implícito |

Estado, revisión y evento se escriben en **la misma transacción**. Cada mutación del item añade un evento; su secuencia final equivale a revision+1. `RecordProgress` registra una etapa tipada bajo claim sin inferir efectos físicos. NeedsReview/Rejected/MissingSource requieren motivo; RetryScheduled requiere UTC futura y conserva count. QueueState no reemplaza JobState. Un registro Completed en este ledger no demuestra por sí solo ejecución real: las fases posteriores deben aportar la verificación de sus contratos antes de solicitarlo.

`AcquireOwner` obtiene mutex nombrado por StateRoot y mantiene propiedad exclusiva de duración de sesión. Epoch durable nuevo en cada adquisición; no hay TTL que robe una instancia viva. Owner es **thread-affine** como el ExecutionMutex existente: adquirir, operar y liberar en el mismo thread, también al integrarlo posteriormente con async. No mantener este lease a través de un cambio arbitrario de thread. Otra instancia puede leer cuando no hay operación en curso; otra adquisición falla Busy. Cada mutación usa CAS de revisión y token/epoch cuando corresponde.

`CreateAttempt` persiste una sola vez el JobId, sin crear su journal. Puede quedar sin perfil para una incidencia ProfileUnknown; `BindProfile` fija una regla completa una sola vez, manteniendo JobId. Claims productivos requieren intento activo + reserva; no basta Queued. Mismo AssetId/hash distinto da AssetCollision; hash igual con otro workflow da ReservationOccupied. Dedupe de ZIP retorna el item existente sin segundo Job. La igualdad de hash de ZIP con tamaño contradictorio se rechaza.

Nuevo propietario tras restart no ejecuta automáticamente un Running antiguo: solo `ParkInterruptedClaim` permite aparcar el claim de epoch anterior en NeedsReview/InterruptedClaim. Se conservan intento/reserva. La resolución/liberación de reserva es explícita, solo para trabajo parado/terminado y **no autoriza descartar ni adoptar efectos físicos**. Un nuevo intento requiere cerrar el anterior y resolver su reserva; se enlaza al parent. La Fase 4 deberá verificar efectos antes de usar estos actos de ledger.

## Políticas y autoridad

Default persistido: Disabled, operaciones None, sin autorización ni perfiles de normalización. `Enabled` existe como representación futura pero se rechaza tanto al cambiar estado como al cargar schema de esta fase; `IsExecutionAuthorized` siempre false. Registrar autorización es registrar una declaración de responsable, sin activar ejecución. No hay selector/UI para crearla en App.

Hash SHA-256 de definición canónica incluye ID, versión, universo, roots, operaciones, budgets y normalización. Una versión no se reemplaza; cambiar contenido exige nueva versión/hash y autorización nueva compatible. Authorization enlaza el hash exacto, actor y UTC. Se guardan historial, pausa y revocación terminal con revisión; policy antigua permanece como snapshot de items/evidencia. Nuevos items toman la policy actual. La activación compatible y la evaluación operativa del permiso corresponden a fases posteriores.

Autoridad de normalización distingue `HumanDecision` (decisión individual, sin PolicyReference) y `AuthorizedPolicy` (policy exacta con declaración global registrada y parameters idénticos). No usa ni falsifica ApprovedByUser y deja recibos humanos v1 intactos. Revocación impide nuevas declaraciones bajo esa policy, conservando historial anterior.

ProfileNormalizationPolicy reutiliza ImageNormalizationPolicy.Validate: área y ejes independientes 0–500 basis points, píxeles input/canvas, bytes archivo/paquete, entries, presupuesto de memoria aparte y método `nearest-edge-canvas-v1`. OperationallyValidated representa validación operativa pendiente; no la supone. Los 300 bp retrato y 100 bp cartografía son propuestas representables, no defaults activos ni permiso para superar ejes/recursos. El canon aprobado está representado como ratios; para policies Nimroel se rechazan ratios distintos en portrait_npc/scene_cartography/scene_narrative. El canon no crea/activa narrativa, sellos ni Star Trek.

## Procedencia y excepciones

NormalizationLineage enlaza operation/item/attempt/universe/Job, originales ZIP/PNG y derivados PNG/ZIP con hashes/tamaños/referencias, geometría, parameters, autoridad, evidence y estados. Proposed puede avanzar una vez a DerivativeDeclared/Rejected/Cancelled mediante nueva versión; originales y autoridad no se cambian. DerivativeDeclared es **declaración de metadata**, no comprobación física de archivos. Geometry se valida con el cálculo mínimo existente, sin generar imágenes.

PreservationReference puede estar ausente. Hash o referencia nunca sustituye conservación física. No se fija ni crea namespace originals: ubicación, backup y retención siguen pendientes de decisión técnica conforme a Fase 0B. La Fase 3 establecerá la preservación verificable y la Fase 4 reconciliará efectos.

EffectiveRuleSnapshot contiene perfil declarativo completo (bytes, hash, versión, universo y par type/profile); reutiliza UniverseProfileLoader, exige routing y conversion compatibles. Un ratio suelto no crea regla. ExceptionalConfigurationDecision registra profile original, regla efectiva, racional reducido y dimensiones que coinciden con conversion, preview hash, scope, actor/UTC y estado/version. Proposed→Approved/Revoked y Approved→Revoked; editar parámetros exige otra identidad/propuesta. Persistirla no cambia profile.json ni el profile del intento, ni activa propuesta reutilizable/global. RecordDecision exige item aparcado. Resolver perfil ausente es una vinculación explícita posterior, no inferencia automática por ratio.

## Uso futuro desde Fase 2

Las interfaces permiten registrar metadata/aliases, batches, incidencias UniverseMismatch, intentos y snapshot de políticas bajo scope inmutable. No aceptar universe declarado distinto del Inbox: la Fase 2 debe compararlo al extraer/leer manifest y registrar Rejected/UniverseMismatch sin traslado. Este store rechaza contratos suministrados con UniverseId ajeno. Los futuros servicios calculan hashes y validan paquetes; este store no los admite.

Fase 3 usará perfil/evidencias y autoridad; Fase 4 evaluará permisos/PASS y verificación antes de completar; Fase 5 compondrá lifecycle respetando ownership thread-affine. Ninguno está implementado en esta entrega.

## Validación

- 47 pruebas nuevas AutomationQueueTests, fixtures sintéticos: reapertura, eventos/CAS/transiciones, rollback antes de commit, JobId estable, dedupe y alias, reservas, owners concurrentes y mutex en proceso PowerShell independiente, dos universos, políticas/versiones/historial/revocación, authority, lineage/decisiones, perfil no resuelto, corruption/schema/JSON y symlinks/WAL fail-closed, invariancia byte-exact de journals/catalog/archivos ajenos.
- Regresión focalizada: **407 aprobadas, 0 fallidas, 0 omitidas** (incluye las nuevas y JobState*, UniverseStorageIsolation*, ImageNormalizationGeometryTests y CatalogBoundaryTests).
- Compilación Release de NAP.sln, incluido targeting WPF: correcta. Persisten las cinco advertencias NuGet preexistentes sobre ImageSharp 3.1.12, sin actualizar dependencias.
- Entorno de ejecución: Linux con .NET 8.0.425 y PowerShell 7.4.13. No se ejecutó suite completa ni WPF/Windows. Validación Windows de mutex/links/lifecycle y futura UI queda pendiente; build Windows-targeted no es ejecución Windows.
- Falta certificar caídas eléctricas reales y filesystem Windows; las interrupciones de transacción se simularon antes de commit y se comprobó reapertura, además de claims durables tras nueva sesión. No se declaran verificaciones físicas de producción.
