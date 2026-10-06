# Job States — Fase 6 · Capítulo 6.4

**Fase 5 — Conversión HECHA. Fase 6 — Integridad HECHA.
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO;
6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.**

El requisito histórico exige Job ID y estados persistentes. 6.4 implementa
vocabulario, reglas puras, snapshot inmutable y un journal mínimo del último
estado durable. **Persistir estado no implementa recovery ni ejecuta trabajo.**
No existe un ProcessingJob/orquestador, historial de eventos o prueba de que las
operaciones de assets representadas por los estados se hayan realizado.

## Estados y reglas puras

JobState es un enum público con exactamente estos nombres, valores explícitos
y tokens persistentes. Los números no se guardan en JSON.

| JobState | Valor | Token |
| --- | --- | --- |
| Detected | 0 | DETECTED |
| Staged | 1 | STAGED |
| Validated | 2 | VALIDATED |
| Planned | 3 | PLANNED |
| Audited | 4 | AUDITED |
| Executed | 5 | EXECUTED |
| Verified | 6 | VERIFIED |
| Completed | 7 | COMPLETED |
| Failed | 8 | FAILED |

El mapping internal es explícito en ambas direcciones, sin conversiones culturales.
STABLE, CONVERTED y READY no pertenecen a este contrato.

```text
DETECTED → STAGED → VALIDATED → PLANNED → AUDITED → EXECUTED → VERIFIED → COMPLETED
```

Solo se permite cada siguiente paso normal. Desde cualquiera de los siete
estados activos DETECTED..VERIFIED también se permite FAILED. COMPLETED y FAILED
son terminales: ninguna salida, ni a sí mismos, ni COMPLETED → FAILED.
Se rechazan same-state, saltos y retrocesos. FAILED no se resucita en 6.4.

```csharp
public sealed class JobStateMachine
{
    public bool CanTransition(JobState from, JobState to);
    public JobStateRecord Create(JobId jobId, UniverseId universeId);
    public JobStateRecord Transition(JobStateRecord current, JobState nextState);
}
```

Stateless y sin I/O, persistencia, timestamps o recovery. CanTransition valida
from y luego to: un enum no definido produce ArgumentOutOfRangeException con ese
ParamName. Create valida jobId/universeId, retiene sus referencias y comienza
Detected. Transition valida current null antes de nextState no definido;
conserva exactamente JobId/UniverseId en un record nuevo y no muta el anterior.
Null produce ArgumentNullException; nextState inválido, ArgumentOutOfRangeException.
Una transición no permitida lanza InvalidOperationException con tokens uppercase:

```text
Invalid Job state transition from 'PLANNED' to 'STAGED'.
```

## Snapshot inmutable

```csharp
public sealed record JobStateRecord
{
    public JobStateRecord(JobId jobId, UniverseId universeId, JobState state);
    public JobId JobId { get; }
    public UniverseId UniverseId { get; }
    public JobState State { get; }
}
```

Valida jobId null, universeId null y enum definido, en ese orden. Mantiene
referencias a los value objects existentes y ofrece igualdad por los tres valores.
No contiene AssetId/AssetKey, ProcessingPlan, timestamp/CreatedAt/UpdatedAt,
Error/Exception, Path/Hash, RetryCount o metadata de recuperación.
La ausencia de AssetId y timestamps es deliberada: solo qué Job, qué universo y
último estado conocido. El contrato JobId de 6.3 permanece intacto.

## Store y frontera física

```csharp
public sealed class JobStateStore
{
    public JobStateStore(UniverseContext context);
    public JobStateRecord Create(JobId jobId);
    public JobStateRecord Load(JobId jobId);
    public JobStateRecord Transition(JobId jobId, JobState nextState);
}
```

UniverseContext es obligatorio; el constructor valida null y no hace I/O.
La única ruta final es `Path.Combine(context.Storage.StateRoot, jobId.Value + ".json")`:

```text
<StateRoot>/job_00112233445566778899aabbccddeeff.json
```

No acepta paths del caller ni construye rutas desde JSON, AssetId o metadata.
No hay Save arbitrario, Delete, Reset, Resume, Recover, Enumerate/List o búsqueda
global. JobId sigue siendo global, pero cada record declara UniverseId y cada
store accede exclusivamente a su StateRoot. Nunca infiere universo desde el ID
ni crea CurrentUniverse. Un universo distinto en el journal causa STOP mediante
InvalidDataException. La prueba con un mismo JobId en dos roots es reutilización
controlada para demostrar aislamiento físico, no una recomendación de identidad.

Create genera Detected mediante JobStateMachine y puede crear StateRoot y sus
ancestors necesarios. Si el final ya existe, incluso corrupto, no sobrescribe:

```text
A persisted state already exists for Job ID 'job_00112233445566778899aabbccddeeff'.
```

La excepción es InvalidOperationException; la publicación no-overwrite protege
también frente a un final aparecido después de la comprobación inicial.

Load valida el ID, no crea StateRoot y no escribe. Un final ausente, también
cuando StateRoot no existe, produce FileNotFoundException. Un contenido inválido,
corrupto o incoherente produce InvalidDataException; no se repara ni normaliza.
Otros errores operativos de lectura/escritura se propagan.

Transition valida jobId y nextState antes de I/O, carga el estado, consulta el
machine y solo publica una transición válida. Las inválidas no crean temporales
ni cambian el final. Las operaciones de escritura se serializan dentro de una
instancia; callers deben coordinar writers de distintas instancias/procesos para
el mismo Job. No se implementan locks distribuidos, leases o compare-and-swap.

## JSON interno v1 estricto

```json
{"schema_version":1,"job_id":"job_00112233445566778899aabbccddeeff","universe_id":"nimroel","state":"DETECTED"}
```

Serialización determinista con System.Text.Json de .NET 8: exactamente ese orden
de propiedades, UTF-8 sin BOM, JSON compacto y un LF final consistente.
schema_version es el entero literal 1; job_id es JobId.Value canónico,
universe_id es UniverseId.Value y state uno de los nueve tokens exactos.
No hay dependencia de CurrentCulture o enum numérico automático.

Load exige un objeto con exactamente las cuatro propiedades, sin duplicados
(también nombres equivalentes mediante escapes), propiedades extra o ausentes.
Rechaza tipos incorrectos/null, versiones distintas o no enteras, JSON malformado,
comments/trailing commas, JobId/UniverseId inválidos y estados desconocidos o con
casing diferente. El job_id interno debe coincidir exactamente con el solicitado
y universe_id por valor con context.Id. El orden/whitespace del input no cambia
sus valores ni causa reescritura. No es schema Manifest o Universe Profile;
sus schemas y los 436 checks existentes permanecen intactos.

## Publicación del último estado

Create/Transition aseguran StateRoot y construyen la representación completa en
memoria antes de abrir un temporal hermano con nombre generado internamente
`<job_id>.<Guid N>.tmp`. FileMode.CreateNew/FileShare.None garantiza creación
exclusiva. Se escriben los bytes, FileStream.Flush(flushToDisk: true) vacía buffers
y solicita flush a disco; se cierra antes de publicar.

File.Move en el mismo directorio publica con overwrite false para Create y true
para Transition. Nunca se escribe JSON progresivamente sobre el final ni se
borra antes el último estado válido. Un fallo anterior a publicación conserva
el final y puede dejar un temporal completo/parcial. El store no busca ni limpia
temporales huérfanos. [Recovery 6.5](JOB_RECOVERY.md) los descubre sin abrirlos,
promocionarlos o borrarlos; no implementa cleanup. Las garantías de renombrado/durabilidad
dependen del filesystem; no se añade un protocolo de crash recovery o fsync de
directorio. Las pruebas observan el estado anterior y el temp completo tras un
conflicto de publicación, sin hooks públicos artificiales.

## Alcance y continuidad

Solo el journal bajo StateRoot introduce escrituras de infraestructura.
No se tocan InboxRoot, StagingRoot, CacheRoot, ProductionRoot o ArchiveRoot.
No se copian maestros, escriben WebP/documentación de producción, mueven o
sobrescriben assets, usa TeraBox o Git de producción. ProcessingPlan, builder,
validator y renderers siguen sin JobId/JobState; no hay CLI/UI, dependencias
nuevas, SQLite, catálogo, schemas nuevos, NapIssueCodes o cambios a PNG/ZIP,
conversiones, loaders o config Nimroel.

[6.5 — Recuperación tras fallo](JOB_RECOVERY.md) HECHO: JobRecoveryScanner enumera
solo StateRoot y reutiliza Load para recuperar exactamente el último checkpoint
durable, separando activos, COMPLETED y FAILED. Detecta journals inválidos,
desapariciones, temps huérfanos y reparse points mediante NapIssueReport, sin
modificar las APIs o semántica de 6.4. No hay ejecución de assets, resume físico,
retry/rollback, journal history, reconciliación, limpieza de temp o detección de
abandonos. Fase 6 HECHA; Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.

Las pruebas cubren la matriz 9×9 tanto en machine como store, contratos,
inmutabilidad, tokens/bytes, parsing adversarial, roundtrips, flujo completo,
FAILED terminal, ausencia de escrituras inválidas, aislamiento de universos,
fallo de publicación, orphans intactos, sentinels en otros roots y tr-TR/ar-SA.
