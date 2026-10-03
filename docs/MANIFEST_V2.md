# Manifest v2 — Fase 2 · Capítulo 2.6.2

Manifest v2 es el contrato multiuniverso vigente para **nuevos assets**.
[Manifest v1](MANIFEST_V1.md), su modelo, schema y fixtures permanecen intactos
como contrato histórico Nimroel-era. No se reinterpreta v1 ni se migran assets.
El [schema v2](../schemas/nap-manifest-v2.schema.json) es Draft 2020-12,
con identidad `urn:nap:manifest:v2` (no es una URL de descarga).

## Contrato exacto

Todas las seis propiedades raíz son obligatorias y no nulas.
`additionalProperties: false` prohíbe otras propiedades raíz.

| Campo | Contrato |
| --- | --- |
| `schema_version` | Entero constante `2`; versión incompatible del contrato. |
| `universe_id` | Naming v1 machine identifier, máximo 64 caracteres. |
| `asset_id` | Naming v1 asset ID, máximo 96 caracteres, secuencia 001–999. |
| `asset_type` | Machine identifier, máximo 64, sin enum cerrado. |
| `production_profile` | Machine identifier, máximo 64, sin enum cerrado. |
| `classification` | Objeto extensible, incluidas claves y valores machine identifiers. |

La gramática de los machine identifiers es lowercase snake_case ASCII,
comienza por letra y no admite underscores dobles o finales. V2 reutiliza
las mismas gramáticas y límites del schema v1 y [Naming v1](NAMING_V1.md),
incluido final absoluto de cadena para rechazar saltos de línea finales.
No se normaliza. El asset_id no incorpora el universo; la identidad runtime
completa es `UniverseAssetKey(UniverseId, AssetId)`.

```json
{
  "schema_version": 2,
  "universe_id": "nimroel",
  "asset_id": "portrait_treskal_farmer_male_001",
  "asset_type": "portrait",
  "production_profile": "portrait_npc",
  "classification": {
    "culture": "norgard",
    "location": "treskal",
    "role": "farmer",
    "sex": "male"
  }
}
```

Es un ejemplo del primer universo. El schema universal no declara dimensiones
especiales ni `if/then` para portrait/portrait_npc. `{}` es estructuralmente
válido para classification; su suficiencia depende del perfil seleccionado.
Claves como `faction`, `ship`, `department` o `rank` son válidas por forma sin
necesitar ser conocidas por el schema. Un ejemplo hipotético de otro universo
solo demuestra genericidad; no implica soporte ni un perfil implementado.

## Frontera transporte/runtime

`AssetManifestV2` es un record pequeño con seis propiedades `required init`
y nombres JSON explícitos. `UniverseId` del DTO es `string`; el runtime sigue
usando el value object `UniverseId` y `UniverseAssetKey`. No hay JsonConverter.
Classification utiliza `Dictionary<string, string>` como v1.

El DTO conserva los datos literalmente: `required` exige presencia al
deserializar, pero no valida versión, formato, nulls ni requisitos del perfil.
Su diccionario es mutable y la serialización directa no sustituye al schema.
En 2.8.2, `AssetManifestV2Loader` añade carga runtime estricta y comprobación
de contrato/Naming, incluidas propiedades duplicadas y
`AssetNamingRules.MatchesAssetType`, fuera del schema. No se cambia el DTO ni
se añade un JSON Schema validator runtime. Véase
[PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md).

## Perfil y boundary de universo

`UniverseProfile` es la fuente de dimensiones y requisitos semánticos.
`TryGetAssetRule(assetType, productionProfile, out rule)` busca la combinación
exacta por comparación ordinal; no infiere ni corrige. La ausencia de regla
significa que no está configurada, nunca que deba aceptarse silenciosamente.
`UniverseAssetRule.ValidateClassification` devuelve `MissingRequired`,
`NotAllowed` e `IsValid`. Solo comprueba las claves; no consulta lore ni
vocabularios de valores. La forma de claves/valores se comprueba en el schema.

`ManifestUniverseScope.Matches(manifest, context)` construye el `UniverseId`
fuerte desde el DTO y lo compara por valor con `UniverseContext.Id`.
Una forma inválida lanza `ArgumentException`, sin normalización; un universo
válido distinto devuelve `false`. Ese mismatch deberá causar STOP en la futura
capa de procesamiento. El helper solo comprueba este boundary: no valida
versión, naming de asset, clasificación, paquete ni soporte de perfil.

## Diferencias con v1 y límites

V2 cambia la versión a 2, añade `universe_id` obligatorio y elimina las
propiedades especiales de classification y requisitos Nimroel-specific del
schema universal. El perfil Nimroel conserva la regla histórica fuera del
contrato universal. No se cambia Naming, filenames, conversiones, parámetros
de producción ni los datos existentes.

2.8.2 implementa PackageSemanticValidator para packages nuevos exclusivamente
v2, con contexto explícito y sin migrar v1. Las migraciones v1→v2, registries
completos de producción, routing, SQLite, TeraBox, UI y vocabularios siguen pendientes.
Los tests cubren transporte, presence y universe match. Las comprobaciones
reproducibles del schema están en `scripts/Test-MultiUniverseSchemas.ps1`:

```powershell
./scripts/Test-MultiUniverseSchemas.ps1
```

El script usa `Test-Json` de PowerShell 7 y no añade paquetes al producto.
