# Plan Validation — Fase 4 · Capítulo 4.3

**Fase 3 HECHA. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan,
4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS.
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; siguiente: 6.3 — Job ID.**

## Objetivo y API

```text
ProcessingPlan
        +
ProductionRepositorySnapshot
        ↓
ProcessingPlanValidator
        ↓
NapIssueReport
```

```csharp
public NapIssueReport Validate(
    ProcessingPlan plan,
    ProductionRepositorySnapshot snapshot)
```

ProcessingPlanValidator es public sealed y sin estado, con esta única API
pública declarada. Interpreta la viabilidad estructural actual del directorio
de producción contra una fotografía ya materializada por 3.2. No escanea,
recalcula destinos, construye otro plan ni modifica las entradas.

## Coherencia y duplicados

Null produce ArgumentNullException con ParamName `plan` o `snapshot`.
El UniverseId del plan debe ser igual por valor al del snapshot. Las raíces
de ProductionDestination y snapshot se comparan mediante Path.GetFullPath y
Path.TrimEndingDirectorySeparator, aceptando normalización léxica y trailing
separator equivalente. Windows usa OrdinalIgnoreCase; otras plataformas Ordinal.
Universe/root mismatch produce ArgumentException sobre snapshot, sin NapIssue.

Antes de interpretar los prefijos, todas las entries se indexan por RelativePath
con StringComparer.OrdinalIgnoreCase en Windows o StringComparer.Ordinal en
otras plataformas. Un duplicado bajo ese comparer produce ArgumentException
sobre snapshot, incluso si está fuera del destino. En sistemas case-sensitive,
dos paths distintos solo por casing pueden coexistir. Esta regla local de 4.3
no modifica ProductionRepositorySnapshot ni el diff ordinal de 3.4.

## Prefijos del destino

RelativeDirectory y RelativePath conservan `/` como separador lógico. El
validator recorre los prefijos desde el más corto al directorio completo;
no utiliza separadores nativos para compararlos ni les atribuye semántica de
asset. Para `assets/example_culture/portrait_example_001`, comprueba `assets`,
`assets/example_culture` y el destino completo.

| Observación del prefijo | Resultado |
| --- | --- |
| Entry ausente | Válido, sin Info/Warning/Error ni operación CreateDirectory |
| Directory ancestro con casing exacto Ordinal | Válido, continuar |
| Entry equivalente en Windows, pero casing distinto Ordinal | STOP por casing conflict, antes de interpretar File/Directory |
| File en cualquier prefijo, incluido el destino completo | STOP por bloqueo |
| Directory exacto en el destino completo | STOP porque ya existe |

En plataformas case-sensitive, un path con otro casing es distinto y no bloquea
el canónico. No se adapta el destino ni se corrige casing legacy. No se buscan
descendants para inventar parents ausentes o reparar snapshots sintéticos;
un snapshot real del scanner ya contiene los parents observados.

Se devuelve como máximo **un issue estructural**: el primer blocker por orden
de prefijos. Dentro de un prefijo, casing precede a File y destino existente.
No hay cascada bajo un blocker. Sin blockers, el report es limpio:
IsClean true, ShouldStop false y CanContinue true.

## Issues exactos

Todos los nuevos issues tienen NapIssueSeverity.Error y NapIssueDisposition.Stop.
SubjectPath conserva **entry.FullPath observado**, sin reconstruirlo.

| Constante / Code | Message | Detail |
| --- | --- | --- |
| PlanDestinationCasingConflict / `plan_destination_casing_conflict` | An existing production path differs from the canonical destination only by casing. | `expected: <plannedPrefix>; observed: <entry.RelativePath>` |
| PlanDestinationBlocked / `plan_destination_blocked` | A file blocks a required production directory path. | Prefijo requerido |
| PlanDestinationExists / `plan_destination_exists` | The planned production destination already exists. | destination.RelativeDirectory |

Un Directory final existente causa STOP aunque esté vacío. Desde
[6.1 — SHA-256](SHA256_INTEGRITY.md) existe la primitiva de cálculo y
[6.2 — Duplicados](DUPLICATE_DETECTION.md) define la semántica pairwise de
UniverseAssetKey + Sha256Digest dentro de un universo. La idempotencia semántica
no prueba un Job completado ni decide reutilización o sobrescritura;
la colisión Error + Stop y el posible duplicado Warning + Continue tampoco
resuelven contenido automáticamente. Plan Validation continúa sin usar hashes
ni inspeccionar contenido; Fase 4 conserva su alcance.

## Nimroel canónico e histórico

El plan real, calculado con Profile v4 desde 5.4 y el routing conservado de v3,
mantiene:

```text
portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040
```

Si existen como Directory exactamente `portraits`, `portraits/norgard`,
`portraits/norgard/treskal`, `portraits/norgard/treskal/farmer` y
`portraits/norgard/treskal/farmer/male`, pero no el asset final, el report es limpio.
Si el asset final ya existe como Directory, produce plan_destination_exists.

Con el snapshot histórico `portraits`, `portraits/Norgard`,
`portraits/Norgard/Treskal`, Windows se detiene en el primer casing incompatible:

```text
Code: plan_destination_casing_conflict
Detail: expected: portraits/norgard; observed: portraits/Norgard
Severity: Error
Disposition: Stop
```

La comprobación de `Treskal/treskal` aplica la misma política si culture ya
coincide exactamente. En sistemas case-sensitive, los paths históricos son
distintos de los canónicos. No se renombra, migra o adapta nada automáticamente.

## Pureza y límite temporal

4.3 realiza **cero filesystem I/O**: solo usa inputs en memoria y cálculo léxico
de roots. No recibe ValidatedProductionRepository/context/profile, llama al
scanner/differ/resolver/builder, re-renderiza Dry Run ni participa un structural
diff before/after. No lee contents, hashes, PNG, manifests o Visual Identity.
No revalida PackageRoot, ManifestPath o FilesByRole ni comprueba sus existencias.

La posible desaparición o modificación posterior de sources es un problema
TOCTOU/pre-execution futuro. ProductionRepositorySnapshot es point-in-time:
el filesystem puede cambiar después. Un report limpio **no autoriza escritura**
ni garantiza condiciones físicas permanentes. Antes de futuras escrituras se
necesitará una frontera pre-execution adecuada; no se implementa en este capítulo.

El resultado lógico es determinista para plan/snapshot equivalentes bajo la
semántica de plataforma, sin cultura actual, timestamps, Guid, random o dependencia
del orden de inserción. No hay creación de carpetas, operaciones ejecutables,
writes, migración, conversión, archive planning, JobId, estados, UI o CLI.
**4.4 — Logs HECHO:** resumen textual privacy-safe de plan + report, sin revalidación ni persistencia. Véase [PLAN_LOGS.md](PLAN_LOGS.md). Fase 4 HECHA dentro del alcance v1; Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; siguiente: 6.3 — Job ID.

Los tests construyen snapshots en memoria usando las fronteras internas existentes:
coherencia, duplicados, missing/ancestors, File blockers, destino existente, casing
Windows-aware, primer blocker, determinismo y política real Nimroel. No crean
filesystem físico para probar 4.3.
