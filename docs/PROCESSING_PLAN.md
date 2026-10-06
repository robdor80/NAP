# ProcessingPlan — Fase 4 · Capítulo 4.1

**Fase 3 HECHA. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan,
4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS.
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; siguiente: 6.5 — Recuperación tras fallo.**

## Propósito y fronteras

ProcessingPlan v1 es la **base inmutable del plan**: congela los hechos validados
y calculados que NAP conoce actualmente. Es completo respecto a las fronteras
implementadas; todavía no es un grafo de ejecución completo ni autoriza escritura.
Las operaciones concretas se incorporarán cuando existan sus contratos.

3.6 calcula el destino por separado:

```text
ValidatedAssetPackage + ValidatedProductionRepository
                         ↓
             ProductionDestinationResolver
                         ↓
             ProductionAssetDestination
```

4.1 combina las tres fronteras existentes:

```text
ValidatedAssetPackage
        +
ValidatedProductionRepository
        +
ProductionAssetDestination
        ↓
ProcessingPlanBuilder
        ↓
ProcessingPlan
        ↓
4.2 Dry Run (HECHO)
```

## API y coherencia

`ProcessingPlanBuilder` es public sealed, sin estado, con una sola API pública
declarada y sin overloads:

```csharp
public ProcessingPlan Build(
    ValidatedAssetPackage package,
    ValidatedProductionRepository repository,
    ProductionAssetDestination destination)
```

Las entradas null producen ArgumentNullException con el nombre correspondiente.
Antes de construir se comprueba:

- UniverseId del package igual por valor al del repository; mismatch produce
  ArgumentException sobre repository.
- AssetKey completo del destination igual por valor al del package: universo y
  AssetId, sin parsing ni reconstrucción; mismatch produce ArgumentException
  sobre destination.
- RootPath de destination equivalente al de repository: Path.GetFullPath y
  Path.TrimEndingDirectorySeparator para comparación léxica, con
  OrdinalIgnoreCase en Windows y Ordinal en otras plataformas. Trailing separator
  equivalente se acepta; raíz distinta produce ArgumentException sobre destination.

Estas comprobaciones expresan coherencia entre fronteras, sin nuevos NapIssueCodes
ni validación operativa user-facing. ProductionDestination se conserva como la
misma instancia recibida, incluso si el root tiene otra representación equivalente.
No se recalculan RelativeDirectory, FullDirectoryPath ni segmentos de routing.

## Snapshot exacto

`ProcessingPlan` es public sealed, con constructor internal, sin setters ni I/O.
Expone exactamente:

| Propiedad | Fuente |
| --- | --- |
| UniverseAssetKey AssetKey | package.AssetKey |
| string AssetType | package.Manifest.AssetType |
| string ProductionProfile | package.Manifest.ProductionProfile |
| IReadOnlyDictionary<string, string> Classification | Snapshot completo de package.Manifest.Classification |
| string PackageRoot | package.PackageRoot |
| string ManifestPath | package.ManifestPath |
| IReadOnlyDictionary<string, string> FilesByRole | Snapshot exacto de package.FilesByRole |
| ProductionAssetDestination ProductionDestination | destination recibido, misma instancia |

El builder obtiene package.Manifest una sola vez. Classification y FilesByRole
se copian a dictionaries nuevos con StringComparer.Ordinal y se exponen mediante
ReadOnlyDictionary. Cambios en inputs mutables o copias del manifest del caller no
alteran el plan; las colecciones expuestas rechazan mutación.

Classification conserva también dimensiones opcionales como realm y region:
el plan conserva metadata validada, aunque routing no use esas dimensiones.
FilesByRole conserva role → source path tal como llegó, sin reconstruir filenames,
inventar archivos opcionales ausentes, imponer orden semántico ni añadir outputs.
El manifest universal queda separado en ManifestPath; no se inserta un role
artificial `manifest` ni se añaden WebP u otros archivos futuros.

Con el perfil real Nimroel, `portrait_treskal_farmer_male_002` y classification
`culture=norgard`, `location=treskal`, `role=farmer`, `sex=male` conservan el destino
de 3.6 `portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_002`.
AssetType es portrait, ProductionProfile portrait_npc; inputs, metadata opcional y
destino se mantienen sin transformación de casing ni inferencia desde AssetId.

## Alcance actual y trabajo posterior

4.1 ejecuta **cero filesystem I/O**. No llama al resolver, interpreta Routing ni
recarga perfiles. No recibe ProductionRepositorySnapshot, consulta repo contents,
comprueba existencia/colisiones o crea/copia/mueve/borra archivos. Paths inexistentes
son suficientes para construir el plan; no se autorizan escrituras.

El plan es determinista para entradas lógicas equivalentes: sin timestamps,
DateTime, Guid, random, contador global, JobId, Status ni estado de ejecución.
No contiene Operations, Steps, Actions, comandos o lista de escrituras.
4.1 no renderiza texto Dry Run, JSON de auditoría ni salida CLI.

Desde [5.4 — Profile v4](UNIVERSE_PROFILE_V4.md), el package validado retiene
AssetRule.Conversion y ImageConversionResolver puede resolverla. ProcessingPlan
v1 todavía no congela la conversion rule y Dry Run v1 no muestra una operación
de conversión. Una futura evolución del plan podrá congelar la regla cuando
se formalicen operaciones de ejecución; no se fija cuándo ni se reabre Fase 4.

Desde [6.1 — SHA-256](SHA256_INTEGRITY.md) existe una primitiva de hashing
independiente. ProcessingPlan v1 sigue sin almacenar hashes y el builder no los
calcula. Plan Validation no los usa y Plan logs no los muestra; no se reabre Fase 4.

Todavía no conoce destino archive/maestro, estructura ArchiveRoot/TeraBox,
conversión PNG → WebP, parámetros runtime, filenames finales de producción,
hashes, colisiones, ejecución o persistencia. El ejemplo completo de la sección
28 del Master Spec conserva su función de objetivo futuro.

4.2 añade [DryRunTextRenderer](DRY_RUN.md), que devuelve texto humano
determinista desde esta base, con LF fijo, orden Ordinal y paths escapados.
No revalida ni ejecuta operaciones. [4.3 — Plan Validation](PLAN_VALIDATION.md)
interpreta estructuralmente el destino contra un snapshot materializado, sin I/O,
source revalidation ni hashes. Un report limpio es point-in-time y no autoriza
escritura. 4.4 — Logs HECHO: resumen textual privacy-safe. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; siguiente: 6.5 — Recuperación tras fallo. El primitive [5.1 — Portrait](PORTRAIT_CONVERSION.md) recibe sourcePath/settings
explícitos y devuelve WebP en memoria, sin depender del plan ni añadir operaciones.
La conexión de conversiones al plan, archive, hashes y jobs sigue pendiente;
JobId pertenece a Fase 6.
No se implementan esos contratos anticipadamente en 4.1.

Los tests verifican el contrato público exacto, coherencia por valor, comparación
léxica de roots según plataforma, snapshots/read-only, determinismo, paths
inexistentes y la política real Nimroel conectada al resolver de 3.6. No crean
archivos físicos para probar esta frontera pura.
