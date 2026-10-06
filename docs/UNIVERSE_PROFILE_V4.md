# Universe Profile v4 — Fase 5 · Capítulo 5.4

**5.4 — Perfiles genéricos HECHO. Fase 4 HECHA dentro del alcance v1.
Fase 5 — Conversión HECHA; 5.1–5.5 HECHOS.
5.5 — No recorte silencioso HECHO.**

Universe Profile v4 declara qué conversión corresponde a una asset rule y
ImageConversionResolver resuelve sus parámetros desde un package validado.
Este capítulo materializa configuración; la ejecución sigue siendo una frontera
posterior. Los converters Portrait/Scene conservan sus APIs de settings explícitos.

## Evolución y contrato JSON

| Versión | Configuración de asset_rule | Conversion runtime |
| --- | --- | --- |
| v1 histórico | Clasificación. | null |
| v2 histórico | Clasificación + package_files. | null |
| v3 histórico | Clasificación + package_files + routing. | null |
| v4 vigente | Clasificación + package_files + routing + conversion. | ImageConversionRule o null |

Los schemas v1/v2/v3 permanecen intactos. UniverseProfileLoader aplica dispatch
explícito 1/2/3/4, sin migración o reinterpretación. Manifest v2 sigue vigente.
El [schema v4](../schemas/nap-universe-profile-v4.schema.json) es Draft 2020-12,
ID `urn:nap:universe-profile:v4`. La raíz cerrada exige exactamente
schema_version (integer const 4), universe_id, display_name,
classification_dimensions y asset_rules.

Cada asset rule cerrada exige asset_type, production_profile,
allowed_classification, required_classification, package_files, routing y
conversion. PackageFiles y Routing conservan exactamente los contratos v3.
**conversion es required aunque nullable**: null declara explícitamente que la
regla no tiene conversión; omitirla es inválido. Su objeto cerrado exige:

```json
{
  "kind": "png_to_webp",
  "source_role": "master",
  "output_width": 768,
  "output_height": 960,
  "webp_quality": 90
}
```

Solo se admite kind string exacto `png_to_webp`. SourceRole usa Naming v1
machine identifier existente, máximo 64 caracteres, sin normalización, trim o
lowercasing. OutputWidth y OutputHeight son enteros 1..16383; WebpQuality,
entero 0..100. Todos los objetos usan additionalProperties false. No hay paths,
output_extension, hashes, formato redundante, crop, fit, resize_mode o padding.
Desde [5.5 — No recorte silencioso](NO_SILENT_CROP.md), png_to_webp significa
frame completo, ratio source/output exacto y resize proporcional: sin crop,
pad, BoxPad, letterbox o distorsión. La garantía no es configurable y el schema
v4 permanece intacto. Upscale compatible se permite; mismatch implica STOP.
Cualquier crop futuro requiere contrato explícito nuevo y versionado, sin
cambiar silenciosamente png_to_webp.

## Modelo e invariantes

ImageConversionKind es enum público con un único valor PngToWebp.
ImageConversionRule es sealed record con constructor público exacto:

```csharp
public ImageConversionRule(
    ImageConversionKind kind, string sourceRole,
    int outputWidth, int outputHeight, int webpQuality)
```

Expone únicamente cinco propiedades get-only: Kind, SourceRole, OutputWidth,
OutputHeight y WebpQuality. Kind desconocido y números fuera de rango producen
ArgumentOutOfRangeException con ParamName exacto; SourceRole inválido usa la
infraestructura existente de identifiers.

UniverseAssetRule añade Conversion get-only nullable y un constructor público
de siete argumentos: assetType, productionProfile, allowedClassification,
requiredClassification, packageFiles, routing y conversion. Conserva exactamente
los constructores históricos de cuatro/cinco/seis argumentos, con Conversion null.

Cuando Conversion existe, su SourceRole debe coincidir Ordinal con una regla de
PackageFiles. La unicidad de Role ya está garantizada. Para PngToWebp esa regla
debe tener Required true, Extension `.png` y ContentValidator `png_master`, con
comparaciones Ordinal. Incumplir cualquiera produce ArgumentException sobre
`conversion` al construir/cargar el perfil, antes de validar packages.
Estas guardas no inspeccionan filesystem.

El loader mapea kind explícitamente, sin Enum.Parse ni dependencia de JSON Schema
runtime. Campos ausentes, duplicados, desconocidos, tipos incorrectos y kind
desconocido producen JsonException; límites y relaciones semánticas se defienden
además en los modelos. Los números matemáticamente enteros son admitidos.
Load(Stream) deja abierto el stream; Load(path) conserva lectura explícita del
JSON. La resolución posterior no realiza I/O.

## Presupuesto runtime y resolución

**MaxInputPixels queda deliberadamente fuera del profile y del schema.** Es un
presupuesto operativo de ejecución, suministrado por el caller; no forma parte
del canon visual ni de ImageConversionRule.

ImageConversionResolver es sealed y stateless, con una única API pública:

```csharp
public ResolvedImageConversion? Resolve(
    ValidatedAssetPackage package, long maxInputPixels)
```

Primero valida package no null (ArgumentNullException, `package`), después
maxInputPixels > 0 (ArgumentOutOfRangeException, `maxInputPixels`), incluso
cuando Conversion es null. Sin conversión devuelve null. Con conversión toma
exclusivamente package.AssetRule.Conversion y el path exacto de
package.FilesByRole[conversion.SourceRole]. Una incoherencia interna de role
ausente produce InvalidOperationException, sin fallback ni NapIssue nuevo.
No hay branching por AssetType, ProductionProfile, universo o filename.

ResolvedImageConversion es sealed class, constructor internal y ocho propiedades
get-only: UniverseAssetKey AssetKey, ImageConversionKind Kind, string SourceRole,
string SourcePath, int OutputWidth, int OutputHeight, int WebpQuality y
long MaxInputPixels. Es un snapshot inmutable, con SourcePath exacto sin
Path.GetFullPath, comprobación de existencia, apertura de archivos o links.
Resolver y snapshot ejecutan cero filesystem I/O: no convierten, validan salida,
escriben WebP ni calculan hashes. No se añade converter genérico público u
orquestador de ejecución.

PackageSemanticValidator conserva su algoritmo. ValidatedAssetPackage retiene
la misma AssetRule y por tanto AssetRule.Conversion. La nueva relación source
role se valida en la configuración; un package aceptado garantiza el required
PNG validado con PngMasterValidator.

## Configuración real y pruebas

El [perfil Nimroel](../config/universes/nimroel/profile.json) migra solo
schema_version 3 → 4 y añade el objeto mostrado a portrait + portrait_npc.
Clasificación, package files y routing siguen intactos. 768×960 Q90 ahora vive
en config, con source role master; MaxInputPixels llega en runtime.
**Nimroel Scene sigue SIN perfil canónico**, resolución, ratio, quality,
production_profile, clasificación o routing. La capacidad técnica Scene de 5.3
no declara ese canon.

El [fixture v4](../test-data/phase5/universe-profile-v4/profile.json) pertenece a
test_universe: image_asset + image_profile declara source_image, 1200×800 Q87;
metadata_asset + metadata_profile declara conversion null. No es otro universo
real ni usa Scene. Los tests cubren contratos, parser estricto, invariantes,
compatibilidad histórica, ausencia de I/O y config → package → resolver tanto
para Nimroel como para el fixture genérico, sin convertir imágenes.
Test-MultiUniverseSchemas.ps1 conserva los controles anteriores y añade v4,
incluidos cruces entre versiones y adversariales.

## Continuidad del plan

ProcessingPlan v1 todavía no congela la conversion rule y Dry Run v1 todavía
no muestra una operación de conversión. 5.4 introduce contrato/configuración;
una futura evolución del ProcessingPlan podrá congelar la regla cuando se
formalicen operaciones de ejecución. No se fija un capítulo para esa evolución
ni se reabre Fase 4. 5.5 — No recorte silencioso HECHO: ImageConversionGeometryValidator
consume PngImageInfo + ResolvedImageConversion mediante HasAspectRatio exacto,
sin I/O o ejecución; limpio o Error + Stop genérico. Los converters actuales
mantienen sus errores históricos y Stretch condicionado al ratio exacto.
Fase 5 HECHA; Fase 6 — Integridad EN CURSO, 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID SIGUIENTE.
