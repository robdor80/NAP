# Structural Change Detection — Fase 3 · Capítulo 3.4

**3.1–3.6 HECHOS. Fase 3 HECHA. Siguiente: Fase 4 — PLAN / Dry Run, 4.1 — ProcessingPlan.**

```text
ProductionRepositorySnapshot(before)
                +
ProductionRepositorySnapshot(after)
                ↓
ProductionRepositoryStructureDiffer
                ↓
ProductionRepositoryStructureDiff
                ↓
Added / Removed / KindChanged
```

3.4 compara únicamente estructura observada, con **cero I/O**. No interpreta si
un cambio es correcto, pertenece a un asset, exige migración o modifica routing.
Los snapshots materializados por 3.2 son toda la información disponible.

## API e identidad del repositorio

La única API pública de ejecución es:

```csharp
ProductionRepositoryStructureDiff Compare(
    ProductionRepositorySnapshot before,
    ProductionRepositorySnapshot after)
```

El differ es una instancia sin estado. No acepta paths, repositorios o contextos,
ni realiza selección global de universo. Before/after nulos producen
ArgumentNullException.

Los UniverseId deben ser iguales **por valor**, sin exigir la misma instancia.
RootPath se compara con OrdinalIgnoreCase en Windows y Ordinal en otras
plataformas. Raíces/universos distintos producen ArgumentException, no NapIssue.
No se normalizan raíces, resuelven links ni consulta su existencia. El diff
conserva exactamente UniverseId y RootPath del snapshot before.

## Identidad de entries y algoritmo

La identidad de cada entry es **RelativePath con StringComparer.Ordinal**,
incluso en Windows. No se usa FullPath, trim, lowercase, normalización Unicode,
Naming v1 ni parsing semántico. Los paths se conservan exactamente como fueron
observados, sin reconstruir rutas absolutas.

Se construye un índice ordinal por snapshot. Un RelativePath duplicado dentro
de cualquiera de ellos produce ArgumentException, sin sobrescribir entradas.
Paths distintos solo por casing siguen siendo distintos y no son duplicados.
Se compara la unión de paths en orden Ordinal:

| Observación | Cambio | BeforeKind | AfterKind |
| --- | --- | --- | --- |
| Solo en after | Added | null | Kind de after |
| Solo en before | Removed | Kind de before | null |
| En ambos, Kind distinto | KindChanged | Kind de before | Kind de after |
| En ambos, mismo Kind | Sin cambio | — | — |

Un File → Directory o Directory → File genera exactamente un KindChanged,
nunca Removed + Added. No se colapsan subárboles: eliminar `a`, `a/b` y
`a/b/file.txt` genera tres Removed; añadirlos genera tres Added.

Un cambio `Treskal/Farmer` → `treskal/Farmer` genera Removed + Added, incluso
en Windows. Casing, espacios, acentos, formas Unicode diferentes y nombres
históricos se comparan sin reinterpretación.

## Modelos e invariantes

RepositoryStructuralChangeKind contiene exactamente Added, Removed y KindChanged.
ProductionRepositoryStructuralChange es sellado e inmutable, con constructor
internal sin I/O y propiedades RelativePath, Kind, BeforeKind y AfterKind.
Rechaza enums indefinidos y combinaciones incoherentes:

- Added: BeforeKind null y AfterKind presente.
- Removed: BeforeKind presente y AfterKind null.
- KindChanged: ambos presentes y diferentes.

ProductionRepositoryStructureDiff es sellado e inmutable, con constructor
internal sin I/O. Expone UniverseId, RootPath, Changes e IsEmpty. Changes toma
snapshot defensivo, rechaza items nulos y paths duplicados, se ordena por
RelativePath Ordinal y se expone mediante colección read-only. Como máximo hay
un cambio por RelativePath. IsEmpty depende únicamente de Changes.Count.

## Límites e interpretación futura

**No se infieren moves ni renames.** Un aparente traslado `a/file.txt` →
`b/file.txt` es Removed + Added. Tampoco hay Modified ni ContentChanged.
Un File que sigue en la misma RelativePath y con el mismo Kind no cambia
estructuralmente, aunque sus bytes reales hayan cambiado. No hay tamaños,
timestamps, hashes, lectura de bytes, JSON, PNG, manifests ni Git status.

No se realizan llamadas File/Directory/Path ni consultas de red. La comparación
funciona aunque RootPath ya no exista físicamente. No se usa routing, profile,
package ni classification: se compara estructura real, no estructura esperada.
No hay ignores; `.git`, `.github`, README y nombres legacy son entries ordinarias.

Los cambios son hechos **neutrales, no errores**. No se generan NapIssue ni
nuevos NapIssueCodes. La comparación no demuestra consistencia transaccional
entre snapshots ni añade información a las observaciones puntuales de 3.2.
Materializa índices y cambios en memoria, sin cuotas nuevas de tamaño.

**3.5 es el primer capítulo que interpreta la estructura.** La
[auditoría histórica](NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md) separa hechos del
corte y política futura, migra Nimroel a Profile v3 y fija su routing canónico.
3.4 no toma ninguna de esas decisiones y su diff permanece neutral.

No se migran assets históricos. [3.6 — Destination Resolver](DESTINATION_RESOLVER.md)
calcula directorios sin usar snapshots; no modifica este diff puro. 3.4 no
resuelve destinos ni crea carpetas. No hay escrituras, ProcessingPlan, Dry Run,
SQLite, TeraBox, automatización Git ni UI implementados en este capítulo.
