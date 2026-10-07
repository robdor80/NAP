# Fase 12 — Backups

**Fase 12 — HECHA. 12.1–12.6 — HECHOS. Fase 13 — HECHA. Siguiente: Fase 14 — UI profesional.**

El [Git operativo de Fase 13](GIT_PRODUCTION.md) usa un runner privado separado (`GitProductionProcess`). `GitBackupProcess` sigue exclusivamente read-only/backup, sin red/push/staging. Snapshots, bundles y commit/push comparten el mutex Production existente; los contratos de backup/restore/retención y SQLite v1 no cambian. UI profesional de Backups continúa pendiente de Fase 14.

Motor portable en `NAP.Core`, sin UI nueva ni globals. Cada entrada pública recibe
`UniverseContext`. Ningún caller puede elegir una DB/repo fuente ni un archivo de
restore mediante una ruta. Los planes de restore/retención son inmutables,
construidos internamente y vinculados al universo y a sus tres roots mediante
un fingerprint en memoria. No se persisten planes ni roots absolutos.

## ArchiveRoot y TeraBox

ArchiveRoot es la carpeta local existente autorizada por el usuario. NAP **no la
crea**. Puede estar sincronizada externamente por el cliente TeraBox, pero NAP no
consulta ni garantiza esa sincronización. No hay API, HTTP, SDK, credenciales,
automatización de UI ni estado remoto. `LocallyVerified` significa únicamente
validación de bytes locales. La disponibilidad remota corresponde al cliente externo.

```text
ArchiveRoot/
├── _nap/
│   ├── archive.lock              # contrato de Fase 8, sin cambios
│   └── master_index.json         # contrato de Fase 8, fuera de retención
├── <assets maestros>/           # fuera de retención
├── NAP_DATABASE_BACKUPS/
│   ├── b_<32 hex>/
│   │   ├── manifest.json
│   │   └── catalog.db
│   └── _recovery/
│       └── recovery_<32 hex>/
│           ├── recovery_manifest.json
│           └── AssetCatalog.corrupt.db # evidencia byte-exact; NO backup SQLite válido
├── NAP_REPOSITORY_SNAPSHOTS/
│   └── b_<32 hex>/
│       ├── manifest.json
│       └── tree/                 # incluye directorios vacíos
└── NAP_GIT_BUNDLES/
    └── b_<32 hex>/
        ├── manifest.json
        └── history.bundle
```

Cada backup es autocontenible. No depende de un índice central ni de la DB activa.
`BackupId.New()` genera `b_` + `Guid.NewGuid().ToString("N")`: 32 hex ASCII lowercase.
Las colisiones se rechazan incluso entre kinds. Ni timestamps ni nombres de
fichero sustituyen al manifest como autoridad. No se sobrescriben finales.

## API pública

```csharp
var database = new DatabaseBackupService().Create(context);
var snapshot = new RepositorySnapshotService().Create(context);
var bundle = new GitBundleService().Create(context);
BackupHistory history = new BackupHistoryReader().Read(context);

var retention = new BackupRetentionService();
var retentionPlan = retention.Plan(context,
    new BackupRetentionPolicy(newest: 5, daily: 7, weekly: 4, monthly: 12));
// Mostrar Decisions (Keep/Delete), revisar y ejecutar explícitamente:
BackupRetentionResult retained = retention.Execute(context, retentionPlan);

var restore = new DatabaseRestoreService();
DatabaseRestorePlan restorePlan = restore.PlanRestore(context, database.BackupId);
// Mostrar Source, ActiveState y RequiresSafetyBackup; ejecutar explícitamente:
DatabaseRestoreResult restored = restore.Execute(context, restorePlan);
```

No se ejecuta retención al crear backups. No hay scheduler. No hay restore de
ProductionRoot, snapshot o bundle. La pantalla profesional pertenece a Fase 14.9;
el shell sigue con Backups deshabilitado.

## Manifest v1 cerrado

`manifest.json` es UTF-8 JSON. Campos comunes **obligatorios**:

| Campo | Contrato |
| --- | --- |
| `schema_version` | entero exactamente 1 |
| `backup_id` | identidad técnica canónica, igual al directorio final |
| `universe_id` | igual al UniverseContext |
| `backup_kind` | `Database`, `RepositorySnapshot` o `GitBundle`, exacto |
| `created_utc` | formato round-trip `O`, cultura invariant, offset +00:00 |
| `artifact` | exactamente `catalog.db`, `tree` o `history.bundle` según kind |
| `sha256` | 64 hex lowercase, según definición del kind |
| `size` | bytes; suma de bytes de archivos para el snapshot |

Database añade exclusivamente `catalog_schema_version: 1` y `purpose`
(`Manual` o `PreRestore`). RepositorySnapshot añade exclusivamente `file_count`,
`files` y `directories`. Cada file tiene exactamente `relative_path`, `size` y
`sha256`; directories es un array de paths relativos. GitBundle añade exclusivamente
`head` (OID Git 40 o 64 hex), `dirty` (boolean) y
`contents: "local_git_objects_and_refs_only"`.

Campos desconocidos, ausentes o duplicados (también dentro de cada file), tipos
incorrectos, nulls en campos requeridos, versión desconocida, universo ajeno,
IDs/kinds inválidos y paths absolutos/traversal causan STOP. Parser con profundidad
máxima 16 y manifest de hasta 16 MiB. Paths con `/`, segmentos portables seguros,
casing exacto y arrays ordenados Ordinal, sin duplicados; cada padre de un path
anidado debe aparecer en directories. No se guardan roots, usuario del host,
ubicación TeraBox, stdout/stderr Git, tokens o credenciales. Los payloads conservan
los bytes originales de las fuentes; no se reescriben documentos o archivos.

### Definición precisa de SHA/size

Database/GitBundle: SHA-256 de todos los bytes del archivo; size es su longitud.
RepositorySnapshot: SHA-256 del JSON UTF-8 compacto emitido por
`BackupManifestCodec.TreeHash`, con orden de propiedades fijo:

```json
{"files":[{"relative_path":"README.md","size":6,"sha256":"<64 hex>"}],"directories":["empty"]}
```

Files y directories se ordenan Ordinal, números enteros invariant, sin espacios,
sin BOM; escaping de `Utf8JsonWriter` con encoder BCL predeterminado .NET 8.
El hash incluye paths, hashes y longitudes de **cada** archivo y todos los
directorios (incluidos vacíos), y excluye metadata temporal/identidad del backup.
Size suma solo bytes de archivos; no mide bloques asignados ni longitud del JSON.
El SHA del manifest original se conserva por separado en memoria para detectar
incluso cambios de whitespace entre Plan y Execute.

## 12.1 — Backup SQLite — HECHO

Fuente única `context.Storage.CatalogPath`. Orden Archive → Catalog. Se comprueban
workspace local, StateRoot, aislamiento, casing, atributos regulares y sidecars.
WAL/SHM/journal presentes requieren revisión y STOP, sin recuperación automática.
Se valida el schema exacto v1, metadata del universo, `PRAGMA integrity_check`,
`foreign_key_check`, journal DELETE y todas las invariantes lógicas de Fase 10.

Se reserva un archivo exclusivo dentro de `.pending-<guid>` en el namespace DB;
`Microsoft.Data.Sqlite.SqliteConnection.BackupDatabase` produce el snapshot
SQLite coherente. Se cierran conexiones (sin pooling), se valida nuevamente el
snapshot completo, flush a disco y SHA/tamaño. El origen se vuelve a comprobar
contra su hash/longitud inicial para detectar escrituras externas. El manifest
se crea con CreateNew y flush; se verifica la unidad y se publica mediante rename
de directorio hermano sin overwrite.

No hay reconstrucción desde assets. Se conservan todas las tablas: assets,
classifications, files, documents, visual_traits, objectives y filtros,
campaigns y relaciones, audit_state y metadata. Backup API puede actualizar
contadores internos de cabecera en el destino; los datos se preservan, y el
archivo fuente permanece byte-exact intacto. Schema SQLite v1 no cambia.

## 12.2 — Histórico local — HECHO

`BackupHistoryReader.Read` usa Archive lease read-only. Solo enumera los tres
namespaces. Reconoce directorios por BackupId y después valida identidad,
manifest cerrado y artifact completo. DB: vuelve a ejecutar validación fuerte.
Snapshot: vuelve a enumerar/hash todo el árbol, comprueba exactamente files y
directories, rechazando entradas extra. Bundle: SHA/tamaño, header Git v2/v3
autocontenido sin prerequisites y checksum interno del PACK (SHA-1/SHA-256
según formato de objetos), con HEAD declarado entre los OIDs anunciados,
todo mediante lectura BCL sin requerir Git o repo
activo. La validación Git adicional de requisitos/refs se realiza al crearlo.

Orden descendente por CreatedUtc y después BackupId Ordinal. Entradas no
gestionadas producen `backup_unmanaged` Warning/Continue, se dejan intactas y
nunca se adoptan. Una unidad reconocible inválida/ajena produce STOP para toda
la lectura, sin un resultado que la presente como restaurable. Reparse en
cualquier entrada causa STOP. Perder la DB activa no afecta al discovery.

## 12.3 — Retención — HECHO

Plan es read-only y muestra todas las decisiones Keep/Delete. Policy explícita
por caller, aplicada **por kind**. Se conserva la unión de:

1. Los N más recientes.
2. Un representante (el más reciente) de cada uno de los D buckets diarios
   no vacíos más recientes, con fecha calendario UTC.
3. Un representante de cada uno de los W buckets semanales no vacíos más
   recientes. Semana de lunes 00:00 UTC a lunes siguiente; clave = lunes.
4. Un representante de cada uno de los M buckets mensuales no vacíos más
   recientes. Clave = primer día del mes UTC.

Empates: BackupId Ordinal. Los buckets no son ventanas relativas al reloj de
ejecución: cuentan periodos con backups existentes, evitando que un largo
intervalo sin backups borre toda la cobertura. Siempre se protege el backup
válido más reciente de cada kind incluso si todos los valores son cero. No
hay opción implícita de eliminar la última copia. Valores negativos se rechazan.

El plan congela SHA del inventario completo de los tres namespaces (paths,
archivos, directorios, manifests exactos y entradas ajenas). Execute adquiere
Archive, vuelve a validar inventario/histórico y recomputa decisiones **antes
de borrar cualquiera**. Cualquier alta/baja/cambio produce `retention_stale_plan`.
Nuevos reparse producen STOP. No acepta paths del caller ni examina masters,
master_index, lock, active DB, settings, Job states o el otro universo.

Cada unidad Delete se verifica y renombra a `.deleting-<guid>` en su mismo
namespace antes de cleanup. Artifact y manifest dejan el histórico reconocido
como una unidad. Cleanup usa exclusivamente paths verificados, deletes de
archivos y deletes de directorios no recursivos, desde hijos a padres. Nunca
borra rutas arbitrarias ni sigue links. Fallos I/O se comunican como
`retention_execution_failed`, con `BackupException.CompletedBackupIds` exactos;
una unidad en cuarentena requiere revisión explícita y nunca se promueve ni
se limpia automáticamente. No hay transacción filesystem única que borre
múltiples unidades; una interrupción durante Execute es explícitamente parcial.

## 12.4 — Restore SQLite — HECHO

PlanRestore selecciona solo BackupId reconocido, Kind Database, validando bytes,
manifest, schema/universe/integridad/FKs/reglas lógicas y DB activa. Congela
manifest SHA, active SHA/size y `CatalogFileStamp` (incluye identidad física).
No escribe. Execute usa Archive → Catalog y revalida todo. Cambio de backup,
contexto o DB activa provoca STOP, incluso reemplazo físico con bytes iguales.

DB activa válida: backup automático verificado `purpose=PreRestore` mediante
el mismo motor, bajo los leases ya adquiridos. Si falla, no se reemplaza la DB.
DB activa ausente: se permite restore sin safety backup; StateRoot debe existir
y no puede haber sidecars pendientes. DB activa corrupta: restore permitido
**solo después de preservar y verificar obligatoriamente sus bytes exactos**
como evidencia forense separada. Universo ajeno legible, versión de schema
desconocida, busy/IO, sidecars o paths inválidos siguen provocando STOP.

`ActiveCatalogState` distingue Missing, Valid y Corrupt; el plan expone
RequiresSafetyBackup/RequiresRecoveryPreservation. La detección de corrupción
incluye bytes no SQLite, archivo vacío, daño físico, schema v1 dañado,
foreign keys o invariantes lógicas inválidas. La identidad de universo se
consulta independientemente de otros objetos/columnas dañados: metadata legible
de otro universo nunca se clasifica como corrupción local. Para bytes opacos sin
identidad legible, el scope procede exclusivamente del CatalogPath autorizado
del contexto, no se atribuye una identidad SQLite que no se pueda leer.

### Preservación forense obligatoria

`CatalogRecoveryArtifact` y `CatalogRecoveryId` son tipos separados de
BackupHistoryEntry/BackupId/BackupKind. El ID es `recovery_` + 32 hex UUID.
Destino: `NAP_DATABASE_BACKUPS/_recovery/<recovery-id>/`, con archivo
`AssetCatalog.corrupt.db` y `recovery_manifest.json` cerrado v1. Sus ocho campos
obligatorios son schema_version, recovery_id, universe_id, created_utc (UTC `O`
invariant), reason=`active_catalog_corrupt`, artifact=`AssetCatalog.corrupt.db`,
sha256 y size. No paths absolutos ni datos de host/credenciales. Size puede ser
cero para preservar un catálogo truncado vacío. No se intenta abrir ni validar
esa evidencia como SQLite y no se la denomina backup válido.

Con Archive → Catalog ya adquiridos: se copia el CatalogPath a un temporal
hermano `.pending-recovery-*` bajo `_recovery`, usando CreateNew y Flush(true).
Se verifica size/SHA contra la observación del plan y se comparan en streaming
**todos los bytes** del source y de la copia. Se crea/flush el manifest, se valida
formato cerrado + bytes + contenido exacto de la unidad, y se publica por rename
sin overwrite. Se vuelve a verificar la unidad final y su igualdad con el
catálogo activo antes de preparar candidate. Si cualquier preservación falla,
no se publica el restore ni se pierde la DB activa. Colisiones no se sobrescriben.

BackupHistory reconoce `_recovery` como namespace reservado y no enumera sus
unidades como backups. Recovery IDs no pueden seleccionarse para restore y
ni siquiera un recovery manifest trasladado a un directorio BackupId puede
convertirse en manifest de backup válido. Retención normal nunca incluye esas
unidades en Keep/Delete ni las borra. El inventario stale incluye sus bytes
para detectar cambios durante una ejecución, sin darles autoridad de backup.
No existe limpieza automática ni política de retención forense; las unidades
finales y los temporales fallidos se conservan para revisión explícita.

Se copia el artifact a `.restore-<guid>.db` con CreateNew dentro de StateRoot,
se verifica SHA/tamaño y validación SQLite completa, se revalida el plan, se
verifica de nuevo candidate y se hace flush. Si hubo corrupción, también se
revalida la evidencia forense publicada antes de reemplazar. DB existente (válida
o corrupta): `File.Replace` con
`.rollback-<guid>.db` local; DB ausente: `File.Move` sin overwrite. Ambas
operaciones permanecen en el mismo filesystem. Se reabre y valida la DB final;
si falla y existía DB previa, se repone atómicamente desde rollback (incluidos
sus bytes corruptos exactos), conservando
el candidato fallido. Tras éxito se elimina solo la copia local rollback
verificada; el safety backup en ArchiveRoot permanece sujeto a política explícita.

Resultado declara Restored, BackupId origen, PriorActiveState, SafetyBackupId y
RecoveryArtifact nullable. Missing declara explícitamente que no había bytes
previos ni safety backup; Valid devuelve safety backup, Corrupt devuelve evidencia
forense y ningún safety backup SQLite falso. No toca
ProductionRoot, masters, archivos físicos ni Job states. Explorer detecta el
reemplazo con su mecanismo de identidad existente; no hay hack en Fase 11.

## 12.5 — Repository snapshot — HECHO

Fuente única ProductionRoot. Orden Production → Archive. Traversal iterativo,
no symlink/junction/reparse/device, contención por segmentos seguros, nombres
exactos, paths `/` y orden Ordinal. Copia **todos** los archivos normales:
tracked/untracked, dirty, ignored, README, `.gitignore`, `.github`, assets,
docs, etc., y todos los directorios incluidos los vacíos.

En Linux se usa statx sin abrir la entrada para distinguir archivos/directorios
ordinarios de FIFO/socket/dispositivos; FileAttributes por sí solo no basta.
Si el host Linux no puede proporcionar esa información se aplica STOP.

Se excluye exclusivamente el metadata root `.git`, sin enumerar su contenido;
un alias/reparse en ese root se rechaza antes de excluirlo. Casing ambiguo `.GIT`
causa STOP. Un `.git` anidado se trata como contenido físico normal, sujeto al
mismo rechazo de links. No se consulta `.gitignore` ni se reutiliza el scanner
de Fase 3 con una semántica incompatible.

Fingerprint previo → copia CreateNew + flush → verificación del destino →
fingerprint completo posterior del origen → publicación de directorio temporal
hermano sin overwrite. Diferencias de bytes/paths/directorios producen STOP,
sin snapshot final. ProductionRoot queda intacto.

## 12.6 — Git bundle — HECHO

Fuente única ProductionRoot, que debe coincidir exactamente con el toplevel
Git. Solo repos standalone con `.git` como directorio local normal; gitdir files,
worktrees vinculados, alternates, `.promisor` y config includes externos requieren
otra política y producen STOP. Repos sin commits no pueden producir bundle.

Allowlist **privada**, sin runner genérico público:

```text
git rev-parse --show-toplevel
git rev-parse --absolute-git-dir
git rev-parse --verify HEAD
git status --porcelain=v1 --untracked-files=all
git show-ref --head
git bundle create <temp-controlado> --all
git bundle verify <temp-controlado>
```

`show-ref --head` es la lectura adicional necesaria para detectar cambios de refs
sin modificar Git. Todos llevan prefijos constantes `--no-pager`,
`-c core.fsmonitor=false`, `-c core.untrackedCache=false`, `-c gc.auto=0`,
`-c maintenance.auto=false`, `-C <ProductionRoot>`.

ProcessStartInfo, UseShellExecute=false, CreateNoWindow=true, ArgumentList sin
shell/concatenación; timeout 2 minutos por comando; stdout/stderr drenados en
paralelo con máximo de 1 Mi caracteres cada uno, cancelación/kill del árbol del
proceso al fallar. Se eliminan variables heredadas GIT_* y se fijan
GIT_TERMINAL_PROMPT=0, GIT_OPTIONAL_LOCKS=0, GIT_CONFIG_NOSYSTEM=1,
GIT_CONFIG_GLOBAL al null device, GIT_NO_REPLACE_OBJECTS=1,
GIT_NO_LAZY_FETCH=1 y GIT_ALLOW_PROTOCOL vacío. No se usa shell, red, autenticación
o GitHub API; no hay add/commit/push/fetch/pull/merge/rebase/reset/clean/checkout,
maintenance/gc ni creación de refs.

Bundle temporal → Git verify → flush → SHA/size → manifest → verificación local
→ rename sin overwrite. HEAD y dirty se observan y guardan, sin nombres de
archivos dirty ni remotes. Dirty **no bloquea** el backup. Fingerprints completos
del working tree y `.git`, junto con HEAD/refs/status, se comparan antes/después;
cambio externo o write de Git causa STOP. Git ausente, timeout, exceso de output
y exit code no cero tienen diagnósticos específicos sin volcar paths/salida.

**Snapshot = bytes actuales, incluidos dirty/untracked/ignored. Bundle = objetos
y refs/historia local comprometida.** El index staging y cambios sin commit no
se incluyen en el bundle. Son complementarios. No se restauran automáticamente
ambos sobre ProductionRoot.

## Coordinación, fallos y garantías

Se reutilizan ArchiveLock/archive.lock, ExecutionMutex Production y CatalogBoundary.
Orden global conservado: Job → Production → Archive → Catalog; Fase 12 no usa Job.
DB backup/restore: Archive → Catalog. Snapshot/bundle: Production → Archive.
History/retención: Archive. Locks no bloqueantes: ocupado → STOP/retry explícito.
Se liberan en todos los caminos, sin retener conexiones tras publicación.

BackupException lleva NapIssueReport con mensajes sin roots/paths, y códigos
estables `backup_*`, `restore_*`, `repository_snapshot_changed`, `git_*` y
`retention_*`. Se preservan los códigos específicos de validación Catalog,
Archive y Production con mensajes redactados. No hay error/success genérico que
oculte una copia inválida o una ejecución destructiva parcial.

Una operación fallida puede conservar `.pending-*`, `.restore-*`, `.rollback-*`
o `.deleting-*` para diagnóstico/recuperación; no son backups válidos y nunca
se adoptan ni se limpian automáticamente. Planificar retención incluye los
residuos del namespace en el fingerprint, pero no los selecciona para borrado.

Los mutexes protegen escritores NAP, no herramientas externas. Las comparaciones
detectan cambios relevantes durante backup, pero no constituyen una transacción
con otras aplicaciones. La validación por atributos debe usarse en roots bajo
control del host; no ofrece contención contra un atacante que sustituya paths
justo entre una comprobación y una operación filesystem. Flush y rename dependen
de las garantías del filesystem local, sin fsync portable de directorios ni
promesa de durabilidad ante corte eléctrico del dispositivo sincronizado.

No se añaden dependencias NuGet ni tablas de backup. Tests portables net8.0,
repos Git temporales reales, corrupción/tampering, multiuniverso, junctions,
planes stale, safety failures y barreras de concurrencia. Los comandos Git de
creación de fixtures/recuperación de tests trabajan exclusivamente en directorios
temporales; no pertenecen al runtime ni modifican la rama del usuario.
