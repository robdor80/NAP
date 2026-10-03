# Universe Profile configuration v2 — Fase 2 · Capítulo 2.8.1

El [schema](../schemas/nap-universe-profile-v2.schema.json), Draft 2020-12,
con ID `urn:nap:universe-profile:v2`, evoluciona la configuración declarativa
para añadir archivos de package por asset rule. La versión es independiente
de Manifest v2 y de [Package Contract v1](PACKAGE_CONTRACT_V1.md).

[Universe Profile v1](UNIVERSE_PROFILE_V1.md) y su schema se conservan intactos
como contrato histórico. Su referencia a Nimroel describe el estado de 2.6.2.
El capítulo 2.8.1 llevó el perfil a v2 y 3.5 conserva ese JSON exacto en el
[fixture v2](../test-data/phase2/universe-profile-v2/profile.json); el
[perfil real actual](../config/universes/nimroel/profile.json) usa v3 para
declarar routing. El [fixture v1](../test-data/phase2/universe-profile-v1/profile.json)
conserva la configuración anterior para pruebas de compatibilidad. No se
reescriben los contratos históricos.

## Contrato JSON

La raíz continúa cerrada, con cinco propiedades obligatorias:
`schema_version` (entero constante 2), `universe_id`, `display_name`,
`classification_dimensions` y `asset_rules`. Mantiene las reglas de v1 para
identificadores, nombre humano, dimensiones únicas y arrays de reglas únicas.
Los arrays pueden estar vacíos.

Cada asset_rule es cerrado y exige `asset_type`, `production_profile`,
`allowed_classification`, `required_classification` y **`package_files`**.
El array package_files es obligatorio pero puede estar vacío; `uniqueItems`
rechaza objetos completos duplicados.

Cada objeto package_file es cerrado y exige exactamente:

| Campo | JSON / contrato |
| --- | --- |
| `role` | String machine identifier Naming v1, máximo 64; vocabulario abierto. |
| `suffix` | String vacío o `_` + machine identifier, máximo 65. |
| `extension` | String con punto inicial y segmentos lowercase ASCII alfanuméricos separados por puntos. |
| `required` | Booleano, nunca string/number/null. |
| `content_validator` | String machine identifier, máximo 64, o null explícito. |

content_validator es obligatorio incluso cuando su valor es null. Los objetos
raíz, asset_rule y package_file usan `additionalProperties: false`. La
combinación suffix `_manifest` + extension `.json` está prohibida: corresponde
al manifest universal, externo a PackageFiles.

```json
{
  "schema_version": 2,
  "universe_id": "test_universe",
  "display_name": "Test Universe",
  "classification_dimensions": [],
  "asset_rules": [{
    "asset_type": "audio",
    "production_profile": "audio_source",
    "allowed_classification": [],
    "required_classification": [],
    "package_files": [{
      "role": "audio_master", "suffix": "", "extension": ".wav",
      "required": true, "content_validator": null
    }]
  }]
}
```

Es un ejemplo de genericidad, no un perfil adicional implementado. Roles y
content validators no tienen enum cerrado; `png_master` es la referencia actual
de Nimroel. Un identificador futuro bien formado no exige tener su capacidad
implementada durante la carga de configuración.

## Loader y modelo runtime

`UniverseProfileLoader.Load(Stream)` detecta primero schema_version y después
aplica un dispatch explícito:

| Versión | Contrato de asset_rule | Runtime |
| --- | --- | --- |
| 1 | Cuatro propiedades históricas; rechaza package_files y routing. | UniverseAssetRule con PackageFiles vacío y Routing null. |
| 2 | Cinco propiedades; exige package_files y rechaza routing. | UniverseAssetRule con AssetPackageFileRule inmutables y Routing null. |
| 3 | Seis propiedades; exige package_files + routing. | Véase [Universe Profile v3](UNIVERSE_PROFILE_V3.md). |
| Otra, string, null o número no entero admitido | Rechazado. | InvalidDataException. |

Las representaciones numéricas `1.0`/`1e0` y `2.0`/`2e0` representan las versiones
enteras correspondientes y siguen aceptándose. El runtime no añade una
propiedad schema_version a UniverseProfile: los contratos construyen el
mismo modelo genérico. No hay conversión silenciosa de contratos ni carga global.

Se rechazan propiedades desconocidas y duplicadas en todos los niveles;
campos ausentes/tipos incorrectos o JSON mal formado producen JsonException.
Las formas/relaciones semánticas inválidas producen ArgumentException o sus
subclases desde los modelos. La versión ausente produce JsonException.
`Load(path)` abre en solo lectura; el stream del caller permanece abierto.
Los errores de filesystem se propagan, sin catch(Exception). No hay
normalización ni JSON Schema validator runtime adicional.

El schema comprueba forma y duplicados completos. C# comprueba roles únicos,
colisiones de suffix + extension, combinación tipo/perfil única, dimensiones
registradas y RequiredClassification ⊆ AllowedClassification. Estos checks son
de configuración; no leen ni validan packages.

Los modelos conservan snapshots ordenados y colecciones read-only. Se mantiene
el constructor histórico de UniverseAssetRule con PackageFiles vacío.

## Nimroel y alcance

Nimroel conserva Id, DisplayName, clasificación y la única combinación
portrait + portrait_npc. Añade master PNG (`png_master`), prompt MD, info MD y
Visual Identity JSON, todos required. El manifest queda fuera de PackageFiles;
ProductionWebP queda fuera del package de entrada. El detalle está en
[PACKAGE_CONTRACT_V1.md](PACKAGE_CONTRACT_V1.md).

Los tests cubren ambos loaders, nombres puros, guardas, snapshots, colisiones
y el fixture histórico v2. `scripts/Test-MultiUniverseSchemas.ps1` conserva las
checks anteriores de Manifest v2 y Profile v1/v2, además del perfil real v3.
No se modifican los contratos históricos v1 ni los schemas de manifest.

Nimroel permaneció en v2 durante 2.8.1–3.4. La auditoría de 3.5 migra el perfil
real a v3 y fija su política canónica; este documento y su fixture siguen siendo
la referencia histórica de v2. No se crea
PackageValidator, registry de contenido, Visual Identity schema, resolución de
destinos, producción WebP ni nuevas dependencias. La carga sigue materializando
configuración explícita en memoria, sin límite nuevo de tamaño; cualquier
límite operativo adicional se decidirá después.
