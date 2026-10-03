# Universe Profile configuration v3 — Fase 3 · Capítulo 3.3

El [schema v3](../schemas/nap-universe-profile-v3.schema.json), Draft 2020-12,
con ID `urn:nap:universe-profile:v3`, añade el
[Routing Contract v1](ROUTING_CONTRACT_V1.md) por asset rule. Los schemas v1/v2
permanecen intactos; no hay reinterpretación ni migración automática.

| Versión | Configuración de asset_rule | Routing runtime |
| --- | --- | --- |
| v1 histórico | Clasificación; sin package_files ni routing. | null |
| v2 | Clasificación + package_files; sin routing. | null |
| v3 | Clasificación + package_files + routing requerido. | AssetRoutingRule |

Desde 3.5, el [perfil real Nimroel](../config/universes/nimroel/profile.json)
usa v3 con la política canónica `portrait_npc`. El
[fixture v3](../test-data/phase3/universe-profile-v3/profile.json) continúa como
caso genérico `test_universe`; el
[fixture Nimroel v2](../test-data/phase2/universe-profile-v2/profile.json)
preserva la compatibilidad histórica sin reinterpretarla como v3.

## Contrato JSON cerrado

La raíz exige exactamente schema_version (entero constante 3), universe_id,
display_name, classification_dimensions y asset_rules. Conserva las reglas de
v2 para identifiers, display name, dimensiones y arrays. asset_rules puede
estar vacío.

Cada asset_rule exige exactamente asset_type, production_profile,
allowed_classification, required_classification, package_files y routing.
**PackageFiles conserva exactamente el contrato v2**: mismos campos, grammar,
tipos, unicidad de objetos completos y reserva del manifest universal. Puede
estar vacío. No se modifican Package Contract ni schemas de Manifest.

Routing es un objeto cerrado que exige únicamente segments, array de al menos
un elemento. El schema usa oneOf para exigir exactamente una de estas formas:

```json
{ "literal": "assets" }
{ "classification": "culture" }
{ "asset_id": true }
```

Cada objeto está cerrado. literal/classification son machine identifiers
Naming v1, máximo 64, sin corrección. asset_id es boolean true, nunca false,
string, número ni null. El orden se conserva y se permiten segmentos repetidos;
no se exige ninguna variante concreta.

El schema valida estructura. El runtime comprueba además relaciones semánticas:
dimensiones registradas, Required ⊆ Allowed, unicidad de combinaciones/roles,
colisiones de filenames y **Classification de routing ⊆ RequiredClassification**.
Una dimensión allowed pero opcional no puede formar parte de la ruta.
La última relación no se expresa como validación cruzada de arrays en el schema.

## Loader y compatibilidad

UniverseProfileLoader aplica dispatch explícito v1/v2/v3. v1 rechaza package_files
y routing; v2 exige package_files y rechaza routing; v3 exige ambos. Todos
construyen el mismo modelo genérico sin agregar schema_version runtime.
Las representaciones numéricas 3, 3.0 y 3e0 siguen el criterio previo de 1/2.
Versiones desconocidas, números no equivalentes y strings/null se rechazan.

El parsing conserva propiedades cerradas y rechaza duplicadas en raíz, rule,
package file, routing y segmento. Campos ausentes, tipos incorrectos y formas
de segmento inválidas producen JsonException. Identifiers, rutas declarativas
vacías y relaciones semánticas inválidas producen ArgumentException desde los
modelos. Una versión no soportada produce InvalidDataException.

Load(Stream) deja abierto el stream del caller, también ante errores.
Load(path) abre en solo lectura; fallos operativos se propagan. No hay
catch(Exception), dependencias nuevas ni JSON Schema validator runtime.
La configuración se materializa en memoria, sin resolver destinos.

## Alcance y controles

Los tests verifican factories, snapshots, required dimensions, parsing estricto,
compatibilidad histórica, package_files y solo lectura. El script
Test-MultiUniverseSchemas.ps1 conserva los controles previos y valida el perfil
real Nimroel contra v3, rechazándolo como v2; hace la comprobación inversa con
el fixture histórico v2. Ningún schema cambia.

La [auditoría de 3.5](NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md) fija exactamente
`portraits/{culture}/{location}/{role}/{sex}/{asset_id}` para Nimroel
`portrait_npc`. `realm` y `region` quedan fuera del routing. No se configuran
transformaciones, mappings ni casing de display, y no se infiere classification
desde AssetId. 3.6 resolverá destinos con reglas ya validadas. Fase 3 sigue EN
CURSO; no se calculan paths, migran assets ni escriben destinos en 3.5.
