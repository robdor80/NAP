# Package Contract v1 — Fase 2 · Capítulo 2.8.1

**HECHO:** contrato genérico y configurable para describir los archivos de un
package normalizado. No valida una instancia real; esa tarea pertenece a 2.8.2.
La versión del Package Contract es independiente de Manifest v2 y de Universe
Profile v2. No existen todavía PackageValidator ni ValidatedAssetPackage.

## Envelope y package flat

Todo package semántico futuro contiene el manifest universal
`<asset_id>_manifest.json`. Se localizará antes de conocer las reglas específicas
del perfil. El manifest no pertenece a `package_files`; estos son archivos
adicionales definidos por la combinación exacta `asset_type` +
`production_profile` del UniverseProfile activo.

Los archivos declarados viven directamente en la raíz del package extraído.
No hay templates ni subdirectorios: cada regla resuelve un filename canónico,
nunca una ruta. Todos utilizan el mismo asset_id. El contrato no hardcodea PNG,
prompt, info ni Visual Identity como requisitos universales.

Package flat y routing son conceptos distintos. El routing futuro podrá
organizar destinos por lugar/profesión/sexo o según otra estructura configurable;
eso no modifica el envelope de entrada.

## AssetPackageFileRule

Record sellado e inmutable, con cinco propiedades de solo lectura:

| Propiedad | Contrato |
| --- | --- |
| `Role` | Naming v1 machine identifier, máximo 64; sin enum cerrado. |
| `Suffix` | `""` o `_` seguido de un machine identifier Naming v1 válido, máximo 65 caracteres incluyendo `_`. |
| `Extension` | Punto inicial y segmentos ASCII lowercase alfanuméricos no vacíos separados por puntos. |
| `Required` | Booleano; distingue archivo requerido de opcional para la validación futura. |
| `ContentValidator` | `null` o machine identifier Naming v1, máximo 64; sin enum cerrado. |

No se normaliza: no hay trim, lowercase ni corrección silenciosa. Los
identificadores comienzan por letra, admiten lowercase snake_case ASCII y no
permiten underscores dobles/finales. Role es identidad configurable, no una
capacidad registrada ni un nombre de archivo.

Suffix admite `""`, `_prompt`, `_info`, `_visual_identity`, `_metadata`;
rechaza `prompt`, `__prompt`, `_Prompt`, `_prompt_`, `_two words`, `../x`,
slashes, backslashes y colon. Extension admite `.png`, `.md`, `.json`, `.webp`,
`.wav`, `.ogg`, `.tar.gz` y `.7z`; rechaza `.`, `..`, `.PNG`, whitespace,
separadores, segmentos vacíos, punto final, caracteres no ASCII y caracteres
peligrosos de Windows. No se establece un límite nuevo de longitud de extensión.

`ContentValidator = null` indica ausencia de validación específica de contenido
en 2.8.2. Un identificador futuro bien formado, como `future_audio_validator`,
es válido en la configuración; la capa operativa decidirá si esa capacidad
está soportada. Este capítulo no crea registry, plugins ni dispatch de contenido.

`ResolveFileName(assetId)` exige `AssetNamingRules.IsValidAssetId(assetId)` y
devuelve exactamente `assetId + Suffix + Extension`. No usa Path.Combine, no
hace I/O ni comprueba presencia de archivos. Argumentos inválidos producen
ArgumentException, sin convertirlos en NapIssue de package.

```csharp
var master = new AssetPackageFileRule("master", "", ".png", true, "png_master");
master.ResolveFileName("portrait_example_001"); // portrait_example_001.png
var prompt = new AssetPackageFileRule("prompt", "_prompt", ".md", true);
prompt.ResolveFileName("portrait_example_001"); // portrait_example_001_prompt.md
```

## UniverseAssetRule y colisiones

La regla añade `IReadOnlyList<AssetPackageFileRule> PackageFiles` al modelo
existente de clasificación. El constructor histórico de cuatro argumentos
produce PackageFiles vacío; el overload de cinco recibe los archivos, toma
snapshot defensivo ordenado y expone un wrapper read-only. Cada file rule
también es inmutable. Se conservan las guardas de identificadores y
RequiredClassification ⊆ AllowedClassification.

No admite colección/elementos nulos, roles duplicados ni dos reglas cuyo
`Suffix + Extension` sea idéntico ordinalmente. No se necesita fabricar un
asset_id para detectar esa colisión. Mismo suffix con distinta extension y
distinto suffix con la misma extension están permitidos.

El ending `_manifest.json` está reservado al envelope universal y se rechaza
en C# y en Profile v2 schema. No se reserva un enum de roles: la protección
afecta al filename del manifest, no a vocabularios específicos de universos.

## Nimroel portrait + portrait_npc

La configuración real pasa a [Universe Profile v2](UNIVERSE_PROFILE_V2.md).
Mantiene exactamente las seis dimensiones culture/realm/region/location/role/sex,
con culture/location/role/sex requeridas. Declara solo cuatro package files:

| Role | Suffix | Extension | Required | ContentValidator |
| --- | --- | --- | --- | --- |
| master | `""` | `.png` | true | `png_master` |
| prompt | `_prompt` | `.md` | true | null |
| info | `_info` | `.md` | true | null |
| visual_identity | `_visual_identity` | `.json` | true | null |

Para `portrait_treskal_farmer_male_001`, el package esperado contiene:

```text
portrait_treskal_farmer_male_001_manifest.json
portrait_treskal_farmer_male_001.png
portrait_treskal_farmer_male_001_prompt.md
portrait_treskal_farmer_male_001_info.md
portrait_treskal_farmer_male_001_visual_identity.json
```

Visual Identity es required por este perfil, no por una regla universal del
Core. No se inventan reglas para scene, heraldry, object ni environment.
ProductionWebP no llega en este package: NAP lo producirá más tarde.
Que `.webp` sea una extensión segura en el modelo no la añade al contrato Nimroel.

`AssetPackageFileNames` se conserva compatible como naming histórico/convenience:
expone los nombres existentes, incluido Manifest y ProductionWebP. No determina
qué archivos exige una asset rule; esa fuente configurable es PackageFiles.
El nuevo modelo no depende de esa clase.

## Seguridad ZIP, validación semántica y siguiente capítulo

La extracción ZIP genérica continúa aceptando contenido opaco seguro, incluso
ZIP sin manifest o con nested entries. Package Contract v1 describe una capa
semántica posterior: no modifica ZIP extraction ni redefine sus resultados.

2.8.1 no enumera directorios, lee manifests de assets, valida PNG, comprueba
universo/classification de packages, rechaza archivos faltantes/extra ni produce
NapIssue de package. El único I/O de la configuración es el loader ya existente.
No hay estado global, routing, conversiones ni nuevas dependencias.

**2.8.2 — Package Semantic Validator es el siguiente capítulo pendiente.**
Deberá reunir envelope, perfil y capacidades de contenido antes de permitir
que routing reciba un asset. Quedan por concretar sus resultados, política de
extras/subdirectorios, capacidades desconocidas y límites operativos de nombres;
no se implementan aquí. Fase 2 vuelve a estar en curso y Fase 3 no está iniciada.
