# Job Recovery — Fase 6 · Capítulo 6.5

**Fase 6 — Integridad HECHA (6.1–6.5). Siguiente: Fase 7 — Auditor IA.**

Recovery v1 recupera conocimiento durable y un punto seguro de reanudación; no reejecuta operaciones de assets.

## Requisito histórico y alcance

Tras apagado, cierre, excepción o bloqueo, el futuro host puede llamar a
JobRecoveryScanner.Scan() para reconstruir lo que NAP dejó persistido. Una
operación potencialmente incompleta se identifica por su estado activo. El
journal no contiene información suficiente para inferir la causa del cierre
ni qué efectos físicos llegaron a realizarse.

La continuidad histórica también contempla pérdida temporal de TeraBox. TeraBox
no está implementado: Fase 8 podrá consumir estos checkpoints cuando defina su
ejecución. No hay integración TeraBox, SQLite/AssetCatalog, executor, CLI, UI,
servicios en background, timers, backups o Git en recovery v1.

## Autoridad y checkpoint exacto

El único checkpoint durable autoritativo es el journal final canónico
`<StateRoot>/<job_id>.json`, publicado por [JobStateStore](JOB_STATES.md).
Se deriva JobId del filename y se reutiliza exclusivamente JobStateStore.Load;
recovery no introduce un segundo parser JSON ni modifica los contratos de 6.4.

| Estado persistido | Colección | Checkpoint devuelto |
| --- | --- | --- |
| DETECTED, STAGED, VALIDATED, PLANNED, AUDITED, EXECUTED, VERIFIED | RecoverableJobs | Exactamente el estado persistido |
| COMPLETED | CompletedJobs | COMPLETED, terminal correcto |
| FAILED | FailedJobs | FAILED, terminal fallido |

Un final PLANNED devuelve PLANNED. No se infiere AUDITED ni se avanza JobState.
FAILED no se resucita y COMPLETED no se considera recuperable. La futura
ejecución decidirá cómo continuar de forma idempotente desde ese checkpoint.
Un estado EXECUTED o VERIFIED tampoco demuestra por sí solo los efectos físicos
actuales de una operación: su reconciliación requiere contratos específicos.

## Contratos públicos

JobRecoveryTempArtifact es un sealed record con constructor
`(JobId jobId, string fileName, bool hasFinalJournal)` y únicamente JobId,
FileName y HasFinalJournal get-only. Valida identidad no null y filename simple
canónico exacto, sin paths, normalización o lectura de contenido.

JobRecoverySnapshot es una sealed class con constructor
`(IEnumerable<JobStateRecord> recoverableJobs, IEnumerable<JobStateRecord> completedJobs,
IEnumerable<JobStateRecord> failedJobs, IEnumerable<JobRecoveryTempArtifact> orphanTemps,
NapIssueReport issues)`. Expone las cuatro colecciones como IReadOnlyList y el
Issues recibido, todos get-only. Copia defensivamente y utiliza colecciones
realmente read-only; rechaza argumentos/elementos null, estados en la categoría
incorrecta, JobId duplicado entre las tres colecciones y universos mezclados.
Los temps pueden compartir JobId con un final o con otros temps; se conservan
todos. No se añaden timestamps ni se modifica el report inmutable.

JobRecoveryScanner es una sealed class con constructor
`(UniverseContext context)` sin I/O y una única operación pública `Scan()` que
devuelve JobRecoverySnapshot. No añade APIs List/Recover/Resume/Delete/Reset/Save
a JobStateStore ni usa CurrentUniverse.

## Scan, aislamiento y determinismo

Scan inspecciona exclusivamente context.Storage.StateRoot del contexto recibido.
Si no existe, devuelve snapshot vacío y limpio sin crear el directorio. Si
existe, enumera solo su nivel superior; no recorre subdirectorios ni interpreta
directorios normales como journals. No consulta otros universos o storage roots.
Cada record cargado debe pertenecer exactamente a context.Id.

El namespace reconocido es únicamente el prefijo Ordinal `job_` y la extensión
Ordinal `.json` o `.tmp`. No se buscan aliases case-insensitive. Un final
canónico exige un [JobId](JOB_ID.md) válido: `job_` + 32 lowercase ASCII hex,
sin all-zero. Un archivo de este namespace con nombre no canónico produce STOP
sin abrirse. Los demás archivos, incluidos AssetCatalog.db, notes.txt y future.dat,
se ignoran sin abrir ni inspeccionar sus atributos individualmente.

Los nombres y atributos de las entradas Job se materializan al inicio y se
ordenan por filename con StringComparer.Ordinal antes de procesar. RecoverableJobs,
CompletedJobs y FailedJobs se ordenan por JobId.Value Ordinal; OrphanTemps por
FileName Ordinal. Issues conserva el orden de descubrimiento por filenames;
las incidencias de la raíz preceden a las de entradas. No depende de cultura
ni del orden de creación del filesystem.

## Temporales de publicación

El patrón exacto de 6.4 es `<valid JobId>.<32 lowercase ASCII hex>.tmp`.
Todo temp canónico se registra en OrphanTemps. HasFinalJournal se calcula con
el conjunto inicial de nombres de archivos finales canónicos enumerados, sin
File.Exists posterior. Indica presencia de nombre final, no validez de su JSON:
un final corrupto conserva su propio Error + Stop.

| Situación | Resultado |
| --- | --- |
| Temp + final canónico | HasFinalJournal true; Warning + Continue por cada temp; el final se carga y clasifica normalmente |
| Temp sin final canónico | HasFinalJournal false; Error + Stop; no RecoverableJob ni creación de journal |
| Varios temps del mismo Job | Todos permanecen en el snapshot, ordenados por filename |
| Nombre temp no canónico en namespace Job | Error + Stop sin abrirlo |

**Un temp nunca es autoritativo**, aunque contenga JSON completo, válido o
flushed. Puede representar una publicación no confirmada. Scan no lo abre,
promociona, borra ni usa para reconstruir un final. No hay limpieza automática;
la ambigüedad requiere intervención o una futura política explícita.

## Incidencias y errores operativos

| NapIssueCodes | Valor | Severity / Disposition | SubjectPath |
| --- | --- | --- | --- |
| JobRecoveryInvalidJournalName | job_recovery_invalid_journal_name | Error / Stop | Filename |
| JobRecoveryInvalidJournal | job_recovery_invalid_journal | Error / Stop | Filename |
| JobRecoveryJournalChanged | job_recovery_journal_changed | Error / Stop | Filename |
| JobRecoveryOrphanTemp | job_recovery_orphan_temp | Warning / Continue | Filename |
| JobRecoveryTempWithoutJournal | job_recovery_temp_without_journal | Error / Stop | Filename |
| JobRecoveryInvalidTempName | job_recovery_invalid_temp_name | Error / Stop | Filename |
| JobRecoveryStateRootReparse | job_recovery_state_root_reparse | Error / Stop | null |
| JobRecoveryEntryReparse | job_recovery_entry_reparse | Error / Stop | Filename |

InvalidDataException de Load produce InvalidJournal y se continúa el diagnóstico
de los demás archivos, sin repair/reescritura/normalización. FileNotFoundException
al cargar un final enumerado produce JournalChanged: el snapshot dejó de ser
fiable; no se reintenta. No hay catch global: UnauthorizedAccessException y otras
IOException inesperadas se propagan. Los issues usan mensajes fijos, Detail null
y filenames simples, nunca paths absolutos o exception.Message.

## Reparse points y cero mutaciones

StateRoot con atributo ReparsePoint produce STOP y snapshot sin recorrerlo,
SubjectPath null. Una entrada del namespace Job con ese atributo produce STOP
con su filename, también si es un directorio enlazado. No se abre, sigue ni
resuelve el target; entradas ajenas no se inspeccionan individualmente.

Scan es read-only: no crea StateRoot ni escribe/mueve/borra archivos o
directorios; no llama a Create/Transition, cambia estado, repara journals,
promociona temps, limpia residuos, ejecuta retry o rollback. InboxRoot,
StagingRoot, CacheRoot, ProductionRoot y ArchiveRoot permanecen intactos.

## Consumo futuro y límites deliberados

El futuro orquestador deberá consultar Issues.ShouldStop antes de decidir una
continuación y coordinar writers/revalidar condiciones en el momento de actuar.
Este resultado es un snapshot point-in-time, no un lock ni una autorización de
ejecución. El scanner no establece exclusión entre procesos, un lease, detección
de abandono, historia de estados o atomicidad entre múltiples journals. Tampoco
protege frente a sustituciones concurrentes de paths después de inspeccionar
sus atributos. Los límites de publicación/durabilidad de 6.4 siguen vigentes.

La ejecución idempotente, reconciliación de efectos físicos y política de
retry/cleanup/rollback quedan para el orquestador y las fases que implementen
filesystem, producción o TeraBox. No se deducen del estado ni de un temp.

## Verificación

Pruebas en directorios temporales aislados cubren los nueve estados creados
mediante JobStateStore, checkpoints exactos, corrupción, nombres, temps bloqueados,
archivos ajenos, subdirectorios, multiuniverso, orden/culturas tr-TR y ar-SA,
contratos inmutables y preservación de bytes/timestamps/roots. Los tests existentes
de 6.4 verifican que sus APIs siguen intactas.

La política ReparsePoint se prueba siempre mediante el helper privado de
clasificación; los casos reales de symlink se ejercitan cuando la plataforma
permite crearlos, sin exigir privilegios en CI ni añadir hooks públicos. El
mapping JournalChanged se prueba directamente en el helper privado de carga
tras borrar un final conocido; no se simula una carrera entre enumeración y Load
con sleeps o timing. Los errores operativos no se convierten en report limpio.
