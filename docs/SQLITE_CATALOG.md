# SQLite Catalog — Fase 10

**Fase 10 — SQLite HECHA (10.1–10.7). Fases 11 y 12 HECHAS. Siguiente: Fase 13 — Git del repo de producción.**

## Propósito y frontera

SQLite es un índice operativo reconstruible por universo. Los assets físicos
de ProductionRoot y los originales archivados con master_index son la fuente
de verdad. El catálogo no modifica ninguno de esos archivos.

`AssetCatalog`, `CatalogImporter` y `CatalogRebuilder` reciben exclusivamente
`UniverseContext`. La DB activa es siempre `context.Storage.CatalogPath`,
`<WorkspaceRoot>/state/AssetCatalog.db`. No hay caller-supplied DB path,
CurrentUniverse, CurrentCatalog, GlobalCatalog ni conexión/SQL públicos.

La identidad es `UniverseAssetKey(UniverseId, AssetId)`. Dos universos pueden
tener el mismo AssetId en sus DB independientes. Metadata y foreign keys
impiden aceptar filas de otro universo, incluso con SQL ejecutado directamente.

Dependencia fijada: [Microsoft.Data.Sqlite 8.0.31](https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.31), compatible con .NET 8;
sin EF, ORM ni migrador. Incluye el runtime nativo SQLite transitivo del provider.
La operación del catálogo es local y no requiere red.

## Apertura, schema y durabilidad

Cada conexión usa Mode explícito, Cache=Private, Pooling=false, ForeignKeys=true,
`foreign_keys=ON`, `synchronous=FULL`, `temp_store=MEMORY` y busy_timeout=1000 ms.
Las DB nuevas usan `journal_mode=DELETE`; una existente debe conservar DELETE.
No se activa WAL. Un archivo -wal/-shm existente se rechaza para revisión, sin
descartarlo ni convertirlo automáticamente.

Lecturas abren ReadOnly. Una DB ausente causa catalog_missing y no se crea
desde Query/Get/Statistics/Planning/CheckIntegrity. Initialize es explícito,
crea schema v1 en una DB temporal y publica sin overwrite. Una existente se
verifica; Initialize no repara ni migra.

Schema v1 tiene `catalog_metadata(schema_version=1, universe_id)` y
`PRAGMA user_version=1`. Apertura comprueba metadata, DDL completo de todas
las tablas/constraints/índices, integrity_check, foreign_key_check e invariantes
lógicas de paths, fingerprints, archivos y bytes documentales. Una versión
desconocida o universo diferente produce STOP. No se aceptan objetos SQL extra.

El [DDL completo](SQLITE_CATALOG_SCHEMA_V1.sql) refleja
`src/NAP.Core/CatalogSchema.cs`; no es un migrador ni una entrada SQL pública.

| Tabla | Información y relaciones |
| --- | --- |
| catalog_metadata | Singleton, versión y UniverseId único |
| assets | PK UniverseId/AssetId; type/profile, lifecycle, audit nullable, master SHA/size, production SHA/size, directorios relativos |
| classifications | PK universo/asset/dimensión; value exacto; FK assets |
| files | PK universo/asset/location/role; kind, relative_path, SHA, size, verified; FK assets; ubicación única por universo |
| documents | Bytes BLOB originales de cada companion/manifest de producción; FK al archivo correspondiente |
| visual_traits | JSON Pointer, valor escalar y tipo; FK assets |
| objectives | ID/nombre, target positivo, filtro estructurado; FK universo |
| objective_classifications | Criterios dimensión/value; FK objectives |
| objective_traits | Criterios path/value/type; FK objectives |
| campaigns | ID/nombre; FK universo |
| campaign_objectives | Relación campaña/objetivo; ambas foreign keys |

Índices explícitos: ix_assets_type_profile, ix_assets_profile,
ix_classification_selection, ix_trait_selection, ix_campaign_objective.
PK y UNIQUE aportan sus índices SQLite. Identidades y criterios usan BINARY;
el orden público se materializa con StringComparer.Ordinal, independiente
del orden UTF-8 de SQLite o CurrentCulture. No hay columnas de vocabulario Nimroel.

No hay fechas de asset ni timestamps operativos. Lifecycle es
`physically_verified`, reconstruible desde el snapshot físico; no pretende
ser el checkpoint de un Job. AuditState es NULL cuando se desconoce. El
pipeline registra PASS solo desde su AiAuditReport validado real; import
no deduce aprobación desde archivos o hashes. Un PASS conocido se conserva
durante rebuild de una DB válida si todos los hechos físicos siguen exactos;
tras perder la DB no se inventa ese historial.

## Snapshots e import existente — 10.3

CatalogImporter.Plan adquiere coordinación de fuentes, valida roots y usa el
ProductionRepositoryScanner existente. Descubre candidatos por manifest; una
imagen PNG/WebP sin manifest también genera issue en vez de desaparecer del
resultado. No interpreta .git/_nap como assets; el scanner sigue rechazando
reparse en el árbol observado.

Por candidato usa Manifest v2, Naming, PackageSemanticValidator aplicado al
paquete original en la ruta determinista de ArchiveRoot, Universe Profile,
ProductionDestinationResolver e ImageConversionResolver existentes. Comprueba:

- universo, type/profile, clasificación y naming válidos;
- directorio de producción exactamente igual al routing canónico;
- entrada master_index verificada, ID/type/ruta/SHA/tamaño coherentes;
- PNG archivado válido y geometría compatible con la conversión completa;
- WebP completo, decodable y con las dimensiones declaradas;
- cada companion y manifest byte-for-byte igual al original archivado;
- solo outputs declarados, sin PNG maestro en producción ni extras ajenos;
- fingerprints calculados sobre archivos reales, sin rutas externas arbitrarias.

Los temporales reconocidos de Archive/Production se conservan, nunca se abren,
promueven o indexan. Se reutiliza exactamente su regla filename + GUID canónico .tmp.
PackageSemanticValidator expone internamente esa lectura de la copia archivada;
su Validate público conserva el rechazo estricto de extras en paquetes originales.
No se recorre ArchiveRoot indiscriminadamente: master_index y routing fijan
qué paquete original se valida.

Plan es inmutable y reúne los issues de todos los candidatos. Un blocker
impide toda mutación SQLite. Import(plan) vuelve a descubrir y compara el
snapshot completo bajo coordinación; cualquier cambio provoca STOP.
La importación completa usa una sola transacción. Un conflicto posterior
revierte también los assets insertados anteriormente en esa operación.

Import y rebuild no requieren journals ni receipts de los Jobs originales.
Esto acredita assets físicos existentes; no otorga ownership a un nuevo Job
de ProductionAssetPlanner ni relaja sus colisiones. Layout histórico no
canónico produce STOP sin movimiento ni compatibilidad inventada.

## Registro, actualización automática y recovery — 10.4

```text
PLANNED + PASS → AUDITED → Archive → Production → verificación
→ EXECUTED → verificación → VERIFIED → SQLite commit → COMPLETED
```

AssetExecutionCoordinator registra después de VERIFIED y de la última
verificación física. RegisterVerified recibe package/ProcessingPlan,
ArchiveMasterResult y ProductionAssetResult coherentes; revalida además los
originales congelados y vuelve a acreditar producción/archive físicos.
SQLite no participa en la publicación del WebP y no modifica el writer.

El snapshot completo de un asset se registra en una transacción con
classifications/files/documents/traits. Misma AssetKey y hechos exactos es
idempotente: no duplica ni reescribe datos derivados. Hash, ruta, documento o
metadata incompatibles causa catalog_asset_conflict STOP, sin UPSERT indiscriminado.
La única incorporación de audit metadata permite pasar de desconocido a PASS
cuando se aporta un AiAuditReport PASS validado y todos los hechos coinciden;
import con audit desconocido nunca borra una aprobación conocida.

Si SQLite falla, el journal sigue VERIFIED: no COMPLETED ni FAILED automático.
Si DB commit termina y hay crash antes de COMPLETED, el siguiente run verifica
el registro exacto y avanza idempotentemente. DB incompatible provoca STOP.

Un Job COMPLETED conserva la rama física read-only de Phase 9. Su rerun no
crea, consulta para reparar ni reconstruye SQLite automáticamente; puede
confirmar los outputs físicos aunque la DB se haya perdido. Query sobre una
DB ausente/corrupta falla y su recuperación usa import/rebuild explícitos.

## Consultas, estadísticas y planificación — 10.5/10.6

```csharp
var catalog = new AssetCatalog(context);
var filter = new CatalogFilter(assetType: "portrait",
    classification: new Dictionary<string, string> { ["location"] = "treskal" },
    traits: [new CatalogVisualTrait("/eye_color", "green-grey", CatalogTraitType.String)]);
var assets = catalog.Query(filter);
var statistics = catalog.Statistics(filter);
var coverage = catalog.Coverage(filter, targetCount: 10);
```

AssetId, type, profile, varias classifications y varios traits se combinan
con AND y comparación exacta. Todos los valores se parametrizan, incluidos
objetivos/nombres, Unicode, comillas y payloads SQL injection. No existe
Query(string sql) ni fragmentos SQL de caller.

Statistics expone total y distribuciones type/profile, dimensión/value y
trait path/value/type, también sobre una selección filtrada. Las distribuciones
son hechos para diversidad, sin calificativos creativos ni sinónimos.

Coverage exige target > 0 y actual >= 0. remaining=max(0,target-actual),
ratio=actual/target, IsComplete=actual>=target. El ratio puede superar 1;
no se recorta ni se pierde información sobre excedentes.

SaveObjective persiste UniverseId, ID/nombre, CatalogFilter y target. Sus
criterios son genéricos, sin imponer vocabulario o presencia actual de valores.
SaveCampaign relaciona objetivos existentes; FK impide referencias inexistentes.
Estas operaciones explícitas permiten editar datos operativos atómicamente;
no modifican assets. Planning devuelve CatalogPlanningReadModel con objetivos,
cobertura, distribución de traits y objetivos de cada campaña. Esta es la
frontera estructurada para RoDo futuro; no implementa agente, IA ni scheduler.

## Visual Identity

Se conserva el BLOB original, sin reserialización. El capability corresponde
al documento de perfil con filename canónico `_visual_identity.json`, cualquiera
que sea su role en el universo. CatalogVisualIdentity.Extract devuelve escalares
de un objeto JSON por paths RFC 6901: `/` se escapa ~1 y `~` se escapa ~0.

Strings conservan valor/casing/Unicode; números conservan su lexema JSON y
tipo Number; booleans true/false conservan tipo Boolean. Objetos se recorren,
arrays se recorren por índice decimal invariante. Null y contenedores vacíos
no inventan traits, pero siguen presentes en el documento original.
Claves duplicadas, incluso tras escape JSON equivalente, generan STOP.
No hay traducción ni normalización semántica: green-grey continúa green-grey.

## Rebuild — 10.7

CatalogRebuilder.Rebuild es explícito: preflight físico completo, DB temporal
nueva en StateRoot, schema v1, carga completa, integrity_check=ok,
foreign_key_check sin filas, verificación universo/schema e invariantes
lógicas, revalidación física y publicación tras cerrar todas las conexiones.
El temporal exclusivo se hace flush a disco; File.Move sustituye la DB activa
solo tras éxito. Ante fallo anterior a publicación la DB previa permanece
intacta; un temp fallido se conserva sin convertirse en autoridad/backup.

En una DB legible se preservan objetivos/campañas. Si solo están dañados datos
derivados o un índice, se exige metadata v1 del universo correcto y tablas
operativas completas con FKs coherentes antes de preservarlos. Daño operativo
legible exige revisión y STOP: no se descartan silenciosamente datos del usuario.

DB inexistente/ilegible permite reconstruir únicamente información física.
CatalogRebuildResult declara AssetCount, OperationalDataPreserved y
PriorCatalogUnreadable. Objetivos/campañas e historial de auditoría no se
reconstruyen mágicamente si su única copia desapareció. No se crea un formato
de backup ni retención desde el rebuild; esos mecanismos están implementados separadamente en [Fase 12](BACKUPS.md).

Una DB de otro universo/schema desconocido nunca se sustituye automáticamente,
ni siquiera en rebuild. Una DB corrupta nunca se acepta ni se repara parcialmente.

## Concurrencia, seguridad y límites

Mutex Catalog deriva de StateRoot canónico + AssetCatalog.db. Protege
creación, lectura, actualización, import y rebuild, entre threads y procesos;
no crea lock files. Adquisición no bloqueante devuelve catalog_busy y exige
retry explícito. SQLite busy/locked también se comunica como catalog_busy,
con rollback. No hay bucles de retry infinitos.

Orden: Job (si hay pipeline) → Production → Archive read lease → Catalog.
El Archive lease no habilita writes ni crea infraestructura. Phase 9 libera
sus leases físicos antes de entrar al catálogo. Consultas/objetivos/Initialize
solo adquieren Catalog y no esperan leases de fuentes, evitando ciclos.

Workspace/StateRoot/catalog/sidecars se comprueban por atributos en cada
frontera; reparse, UNC/network drive, casing conflict y entries no regulares
provocan STOP. Las ubicaciones de assets persistidas son relativas con `/`,
usando containment/segmentos seguros existentes; no contienen roots absolutos.
Los BLOB documentales mantienen exactamente el contenido proporcionado por
sus autores, sin modificar texto para eliminar strings que parezcan paths.

La coordinación cubre clientes NAP. El Workspace y asset roots deben estar
bajo control del host: mutación concurrente por herramientas externas o
falsificación de metadata no es una transacción soportada entre roots.
DELETE/FULL y flush se apoyan en las garantías del filesystem local; no hay
transacción única entre journal, archivos físicos y DB. Los checkpoints hacen
explícitas las fronteras de recovery. Las consultas materializan snapshots y
documentos; paginación y optimizaciones para catálogos enormes quedan fuera del MVP.

Issues catalog_missing/invalid/corrupt/wrong_universe/schema_unsupported,
asset_conflict/source_invalid/integrity_failed/busy/rebuild_failed distinguen
causas con Error + Stop. Errores I/O operativos pueden propagarse sin éxito falso.

Fase 11 añade WPF/thumbnails y Fase 12 añade backups históricos y Git bundle de solo lectura. No hay Git operativo de Fase 13, servicios, red/API cloud, generación de assets ni IA RoDo. CI sigue ubuntu-latest.
La validación local se ejecuta en Windows; no se declara CI Linux ejecutada.
