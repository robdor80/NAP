# Naming v1 — Fase 2 · Capítulo 2.3

El manifest es la fuente de verdad estructural. Naming v1 comprueba la forma
de identificadores y deriva nombres canónicos; no deduce clasificación,
no consulta vocabularios y no modifica los valores recibidos.

## Identificadores de máquina

`asset_type`, `production_profile`, claves y valores de `classification`
usan lowercase snake_case ASCII: letras `a-z`, dígitos `0-9` y underscore.
Empiezan por letra, tienen como máximo 64 caracteres, sin underscores
iniciales/finales ni dobles. No se admiten espacios, acentos ni guiones.

La gramática es `^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$`, aplicada a la cadena
completa. Ejemplos válidos: `portrait`, `portrait_npc`, `norgard`, `treskal`,
`farmer`, `male`, `house_aethros`. `Portrait`, `portrait NPC`, `portrait-npc`,
`_treskal`, `treskal_`, `treskal__north` y `Norgård` son inválidos.
No hay enums cerrados: una categoría futura que respete la forma puede pasar
Naming, aunque su soporte semántico siga pendiente de un registro.

## Asset ID y coherencia de categoría

El ID usa lowercase snake_case ASCII, comienza por letra, admite como máximo
96 caracteres y termina en `_` seguido de exactamente tres dígitos entre
`001` y `999`. `000` se rechaza. Hay al menos un descriptor entre el
`asset_type` completo y la secuencia. Los descriptores son segmentos no
vacíos de letras/dígitos separados por underscores; no se les asigna semántica.

Son compatibles los IDs históricos de este estilo:

```text
portrait_treskal_farmer_male_001
portrait_treskal_boy_001
portrait_treskal_farmer_boy_002
scene_treskal_market_dusk_001
heraldry_aethros_banner_001
object_norgard_iron_sword_001
```

El prefijo debe coincidir exactamente con `<asset_type>_`, mediante comparación
ordinal. `scene` con `portrait_treskal_farmer_male_001` es incoherente.
Una categoría puede tener underscores: `future_type_descriptor_001` coincide
con `future_type`; `future_type_001` no, porque falta el descriptor tras ese
tipo completo. La comprobación genérica de forma no puede resolver esa
coherencia dinámica por sí sola.

El resto del ID no se interpreta como `culture`, `location`, `role`, `sex`,
`house`, edad ni ninguna otra dimensión. Una corrección de classification
no invalida ni regenera automáticamente el ID histórico. No se impone que
el descriptor refleje la metadata actual.

## Nombres canónicos

Para un ID válido, se derivan exclusivamente estos nombres, con extensiones
lowercase:

| Uso | Nombre |
| --- | --- |
| ZIP | `<asset_id>.zip` |
| PNG maestro | `<asset_id>.png` |
| Prompt | `<asset_id>_prompt.md` |
| Info | `<asset_id>_info.md` |
| Manifest | `<asset_id>_manifest.json` |
| Visual Identity, cuando aplique | `<asset_id>_visual_identity.json` |
| Producción futura | `<asset_id>.webp` |

ZIP = `<asset_id>.zip` convierte en convención oficial la coincidencia con
el stem que usa el extractor. El extractor sigue sin validar esa coherencia:
la futura validación del paquete deberá comprobar sus nombres y contenido.
Derivar el nombre de Visual Identity no significa exigir ese archivo para
una categoría. Los archivos extra se definirán con sus futuros perfiles.
No se añade conversión WebP, creación de archivos ni routing.

## Estabilidad de identidad

Tras la primera incorporación oficial al catálogo, el ID es identidad
estable: no se renombra automáticamente por cambios de classification.
Antes de esa incorporación puede corregirse un paquete incorrecto.
Una corrección de ID registrado requerirá una migración explícita futura,
nunca silenciosa. La secuencia forma parte de la identidad; no se reutiliza
deliberadamente la identidad/número de un asset registrado para otro asset.
No se impone unicidad global de cada sufijo de tres dígitos entre todos los
assets: la comprobación de unicidad de la identidad completa corresponde al
catálogo/SQLite. En este capítulo no hay registro, renames ni migraciones.

## Código y schema

`AssetNamingRules` es estático y sin estado. Ofrece
`IsValidMachineIdentifier`, `IsValidAssetId` y `MatchesAssetType`; devuelven
`false` ante entradas inválidas o `null`. Un recorrido común de caracteres
ASCII comprueba snake_case sin duplicar regex en el código C#; las comprobaciones
de longitud, secuencia y prefijo completan cada regla. No hay trim, lowercase,
traducción, corrección, I/O ni inferencia de classification.

`AssetPackageFileNames` conserva el ID y expone los siete nombres. Su constructor
exige la forma válida del ID mediante `AssetNamingRules` y lanza
`ArgumentException` si no se cumple. No recibe categoría, por lo que no comprueba
la coherencia dinámica; esa comprobación se ofrece por separado.

El schema de Manifest v1 mantiene `schema_version = 1`. `$defs.machineIdentifier`
usa la gramática anterior y `maxLength: 64` en tipos, perfiles, claves de
classification (`propertyNames`) y todos sus valores. `$defs.assetId` usa
`maxLength: 96` y la gramática:

```text
^[a-z][a-z0-9]*(?:_[a-z0-9]+)+_(?!000)[0-9]{3}$
```

Ambos patrones del schema añaden `(?![\s\S])` después de `$` para exigir
el final absoluto y rechazar también un salto de línea final. El schema
comprueba forma genérica; `MatchesAssetType` comprueba el prefijo y descriptor
respecto al tipo completo. No se incorporan enums ni comparación dinámica
de propiedades al schema. Estas reglas completan lo reservado para 2.3;
no se crea Manifest v2 ni se modifica `AssetManifestV1` para validar datos.

Los tests de Naming cubren formatos válidos/inválidos, límites exactos de
longitud, secuencia, categorías compuestas, nombres canónicos y preservación
de metadata. Las comprobaciones puntuales del schema pueden ejecutarse con
`Test-Json` de PowerShell, sin añadir paquetes NuGet.

Quedan pendientes validación del paquete, soporte de vocabularios/perfiles,
obligatoriedad de Visual Identity, unicidad en catálogo y migraciones. El
siguiente capítulo es **2.4 — Validación PNG**, aún sin implementar.
