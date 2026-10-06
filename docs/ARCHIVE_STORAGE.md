# Archive Storage — Fase 8

**Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6).**
**Siguiente: Fase 9 — Producción repo Nimroel.**

NAP conserva el paquete original validado en una carpeta local autorizada por
`UniverseContext.Storage.ArchiveRoot`. TeraBox sincroniza esa carpeta externamente:
NAP no usa API, HTTP, SDK, credenciales ni automatización de su interfaz.
El caller configura la raíz; Core no inventa rutas o niveles de TeraBox/Nimroel.

## 8.1 — ArchiveRoot · HECHO

`ArchiveRootValidator.Validate(UniverseContext)` devuelve un `NapIssueReport` de
solo lectura. La raíz debe existir, ser directorio, absoluta, del contexto
correcto y estar aislada de ProductionRoot y WorkspaceRoot, incluso por
solapamiento de ancestors. NAP **nunca crea ArchiveRoot si falta**: una unidad
o carpeta de sincronización desconectada debe producir STOP.

Se inspeccionan atributos desde la raíz del volumen hasta cada camino relevante,
sin seguir symlinks, junctions, mount points u otros reparse points. Una raíz
ausente, inválida o reparse produce respectivamente `archive_root_missing`,
`archive_root_invalid` o `archive_root_reparse`; un componente interno reparse,
`archive_entry_reparse`. Todos son Error + Stop.

Los destinos quedan estrictamente debajo de ArchiveRoot mediante containment
con boundary de segmento, no un prefijo textual arbitrario. Se rechazan `..`,
`.`, segmentos vacíos, paths absolutos, unidades/UNC inyectados, backslashes,
colon, controles, caracteres Windows inválidos, dispositivos reservados y
terminaciones ambiguas. `_nap` está reservado a infraestructura.
Solo después de validar y revalidar la raíz se crean directorios controlados
debajo de ella. No se enumera recursivamente TeraBox.

## 8.2 — Master Index · HECHO

`ArchiveMasterIndexEntry` conserva AssetId, AssetType, MasterSha256,
MasterSizeBytes, RelativeDirectory y Verified. Exige Naming v1, digest canónico
de 64 lowercase hex, tamaño positivo, directorio relativo seguro y `Verified=true`.
`ArchiveMasterIndex` conserva UniverseId y una copia defensiva read-only de las
entradas, únicas por AssetId y ordenadas por AssetId Ordinal.

`ArchiveMasterIndexStore(context).Load()` valida la raíz y lee exclusivamente
`_nap/master_index.json`. Si falta, devuelve un índice vacío de ese universo.
El parser v1 rechaza JSON malformado, roots/entries de tipo incorrecto,
propiedades ausentes, extra o duplicadas, versión distinta del entero literal
1, otro universo, identidades/digests/tamaños/rutas inválidos, entradas nulas,
AssetId duplicado y verified distinto de true. No repara, normaliza, reconstruye
ni ignora corrupción: `archive_index_invalid`, Error + Stop.

```json
{"schema_version":1,"universe_id":"nimroel","entries":[{"asset_id":"portrait_treskal_farmer_male_040","asset_type":"portrait","master_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","master_size_bytes":80,"relative_directory":"portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040","verified":true}]}
```

Este ejemplo ilustra el formato, no una huella de un maestro real. La publicación
usa Utf8JsonWriter explícito: schema_version, universe_id, entries; cada entrada
usa exactamente el orden mostrado. UTF-8 sin BOM, JSON compacto y LF final.
No guarda timestamp obligatorio, rutas absolutas, sources, ProductionRoot,
JobId, username, hostname o secretos. El índice operativo no es SQLite ni
un catálogo: SQLite sigue reservado a Fase 10.

`Publish` es interno: requiere un lease vivo con el stream exclusivo
`_nap/archive.lock`, adquirido por el executor después de PASS. Escribe
`master_index.<guid N>.tmp` hermano con CreateNew/Write/None, termina el JSON,
hace `Flush(true)`, cierra y usa `File.Move(temp, final, overwrite:true)`.
Nunca trunca ni borra primero el índice final. Un fallo de publicación conserva
los masters correctos y el índice anterior para recuperación.

## 8.3 — Incremental Copy · HECHA

API de planificación, sin escrituras:

```csharp
var planner = new ArchiveMasterPlanner(context);
ArchiveMasterPlan archivePlan = planner.Plan(validatedPackage, processingPlan);
ArchiveMasterResult result = new ArchiveMasterExecutor(context).Execute(archivePlan, auditReport);
```

La identidad, universo, metadata, inputs y raíces del ProcessingPlan deben
coincidir con el ValidatedAssetPackage/contexto. El PNG maestro se resuelve
desde la única regla cuyo `ContentValidator == "png_master"`, required y
presente en FilesByRole. Cero, varias, optional o missing producen rechazo.
En Nimroel resulta el role `master` y `.png`; Core no exige ese role literal.

El conjunto exacto es `ValidatedAssetPackage.FilesByRole` + `ManifestPath`.
Preserva PNG, prompt, info, visual_identity y cualquier otro companion legítimo
del perfil, incluidos opcionales presentes. El manifest conserva su nombre
real; una colisión de role con el manifest sintético se rechaza. No se buscan
archivos adicionales aparecidos en PackageRoot. No se añaden WebP generado,
ZIP de transporte, caché, logs, journals o outputs de producción. No se
reformatea JSON/Markdown: los bytes quedan congelados mediante hash/tamaño
en el plan y deben mantenerse durante ejecución.

El plan, sus archivos y resultado son inmutables. Incluyen identidad, tipo,
raíz, directorio relativo/absoluto, master digest/tamaño, archivos, subset
FilesToCopy, issues y acción. Cada archivo congela role, source, nombre, destino,
SHA y tamaño. Los paths permanecen dentro de NAP y no entran en AiAuditRequest.

| Acción del plan | Outcome | Comportamiento |
| --- | --- | --- |
| CopyAndIndex | Copied | Copia solo archivos faltantes, verifica todos e indexa. |
| IndexExisting | IndexedExisting | Todos los finales son exactos, falta entrada; verifica e indexa sin recopia. |
| AlreadyArchived | AlreadyArchived | Índice, identidad, ruta y todos los archivos coinciden; cero escrituras, incluido índice. |

Una acción de un plan con STOP no permite ejecución. La acción se vuelve a
calcular bajo lock; el snapshot inicial no es una autorización permanente.
Si se interrumpe tras 2 de 5 archivos correctos, el próximo plan copia solo 3.
Si están los 5 sin índice, IndexExisting recupera la publicación sin recopiarlos.

## 8.4 — Verification · HECHA

Sha256Digest/Sha256Hasher existentes se reutilizan para cada archivo original.
Antes de copiar se comprueban existencia, atributos, tamaño y SHA del source
contra el snapshot. `archive_source_changed` detiene una fuente desaparecida
o modificada; un enlace también detiene, sin traversal.

Cada faltante se copia como `<finalname>.<guid N>.tmp` en el mismo directorio,
CreateNew/Write/None. Tras copiar bytes, `Flush(true)` y cierre, se verifica
tamaño y SHA del temporal contra el original congelado. Solo una copia exacta
se publica con `File.Move(temp, final, overwrite:false)`, y se vuelve a abrir
y verificar el final. Un final que aparece durante la copia causa colisión,
aunque coincida: no se resuelve una carrera automáticamente.

Antes de indexar se revalidan fuentes y **todos** los finales del paquete:
regular files, existentes, sin reparse, tamaño y SHA exactos. Deben cumplirse
`source SHA == archived SHA` y `source size == archived size`. Una copia/final
incorrecta causa `archive_verification_failed`, Error + Stop: no se publica
entrada, no se devuelve éxito ni se borran archivos copiados.

Los temporales huérfanos no son finales ni índices y no se leen como autoridad.
No hay limpieza/GC automático: permanecen para inspección; un final ausente
recibe una nueva copia segura. La atomicidad es por archivo/índice, no una
transacción global del paquete ni una garantía de sincronización remota.

## 8.5 — Collisions · HECHAS

| Estado | Resultado |
| --- | --- |
| Mismo ID, mismo PNG, índice/ruta/tamaño y paquete físico exactos | AlreadyArchived, sin issue ni write. |
| Mismo ID en índice, PNG distinto | archive_asset_collision, Error + Stop. |
| Mismo PNG, cualquier prompt/info/manifest/visual_identity distinto | archive_file_collision, Error + Stop. |
| Final físico distinto o bloqueado, incluso sin índice | archive_file_collision, Error + Stop. |
| Entrada existente pero falta un archivo físico | archive_index_inconsistent, Error + Stop; sin recreación automática. |
| Entrada con directorio/tipo/tamaño diferente | archive_index_inconsistent, Error + Stop; sin mover/reparar. |
| Otro AssetId con el mismo master SHA | archive_possible_duplicate, Warning + Continue. |

AssetDuplicateAnalyzer/AssetContentFingerprint se reutilizan sin cambiar sus
contratos. El warning conserva su diagnóstico en el resultado y permite que
una política manual futura decida sobre el duplicado; no lo elimina ni lo
fusiona. El executor sigue exigiendo PASS de IA. No sobrescribe, reemplaza,
borra ni escoge entre masters o metadata distintos. Índice y filesystem deben
coincidir; ninguno constituye por sí solo prueba suficiente de conservación.
Una ejecución exacta AlreadyArchived no repite avisos de otros IDs con el
mismo master: el candidato ya está indexado y físicamente verificado.

El lock abarca recarga del índice, source/destination preflight, copia,
verificación y publicación. `_nap/archive.lock` usa OpenOrCreate/ReadWrite/None
y puede permanecer vacío. No espera/reintenta indefinidamente: un lock ocupado
propaga IOException. Un mutex nombrado por la raíz canónica coordina procesos
antes de crear infraestructura y permite AlreadyArchived sin crear un lock
ausente. Se abre el lock existente sin modificarlo; solo una operación con
writes habilita su creación después del preflight bajo mutex.

Dos planes antiguos recargan el índice actual bajo el lock y conservan las
entradas incorporadas por el primer executor. Un cambio relevante incompatible
detiene el segundo. Otra entrada compatible se incorpora al estado actual,
incluidos sus warnings, en vez de reemplazarlo por un índice antiguo. También
se compara otra recarga antes de publicar para detectar cambios de un escritor
que no respete el lock mientras se copian los archivos.

## 8.6 — Final Structure · HECHA

Se usa exactamente `ProcessingPlan.ProductionDestination.RelativeDirectory`,
sin volver a inferir clasificación ni recargar/hardcodear Nimroel. Se conserva
`/` en el índice y se combina cada segmento con Path.Combine en disco:

```text
ArchiveRoot/
  portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040/
    portrait_treskal_farmer_male_040.png
    portrait_treskal_farmer_male_040_prompt.md
    portrait_treskal_farmer_male_040_info.md
    portrait_treskal_farmer_male_040_visual_identity.json
    portrait_treskal_farmer_male_040_manifest.json
  _nap/
    master_index.json
    archive.lock
```

ArchiveRoot ya representa la raíz de maestros del universo. NAP no antepone
TeraBox/Nimroel/02_ASSETS_MAESTROS. Dos universos usan raíces/índices separados;
el mismo AssetId puede conservarse independientemente en ambos. El caller
mantiene la validación del conjunto de configuraciones con
UniverseStorageIsolationValidator; una operación rechaza planes/índices de
otro contexto o raíz.

## Auditor IA, permisos y límites

`Execute(plan, auditReport)` exige `Decision == Pass` antes de cualquier I/O.
WARNING, FAIL, enum inválido o ausencia de informe no permiten writes, ni
infraestructura. Un fallo de cliente no produce PASS. PASS es necesario y
requiere además preflight determinista limpio; no sustituye hashes/colisiones.
IA conserva el request de Fase 7 sin ArchiveRoot, destinations absolutos,
index/source paths, tools o permisos de filesystem. NAP realiza las copias.

ProductionRoot y WorkspaceRoot/StateRoot permanecen intactos. No hay creación
de producción, WebP final, copia de documentación al repo, SQLite, Git o red
en Archive Storage. No realiza AUDITED → EXECUTED → VERIFIED → COMPLETED ni
otras transiciones JobState. No incorpora CLI/UI, orquestador global, backups,
retención o restore de fases posteriores. No añade dependencias NuGet.

La sincronización/disponibilidad remota de TeraBox es responsabilidad de su
cliente. Los controles BCL revalidan caminos antes de I/O y se coordinan entre
executors NAP; no sustituyen permisos del sistema ni impiden que un proceso
externo no cooperante cambie un directorio entre la comprobación de atributos
y su apertura. Esos procesos deben respetar el archivo y su lock. No se
promete una transacción frente a modificaciones externas o pérdida de energía.

Las pruebas usan raíces temporales aisladas, bytes/tamaños/hashes/timestamps,
junctions reales, un proceso externo con FileShare.None y escenarios
deterministas de fallo; no necesitan TeraBox, credenciales o red real.
