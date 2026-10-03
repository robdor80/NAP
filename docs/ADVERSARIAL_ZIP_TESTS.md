# Boundary ZIP adversarial — Fase 2 · Capítulo 2.7

**HECHO.** La auditoría refuerza la extracción segura y opaca de ZIP; no crea
PackageValidator ni integra validación semántica o routing. Los tests nuevos
son reproducibles, pequeños y generan sus ZIP en C#, sin red, sleeps,
configuración local, dependencias nuevas ni fixtures binarios en Git.
Ningún contenido del ZIP se ejecuta.

## Inventario previo y huecos reales

Se revisaron todos los tests de `StagedPackageExtractorTests` y la integración
Inbox → Detection → Readiness → Staging → Safe Extraction de
`PhaseOneIntegrationTests` antes de añadir casos. La suite previa tenía 409
tests en total; no se sustituyen ni debilitan.

| Familia | Cobertura previa confirmada (tests actuales) | Hueco cubierto en 2.7 |
| --- | --- | --- |
| ZIP legítimo, nested, vacío | `ValidZip_ExtractsFilesAndPreservesStagedZip`, `NestedEntries_PreserveNormalStructure`, `EmptyZip_PublishesEmptyDirectory`, integración Fase 1 | Control deflated sin metadata de package. |
| Traversal, absoluto, UNC, drive, ADS | `UnsafePath_IsRejectedWithoutWritingOutsideRoot` | `./file`, dot-dot interno, separadores mixtos. |
| Segmentos y nombres peligrosos | El mismo theory: doble slash, punto/espacio final en padre, algunos reservados | Segmento vacío con separadores mixtos, punto/espacio final en archivo, NUL, controles, DEL y `< > " | ? *`. |
| Dispositivos Windows | NUL.txt, COM¹.txt, CON .txt y CONOUT$ | Familias CON/PRN/AUX/NUL, COM1–9/LPT1–9 y variantes lowercase/con extensión. |
| Colisiones internas | `DuplicateEntry_CannotOverwriteAnExtractedFile`, `CaseInsensitiveEntryCollision_IsRejected`, `FileDirectoryCollision_IsRejected` | Directorio implícito seguido de explícito: spelling igual aceptado; distinto casing rechazado. |
| Destino final y contenido ajeno | `ExistingFinalDirectory_IsCollisionAndUntouched`, `CaseInsensitiveFinalDirectoryCollision_IsRejectedOnLinuxToo`, `ExistingFinalFile_IsCollisionAndUntouched`, `UnrelatedExtractionRootContent_IsPreserved` | Snapshot conjunto de ZIP, sentinels y temporal ajeno para cada fallo nuevo. |
| Tipos prohibidos | `DeclaredSymlink_IsRejected`, `SpecialEntryTypes_AreRejected`: FIFO, block device, reparse, archivo marcado directory | Character device, socket, directory marcado regular, directory con datos y nombre vacío. |
| Integridad | `CorruptZip_DoesNotPublishFinalDirectory`, `CorruptEntryAfterEarlierFile_CleansOperationTemporaryDirectory`, `StoredEntryWithCorruptPayload_IsRejectedByCrc`, `UnderstatedEntryLength_CannotPublishTruncatedContent` | Cabeceras/offsets/longitudes de nombres y extras, método 99, truncamientos del archivo, EOCD y counts inconsistentes. |
| ZIP64 y comentarios | `Zip64EndRecords_AreSupported`, `ZipWithArchiveComment_ExtractsCorrectly` | Locator/end ZIP64 corruptos, offset fuera de rango y counts inconsistentes; indicador split clásico. |
| Recursos | `Limits_AreEnforced`, `InvalidOptions_AreRejected` | Tamaño declarado cercano a 4 GiB sin asignarlo; stored de 512 bytes declarado como 1, con límites de entrada y total. |
| Cancelación y raíz | `Cancellation_DoesNotPublishOrLeaveTemporaryDirectory`, `CancellationDuringExtraction_CleansOnlyItsTemporaryDirectory`, `RootThroughJunction_IsRejectedWithoutChangingTarget`, `MissingExtractionRoot_IsCreated` | Se conserva esta cobertura; no se añaden nuevas carreras ni archivos grandes. |
| NapIssue | Tests de mapeo de resultados construidos | Mapeo de resultados reales en cada fallo adversarial nuevo. |

## Matriz de resultados y protecciones

`Rejected` identifica una prohibición de política NAP (rutas, nombres, tipos,
colisiones internas, límites o split ZIP no soportado). `InvalidArchive`
identifica corrupción/inconsistencia que el parser o la comprobación de
integridad detectan. No se fuerza la clasificación por un texto humano.
Las colisiones con el destino final mantienen su estado propio `Collision`.

| Vector | Resultado esperado | Protección/componente |
| --- | --- | --- |
| Dot/dot-dot, absoluto, UNC/drive/ADS, segmentos vacíos, controles y caracteres peligrosos | `Rejected` | `ValidateEntries`, canonicalización de separators, `IsUnsafeSegment`, `EnsureContained`. |
| Dispositivos Windows, con casing/extensión variados | `Rejected` | `IsUnsafeSegment`, política conservadora Windows-compatible también en otras plataformas. |
| Duplicados, file/directory y colisión de casing | `Rejected` | Conjuntos ordinal-ignore-case y spelling de directorios explícitos/implícitos. |
| Unix links/tipos especiales, reparse, atributos incoherentes, directory con datos | `Rejected` | Validación de tipo y longitud antes de escribir. |
| MaxEntries, tamaño declarado o bytes reales sobre límites | `Rejected` | Límite de count antes de asignar entradas; límites por entry/total antes de escribir cada bloque. |
| Indicador de archivo split clásico | `Rejected` | Política actual en `ZipEntryIntegrity`. |
| Signaturas, offsets, campos de cabecera, counts, EOCD/ZIP64 manipulados y truncamientos probados | `InvalidArchive` | `ZipEntryIntegrity`, `ZipArchive` y adaptación acotada de apertura de entry. |
| Método de compresión 99 | `InvalidArchive` | `ZipArchiveEntry.Open` informa `InvalidDataException`. |
| CRC incorrecto o bytes extraídos distintos de la longitud declarada | `InvalidArchive` | Conteo y CRC-32 calculados durante lectura frente al directorio central. |
| Destino final existente | `Collision`, contenido intacto | Comprobaciones previas y anteriores a publicación; move sin overwrite. |
| ZIP válido vacío o sin manifest/PNG; directorio explícito compatible con implícito | `Extracted` | Extracción genérica de contenido opaco. |

Los tamaños manipulados stored de los tests nuevos alcanzan los límites de
bytes reales y devuelven `Rejected`. Los tests previos de deflate con longitud
menor terminan en `InvalidArchive` por longitud/CRC en .NET 8. Ambas rutas
impiden publicar; no se modifica el parser para forzar una clasificación única.

## Defecto demostrado y corrección mínima

Un ZIP con una primera entry válida y el offset local de la segunda apuntando
al EOF provocaba `EndOfStreamException` desde `ZipArchiveEntry.Open` en .NET 8.
El cleanup ya se ejecutaba, pero la excepción escapaba en lugar de devolver
el resultado controlado `InvalidArchive`.

`StagedPackageExtractor.OpenEntry` captura exclusivamente esa excepción al
abrir la entrada y la convierte en `InvalidDataException`. El catch existente
produce `InvalidArchive`; el finally conserva la limpieza. El test original y
dos variantes con uno/tres bytes restantes verifican la regresión después de
una primera entry extraída. No se altera la lectura de ZIP válidos, los códigos
NapIssue ni el tratamiento de otros errores de E/S o cancelación. No se crea
otro parser ni se modifica `ZipEntryIntegrity`.

## Invariantes y fixtures

`AdversarialZipFactory` es infraestructura exclusiva de tests: genera headers
clásicos mínimos y end records ZIP64, datos stored/deflated y CRC independiente,
con offsets explícitos para mutaciones pequeñas. No contiene ejecutables.
`AdversarialZipTests` añade 74 casos parametrizados, incluidos dos controles
válidos; todos los fallos pasan por un helper común.

Cada fallo nuevo comprueba `FinalPath == null`, ausencia de destino final,
igualdad del inventario de directorios y bytes de todos los archivos bajo el
temp root controlado. Esto incluye el ZIP staged original, un sentinel fuera
de ExtractionRoot, otro dentro y un `.nap-*.extracting` preexistente ajeno.
No queda ningún temporal propio; el ajeno permanece intacto. Ningún test nuevo
escribe fuera de su temp root. Los fallos de apertura/lectura de la segunda
entry prueban además la limpieza tras extracción parcial dentro del temporal.

Para cada fallo se mapea el resultado real a `zip_rejected` o
`zip_invalid_archive`, `Error`, `Stop`, `StopsProcessing == true`,
`SubjectPath == StagedZipPath` y `Detail == Reason`. No se fija el texto exacto
de Reason. Los controles válidos no producen issue.

## Límites y siguiente decisión de arquitectura

La raíz de extracción debe estar controlada por NAP. Se rechazan ancestros
reparse y entries link/reparse; se comprueba colisión antes de extraer y justo
antes de publicar (también al fallar el move). Los tests deterministas cubren
colisiones preexistentes; la segunda comprobación se verifica por inspección
de código, sin un test de carrera de publicación. No se garantiza seguridad
frente a mutación externa concurrente del filesystem. El test previo de
junction es Windows-only y el de cancelación durante extracción depende de
observar progreso; no se amplían esas condiciones con sleeps nuevos.

Los vectores sintéticos no prueban toda la conformidad ZIP ni todas las
combinaciones de metadatos. `ZipArchive` sigue siendo el parser; no se compara
cada campo central/local ni se añade un parser estricto alternativo. Los límites
absolutos existentes acotan entradas y bytes extraídos, sin cuota nueva de
CPU/tiempo. Errores operativos de E/S y cancelación siguen propagándose.

El extractor no inspecciona Manifest v1/v2, universe_id, asset_id, profile,
classification, PNG, prompt, info ni visual_identity; no recibe UniverseContext.
Manifest ausente, universo incorrecto o PNG ausente no convierten un ZIP legible
y seguro en corrupto. El caller futuro seleccionará contexto y raíces.

**Al cierre de 2.7 quedaron hechos 2.1–2.7.** La revisión posterior insertó
2.8 — Package Semantic Validation antes de routing y reabrió Fase 2.
[2.8.1 — Package Contract v1](PACKAGE_CONTRACT_V1.md) y
[2.8.2 — Package Semantic Validation](PACKAGE_SEMANTIC_VALIDATION.md) están hechos;
Fase 2 completa. Esta auditoría conserva su alcance ZIP; la capa semántica
posterior está separada y Fase 3 no está iniciada.
