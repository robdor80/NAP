# NAP 2.0 — Fase 2: monitorización y admisión segura de Inbox

Base: `main` tras PR #43, `84c8f65`. Rama: `codex/nap-2-fase-2-inbox-admission`. Fecha: 2026-10-10.
Autoridad: [Fase 0/0B](NAP_2_0_FASE_0_AUDITORIA.md), secciones 9 y 11, y [Fase 1](NAP_2_0_FASE_1_COLA_DURABLE.md). La [Master Specification](NAP_MASTER_SPEC.md) conserva las 17 fases históricas y afirmaciones point-in-time; no reemplaza el plan NAP 2.0 de siete fases. Ejemplos históricos que exigen nombre ZIP=AssetId, selección de universo UI o aprobación humana para toda normalización no gobiernan esta admisión. Se mantienen los formatos vigentes Manifest v2, Profile v1–v4, journal Job v1 y catálogo existente.

## Alcance implementado

Componentes portables .NET 8 en Core, sin nuevas dependencias:

- `InboxAdmissionMonitor`: watcher + reconciliación periódica, múltiples contextos explícitos, pending acotado, debounce, diagnóstico por universo y cierre cancelable.
- `InboxAdmissionService`: readiness, identidad real, admisión de transporte/envelope, snapshot privado, extracción, universo y reserva/reconciliación de JobId.
- `InboxAdmissionEvidence`: receipts inmutables encadenados, publicados con CreateNew/Flush(true)/rename sin overwrite y vinculados al intento en SQLite.
- `InboxAdmissionContracts`: opciones, códigos de resultado y checkpoints tipados.
- `AutomationQueueAdmission.RecordAdmissionEvidence`: actualiza `WorkflowAttempt.ExpectedEvidence` y añade evento de Admission en la misma transacción, con owner/revisión/attempt exactos; no cambia DDL ni payloads existentes. Ese campo enlaza el hash del último receipt de admisión mientras el intento está en esta etapa.

Se reutilizan UniverseContext/UniverseStorageConfig, detector, readiness, stager, extractor/CRC, Manifest v2 loader, SHA-256, ProductionPaths, JobStateStore, ExecutionMutex y cola de Fase 1. El stager incorpora un adapter interno para copiar desde el handle retenido por admisión, compartiendo su bucle de copia; su API manual conserva readiness y resultados. El detector conserva snapshot/orden de API pública y expone enumeración interna streaming para el monitor.

No se integra en App ni ShellViewModel: el caller debe crear el componente y llamar `Start()`. No se activa un host productivo, watcher al arrancar la app, IA, PASS, normalización, WebP, archive, catálogo, producción, reservas productivas ni Git. La Fase 5 compondrá este monitor con el lifecycle de App. La automatización productiva continúa Disabled y Enabled sigue prohibido.

## Monitorización y aislamiento

Cada contexto fija `UniverseId`, perfil y las tres raíces configuradas. `InboxRoot` se deriva exclusivamente de settings, nunca del nombre del universo. Se valida aislamiento del conjunto, unicidad y boundaries. Misma identidad de asset o hash ZIP en diferentes universos no se fusiona.

Un watcher por Inbox, sin recursión ni dependencia de pantalla/selector. Created/Changed/Deleted/Renamed son avisos; Error/overflow solicita rescan. Las notificaciones se coalescen en una bandera por scope, sin cola ilimitada de eventos. Un cursor streaming de rescaneo alimenta una FIFO de hasta `MaxPendingCandidates` (default 64). No se pierde el resto del directorio cuando se alcanza ese límite: el cursor continúa al vaciar espacio. Inicialización, periodicidad (default 30 s) y petición explícita recuperan eventos perdidos. La periodicidad puede arrancar aun con eventos continuos que prolonguen debounce.

Un thread dedicado adquiere, utiliza y libera todos los owners, manteniendo thread-affinity. Las tareas de readiness/copia/extracción se esperan desde ese thread; callbacks del watcher no usan SQLite. Admisión secuencial round-robin entre scopes, vigilados simultáneamente; una operación lenta retrasa otras admisiones pero no acumula bytes/imágenes en memoria. Ningún worker consume los items Queued. Contención entre instancias produce Busy/Blocked en el ámbito, nunca robo por TTL.

Corrupción de cola/schema, pérdida de boundary compartido o errores de ownership bloquean el universo. Otra Inbox independiente sigue. Errores de un paquete se aparcan/rechazan individualmente. No se reinicia silenciosamente un scope bloqueado.

## Protocolo de productor, estabilidad e identidad

Recomendado: copiar `nombre.partial` y renombrar a `nombre.zip` al finalizar. `.partial` nunca se admite; el rename genera un aviso y el rescan también lo descubre si ese aviso se pierde. NAP no renombra, mueve, borra ni modifica el origen.

Copia directa: tres muestras en cinco segundos por defecto, apertura exclusiva de readiness, handle read-only sin compartir escritura durante hash/copia, preflight de central directory/end record con límites, extracción completa con CRC y comparación SHA/tamaño del source y del snapshot. Se verifica de nuevo el path original, además del handle, después de copiar. No basta tamaño/mtime estable. Copia cerrada entre ráfagas pero truncada permanece WaitingStable sin intento/journal; tras gracia configurable (default cinco minutos por identidad de bytes) puede quedar Rejected/InvalidZip. Bytes nuevos constituyen otro contenido, no resucitan el registro rechazado.

**Límite explícito aprobado en la auditoría:** ningún heurístico prueba la intención de un productor que cierra/reabre o sustituye un ZIP ya completo. Para esa garantía hace falta `.partial`→`.zip` o señal final externa. Se admite exclusivamente un snapshot completo verificado; no se promete conocimiento absoluto de la finalización del productor. Compartición y locks también requieren validación Windows real.

SHA-256 streaming y longitud real identifican transporte exacto. Unique hash por DB/universo enlaza observaciones/aliases, sin segundo intento/Job al reescanear. Nombres diferentes con bytes iguales comparten item; mismo nombre con bytes distintos genera otro item. El nombre ZIP no contiene necesariamente universo o asset. No se deduplica por mtime, nombre, PNG o equivalencia semántica. Reempaquetados distintos siguen siendo transporte distinto; la reserva/dedupe semántica corresponde a Fase 3.

Una observación no acredita admisión completa. `Queued` permite reservar el intento preparatorio según Fase 1; el consumidor futuro debe exigir receipt final `Admitted` verificado, además de sus propios permisos/validadores. Mientras se escribe un intento puede haber Queued con checkpoint inicial; no es trabajo productivo autorizado. `WaitingStable` puede existir sin intento para contenedor incompleto. El hash de esa copia parcial se conserva como observación histórica aunque luego llegue contenido nuevo.

## Staging y receipts

Rutas bajo `StagingRoot/automation-admission/<QueueItemId>/operation_<id>/snapshot/package.zip` y `.../extracted/<asset_id>/`. La operación y sus paths se reservan en el intento antes de crear/copiar archivos. El nombre privado fijo no cambia bytes del ZIP. Extracción usa el motor existente y luego publica su directorio propio con nombre canónico obtenido del manifest; el origen queda intacto.

Receipts bajo `StateRoot/automation/admission/<QueueItemId>/<WorkflowAttemptId>/000001.json` etc. Incluyen universo, item, intento, JobId, hash/tamaño ZIP, path origen, paths relativos, checkpoint, UTC, hash previo, inventario de archivos SHA/tamaño, universo declarado, asset_id y código. No secretos ni mensajes de excepción como reglas de estado. Hash del último receipt enlazado a `ExpectedEvidence`; revisions/events permanecen transaccionales en SQLite schema v1.

Orden: Intent → SnapshotReady → ExtractionReady → JournalIntent → Admitted. Rejected/NeedsReview son finales alternativos. Cada receipt se publica durable antes de enlazarlo a la cola. No se afirma transacción distribuida: una caída entre filesystem y SQLite deja evidencia sin binding y se aparca; nunca se adopta por filename.

Se rechazan links/reparse en origen, roots/ancestros y artifacts; nombres/dispositivos peligrosos, traversal, ADS, colisiones, entradas especiales y ZIP con límites/CRC inválidos por el extractor vigente. El snapshot se compara con la identidad observada. Los archivos extraídos se flushean y se inventarían antes de registrar ExtractionReady. Inventario y snapshot se verifican de nuevo al reabrir. No se considera durable un final solo porque exista o tenga el hash esperado sin su receipt vinculado.

Envelope de admisión: directorio flat, un manifest canónico v2 estricto y Naming válido; universe declarado coincide con Inbox antes de usar su perfil. ZIP cruzado registra Rejected/UniverseMismatch, expected/declarado/origen/snapshot/inventario; no crea journal. Regla desconocida queda NeedsReview/ProfileUnknown, sin improvisación ni journal. Se registra un JobId del intento para continuidad de metadata, que no autoriza producción ni se vuelve journal para un ZIP cruzado.

No se validan todavía PNG, archivos por perfil, clasificación semántica, proporción, regla efectiva completa o plan de publicación. `Admitted` significa transporte/envelope seguro para Fase 3, **no asset validado ni PASS**. Fixtures de volumen contienen metadata válida y payloads sintéticos de transporte; no se certifica producción de imágenes con esas pruebas.

## JobId y recuperación

CreateAttempt reserva una sola vez JobId antes de cualquier journal. JournalIntent vinculada precede Create/Load del journal bajo mutex Job; se comprueba que no exista un journal antes de publicar esa intención. Un journal preexistente sin ella no se adopta, aunque declare el mismo ID/estado. Se aceptan únicamente DETECTED/STAGED para terminar esa etapa; se transiciona a STAGED cuando snapshot/extracción se verificaron. Una admisión final no recrea un journal perdido y permite leer checkpoints posteriores coherentes sin reejecutarlos. No hay salto a VALIDATED ni nuevos campos de journal v1.

| Interrupción / cambio | Resultado implementado |
| --- | --- |
| Antes de observación | Rescan vuelve a identificar bytes; ningún Job. |
| Observación registrada, sin intento | Continúa preparación del mismo item tras readiness; incomplete sigue esperando. |
| Intento reservado sin receipt ligado | NeedsReview/InvalidEvidence; conserva JobId, no asigna otro arbitrariamente. |
| Intent ligado sin filesystem propio | Recopia solo source del hash/tamaño exactos, con mismo intento/Job. |
| Intent con artifacts sin SnapshotReady | NeedsReview; no adopta final/partial ni borra residuos. |
| SnapshotReady ligado, extracción ausente | Verifica snapshot y extrae; no requiere Inbox original para seguir desde ese snapshot. |
| Extracción/final presente sin ExtractionReady | NeedsReview; ningún rename/adopción por nombre. |
| ExtractionReady ligado | Inventario exacto, universo y manifest revalidados; continúa al mismo journal. |
| Journal creado, todavía JournalIntent | Load verifica identidad/universo/estado; termina STAGED sin otro Job. |
| Receipt publicado sin binding SQLite | NeedsReview por publicación ambigua; conserva archivos. |
| Source desaparecido antes de snapshot | MissingSource; mismos bytes al regresar permiten reanudar, distintos a NeedsReview/SourceChanged. |
| Snapshot/inventario/receipt corrupto | Incidencia individual; no reconstrucción/adopción silenciosa. |
| Journal/cola incoherentes o boundary compartido perdido | Bloqueo seguro o incidencia de evidencia según ámbito; nunca producción. |

Inicialización del monitor reconcilia intentos de admisión Queued/MissingSource antes de escanear Inbox, también sin source si existe snapshot ligado verificable. No recupera intentos de otros workflows por parecido. No interpreta/desbloquea Running de producción ni realiza recuperación de Fase 4. Operaciones con evidencia ambigua quedan aparcadas para revisión futura; un rescan no las aprueba.

Temporales del stager/extractor creados en la invocación tienen su limpieza local existente. Residuales tras fallo/caída, snapshots, receipts y namespaces desconocidos se conservan. No hay retención automática. Ubicación/backup de conservación permanente del PNG original sigue pendiente para Fase 3: un snapshot de admisión no certifica el protocolo de preservación original/derivación ni su respaldo.

## Límites y optimización conservadora

Defaults: ZIP 256 MiB; extractor hasta 10.000 entradas, 512 MiB por entrada, 2 GiB total; retenido de admisión 2 GiB, mínimo libre 256 MiB; pending 64. Las opciones no pueden ampliar los techos críticos del extractor. Se comprueban tamaños declarados y reales. Presupuesto retained suma artifacts bajo el namespace de admisión (sin borrar desconocidos), más ZIP y extracción nuevos; espacio libre se comprueba antes de copiar. El crecimiento de receipts/cola también consume disco: fallar al persistir bloquea el scope. Sin decoder ni buffers de imágenes de lote.

El presupuesto no reserva espacio frente a procesos externos: ENOSPC real sigue posible y se trata con diagnóstico y evidencia conservada. La falta de espacio se inyecta en tests; no se agotó hardware real. Filesystem controlado excluye modificaciones externas maliciosas del staging durante una operación. Links existentes se rechazan; no se afirma defensa kernel frente a administrador que cambie todos los archivos concurrentemente.

La Fase 1 validaba schema, integridad y todas las filas en cada Open. Se evita repetir **validación lógica de bytes idénticos** mediante SHA-256 del archivo DB previamente validado. Cada contenido cambiado, incluidos commits propios, exige validación completa; hash antes/después detecta cambio durante esa validación. Boundary, schema, quick_check y FK siguen comprobándose en cada Open. No se usa mtime/size para confiar en estado y no se marca una escritura nueva como validada sin inspeccionarla. Cache de proceso, no de archivo persistido ni modificación de schema. Se mantiene mutex I/O y ownership. Sigue habiendo coste proporcional al historial para cada contenido nuevo; las métricas no justifican el rendimiento continuo de decenas de miles de items.

## Pruebas y resultados

Compilación completa `dotnet build NAP.sln --configuration Release --no-restore -m:4`: 0 errores y cinco advertencias NuGet preexistentes de ImageSharp 3.1.12. Sin warnings nuevos ni cambios de dependencias.

Suite disponible en Linux, separando únicamente la categoría de volumen: `dotnet test NAP.sln --configuration Release --no-build --filter 'Category!=AdmissionVolume'`: **3.488 aprobadas, 0 fallidas, 4 omitidas, 3.492 totales**. Incluye 39 casos nuevos de admisión y todas las regresiones existentes (AutomationQueue, readiness/stager/extractor, ZIP adversarial, boundaries, aislamiento y UI/pipeline manual portable). Las cuatro omitidas pertenecen a VisualExplorerWindowsSmokeTests y requieren Windows/WPF; no se declaran superadas.

Los casos nuevos verifican aliases, nuevo contenido bajo el mismo nombre, rename real con watcher, copia en uso/cambiante, pausa de truncado superior a la ventana default de cinco segundos, manifest/CRC/traversal/symlinks, universos independientes/ZIP cruzado, perfiles desconocidos, ownership entre instancias, overflow señalado y rescaneo, presupuesto/ENOSPC inyectado, fuentes desaparecidas/cambiadas, corrupción individual frente a shared DB, residuos desconocidos, interrupciones en nueve checkpoints, rollback de binding, JobId estable y no adopción/recreación de journals. Los ZIP y sentinels nuevos son exclusivamente sintéticos en temporales; las regresiones usan sus fixtures aisladas existentes. Sin servicios IA reales ni Git productivo.

Volumen definitivo con `dotnet test tests/NAP.Tests/NAP.Tests.csproj --configuration Release --no-build --filter 'Category=AdmissionVolume'`: **4 aprobadas, 0 fallidas**. Cada caso comprueba admisión durable, identidad/JobId únicos, conservación del origen, segundo recorrido deduplicado y máximo observado de siete candidatos pendientes con ese límite configurado.

| ZIP sintéticos | Tiempo de admisión | Máximo muestreado de working set del proceso |
| --- | --- | --- |
| 1 | 0,566 s | 203.845.632 bytes |
| 10 | 1,233 s | 218.451.968 bytes |
| 100 | 19,623 s | 252.952.576 bytes |
| 1.000 | 942,170 s | 257.388.544 bytes |

Métricas de Linux, SDK 8.0.425 y temporales en `/dev/shm`, con ZIP diminutos y `SampleInterval=0` exclusivamente en los fixtures de volumen. El tiempo mide la admisión inicial; el segundo recorrido y las verificaciones aumentan la duración de cada test. El working set incluye el runner y es un máximo muestreado, no una cota de memoria certificada. La batería de volumen completa tardó 17,2839 minutos. El coste de validación durable crece con el historial; 1.000 ZIP no constituye una promesa de rendimiento en disco Windows ni con la ventana de estabilidad default. Ejecuciones exploratorias canceladas no se contabilizan como aprobadas.

La revisión posterior al lote grande añadió una guarda contra paths/inventarios nulos en receipts corruptos y cuatro regresiones de aislamiento. Dos de esos casos reprodujeron NullReferenceException antes de corregirla. La suite sin volumen se ejecutó de nuevo después de la corrección; el lote de 1.000 no se repitió por esta guarda de datos inválidos, que no cambia los receipts válidos ni el recorrido normal. Las métricas de la tabla pertenecen a la ejecución anterior a esa guarda.

Resultado conjunto de casos verificados: **3.492 aprobadas, 0 fallidas, 4 omitidas, 3.496 totales**, incluidos **43 casos nuevos** de admisión. Logs/TRX son artefactos locales de verificación, no cambios versionados. GitHub Actions debe validar la PR cuando esté disponible; estos resultados corresponden al entorno Cloud, no a una certificación Windows.

## Dependencias de Fase 3 y riesgos residuales

Fase 3 deberá consumir receipts Admitted con mismo Job STAGED, revalidar bytes/envelope y congelar perfil completo, validar required/classification/PNG/geometría, resolver reservas semánticas y conservar originales según contrato aprobado. Después podrá implementar normalización bajo policy y auditoría durable; ninguno de esos actos existe aquí.

Pendientes: Windows real (sharing, watcher overflow físico, junctions, mutex/lifecycle), disco lleno/pérdida eléctrica reales, retención/backup y cierre técnico del namespace de preservación. No extrapolar métricas de paquetes diminutos ni TMPDIR en memoria a imágenes grandes en disco Windows. No hay UI de incidencias o aprobación, que corresponden a Fase 6. Watcher inválido/overflow solicita rescaneo; la vigilancia nativa degradada requiere revisión y el rescan sigue siendo fuente de verdad.
