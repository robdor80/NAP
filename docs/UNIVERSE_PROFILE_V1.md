# Universe Profile configuration v1 — Fase 2 · Capítulo 2.6.2

El [schema genérico](../schemas/nap-universe-profile-v1.schema.json), Draft
2020-12 con ID `urn:nap:universe-profile:v1`, describe configuración declarativa.
La versión de este contrato es independiente de la versión del manifest.
El primer perfil real está en
[config/universes/nimroel/profile.json](../config/universes/nimroel/profile.json).
El Core no hardcodea sus dimensiones ni su significado.

## Contrato JSON

Objeto raíz cerrado con exactamente cinco propiedades obligatorias:

| Propiedad | Tipo y regla |
| --- | --- |
| `schema_version` | Entero constante `1`. |
| `universe_id` | Machine identifier Naming v1, máximo 64 caracteres. |
| `display_name` | String humano con al menos un carácter no whitespace. |
| `classification_dimensions` | Array de machine identifiers únicos. |
| `asset_rules` | Array de objetos asset_rule; `uniqueItems: true`. |

Cada asset_rule es cerrado y exige `asset_type`, `production_profile`,
`allowed_classification` y `required_classification`. Los dos primeros son
machine identifiers; los dos últimos son arrays de machine identifiers únicos.
Los arrays pueden estar vacíos. No hay enums de universos/dimensiones/tipos.
Todos los machine identifiers reutilizan forma y límite Naming v1.

`uniqueItems` impide duplicados expresables directamente en el schema.
La unicidad de la combinación tipo/perfil, required ⊆ allowed y registro de
dimensiones se comprueban en C#: el schema no implementa relaciones dinámicas
entre arrays. Pasar el schema no basta para aceptar configuración semántica.

## Modelo runtime

`UniverseAssetRule` es inmutable y conserva `AssetType`, `ProductionProfile`,
`AllowedClassification` y `RequiredClassification`. Rechaza identificadores
inválidos, listas nulas, elementos nulos, duplicados y required fuera de allowed.
Toma copias ordenadas y expone colecciones de solo lectura.

`UniverseProfile` mantiene `Id` y `DisplayName` y añade listas de solo lectura
`ClassificationDimensions` y `AssetRules`. Rechaza dimensiones inválidas o
duplicadas, reglas nulas, combinaciones duplicadas y dimensiones de reglas no
registradas por el perfil. Required ya está contenido en Allowed por el contrato
de la regla. No deduce nada desde el asset_id ni normaliza.
El constructor anterior `(id, displayName)` sigue funcionando con listas vacías.
`TryGetAssetRule` busca por strings ordinales exactos; un lookup desconocido
devuelve `false` y `null`. El registry continúa buscando perfiles por UniverseId.

## Loader explícito

`UniverseProfileLoader.Load(Stream)` usa `System.Text.Json`, desde la posición
actual y sin cerrar el stream del caller. `Load(path)` abre en solo lectura.
No existe carga global automática, discovery ni reload.

El loader exige versión 1 y los objetos/campos/tipos indicados; no acepta
propiedades desconocidas ni duplicadas, nombres case-insensitive, comentarios
o trailing commas. Construye UniverseId, las reglas y finalmente UniverseProfile,
aplicando las guardas semánticas. No corrige ni completa campos ausentes.
No ejecuta un JSON Schema validator runtime ni añade dependencias.

JSON mal formado, tipos incorrectos, campos ausentes/desconocidos/duplicados
producen `JsonException` (incluidas sus subclases). Versión no soportada produce
`InvalidDataException`. Formas o relaciones semánticas inválidas producen
`ArgumentException` o sus subclases desde los modelos. Los argumentos del API
y errores operativos de I/O propagan su excepción; no hay catch global.
La versión debe ser un número igual a 1; también admite `1.0` o `1e0`,
que representan ese entero en JSON Schema. No se convierte desde strings.

La carga materializa un JsonDocument y listas en memoria; no hay límite nuevo
de tamaño de archivo aparte de las opciones por defecto del parser. Es una
carga explícita de configuración, no un parser de paquetes no confiables.
Los límites adicionales de configuración podrán decidirse después.

## Nimroel: alcance exacto

- ID `nimroel`; nombre `Nimroel`.
- Dimensiones: `culture`, `realm`, `region`, `location`, `role`, `sex`.
- Única combinación: `portrait` + `portrait_npc`.
- Allowed: las seis dimensiones anteriores.
- Required: `culture`, `location`, `role`, `sex`.
- `realm` y `region` son opcionales.

`ValidateClassification` acepta la clasificación mínima, informa de location
ausente y rechaza faction para esta combinación. Devuelve un resultado
inmutable con `MissingRequired`, `NotAllowed` e `IsValid`; puede señalar ambos
problemas a la vez. Usa identidad ordinal de claves, incluso si el diccionario
del caller utiliza otro comparer. No inspecciona valores ni infiere dimensiones.
Su caller debe comprobar por separado la forma estructural del manifest.

No se añaden reglas de scene, heraldry, object ni environment, vocabularios
concretos, especies, casas o profesiones. No existen perfiles Star Trek/Star Wars.
Tampoco hay todavía reglas de carpetas/routing, parámetros de conversión,
registries completos de producción o PackageValidator.

Los tests cargan el archivo real y comprueban el modelo, el loader y sus guardas.
El script `scripts/Test-MultiUniverseSchemas.ps1` valida ambos schemas mediante
PowerShell 7 `Test-Json`, incluyendo genericidad y duplicados estructurales.
