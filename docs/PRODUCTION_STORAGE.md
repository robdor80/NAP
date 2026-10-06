# Production Storage + Verified Completion — Fase 9

**FASE 9 — HECHA (9.1–9.4). Fase 10 — SQLite HECHA (10.1–10.7).
Siguiente: Fase 11 — Explorador visual, sin iniciar.**

El Core puede conservar un package original aprobado, generar su WebP de
producción, copiar su documentación exactamente, verificar todos los finales
y cerrar un Job en COMPLETED. Nimroel es el primer perfil real; el writer es
universal y recibe un UniverseContext explícito. No existe CurrentUniverse.

## Fronteras y uso

```csharp
var productionPlan = new ProductionAssetPlanner(context).Plan(
    validatedPackage, processingPlan, archiveResult, maxInputPixels, jobId);
var productionResult = new ProductionAssetExecutor(context).Execute(
    productionPlan, auditReport);

// Coordinación opcional de las dos fronteras físicas y los checkpoints:
var executionResult = new AssetExecutionCoordinator(context).Execute(
    jobId, validatedPackage, processingPlan, archivePlan,
    auditReport, maxInputPixels);
```

Los constructores no hacen I/O. El planner lee, hashea, convierte y valida en
memoria; nunca crea directorios, temporales, journals, locks físicos ni índice.
ProcessingPlan sigue siendo un snapshot de hechos y destino, sin permiso de
escritura ni modificación de sus contratos/renderers históricos.

ProductionAssetPlan congela AssetKey, AssetType, ProductionRoot,
RelativeDirectory, DestinationDirectory, ResolvedImageConversion, Files,
FilesToWrite, Issues, Action y JobId opcional. Sus colecciones son defensivas y read-only.
Cada ProductionAssetFile declara Kind, Role, FileName, SourcePath nullable,
DestinationPath, Digest y SizeBytes. El WebP conserva bytes privados; ToArray
devuelve una copia. ProductionAssetResult expone AssetKey, Outcome,
RelativeDirectory, FilesVerified e Issues, todos inmutables.

## ProductionRoot y rutas autorizadas

ProductionStorageRootValidator exige una raíz absoluta ya existente, directory,
del contexto/universo correcto y aislada de WorkspaceRoot y ArchiveRoot,
incluyendo relación ancestor/descendant. Rechaza reparse points en la raíz y
todos sus ancestors; tampoco permite una raíz dentro de metadata `.git`.
Una raíz missing produce STOP; el writer no la crea.
No clona repositorios, consulta Git ni exige que exista .git.

La jerarquía procede exactamente de
`ProcessingPlan.ProductionDestination.RelativeDirectory`. No se recalcula
routing ni classification. El destino debe quedar estrictamente dentro de la
raíz por segmentos; un prefijo textual parecido no demuestra containment.
Se rechazan rooted children, `.`, `..`, segmentos vacíos, backslash, colon,
controles, caracteres Windows inválidos, devices reservados, trailing dot/space
y segmentos `.git`/`_nap`. No hay corrección automática de casing Windows.

Después de PASS y del preflight bajo coordinación, el executor crea solo los
segmentos canónicos faltantes, uno a uno, revalidando root, parent y atributos
antes/después. Un file que bloquea un directorio o un reparse produce STOP.
No migra, renombra o reorganiza estructura histórica.

## Archive prerequisite y file set

ArchiveMasterResult es obligatorio. Copied, IndexedExisting y AlreadyArchived
son válidos cuando corresponden físicamente al package actual. Se contrastan
UniverseAssetKey, destino relativo, todos los roles/nombres/paths, hashes y
tamaños originales y el maestro físico/indexado. Un resultado ajeno o un
package cambiado después de archivarse causa production_archive_mismatch.
Production Storage consulta Archive Storage en solo lectura; no lo escribe.

Los outputs salen de FilesByRole + ManifestPath y el WebP generado. Se excluye
el source de conversión declarado por el perfil, que debe ser el único PNG
maestro requerido archivado. Todos los demás roles legítimos se conservan,
incluidos roles futuros. No se escanea PackageRoot para descubrir extras.
Una colisión con el rol sintético manifest o entre filenames produce STOP.

Para el portrait Nimroel actual:

```text
ProductionRoot/portraits/norgard/treskal/farmer/male/
  portrait_treskal_farmer_male_040/
    portrait_treskal_farmer_male_040.webp
    portrait_treskal_farmer_male_040_prompt.md
    portrait_treskal_farmer_male_040_info.md
    portrait_treskal_farmer_male_040_visual_identity.json
    portrait_treskal_farmer_male_040_manifest.json
```

El PNG original permanece intacto en ArchiveRoot. No se copian ZIP, caches,
logs, journals, SQLite, temporales ni extras desconocidos. Los companions y
manifest son byte-for-byte: no se normaliza Markdown ni se reserializa JSON.

## Conversión genérica y validación

ImageConversionResolver suministra ResolvedImageConversion y el pixel budget
runtime. La primitiva interna ProductionImageEngine ejecuta PngToWebp sin
condiciones por universo, asset type o production profile. Usa ImageSharp
3.1.12 ya existente; no modifica las APIs, mensajes, códigos o resultados de
Portrait/Scene y sus tests completos siguen pasando.

PngMasterValidator inspecciona estructura antes de decode. El producto long
width×height se compara con MaxInputPixels antes de asignar el raster; se exige
ratio exacto mediante ImageConversionGeometryValidator. No hay crop, pad,
letterbox ni AutoOrient. El resize Lanczos3 conserva el frame completo; Stretch
solo se usa después de demostrar el mismo ratio. WebP es lossy con quality y
dimensiones del perfil. Nimroel declara 768×960 Q90; el Core no los hardcodea.

El WebP en memoria debe tener un contenedor RIFF/WEBP completo y decodificar
realmente con las dimensiones resueltas. Se repite esa validación en el temp y
en el final persistido. Quality es metadata contractual del encoder; no se
intenta inferir Q desde el bitstream.

## Publicación y verificación física

Cada faltante usa `<finalname>.<guid N lowercase no vacío>.tmp` hermano del
final. Se revalida source, se abre CreateNew/Write/None, se copian bytes o se
escribe el WebP congelado y se hace Flush(true). Tras cerrar se verifican SHA y
size del temp, se revalidan sources/archive/destino y ausencia del final, y se
publica con File.Move(overwrite:false). El final se reabre para SHA/size y,
si es WebP, contenedor/decode/dimensiones. No se usa Create, Replace, Delete ni
overwrite sobre asset finals. Un final aparecido durante publicación causa STOP.

La atomicidad es **por archivo**, sin transacción global del directorio.
Antes de éxito se verifican todos los outputs y las entradas del directorio.
Un fallo conserva los finales correctos ya publicados; no hay rollback
destructivo ni limpieza automática.

## Idempotencia, recovery y colisiones

| Estado físico | Acción |
| --- | --- |
| Job propio con recibo durable; faltan outputs aún no publicados | WriteAndVerify: escribir solo faltantes y verificar todos |
| Mismo Job con recibos de todos los finales y hash/size exactos | AlreadyProduced: cero writes, sin tocar timestamps |
| Fresh execution encuentra destino/final preexistente, incluso idéntico | production_file_collision, Error + Stop |
| Recovery encuentra final idéntico sin recibo propio | production_file_collision, Error + Stop |
| Final existente distinto | production_file_collision, Error + Stop |
| Entrada desconocida, incluso directory | production_unexpected_entry, Error + Stop |
| Reparse en destino/entry | production_entry_reparse, Error + Stop |
| Source cambiado tras planning | production_source_changed, Error + Stop |

Un temp propio reconocible se ignora como autoridad, pero se conserva sin
abrirlo, promoverlo o borrarlo. El patrón exige un filename final esperado y
GUID N canónico de 32 lowercase hex no all-zero. Otros temps son inesperados.
El segundo run puede recalcular WebP en memoria para comparar expectativas;
no reescribe ningún final. Un plan antiguo también se revalida físicamente.

Hash/size exactos no prueban ownership. PLANNED es fresh: el coordinator exige
destino ausente antes de AUDITED. AUDITED sin recibo también es fresh respecto
a producción; el estado por sí solo no permite adoptar finales. EXECUTED,
VERIFIED y COMPLETED requieren evidencia previa. Plan sin JobId sirve solo
para una ejecución fresh y no puede reclamar idempotencia ni recovery.

La evidencia está en `StateRoot/production-executions/<job_id>.json`, fuera de
ProductionRoot y del nivel superior que escanea JobRecoveryScanner. Parsing
estricto vincula schema v1, JobId, UniverseId, AssetId, ProductionRoot, destino
relativo y SHA del snapshot: maestro archivado, source path/role, conversión,
dimensiones/quality/budget y outputs con roles/nombres/hashes/tamaños. Un
recibo ajeno, corrupto o incoherente produce production_recovery_invalid STOP.

Tras PASS, con leases de Job y ProductionRoot, el executor registra primero
el snapshot sin ownership, antes de crear destino/outputs. Solo después de
Move sin overwrite y verificación final registra cada filename publicado.
Los recibos usan temp exclusivo, Flush(true) y publicación atómica; únicamente
esta metadata de StateRoot admite reemplazo. Planning/Verify/COMPLETED no
escriben recibos. No se escanean otros Jobs para adoptar outputs coincidentes.

La publicación del asset y el recibo no es una transacción entre roots. Un
crash después del Move y antes del recibo deja un final sin prueba durable:
STOP incluso con bytes exactos, sin reconstruir ownership desde el filesystem
ni promover un recibo temp. Un final registrado que desaparece también exige
revisión explícita. Partial recovery automático se limita a faltantes aún no
publicados y finales exactos con recibos del mismo Job. StateRoot debe estar
bajo control del host: los recibos son evidencia local, no firmas frente a
un actor con permiso para falsificar journals/metadata.

Otros códigos estables: production_root_missing, production_root_invalid,
production_root_reparse, production_path_invalid, production_verification_failed,
production_archive_mismatch, production_conversion_failed,
production_output_invalid, production_recovery_invalid y production_completed_inconsistent. Las incidencias
deterministas peligrosas son siempre Error + Stop. Los errores operativos de
acceso/I/O pueden propagarse; nunca equivalen a éxito.

## AI gate y coordinación

Execute exige AiAuditReport con Decision == Pass y Passed == true antes de
cualquier I/O de ejecución, mutex o write. WARNING/FAIL/enum inválido rechazan
sin crear carpetas/temp ni alterar roots o journal; null produce
ArgumentNullException. PASS no sustituye los controles deterministas.
AiAuditRequest conserva exclusivamente hechos lógicos y destino relativo; no
recibe roots, source paths, filesystem tools ni permisos de ejecución.

Un named mutex BCL derivado mediante SHA-256 de ProductionRoot absoluto
normalizado (casing normalizado en Windows) serializa executors NAP de la raíz.
La adquisición WaitOne(0) es acotada: ocupado produce IOException controlada,
sin retries infinitos. También se impide reentrada en el mismo proceso.
El lock cubre revalidación, creación, publicación y verificación. No hay lock
file dentro de ProductionRoot; distintos assets pueden serializarse en v1.

Otro named mutex deriva de StateRoot canónico + JobId y abarca la operación
del coordinator y de Execute/Verify cuando el plan contiene JobId. El mutex
no crea archivos en StateRoot ni cambia JobRecoveryScanner
o la protección in-process histórica de JobStateStore.

## Job lifecycle y COMPLETED

El coordinator exige un journal existente y contexto/rutas/plan de archivo
coherentes. PASS se comprueba antes de AUDITED. No crea Jobs ni salta estados:

```text
PLANNED + PASS → AUDITED → Archive execution → Production execution
              → EXECUTED → physical verification
              → VERIFIED → physical verification → SQLite (Fase 10) → COMPLETED
```

| Checkpoint inicial | Comportamiento |
| --- | --- |
| PLANNED | PASS, destino ausente, preflight y avance AUDITED antes de ejecutar |
| AUDITED | Fresh si no hay recibo; recovery solo con ownership durable propio |
| EXECUTED | Revalidar efectos físicos, avanzar VERIFIED y COMPLETED |
| VERIFIED | Revalidar efectos físicos antes de COMPLETED |
| COMPLETED | Solo verificar; devolver AlreadyArchived/AlreadyProduced sin writes |
| FAILED | Rechazar, terminal |
| DETECTED/STAGED/VALIDATED | Rechazar; no saltar validación/PLAN |

Un IOException, fallo de publicación del journal o interrupción conserva el
último checkpoint durable; no se escribe FAILED automáticamente. COMPLETED
inconsistente produce production_completed_inconsistent y **no se repara**,
ni siquiera si solo falta un final/índice. El resultado global expone JobId,
FinalState, Archive y Production. Fase 10 añade el registro del catálogo
entre VERIFIED y COMPLETED sin reescribir el writer. Si SQLite falla, el Job
permanece VERIFIED; un commit previo exacto permite recovery idempotente.
La rama COMPLETED conserva su comprobación física read-only y no reconstruye
una DB desaparecida. Véase [SQLITE_CATALOG.md](SQLITE_CATALOG.md).

## Límites y pruebas

162 tests nuevos ejercitan raíces, rutas, junctions reales, colisiones por
output, byte/hash/size/timestamps, conversiones de un perfil genérico emblem,
archive prerequisite, temps y carreras deterministas en la frontera de
publicación, ownership durable, checkpoints reales sin asumir rename Windows, mutexes entre threads y
procesos, dos universos y el ZIP Nimroel real → COMPLETED → run idempotente.
La suite completa suma 2662 tests, sin fallos ni skipped; schemas sin cambios,
436 checks. Los tests de filesystem usan roots temporales propios y limpieza
en Dispose, incluyendo fallo de construcción de la fixture de producción.

Los dos tests heredados de Archive que asumían que un reader bloqueaba rename
se sustituyen por revocación determinista del lease mientras el publisher
serializa. Se ejercitan las copias reales, Flush/cierre del temp, el guard
final de lease, preservación del índice anterior/temp completo y recuperación
IndexExisting sin recopia. El runtime de Archive y CI ubuntu-latest no cambian;
no hay sleeps, permisos dependientes del usuario ni skips por plataforma.

El writer físico de Fase 9 no usa SQLite. No hay Git runtime (Fase 13), red, API TeraBox, UI, CLI de producción,
watchers, daemon, servicio, backups/retención, rollback global ni dependencias
nuevas. No se toca producción real del usuario.

Los mutexes coordinan procesos NAP cooperantes que usan las mismas raíces
canónicas y namespace de mutex del sistema. Un proceso externo que ignore la
coordinación puede cambiar paths entre comprobación de atributos y apertura.
Los controles BCL revalidan antes/después de I/O, pero no ofrecen handles de
directorio resistentes a todas esas carreras, transacción global ni garantía
frente a pérdida de energía. Las raíces deben estar bajo control del host y
los writers deben respetar el protocolo; no se sigue deliberadamente ningún
reparse detectado ni se promete inmunidad a modificaciones externas hostiles.
