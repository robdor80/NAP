# Destination Resolver — Fase 3 · Capítulo 3.6

**3.1–3.6 HECHOS. Fase 3 — Routing y repo HECHA. Fase 4 — PLAN / Dry Run EN CURSO.
4.1 — ProcessingPlan HECHO. Siguiente: 4.2 — Dry Run.**

```text
ValidatedAssetPackage
        ↓
AssetRule.Routing
        +
ValidatedProductionRepository
        ↓
ProductionDestinationResolver
        ↓
ProductionAssetDestination
        ↓
Fase 4 ProcessingPlan (4.1 HECHO)
```

## Autoridad y API

```csharp
public ProductionAssetDestination Resolve(
    ValidatedAssetPackage package,
    ValidatedProductionRepository repository)
```

`ProductionDestinationResolver` es sellado y sin estado. Calcula exclusivamente
el **directorio deseado del asset**. El package ya procede de la validación
semántica; el repository ya representa la raíz autorizada por
`ProductionRepositoryValidator`. No se revalida físicamente esa raíz.

La única autoridad es `package.AssetRule.Routing`: el package conserva la regla
con la que fue validado. No se recarga UniverseProfile ni se busca configuración
en runtime. Un cambio posterior de política requiere una nueva validación para
adoptarlo. `ProductionRepositorySnapshot` no participa y no hay overload que lo
acepte; la estructura existente no adapta el destino canónico.

Null en cualquiera de las entradas produce `ArgumentNullException`. Los
UniverseId de `package.AssetKey` y repository deben coincidir **por valor**;
un mismatch produce `ArgumentException` antes de calcular cualquier destino.
Una regla sin Routing produce `InvalidOperationException`, sin fallback. Los
Profiles v1/v2 históricos siguen soportados, pero no tienen destino resoluble
con sus reglas sin routing. No se añaden NapIssue ni códigos nuevos.

## Segmentos y seguridad

Se recorre Routing.Segments exactamente en su orden, conservando repeticiones:

| Kind | Valor usado |
| --- | --- |
| Literal | `segment.Value` |
| Classification | Valor de la dimensión `segment.Value` en el snapshot de `package.Manifest.Classification` |
| AssetId | `package.AssetKey.AssetId` |

Una dimensión ausente produce `InvalidOperationException`, aunque esa
incoherencia normalmente queda impedida por RequiredClassification y la
validación semántica. Cada valor Literal/Classification debe cumplir Naming v1
de machine identifiers; AssetId debe cumplir Naming v1 de asset IDs. Un valor
inseguro o kind inválido produce `InvalidOperationException`. No hay trim,
lowercase, title casing, inferencia desde tokens de AssetId ni adaptación al
layout legacy. PackageRoot, ManifestPath y FilesByRole no intervienen.

Como seguridad de destinos, se rechazan los segmentos exactos `con`, `prn`,
`aux`, `nul`, `com1`–`com9` y `lpt1`–`lpt9`, con comparación case-insensitive
en **todas las plataformas**. Naming v1 global no cambia. `content`, `com10`
y `lpt10` no son nombres reservados exactos.

## Resultado y contención

`ProductionAssetDestination` es sellado, inmutable, sin I/O y con constructor
internal. Expone exactamente cuatro propiedades sin setters:

| Propiedad | Significado |
| --- | --- |
| `UniverseAssetKey AssetKey` | Identidad completa procedente del package |
| `string RootPath` | RootPath autorizado del repository, conservado exactamente |
| `string RelativeDirectory` | `string.Join("/", resolvedSegments)`, representación lógica portable |
| `string FullDirectoryPath` | Path.Combine del root y cada segmento, normalizado por Path.GetFullPath |

RelativeDirectory usa siempre `/`, sin leading/trailing slash y sin incluir
ProductionRoot, PackageRoot ni ManifestPath. No incluye filenames WebP,
prompt, info o manifest. Literal-only `assets`, AssetId-only y rutas sin AssetId
son válidas; `assets/{culture}/{culture}/{asset_id}/{asset_id}` conserva todas
sus repeticiones.

FullDirectoryPath es absoluto y native. La contención normaliza root/candidate
con Path.GetFullPath y exige un descendiente estricto del root, con boundary de
directory separator: `prod` no autoriza `production_evil`. Windows compara con
OrdinalIgnoreCase; otras plataformas con Ordinal. Una contención fallida
produce `InvalidOperationException`. Esta comprobación es léxica, sin resolver
links ni prometer seguridad frente a cambios físicos posteriores.

## Política real Nimroel

El perfil real usa [Universe Profile v4](UNIVERSE_PROFILE_V4.md) desde 5.4;
conserva el routing declarado en v3 y Manifest v2 continúa vigente.
La política conserva `portraits/{culture}/{location}/{role}/{sex}/{asset_id}`:

| AssetId | role / sex del manifest | RelativeDirectory |
| --- | --- | --- |
| portrait_treskal_farmer_male_002 | farmer / male | portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_002 |
| portrait_treskal_farmer_boy_002 | farmer / male | portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_boy_002 |
| portrait_treskal_boy_001 | village_child / male | portraits/norgard/treskal/village_child/male/portrait_treskal_boy_001 |
| portrait_treskal_elder_male_001 | village_elder / male | portraits/norgard/treskal/village_elder/male/portrait_treskal_elder_male_001 |

`realm` y `region` pueden estar presentes como metadata opcional; al no ser
segmentos de ruta, no cambian el destino. `children/boy`, `elder` y el casing
`Norgard/Treskal` son hechos históricos, no autoridades de resolución.

## Límites y siguiente frontera

3.6 realiza **cero filesystem I/O**: Path.Combine y Path.GetFullPath son
operaciones léxicas. No comprueba existencia ni colisiones, lee contenidos,
escanea el repository, crea carpetas, copia/mueve archivos o escribe producción.
Un root/parent inexistente, un destino existente o un File donde se deseará un
Directory no afectan al cálculo. No genera filenames, ProcessingPlan, Dry Run,
conversión, hashes, SQLite, TeraBox ni migración histórica.

Resolver un destino **no autoriza una escritura**. [4.1 — ProcessingPlan](PROCESSING_PLAN.md)
combina las fronteras validadas y este destino como snapshot inmutable, sin
re-routing ni grafo de ejecución. 4.2 — Dry Run es siguiente; existencia,
colisiones y diferencias entre estructura observada/deseada pertenecen a
4.3 — Plan Validation, pendiente. Antes de futuras
escrituras deberán mantenerse las comprobaciones físicas de raíz, contención
y links que correspondan.

Los tests cubren la política real cargada desde ConfigPath, ambas fronteras
públicas, rutas genéricas, invariantes internas deliberadamente rotas,
dispositivos Windows, contención y determinismo. Usan roots absolutos sin
crearlos salvo fixtures concretos para probar contenido existente y la conexión
con las fronteras de validación. El resolver no requiere esos fixtures físicos.
