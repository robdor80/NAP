# Routing Contract v1 — Fase 3 · Capítulo 3.3

**3.3 HECHO como contrato declarativo, 3.4 HECHO como diff puro y 3.5 HECHO
como política canónica Nimroel. Fase 3 EN CURSO. Siguiente: 3.6 — Destination Resolver.**

El contrato define qué es una regla de routing, sin resolver destinos ni formar
paths. No compara snapshots, crea carpetas, mueve assets o interpreta estructura
histórica. La versión del contrato de routing es independiente de Manifest,
Package Contract y de la versión de configuración del Universe Profile.

## Segmentos estructurados

`AssetRouteSegmentKind` tiene exactamente Literal, Classification y AssetId.
`AssetRouteSegment` es un record sellado e inmutable, con constructor privado
que impide estados incoherentes. Sus factories públicas son:

```csharp
AssetRouteSegment.Literal("portraits")
AssetRouteSegment.Classification("culture")
AssetRouteSegment.AssetId()
```

Expone Kind y Value. Literal exige un machine identifier Naming v1; Classification
exige el **nombre de dimensión**, también machine identifier Naming v1. Ambos
usan exactamente la gramática existente: lowercase snake_case ASCII, máximo 64,
sin trim ni lowercase automático. AssetId lleva Value null, sin string redundante.

No hay variantes AssetType, ProductionProfile, Root, Filename, Custom ni Script.
No existen templates libres, parser de placeholders ni interpolación textual.
Naming v1 impide separators, puntos, `..`, drive letters, UNC, variables de
entorno, macros y scripts en los valores configurados.

## Regla e invariantes

`AssetRoutingRule(IEnumerable<AssetRouteSegment>)` es sellado e inmutable y
expone Segments como IReadOnlyList mediante una copia defensiva read-only.
El orden es significativo y se conserva exactamente. Rechaza colección nula,
items nulos y lista vacía. Permite repetir segmentos, incluidos AssetId y
literals; no exige que aparezca Classification ni AssetId.

Que dos assets puedan compartir un directorio no invalida la configuración.
Colisiones y comportamiento de destinos pertenecen a capítulos posteriores.

`UniverseAssetRule` añade `AssetRoutingRule? Routing`. Sus constructores
históricos de cuatro y cinco argumentos siguen funcionando con Routing null.
La nueva forma acepta packageFiles + routing, manteniendo las mismas guardas y
snapshots de PackageFiles y clasificación. Conserva la referencia del routing
inmutable recibido; no copia ni calcula paths.

**Cada Classification("x") usada en routing debe pertenecer a
RequiredClassification de esa misma regla, mediante comparación Ordinal.**
Estar solo en AllowedClassification no basta: un package validado debe poder
proporcionar siempre la dimensión que un futuro resolver necesite. La violación
lanza ArgumentException de configuración, sin NapIssue. Literal y AssetId no
requieren dimensiones.

## JSON y seguridad

[Universe Profile v3](UNIVERSE_PROFILE_V3.md) exige routing por asset_rule:

```json
{
  "segments": [
    { "literal": "portraits" },
    { "classification": "culture" },
    { "classification": "location" },
    { "classification": "role" },
    { "classification": "sex" },
    { "asset_id": true }
  ]
}
```

La forma del contrato se validó inicialmente con un fixture genérico. En 3.5,
Nimroel adopta esta misma secuencia como política canónica para `portrait_npc`.
Routing solo tiene segments; cada segmento contiene exactamente una de las
tres propiedades anteriores. asset_id debe ser boolean true. Objetos cerrados,
sin variantes mezcladas, campos desconocidos ni duplicados en el loader.

Los futuros segmentos podrán proceder únicamente de un literal seguro, el valor
de una clasificación required ya validada como machine identifier o el asset_id
validado. **3.3 todavía no sustituye esos valores ni forma paths.** No admite
root ni rutas absolutas desde metadata y no conecta ValidatedAssetPackage,
ValidatedProductionRepository o ProductionRepositorySnapshot al contrato.

Los modelos no realizan I/O. No existen RouteResolver, DestinationResolver,
ResolvePath, ResolveDirectory, GetDestination, BuildPath ni Path.Combine para
routing. No hay nuevos NapIssueCodes: los errores actuales son de configuración.

## Nimroel y siguientes capítulos

Tras la [auditoría histórica de 3.5](NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md),
Nimroel usa Universe Profile v3 y declara exactamente
`portraits/{culture}/{location}/{role}/{sex}/{asset_id}`. `realm` y `region`
siguen allowed, pero no son required ni segmentos de ruta. Los valores futuros
son machine identifiers; no existe title casing, mapping, condicional, life
stage ni inferencia desde AssetId. Routing Contract v1 es suficiente.

- **3.4 HECHO:** [diff estructural puro](STRUCTURAL_CHANGE_DETECTION.md) mediante snapshots, sin usar routing.
- **3.5 HECHO:** estructura histórica auditada y política canónica Nimroel declarada, sin migración.
- **3.6 — Destination Resolver siguiente:** calcular destinos usando las reglas ya validadas.

Este capítulo no implementa resolución, diff de repositorio, auditoría histórica, migración,
colisiones, creación de carpetas, ProcessingPlan, Dry Run, hashes, conversión,
SQLite, TeraBox, automatización Git ni UI en este capítulo.
