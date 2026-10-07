# Informe de auditoría — Fase 12 completa (12.1–12.6)

Validación local final: 7 de octubre de 2026, Windows, .NET 8.
Trabajo exclusivamente en el working tree de `robdor80/NAP`.

Corrección funcional de 12.4: un catálogo activo corrupto ahora se recupera desde
backup válido después de publicar/verificar evidencia forense obligatoria de sus
bytes previos. Esa evidencia no es un backup SQLite válido y queda excluida de
restore/histórico/retención ordinarios. DB válida conserva safety backup normal;
DB ausente declara explícitamente su ausencia. La corrección añade 21 casos de
test y reemplaza un caso obsoleto: 3.004 → 3.024 tests, sin Fases 13/14.

1. **Rama:** `feature/backups-v1`, exactamente la requerida. Sin cambio de rama.

2. **HEAD inicial y final:** `2147158a5bf5a7bfcac213878233ab310472e6c2`.
   Preflight ejecutado antes de cualquier modificación; status inicial vacío.

3. **`git status --short` final:** siete archivos modificados y 26 nuevos sin staging:

   ```text
    M CHANGELOG.md
    M README.md
    M docs/MULTI_UNIVERSE_ARCHITECTURE.md
    M docs/NAP_MASTER_SPEC.md
    M docs/PRODUCTION_STORAGE.md
    M docs/SQLITE_CATALOG.md
    M src/NAP.Core/NapIssueCodes.cs
   ?? docs/BACKUPS.md
   ?? docs/PHASE12_AUDIT.md
   ?? src/NAP.Core/BackupFileTypes.cs
   ?? src/NAP.Core/BackupHistoryReader.cs
   ?? src/NAP.Core/BackupManifestCodec.cs
   ?? src/NAP.Core/BackupModels.cs
   ?? src/NAP.Core/BackupRetentionService.cs
   ?? src/NAP.Core/BackupStorage.cs
   ?? src/NAP.Core/BackupTree.cs
   ?? src/NAP.Core/CatalogRecoveryArtifact.cs
   ?? src/NAP.Core/CatalogRecoveryStore.cs
   ?? src/NAP.Core/DatabaseBackupService.cs
   ?? src/NAP.Core/DatabaseRestoreService.cs
   ?? src/NAP.Core/GitBackupProcess.cs
   ?? src/NAP.Core/GitBundleIntegrity.cs
   ?? src/NAP.Core/GitBundleService.cs
   ?? src/NAP.Core/RepositorySnapshotService.cs
   ?? tests/NAP.Tests/BackupHistoryTests.cs
   ?? tests/NAP.Tests/BackupRetentionTests.cs
   ?? tests/NAP.Tests/BackupSafetyTests.cs
   ?? tests/NAP.Tests/BackupTestSupport.cs
   ?? tests/NAP.Tests/CorruptCatalogRestoreTests.cs
   ?? tests/NAP.Tests/DatabaseBackupTests.cs
   ?? tests/NAP.Tests/DatabaseRestoreTests.cs
   ?? tests/NAP.Tests/GitBundleBackupTests.cs
   ?? tests/NAP.Tests/RepositorySnapshotBackupTests.cs
   ```

4. **Arquitectura:** servicios separados en Core para DB backup, restore,
   snapshot, Git bundle, discovery y retención. Primitivas privadas compartidas
   de storage, manifest, traversal, tipo filesystem y PACK. Contexto explícito
   por operación; sin CurrentUniverse/CurrentCatalog/CurrentProductionRoot global.
   Fases 8–11, Core + Universe Profile, proyectos WPF/MVVM y CI se preservan.

5. **Archivos nuevos:** los 26 `??` anteriores: 15 Core, nueve tests/support,
   `BACKUPS.md` e informe de auditoría. Ninguna nueva UI, config o dependencia.

6. **Archivos modificados:** los siete `M` anteriores. Docs: estado actual,
   alcance y enlaces actualizados, sin reescribir entradas históricas del
   changelog. Código existente: solo 23 NapIssueCodes nuevos. `git diff --stat`
   de archivos tracked: siete archivos, 74 inserciones y 44 eliminaciones;
   Git no incluye los archivos untracked en ese resumen hasta staging.

7. **Modelos/API:** BackupId, BackupKind, BackupFile, BackupManifest,
   BackupHistoryEntry/BackupHistory, BackupException; DatabaseBackupService.Create;
   BackupHistoryReader.Read; BackupRetentionPolicy/Decision/Plan/Result y
   BackupRetentionService.Plan/Execute; ActiveCatalogState,
   DatabaseRestorePlan/Result y DatabaseRestoreService.PlanRestore/Execute;
   RepositorySnapshotService.Create; GitBundleService.Create; CatalogRecoveryId/
   CatalogRecoveryArtifact (evidencia forense; no BackupKind). Todos los entry
   points reciben UniverseContext, y restore selecciona exclusivamente BackupId.
   Planes con constructores internos y colecciones read-only; no paths arbitrarios.

8. **Layout:** `ArchiveRoot/NAP_DATABASE_BACKUPS/<BackupId>/{manifest.json,catalog.db}`;
   `NAP_REPOSITORY_SNAPSHOTS/<BackupId>/{manifest.json,tree/}`;
   `NAP_GIT_BUNDLES/<BackupId>/{manifest.json,history.bundle}`.
   No índice central obligatorio. `_nap/master_index.json` y assets maestros
   permanecen fuera de estos namespaces y de la retención. Evidencia forense
   separada: `NAP_DATABASE_BACKUPS/_recovery/<recovery-id>/` con
   `AssetCatalog.corrupt.db` y `recovery_manifest.json`; no es backup restaurable.

9. **BackupId:** `b_` + UUID generado con Guid.NewGuid, 32 hex lowercase ASCII,
   inmutable y sin timestamp como autoridad. Colisiones comprobadas entre los
   tres kinds y publicación sin overwrite.

10. **Manifest:** schema_version exactamente 1; campos comunes backup_id,
    universe_id, backup_kind, created_utc, artifact, sha256, size.
    Database: catalog_schema_version=1, purpose Manual/PreRestore.
    Snapshot: file_count, files(relative_path,size,sha256), directories.
    Git: head, dirty, contents=local_git_objects_and_refs_only.
    Parser cerrado también en objetos file: campos desconocidos/duplicados,
    ausentes, nulls, versión/kind/universo incorrectos → STOP.
    UTC round-trip invariant, paths relativos seguros `/`, orden Ordinal;
    límite 16 MiB/depth 16. Sin roots absolutos, tokens, credenciales ni stdout Git.

11. **12.1 SQLite backup:** DB única de context.Storage.CatalogPath, snapshot
    temporal en el namespace autorizado, cierre de conexiones, validación fuerte,
    flush/SHA/size, manifest verificado y rename de unidad sin overwrite.
    No reconstruye datos desde assets y no escribe el catálogo fuente.

12. **Mecanismo SQLite:** SqliteConnection.BackupDatabase de Microsoft.Data.Sqlite
    existente. Source read-only, destination exclusivo, DELETE/FULL y pooling=false.
    Las cabeceras internas del destino pueden diferir; todos los datos de todas
    las tablas se conservan y se verifican en tests. El origen queda byte-exact.

13. **Validaciones DB:** workspace/state local aislado, ArchiveRoot existente,
    casing/contención, archivo regular, sin reparse ni journal/WAL/SHM;
    schema exacto v1, metadata de universo, journal DELETE, integrity_check,
    foreign_key_check e invariantes lógicas completas de CatalogAssetData.
    SHA/size del source se revalidan antes de publicar; snapshot validado
    nuevamente antes de convertirlo en final.

14. **12.2 histórico:** solo los tres namespaces; manifest + artifact,
    identity/kind/universe y contenido completo. Orden CreatedUtc descendente,
    BackupId Ordinal. Corruptos reconocibles → STOP; entradas ajenas → issue
    backup_unmanaged y no adopción. Funciona tras pérdida de DB y ProductionRoot.

15. **TeraBox:** únicamente sincronización externa potencial de ArchiveRoot.
    NAP no sabe si se sincronizó, no comprueba nube y no usa API/HTTP/SDK/login.
    LocallyVerified declara exclusivamente validación de bytes locales.

16. **12.3 retención:** Plan read-only, revisión estructurada Keep/Delete,
    Execute explícito con policy caller-supplied por kind. Se congela inventario
    completo incluyendo manifests exactos y entradas ajenas; se revalida y
    recomputa la decisión antes de borrar cualquiera. Cambios → stale STOP.
    No scheduler ni ejecución al crear backups.

17. **Calendario UTC:** unión de N newest, representante más reciente de cada
    uno de D días/W semanas/M meses no vacíos más recientes. Semana comienza
    lunes UTC; mes en su día 1. Empates por BackupId Ordinal. No depende de la
    fecha de ejecución, cultura local o timestamps filesystem. Siempre al menos
    el más reciente por kind aunque policy sea cero; no opción de borrar la última copia.

18. **Borrado seguro:** solo unidades NAP plenamente verificadas; rename hermano
    a `.deleting-*` retira artifact+manifest juntos. Cleanup con lista exacta,
    checks de atributos/hash y deletes de directorios no recursivos. No masters,
    master_index, archive.lock, active DB, settings, jobs, Production o universo B.
    Sin seguir links. Error parcial devuelve STOP y CompletedBackupIds exactos;
    no transacción de borrado multiunidad ni fallo parcial silencioso.

19. **12.4 restore:** PlanRestore read-only por BackupId Database reconocido;
    manifiesto/bytes/SQLite completos y estado actual verificados. Binding al
    contexto, SHA del manifest, active SHA/size y CatalogFileStamp congelados.
    Execute revalida antes del safety backup y antes de publicación; stale STOP.

20. **Safety backup:** automático y obligatorio para active DB válida,
    purpose=PreRestore, mediante DatabaseBackupService con leases ya adquiridos.
    Queda verificado en ArchiveRoot antes de preparar/publicar candidate.
    Si falla, active DB se conserva sin reemplazo. Resultado incluye SafetyBackupId.

21. **Publicación restore:** candidate CreateNew + copia/flush en StateRoot,
    hash/size/schema/universe/integridad/FKs/lógica completos. Reemplazo existente
    mediante File.Replace con rollback local; ausencia mediante File.Move sin
    overwrite. Mismo filesystem. Reapertura y hash/validación final; fallo con DB
    previa repone su rollback atómicamente y conserva candidate fallido.
    Tras éxito se elimina solo rollback local verificado, no el safety backup.

22. **DB ausente/corrupta:** ausencia real sin sidecars permite restore validado
    sin safety inventado; StateRoot debe existir. Corrupción → preservación forense
    obligatoria byte-for-byte (SHA/size + comparación completa en streaming),
    manifest cerrado v1, publicación sin overwrite y verificación final; después
    candidate válido y reemplazo atómico. Si la preservación falla, NO restore.
    Universo ajeno legible/schema desconocido/busy/IO/sidecars siguen STOP.
    PriorActiveState identifica Missing/Valid/Corrupt; resultado Corrupt incluye
    RecoveryArtifact y no un safety backup SQLite falso. History y retención
    ordinaria excluyen la evidencia; no se elimina automáticamente.

23. **Prueba operativa:** RestoreReturnsExactOperationalDataDocumentsTraitsAssetsAndExplorerRevalidates
    crea asset y objetivo/campaña con filtros, hace backup, modifica objetivos y
    relaciones, restaura y compara planning original y asset lógico completo,
    incluidos traits/documents. Compara todas las tablas del safety backup con
    el catálogo pre-restore. Explorer incrementa su validación fuerte tras el
    reemplazo físico. No cambia código de Explorer.

24. **12.5 snapshot:** fuente única ProductionRoot, scanner propio iterativo,
    fingerprints previos/posteriores y copia de bytes exactos a tree temporal;
    verifica todo y publica por Directory.Move hermano sin overwrite.

25. **Inclusión:** todos los archivos/directorios normales, vacíos incluidos,
    Unicode/casing, README, .gitignore, .github, dirty, untracked e ignored.
    No usa .gitignore para omitir bytes. Paths seguros portables, `/`, orden Ordinal.

26. **`.git`:** solo el metadata root exacto se excluye sin recorrerlo; alias o
    casing ambiguo se rechaza. `.git` anidado es contenido físico normal, sujeto
    a las mismas reglas de links. La historia del root se conserva en el bundle.

27. **Cambios concurrentes:** Production mutex cubre writers NAP; se compara
    fingerprint completo del árbol inicial, destino y source final. Alta/baja/
    modificación relevante → STOP sin final. Snapshot SHA es el fingerprint
    definido de JSON canónico files/directories con tamaños y hashes; no se
    presenta como un hash de directorio indefinido. Size suma bytes de files.

28. **12.6 bundle:** repo standalone cuyo toplevel exacto coincide con ProductionRoot.
    Bundle temporal --all, Git verify, SHA/size y manifest; sin overwrite. Linked
    worktrees/gitdir files, alternates, partial clones/promisor y config includes
    externos se rechazan. Repos sin commits no producen un bundle válido.

29. **Git allowlist exacta:** rev-parse --show-toplevel;
    rev-parse --absolute-git-dir; rev-parse --verify HEAD;
    status --porcelain=v1 --untracked-files=all; show-ref --head;
    bundle create <temp-controlado> --all; bundle verify <temp-controlado>.
    show-ref es la lectura adicional para congelar/revalidar refs.
    Prefijos constantes: --no-pager, -c core.fsmonitor=false,
    -c core.untrackedCache=false, -c gc.auto=0, -c maintenance.auto=false,
    -C <ProductionRoot>.

30. **Writes/red:** ningún comando runtime altera working tree, index, HEAD,
    refs, config o remotes. Solo writes de backup en ArchiveRoot y reutilización
    de infraestructura ArchiveLock existente. ProcessStartInfo/ArgumentList sin
    shell, timeout por comando de dos minutos, salida acotada y kill del árbol.
    Sin autenticación/red/GitHub/TeraBox; GIT_OPTIONAL_LOCKS=0,
    GIT_TERMINAL_PROMPT=0, configuración global/system deshabilitada,
    lazy-fetch/replaces deshabilitados y protocolos denegados.
    Los comandos init/add/commit/checkout/clone de fixtures se ejecutan únicamente
    en repos temporales de tests, nunca en `robdor80/NAP`, y no son API runtime.

31. **HEAD/dirty:** metadata con OID completo de HEAD y boolean dirty observado;
    sin paths de status o remotes persistidos. Dirty no bloquea. Se revalida
    HEAD/refs/status y fingerprints completos de working tree y metadata `.git`.
    Bundle excluye cambios sin commit; snapshot conserva esos bytes físicos.

32. **Verificación bundle:** Git bundle verify al crearlo; SHA-256/size congelados
    y revalidados antes de publicar. Discovery BCL offline valida header v2/v3
    autocontenido, HEAD entre OIDs anunciados, formato PACK y checksum interno
    SHA-1/SHA-256. Tests verifican bundle, recuperación en nuevo bare repo,
    commits/branch, dirty/untracked fuera de bundle, SHA-256 y HEAD detached,
    corrupción de PACK incluso si se recalcula SHA del manifest.

33. **Locks:** DB backup/restore Archive → Catalog; snapshot/bundle Production →
    Archive; history/retención Archive. Sin Job ni orden inverso. Se reutilizan
    ArchiveLock, ExecutionMutex y CatalogBoundary. STOP no bloqueante ante busy.
    Preflight de source snapshot/Git bajo Production antes de adquirir Archive,
    reduciendo tiempo de bloqueo; leases liberados en errores y éxito.

34. **NapIssueCodes añadidos (23):** backup_source_invalid,
    backup_destination_invalid, backup_collision, backup_integrity_failed,
    backup_manifest_invalid, backup_wrong_universe, backup_busy, backup_changed,
    backup_reparse, backup_unmanaged, backup_io_failed; restore_invalid,
    restore_stale_plan, restore_publication_failed, recovery_preservation_failed;
    repository_snapshot_changed;
    git_unavailable, git_repository_invalid, git_bundle_failed, git_timeout,
    git_output_limit; retention_stale_plan, retention_execution_failed.
    Se preservan códigos específicos Catalog/Archive/Production con mensajes
    redactados sin roots, SubjectPath o Detail.

35. **Schema SQLite:** ningún cambio. Schema v1, tablas, constraints e índices
    intactos; no tablas de backups ni historia en la DB activa.

36. **NuGet:** ninguno añadido. BCL + Microsoft.Data.Sqlite 8.0.31 existente;
    ImageSharp 3.1.12 y archivos de proyectos intactos.

37. **Tests anteriores:** 2.878 pasados, cero fallos/omitidos, medidos antes de
    compilar la implementación nueva.

38. **Tests finales:** 3.024 pasados, cero fallos/omitidos. **146 casos netos
    nuevos** respecto a los 2.878 del preflight original. Corrección de corrupción:
    21 casos añadidos y un caso obsoleto sustituido; incremento neto de 20.

39. **Cobertura añadida:** nueve archivos de tests/support listados en status.
    Backup real/all tables/origen intacto, manifests y hashes, DB corrupta/schema/
    universo/FKs/lógica/sidecars; colisión/busy; history de los tres kinds y
    entradas ajenas; buckets UTC, read-only plans, último por kind, stale por
    artifact/manifest/foreign, exact deletion y error parcial; restore operativo,
    missing/corrupt DB, candidate/safety/publication failures y Explorer;
    árbol visible/empty dirs/Unicode/casing/.git/dirty/untracked, cambios externos,
    junctions reales y dos universos; Git repos reales, commits/branch/refs,
    clean/dirty/staged, recuperación bundle, SHA-1/SHA-256/detached, root exacto,
    unavailable/timeout/output/exit error/collision, bytes/index/config/HEAD intactos,
    offline boundary/PACK; concurrencia con barreras DB vs DB/archive master,
    snapshot vs Production y restore vs locks. Test FIFO específico de Linux.
    Corrección: 21 casos de restore desde corrupción, bytes/SHA/size/evidencia,
    planning read-only, no adopción/restore/retención forense, fallos de copia/
    manifest/collision/candidate/publicación, evidencia revalidada al publicar,
    backup invalidado antes/después del plan, stale activo, wrong universe incluso
    con schema dañado, aislamiento, objetivos/campañas y Explorer. Se sustituye
    el caso obsoleto que esperaba STOP incondicional de DB activa corrupta.

40. **`dotnet restore NAP.sln`:** exit 0; todos los proyectos restaurados/al día.

41. **`dotnet build NAP.sln --configuration Release --no-restore`:** exit 0,
    seis proyectos, cero errores y cero warnings, incluido NAP.App WPF.

42. **`dotnet test NAP.sln --configuration Release --no-build`:** exit 0;
    3.024/3.024 pasados, duración final 47 segundos. Los tests Git se ejecutaron
    realmente; Git utilizado: `2.52.0.windows.1`.

43. **Schema checks:** `pwsh -ExecutionPolicy Bypass -File
    scripts/Test-MultiUniverseSchemas.ps1`, exit 0, **436 checks passed**.
    Schemas existentes y script intactos.

44. **Auditoría diff:** git diff --check limpio, sin whitespace errors;
    git diff --stat y git status --short ejecutados, sin staging.
    Verificación adicional de whitespace en los archivos nuevos. Rama y HEAD
    final idénticos a los del preflight.

45. **Warnings:** cero en build final; tests y checks sin warnings finales.
    Se normalizaron finales de línea del archivo existente NapIssueCodes para
    evitar el aviso local LF/CRLF de Git. No hay nuevos warnings NuGet.

46. **Limitaciones/riesgos para revisar antes de commit:**
    - Validación ejecutada en Windows. CI Ubuntu no se ejecutó remotamente porque
      no hubo push/PR; workflow ubuntu-latest y proyectos portables permanecen
      intactos. WSL no está instalado. La prueba FIFO retorna en Windows, por
      tanto statx/tipo Unix y runtime Linux requieren confirmación en CI.
    - Los namespaces/roots deben permanecer bajo control del host. Los checks
      de atributos no son una defensa contra sustitución maliciosa de paths justo
      entre comprobación y syscall; writers externos tampoco participan en mutexes.
    - Flush/rename dependen del filesystem local; no fsync portable de directorios
      ni garantía adicional de durabilidad frente a corte eléctrico/TeraBox.
    - No limpieza automática de temps/cuarentenas. Fallos pueden conservar
      `.pending-*`, `.restore-*`, `.rollback-*` y `.deleting-*`, nunca promovidos
      o adoptados. Retención multiunidad puede quedar explícitamente parcial tras
      fallo I/O: revisar CompletedBackupIds y manifests de cuarentena.
    - La evidencia de DB corrupta permanece fuera de la retención ordinaria;
      no hay política de cleanup forense automática. El restore requiere su
      preservación completa previa; para bytes opacos sin metadata legible, el
      universe scope procede del CatalogPath autorizado y no de una identidad
      SQLite inventada. Metadata legible de otro universo siempre produce STOP.
    - Git backup requiere repo standalone, Git instalado, historia local completa
      y output dentro del límite. Config includes/alternates/partial clones y
      worktrees enlazados quedan fuera del contrato.
    - SHA/checksums aportan integridad, no firma/autenticación del autor. Las
      fuentes físicas se conservan exactamente: no se filtran ni reescriben los
      secretos que ya pudieran existir en documentos o working tree; los manifests
      no añaden credenciales/roots.

47. **Fase 13:** no implementada ni iniciada. Documentación marca siguiente:
    Fase 13 — Git del repo de producción. Runtime Git se limita a lectura/backup.

48. **UI Backups:** no implementada. ExplorerViewModel conserva `Backups, false`.
    UI profesional/crear/histórico/retención/restore permanece en Fase 14.9.

49. **Git del working tree:** **NO staging, NO commit, NO push, NO merge,
    NO PR, NO cambio de rama, NO force/reset/clean**. HEAD inalterado. Todos los
    cambios quedan revisables en el working tree; commit requiere autorización
    expresa posterior.

Contrato detallado, layout, algoritmo de hashes, calendario y recuperación:
[BACKUPS.md](BACKUPS.md).
