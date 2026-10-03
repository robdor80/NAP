# ProcessingPlan — Fase 4 · Capítulo 4.1

**Fase 3 HECHA. Fase 4 — PLAN / Dry Run EN CURSO. 4.1 — ProcessingPlan HECHO.
Siguiente: 4.2 — Dry Run. 4.3 — Plan Validation y 4.4 — Logs pendientes.**

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
4.2 Dry Run (siguiente)
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

Todavía no conoce destino archive/maestro, estructura ArchiveRoot/TeraBox,
conversión PNG → WebP, parámetros runtime, filenames finales de producción,
hashes, colisiones, ejecución o persistencia. El ejemplo completo de la sección
28 del Master Spec conserva su función de objetivo futuro.

4.2 desarrollará Dry Run a partir de esta base. 4.3 añadirá validación operativa
del plan y 4.4 logs; ambos siguen pendientes. Los contratos de conversión,
archive, hashes y jobs corresponden a fases posteriores; JobId pertenece a Fase 6.
No se implementan esos contratos anticipadamente en 4.1.

Los tests verifican el contrato público exacto, coherencia por valor, comparación
léxica de roots según plataforma, snapshots/read-only, determinismo, paths
inexistentes y la política real Nimroel conectada al resolver de 3.6. No crean
archivos físicos para probar esta frontera pura.
