# NAP — Master Specification

**Proyecto:** NAP\
**Nombre oficial:** Nexus Asset Platform\
**Origen histórico:** Nimroel Asset Pipeline\
**Repositorio:** `robdor80/NAP`\
**Estado actual verificado:** Fase 0, Fase 1 (1.1–1.6) y Fase 2 (2.1–2.8) HECHAS. 2.8.1 — Package Contract v1 + Universe Profile v2 y 2.8.2 — Package Semantic Validator completos. Fase 3 — Routing y repo HECHA. 3.1 — Production Repository Boundary, 3.2 — Repository Scanner, 3.3 — Routing Contract v1, 3.4 — Structural Change Detection, 3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy y 3.6 — Destination Resolver HECHOS. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan, 4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Routing solo podrá consumir ValidatedAssetPackage.\
**Plataforma principal:** Windows 11, C# / .NET 8\
**Equipo objetivo principal:** MSI Raider GE78 HX 14V\
**Propósito de este documento:** trasladar a una nueva conversación
todas las decisiones, ideas y requisitos acordados para continuar el
diseño y desarrollo sin depender de conversaciones anteriores.


## Índice

- [Qué es NAP](#1-qué-es-nap)
- [Principios fundamentales](#2-principios-fundamentales)
- [Flujo de un asset](#3-flujo-de-un-asset)
- [Manifest y Visual Identity](#5-manifest)
- [Conversión y routing](#7-conversión-de-imágenes)
- [TeraBox e integridad](#9-terabox)
- [SQLite](#11-sqlite----requisito-fundamental)
- [Catálogo, estadísticas y objetivos](#15-catálogo-visual)
- [RoDo e IA](#22-rodo)
- [RobStyle UI y Companion](#32-ui----robstyle-ui)
- [Git y arquitectura](#46-git-del-propio-nap)
- [Estado actual y hoja de ruta](#55-hoja-de-ruta-acordada)
- [Decisiones críticas](#58-decisiones-que-no-deben-olvidarse)

------------------------------------------------------------------------

# 1. Qué es NAP

NAP — Nexus Asset Platform será la aplicación de escritorio encargada de automatizar y
gestionar la producción, conservación, catalogación, auditoría y planificación
de assets visuales organizada por **universos/perfiles**. **Nimroel** es
el primer Universe Profile real soportado, no una dependencia del núcleo.

La separación del RPG en **CoreRPG + Universe Pack** (consola + cartucho)
se corresponde conceptualmente con **NAP Core + Universe Profile**.
El núcleo permanece genérico; vocabularios, schemas y reglas específicas
proceden de configuración/perfiles. Star Trek y Star Wars son únicamente
ejemplos futuros, sin implementación en este trabajo.

En **2.6.1 — Core Universe Scope** se introducen `UniverseId`,
`UniverseAssetKey`, `UniverseProfile`, `UniverseRegistry`,
`UniverseStorageConfig` y `UniverseContext`. El contexto se pasa explícitamente:
no existe universo actual global. Identidad y almacenamiento quedan scoped
por universo, sin routing, SQLite, TeraBox ni configuración JSON todavía.
En 2.6.2 se añade configuración JSON genérica con el primer perfil real
Nimroel, Manifest v2, comprobaciones puras de clasificación/universo y
aislamiento de raíces antes de futuras escrituras. En ese punto, routing,
SQLite y TeraBox quedaban pendientes.
Véase [MULTI_UNIVERSE_ARCHITECTURE.md](MULTI_UNIVERSE_ARCHITECTURE.md).

Los ejemplos de culturas, lugares, profesiones y portraits que siguen
documentan el primer universo, Nimroel; no definen vocabularios universales
del Core. Se conserva la historia del diseño sin generalizar sus reglas.

La necesidad nace de que Nimroel necesitará potencialmente **miles de
assets**, especialmente:

-   portraits de NPC;
-   scenes;
-   heráldica;
-   objetos;
-   entornos;
-   futuras categorías visuales.

El flujo actual es demasiado manual para escalar.

La idea fundamental es:

``` text
ChatGPT genera asset
        ↓
ZIP normalizado
        ↓
NAP
        ↓
validación
        ↓
plan de operaciones
        ↓
auditoría IA
        ↓
PNG maestro → TeraBox
        ↓
PNG → WebP de producción
        ↓
WebP + documentación → repo del universo activo
        ↓
actualización SQLite
        ↓
objetivos / estadísticas / RoDo
```

NAP debe convertirse en la **fuente operativa de verdad de la producción
visual**, aunque los assets y sus metadatos individuales sigan siendo
reconstruibles sin depender exclusivamente de su base de datos.

------------------------------------------------------------------------

# 2. Principios fundamentales

## 2.1 Seguridad antes que automatización

NAP nunca debe empezar siendo un programa que mueve archivos
alegremente.

Orden conceptual:

1.  recibir;
2.  leer;
3.  validar;
4.  calcular;
5.  mostrar qué piensa hacer;
6.  auditar;
7.  ejecutar;
8.  verificar;
9.  registrar.

Primero se desarrollará un **Dry Run / PLAN** completo que no modifique
nada.

------------------------------------------------------------------------

## 2.2 Nunca destruir silenciosamente

NAP no debe:

-   sobrescribir maestros diferentes;
-   recortar imágenes silenciosamente;
-   resolver colisiones por su cuenta;
-   borrar archivos importantes;
-   asumir rutas dudosas;
-   continuar después de una validación crítica fallida.

Ante duda importante:

``` text
STOP
```

y explicación clara al usuario.

------------------------------------------------------------------------

## 2.3 Los maestros son sagrados

El PNG original generado/aprobado será el **maestro**.

El maestro se conservará en el ArchiveRoot del universo activo; TeraBox
es el destino de conservación previsto. Su integración todavía es futura.

El repositorio de producción utilizará normalmente WebP optimizado.

------------------------------------------------------------------------

## 2.4 Todo debe poder reconstruirse

SQLite será fundamental, pero **no debe ser el único lugar donde exista
la información**.

Cada asset tendrá metadatos individuales suficientes para reconstruir el
catálogo.

Arquitectura conceptual:

``` text
asset individual
├── imagen producción
├── prompt
├── info
├── manifest
└── visual_identity
        ↓
fuentes persistentes

SQLite
        ↓
índice operativo reconstruible
```

------------------------------------------------------------------------

# 3. Flujo de un asset

El flujo deseado final es aproximadamente:

El caller trabajará con un `UniverseContext` explícito y la identidad
completa `(UniverseId, AssetId)`; toda escritura futura quedará dentro de
las raíces autorizadas de ese contexto.

``` text
1. ChatGPT crea imagen.
2. Usuario aprueba imagen.
3. ChatGPT prepara ZIP.
4. Usuario descarga ZIP.
5. Usuario lo deposita en Inbox de NAP.
6. NAP detecta paquete.
7. NAP espera a que esté completamente copiado.
8. NAP copia a staging.
9. NAP extrae de forma segura.
10. NAP valida semánticamente el package y produce ValidatedAssetPackage.
11. NAP identifica asset.
12. NAP determina destino.
13. NAP genera ProcessingPlan.
14. Gemini audita el plan.
15. Si PASS:
       PNG maestro → TeraBox
       PNG → WebP producción
       documentación → repo
16. NAP verifica hashes y destinos.
17. NAP actualiza SQLite.
18. NAP actualiza objetivos/estadísticas.
19. NAP registra operación.
20. Asset → COMPLETED.
```

------------------------------------------------------------------------

# 4. Contenido previsto del ZIP

El [Package Contract v1](PACKAGE_CONTRACT_V1.md) define el package normalizado
**flat**, con archivos en su raíz y sin templates de subdirectorios. Todo
package semántico futuro contiene el manifest universal
`<asset_id>_manifest.json`, localizado antes de conocer las reglas del perfil.
Los demás archivos provienen exclusivamente de `UniverseAssetRule.PackageFiles`
para la combinación exacta asset_type + production_profile. PackageFiles fue
introducido por [Universe Profile v2](UNIVERSE_PROFILE_V2.md) y se conserva sin
cambios en [Universe Profile v3](UNIVERSE_PROFILE_V3.md) y
[Universe Profile v4](UNIVERSE_PROFILE_V4.md), vigente para Nimroel desde 5.4.
No hay requisito universal de PNG, prompt, info ni Visual Identity.

El perfil Nimroel actual, únicamente portrait + portrait_npc, exige:

``` text
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_manifest.json
<asset_id>_visual_identity.json
```

El manifest queda fuera de package_files. ProductionWebP no llega en el
package de entrada: lo producirá NAP más tarde. Visual Identity es required
para esta combinación por decisión del perfil, sin generalizarlo a otros assets.

Todos los archivos deben compartir un `asset_id` coherente.

`AssetPackageFileRule` es puro e inmutable: Role y ContentValidator son machine
identifiers abiertos; Suffix es vacío o `_` + machine identifier; Extension son
segmentos lowercase ASCII alfanuméricos separados por puntos; Required es booleano.
ResolveFileName exige Naming v1 y devuelve asset_id + Suffix + Extension sin rutas.
PackageFiles toma snapshot read-only, exige roles/filenames únicos y reserva
`_manifest.json` al envelope. AssetPackageFileNames conserva compatibilidad como
naming histórico/convenience, sin determinar los archivos requeridos del perfil.

2.8.1 define este contrato y 2.8.2 implementa
[Package Semantic Validation](PACKAGE_SEMANTIC_VALIDATION.md) de solo lectura
para packages reales: root/estructura, manifest v2 estricto, envelope canónico,
universo/rule/classification y archivos/contenido por perfil. ZIP extraction
continúa genérica y opaca, también para ZIP seguros con nested entries. Package
flat no limita los destinos futuros. Routing solo podrá consumir ValidatedAssetPackage.

------------------------------------------------------------------------

# 5. Manifest

## 5.1 Contrato multiuniverso vigente para nuevos assets

[Manifest v2](MANIFEST_V2.md) y
[nap-manifest-v2.schema.json](../schemas/nap-manifest-v2.schema.json)
definen exactamente seis campos obligatorios: `schema_version = 2`,
`universe_id`, `asset_id`, `asset_type`, `production_profile`, `classification`.
El objeto raíz está cerrado. El schema comprueba Naming v1 de identificadores,
claves y valores, sin dimensiones especiales ni condiciones de portrait.
`universe_id` utiliza machine identifier de hasta 64 caracteres; asset_id
conserva forma y límite 96, scoped por universo sin cambiar sus filenames.

`AssetManifestV2` es un DTO con string para universe_id; el runtime sigue
usando UniverseId/UniverseAssetKey. `ManifestUniverseScope.Matches` exige
coincidencia con UniverseContext.Id; mismatch en 2.8.2 implica STOP antes de aplicar el perfil activo.
El helper no es un PackageValidator ni valida el resto del manifest.

AssetManifestV2Loader aporta carga estricta de contrato/Naming, sin cambiar el
DTO ni añadir JSON Schema runtime. PackageSemanticValidator reúne después las
comprobaciones con contexto explícito y produce un snapshot inmutable únicamente
con report limpio. Manifest v1 sigue histórico, sin compatibilidad/migración silenciosa.

`UniverseProfile` obtiene las dimensiones y reglas semánticas de configuración
versionada: [Profile v1](UNIVERSE_PROFILE_V1.md) y
[Profile v2](UNIVERSE_PROFILE_V2.md) se conservan como contratos históricos
soportados; [Profile v3](UNIVERSE_PROFILE_V3.md) añade Routing y conserva
PackageFiles de v2. [Profile v4](UNIVERSE_PROFILE_V4.md) añade conversion
required nullable. `UniverseProfileLoader` aplica dispatch explícito por
schema_version 1/2/3/4, sin reinterpretación silenciosa. Estas versiones del
Universe Profile son independientes de Manifest v2, que sigue siendo el
manifest vigente para nuevos assets.
El primer perfil real,
[Nimroel](../config/universes/nimroel/profile.json), usa Profile v4 desde 5.4,
tras migrar a v3 en 3.5,
y registra culture/realm/
region/location/role/sex y únicamente portrait + portrait_npc: requiere
culture/location/role/sex y permite realm/region como opcionales.
`UniverseAssetRule.ValidateClassification` devuelve dimensiones faltantes
y no permitidas sin validar vocabularios de valores. Esos nombres no son
conceptos especiales del Core universal. No se inventan otras reglas.

## 5.2 Contrato histórico Manifest v1 (Nimroel-era)

Manifest v1 se conserva intacto como contrato histórico, incluidos sus
requisitos específicos del primer universo. No se añade `universe_id` a v1.
2.6.2 no modifica el modelo, schema, fixtures ni reglas históricas de v1.
No existe migración automática v1→v2.

**Manifest v1 está formalizado en Fase 2 · Capítulo 2.1.** Representa
exclusivamente la identidad administrativa/estructural del asset, su
clasificación estructural y la referencia al perfil de producción.
El contrato histórico se detalla en [MANIFEST_V1.md](MANIFEST_V1.md) y
[nap-manifest-v1.schema.json](../schemas/nap-manifest-v1.schema.json).

Ejemplo canónico:

``` json
{
  "schema_version": 1,
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

Las cinco propiedades raíz son obligatorias y no se admiten propiedades
raíz adicionales. `schema_version` es el entero `1`, versión incompatible
del contrato; no es SemVer ni una revisión del asset. `asset_id`, `asset_type`
y `production_profile` son strings con al menos un carácter no whitespace;
la misma condición se aplica a los valores de `classification`. La gramática
ya está definida en [NAMING_V1.md](NAMING_V1.md): machine identifiers lowercase
snake_case ASCII de hasta 64 caracteres; asset_id de hasta 96, con descriptor
y secuencia 001–999. Debe comenzar por el asset_type completo seguido de `_`;
classification no se deduce del ID. El ZIP es `<asset_id>.zip` y la futura
producción `<asset_id>.webp`. El ID es estable tras su registro. Las categorías
y perfiles no se cierran mediante enums.

`classification` es un objeto extensible de dimensiones estructurales con
valores de máquina normalizados, no nombres de presentación. Dimensiones
conocidas: `culture`, `realm`, `region`, `location`, `role` y `sex`.
Para `portrait` con `portrait_npc`, son obligatorias `culture`, `location`,
`role` y `sex`; `realm` y `region` son opcionales. Dimensiones futuras, como
`house`, pueden admitirse estructuralmente; la futura capa semántica deberá
rechazar dimensiones no registradas y detenerse ante categorías/perfiles
no soportados. V1 mantiene ese schema intacto; en v2, 2.6.2 implementa el
registro de dimensiones/reglas por perfil y su comprobación de claves,
sin implementar vocabularios de valores ni PackageValidator.

`apparent_age` y todos los rasgos visuales pertenecen a
`<asset_id>_visual_identity.json`, nunca al manifest ni a classification.
La edad o apariencia solicitadas pueden conservarse en el prompt.
El manifest describe qué es el asset; Visual Identity describe qué se ve
realmente en la imagen final.

No contiene parámetros de conversión, rutas, hashes, estados del pipeline,
jobs, auditorías, thumbnails, tags libres ni listas redundantes de archivos.
El perfil `portrait_npc` referencia la decisión existente: fuente PNG,
proporción 4:5, salida WebP 768 × 960, Q90 y crop no permitido. Esos parámetros
pertenecen a la futura configuración de perfiles, no al JSON del manifest.
Scenes sigue sin perfil, resolución ni proporción definitivos.

Los cambios incompatibles exigen un nuevo entero `schema_version`; ampliar
registros compatibles con esta estructura extensible no lo exige por sí solo.
El ejemplo conceptual anterior queda como contexto histórico en
`NAP_CONTINUIDAD_NAP1.md`; v1 permanece como contrato histórico y v2 es
el contrato vigente para nuevos assets.

------------------------------------------------------------------------

# 6. Visual Identity

Se ha decidido crear para cada portrait:

``` text
<asset_id>_visual_identity.json
```

Su función es describir **lo que realmente aparece visualmente en la
imagen final**, no simplemente lo solicitado en el prompt.

Debe permitir alimentar SQLite con rasgos físicos normalizados.

Campos previstos, entre otros:

-   etapa vital;
-   edad aparente;
-   banda de edad;
-   complexión;
-   forma facial;
-   anchura facial;
-   mandíbula;
-   pómulos;
-   nariz;
-   color de ojos;
-   color de pelo;
-   textura;
-   longitud;
-   pérdida de cabello;
-   canas;
-   presencia de barba/bigote;
-   tipo de vello facial;
-   color del vello facial;
-   tono de piel;
-   desgaste/exposición;
-   rasgos visibles distintivos.

Confianza:

``` text
high
medium
doubtful
not_visible
not_applicable
```

Especialmente para ojos y rasgos pequeños debe evitarse inventar.

Los campos `doubtful` deberán poder revisarse posteriormente con el PNG
maestro.

## Estado de los 39 portraits iniciales

Actualmente existen 39 portraits históricos de Treskal.

Otra conversación ha creado 39 `*_visual_identity.json`.

Se está realizando una auditoría independiente del lote antes de
considerarlo fuente fiable para SQLite.

**No asumir que esa auditoría está terminada al continuar el desarrollo.**

------------------------------------------------------------------------

# 7. Conversión de imágenes

Las decisiones Portrait siguientes pertenecen al primer perfil previsto,
Nimroel; Scene dispone de capacidad técnica, sin canon gráfico decidido.
Las reglas futuras de producción se resolverán desde
el profile/config del universo activo; no son parámetros universales del Core.

**Arquitectura final de Fase 5 — Conversión HECHA:**

```text
Package validado
    ↓
AssetRule retenida del Universe Profile v4
    ↓
ImageConversionRule
    ↓
ImageConversionResolver
    ↓
ResolvedImageConversion
    + PngImageInfo obtenido de PngMasterValidator sobre el source
    ↓
ImageConversionGeometryValidator
    ↓
clean / STOP
```

ImageConversionKind.PngToWebp (`png_to_webp`) requiere PNG estático validado,
WebP con dimensiones configuradas, ratio source/output idéntico y frame completo.
PngImageInfo.HasAspectRatio compara productos long, sin floating point ni
tolerancia. Crop, Pad, BoxPad, letterbox y distorsión están prohibidos; reducción
y upscale proporcional se permiten. Geometría incompatible produce exactamente
image_conversion_aspect_ratio_mismatch, Error + Stop, SubjectPath source exacto
y Detail invariant `source=<width>x<height>; output=<width>x<height>`.

ImageConversionGeometryValidator.Validate(PngImageInfo source,
ResolvedImageConversion conversion) solo valida geometría, sin I/O, decode,
WebP, pixel budget, quality o hashes. Portrait converter y Scene converter
siguen como primitives separadas con sus settings y errores históricos. Ambos
usan HasAspectRatio antes de decode/resize y mantienen Stretch: ratio exacto
+ Stretch + ausencia de crop/pad hace el resize proporcional. No existe todavía
ejecución/orquestación genérica. Resolver configuración y validar geometría
no ejecutan; report clean no autoriza escritura. No cambia Profile v4, no hay
Profile v5 ni campos de crop. Cualquier crop futuro deberá ser explícito y
versionado, sin cambiar silenciosamente png_to_webp. Véase
[NO_SILENT_CROP.md](NO_SILENT_CROP.md).

## Portrait

Perfil previsto actualmente:

``` text
entrada:
PNG maestro

salida:
WebP
768 × 960
relación 4:5
Q90
```

No debe haber recorte silencioso.

**Estado real de 5.1 — Portrait HECHO:** primitive de conversión del Core:
PNG → PngMasterValidator → safety pixels → ratio exacto antes del decode
→ decode PNG real Rgba32 → resize sin crop con Lanczos3 → WebP lossy en memoria.
El source se abre read-only y una misma FileStream se usa para estructura/decode,
reseteando Position = 0. Se permite upscale y no hay pad, letterbox o AutoOrient.

Antes de 5.4 los converters recibían settings explícitos sin conexión declarativa
al Universe Profile. Desde **5.4 — Perfiles genéricos HECHO**, Universe Profile v4
puede declarar conversion con kind, source_role, output_width, output_height y
webp_quality, o null explícito. Runtime puede resolver
ValidatedAssetPackage → ImageConversionResolver → ResolvedImageConversion.
El perfil real Nimroel Portrait declara PNG master → WebP, 768×960 Q90, 4:5;
no es hardcode Core ni tamaño mínimo del maestro. MaxInputPixels sigue siendo
runtime, fuera del profile. Los converters conservan settings explícitos y
5.4 solo resuelve configuración, sin ejecutar o validar salida.
ImageSharp 3.1.12 sigue siendo la única dependencia gráfica, managed y
cross-platform. Sin ProductionRoot/ArchiveRoot, ampliación de ProcessingPlan,
escrituras o permiso de ejecución. Fase 5 HECHA; 5.1–5.5 HECHOS;
5.5 — No recorte silencioso HECHO. Véase
[UNIVERSE_PROFILE_V4.md](UNIVERSE_PROFILE_V4.md) y
[PORTRAIT_CONVERSION.md](PORTRAIT_CONVERSION.md).

**Estado real de 5.2 — Validar salida HECHO:**
PortraitWebpImage → metadata/settings → contenedor y forced WebP real decode
→ dimensiones reales → NapIssueReport. PortraitWebpOutputValidator devuelve
como máximo el primer fallo Error + Stop, SubjectPath null. Cero filesystem I/O,
sin re-encode/repair, hashes ni inferencia de quality real del bitstream;
quality solo se contrasta como metadata contractual. Report limpio no autoriza
escritura ni significa COMPLETED. No sustituye verificaciones futuras después
de persistir bytes, que seguirán siendo necesarias. Véase
[PORTRAIT_OUTPUT_VALIDATION.md](PORTRAIT_OUTPUT_VALIDATION.md).

ProcessingPlan v1 todavía no congela la conversion rule y Dry Run v1 todavía
no muestra una operación de conversión. Una futura evolución del plan podrá
congelarla cuando se formalicen operaciones de ejecución, sin fijar cuándo
ni reabrir Fase 4.

## Scenes

Se definirá un perfil específico posteriormente.

**Estado real de 5.3 — Scene HECHO:** settings explícitos y contratos Scene propios:
PNG → PngMasterValidator → pixel safety → exact requested ratio → decode PNG
real → resize Lanczos3 sin crop → WebP lossy en memoria → output validation.
Sin pad, letterbox o AutoOrient; upscale permitido. SceneWebpOutputValidator
comprueba metadata/settings, contenedor, decode WebP real y dimensiones.
WebpContainerValidator internal comparte exclusivamente RIFF/WEBP, longitud
total y límites/padding de chunks con Portrait, sin parser VP8 completo.

Scene conversion capability exists, but no canonical Nimroel Scene production profile has been defined yet.

NO EXISTE TODAVÍA perfil canónico Nimroel Scene: no se han decidido resolución,
proporción, quality, production_profile, routing o clasificación. Ejemplos técnicos
1920×1080 → 1280×720 Q88 y 1500×1000 → 900×600 Q93 no son canon de Nimroel.
5.4 migra únicamente Portrait a Profile v4; no añade Scene ni su canon.
Sin escrituras, ampliación de ProcessingPlan o hashes.
5.4 HECHO; 5.5 — No recorte silencioso HECHO. Véase
[SCENE_CONVERSION.md](SCENE_CONVERSION.md).

NAP debe terminar utilizando un sistema genérico de perfiles:

``` text
portrait
scene
heraldry
object
environment
...
```

------------------------------------------------------------------------

# 8. Destinos y routing

La raíz de producción autorizada procederá de `UniverseContext.Storage.ProductionRoot`.
Las reglas de routing vienen del profile/config y quedan conservadas en
`ValidatedAssetPackage.AssetRule.Routing`; ninguna metadata de asset puede
seleccionar una raíz de otro universo. 3.6 implementa resolución pura del
directorio canónico, tras la frontera semántica de 2.8.2:

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

El resolver usa exclusivamente la regla retenida en el package, sin recargar
perfiles ni consultar snapshots. Resuelve Literal/Classification/AssetId en
orden, sin inferencia ni cambios de casing. Exige universo coincidente por
valor, Naming v1 defensivo, rechazo de dispositivos Windows reservados en todas
las plataformas y contención léxica estricta bajo la raíz validada. La ruta
relativa usa `/`; la absoluta usa Path.Combine/Path.GetFullPath native.
No comprueba existencia/colisiones ni crea carpetas o escribe producción.
Resolver un destino no autoriza escritura. 4.1 — ProcessingPlan conserva las
fronteras validadas y el destino recibido como base inmutable, sin re-routing.
4.2 — Dry Run renderiza esos hechos de forma humana y determinista.
4.3 — Plan Validation contrasta el destino con un snapshot materializado,
sin I/O ni source revalidation. El report limpio es point-in-time y no autoriza
escritura. 4.4 — Logs HECHO: resumen textual privacy-safe. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.
Véanse [DESTINATION_RESOLVER.md](DESTINATION_RESOLVER.md) y
[PROCESSING_PLAN.md](PROCESSING_PLAN.md).
Los ejemplos siguientes corresponden a Nimroel.

Uno de los problemas importantes es determinar automáticamente dónde va
cada asset.

Ejemplos:

``` text
Norgard
└── Treskal
    └── farmer
        └── male
```

pero posteriormente existirán:

-   Hallheim;
-   Darovan;
-   otras localizaciones;
-   Belendril;
-   elfos;
-   otras culturas/reinos;
-   nuevas profesiones;
-   nuevas categorías.

NAP no debe tener toda la estructura codificada rígidamente para
siempre.

Debe:

1.  conocer la configuración de routing;
2.  leer la estructura real del repositorio local;
3.  detectar cambios;
4.  detectar nuevas carpetas;
5.  distinguir cambios normales de cambios estructurales;
6.  avisar cuando una modificación pueda afectar a las reglas.

Política canónica real de Nimroel para `portrait + portrait_npc`, relativa a
`ProductionRoot`:

``` text
portrait
→ portraits/{culture}/{location}/{role}/{sex}/{asset_id}
```

En el perfil se representa como Literal `portraits`, Classification `culture`,
`location`, `role`, `sex` y AssetId, en ese orden. `realm` y `region` son
opcionales y no forman parte de la ruta. Esta política no se generaliza a otros
universos, asset types o production profiles.

------------------------------------------------------------------------

# 9. TeraBox

La conservación y los backups de assets se resolverán por universo mediante
`UniverseContext.Storage.ArchiveRoot`. Las raíces se configuran ya resueltas;
el Core no hardcodea rutas de Nimroel. Cada universo deberá tener storage
separado. La integración TeraBox todavía no está implementada.
En 2.6.2, `UniverseStorageIsolationValidator` comprueba sin I/O todas las
combinaciones Workspace/Production/Archive entre universos distintos:
igualdad o contención produce `universe_storage_overlap`, Error + Stop.
La comparación léxica respeta boundaries de segmentos y casing de plataforma.
No resuelve symlinks/junctions ni introduce política para roots del mismo universo.
Configuraciones duplicadas del mismo UniverseId son errores de argumentos.

TeraBox está instalado en Windows y se comporta como una carpeta/unidad
accesible localmente.

Se utilizará principalmente para **conservación**.

Actualmente existen dos conceptos distintos:

1.  copia del repositorio;
2.  archivo de maestros PNG y sus datos.

Fase 8 implementa la incorporación incremental del paquete original validado
en ArchiveRoot, tratado como carpeta local. La copia del repositorio y los
backups generales siguen reservados a fases posteriores. NAP no usa API,
credenciales ni UI de TeraBox y nunca crea una ArchiveRoot ausente.

Si hoy se archivan 5 assets y mañana aparecen 2 nuevos:

``` text
NO volver a copiar 7
SÍ detectar y copiar sólo los 2 nuevos
```

El archivo v1 utiliza:

-   Asset ID;
-   hashes;
-   índice;
-   master_index.json v1 (SQLite sigue reservado a Fase 10);
-   verificación origen/destino.

Nunca sobrescribir silenciosamente un maestro con otro distinto.

------------------------------------------------------------------------

# 10. Índice de maestros

Fase 8 implementa el registro operativo estricto y universe-scoped en
`ArchiveRoot/_nap/master_index.json`. No sustituye el futuro catálogo SQLite.
Sus entradas verificadas conservan exactamente:

-   Asset ID;
-   hash SHA-256;
-   ruta relativa canónica con `/`;
-   tamaño;
-   tipo;
-   estado de verificación.

Schema 1 literal, universe_id del contexto, IDs únicos, entradas por AssetId
Ordinal, SHA canónico, tamaño positivo y verified=true. No guarda rutas
absolutas, secretos, JobId o timestamp obligatorio. Publicación atómica del
índice tras verificar todos los archivos, bajo archive.lock. Véase
[ARCHIVE_STORAGE.md](ARCHIVE_STORAGE.md).

La copia a TeraBox debe terminar con:

``` text
hash origen == hash destino
```

------------------------------------------------------------------------

# 11. SQLite --- requisito fundamental

**SQLite es una función obligatoria de NAP. No es opcional.**

Arquitectura futura: **un catálogo SQLite por universo**, dentro de su
workspace local. Ruta derivada de `UniverseStorageConfig.CatalogPath`:

``` text
<WorkspaceRoot>/state/AssetCatalog.db
```

La DB activa debe residir en disco local normal, no directamente dentro
de una carpeta sincronizada por TeraBox/Dropbox.
El catálogo se reconstruirá en el contexto de ese universo. SQLite aún
no está implementado; el nombre genérico sustituye la convención anterior.

Objetivos de SQLite:

-   gestionar miles de assets;
-   búsquedas instantáneas;
-   filtros;
-   estadísticas;
-   diversidad;
-   objetivos;
-   cobertura;
-   historial;
-   soporte para RoDo;
-   soporte futuro para Android.

------------------------------------------------------------------------

# 12. Información prevista en SQLite

La identidad completa de un asset es `UniverseAssetKey(UniverseId, AssetId)`.
Un mismo AssetId puede existir en universos distintos. La clasificación y
los vocabularios siguientes son ejemplos del perfil Nimroel; los schemas
y vocabularios configurables por universo se definirán posteriormente.

El esquema definitivo se diseñará durante el desarrollo, pero
conceptualmente debe representar:

## Assets

-   Asset ID;
-   categoría;
-   cultura;
-   reino/región;
-   lugar;
-   profesión/rol;
-   sexo;
-   edad aparente;
-   etapa vital;
-   banda de edad;
-   complexión;
-   rasgos visuales;
-   estado;
-   fecha;
-   hash maestro;
-   hash producción;
-   ruta maestro;
-   ruta producción;
-   prompt;
-   manifest;
-   identidad visual;
-   estado auditoría.

## Rasgos visuales

Especialmente para portraits:

-   ojos;
-   pelo;
-   barba;
-   calvicie;
-   canas;
-   complexión;
-   edad;
-   etc.

Los vocabularios deben estar **normalizados** para que SQLite no termine
con sinónimos incompatibles.

------------------------------------------------------------------------

# 13. Reconstrucción de SQLite

Debe existir una función que permita reconstruir la DB desde los assets.

Si:

``` text
<WorkspaceRoot>/state/AssetCatalog.db
```

se pierde o corrompe, NAP debe poder recorrer:

``` text
manifest.json
visual_identity.json
otros metadatos
```

y regenerar el catálogo.

Esto debe probarse deliberadamente durante el desarrollo.

------------------------------------------------------------------------

# 14. Backups de SQLite

Además de poder reconstruirla, se quieren backups históricos.

Arquitectura:

``` text
WorkspaceRoot del universo
└── state/
    └── AssetCatalog.db
             ↓
        snapshot seguro
             ↓
        verificación
             ↓
          ArchiveRoot del universo (por ejemplo TeraBox)
```

Destino conceptual:

``` text
ArchiveRoot del universo/
└── NAP_DATABASE_BACKUPS/
    ├── AssetCatalog_YYYY-MM-DD_HHMM.db
    └── ...
```

Opciones futuras:

-   diario;
-   semanal;
-   mensual;
-   personalizado;
-   política de retención;
-   backup extraordinario antes de operaciones grandes.

TeraBox es el destino principal elegido para los históricos de la DB.

------------------------------------------------------------------------

# 15. Catálogo visual

NAP no será sólo pipeline.

Debe tener un **explorador visual de assets**.

Ejemplo:

``` text
CATÁLOGO

Tipo
Cultura
Lugar
Profesión
Sexo
Edad
Pelo
Ojos
Barba
...
```

Resultado:

``` text
[miniatura] Farmer 001
[miniatura] Farmer 004
[miniatura] Farmer 008
...
```

Al pulsar un asset:

-   ficha completa;
-   miniatura;
-   metadatos;
-   rutas;
-   hashes;
-   prompt;
-   estado;
-   maestro;
-   producción;
-   auditorías.

------------------------------------------------------------------------

# 16. Miniaturas

No cargar miles de PNG maestros.

NAP debe generar/usar thumbnails ligeros y caché.

La UI sólo debe cargar los elementos visibles durante scroll.

Esto debe permitir que el catálogo siga siendo fluido incluso con miles
o decenas de miles de registros.

------------------------------------------------------------------------

# 17. Estadísticas y diversidad

SQLite permitirá responder preguntas como:

``` text
¿Cuántos farmer male de Hallheim hay?
¿Cuántas campesinas de Treskal de 20–30 años?
¿Cuántos NPC tienen barba?
¿Cuántos son calvos?
¿Cuántos tienen pelo claro?
¿Cómo se distribuyen las edades?
```

Pero también permitirá detectar **falsa diversidad**.

Ejemplo:

``` text
Farmer male Treskal = 10/10
```

puede parecer completo, pero:

``` text
7 tienen 30–40 años
8 tienen barba
7 son robustos
8 tienen pelo oscuro
```

NAP/RoDo debe poder señalar que el objetivo numérico está cumplido pero
la diversidad visual es pobre.

------------------------------------------------------------------------

# 18. Objetivos de producción

Función obligatoria del catálogo.

Ejemplo:

``` text
Hallheim
farmer
male
objetivo = 10
```

NAP calcula:

``` text
3 / 10
```

El contador **no se incrementa manualmente**.

SQLite consulta cuántos assets válidos cumplen los criterios.

Cuando llega a:

``` text
10 / 10
```

se marca:

``` text
OBJETIVO CUMPLIDO
```

------------------------------------------------------------------------

# 19. Objetivos complejos

Los objetivos podrán usar filtros:

``` text
Treskal
farmer
female
20–30 años
objetivo = 8
```

También podrán existir para:

-   scenes;
-   profesiones;
-   culturas;
-   edades;
-   sexo;
-   otros atributos.

Un mismo asset puede contribuir a varios objetivos simultáneamente.

------------------------------------------------------------------------

# 20. Campañas de objetivos

Agrupar objetivos en campañas.

Ejemplo:

``` text
CAMPAÑA:
Población base de Treskal

Farmer male        10
Farmer female      10
Fisher male         8
Fisher female       8
Blacksmith male     6
Blacksmith female   6
...
```

Progreso:

``` text
64 / 100
64 %
```

Al completarse:

``` text
CAMPAÑA COMPLETADA
```

------------------------------------------------------------------------

# 21. Concepto de cobertura

NAP debe distinguir:

``` text
Explorar
→ ¿qué tenemos?

Estadísticas
→ ¿cómo está distribuido?

Objetivos
→ ¿cuánto nos falta?

Diversidad/Cobertura
→ ¿lo que tenemos está equilibrado?
```

------------------------------------------------------------------------

# 22. RoDo

**RoDo debe formar parte oficial de NAP.**

RoDo es el asistente transversal de las aplicaciones del usuario.

En NAP no será sólo decoración.

Funciones previstas:

-   consultas naturales del catálogo;
-   explicación de estados;
-   resumen de cobertura;
-   ayuda con errores;
-   planificación de sesiones;
-   avisos;
-   análisis de evolución;
-   interpretación de estadísticas.

Ejemplos:

``` text
RoDo, ¿cuántas campesinas de Treskal entre 20 y 30 años tengo?
```

o:

``` text
RoDo, ¿qué objetivos están más retrasados?
```

------------------------------------------------------------------------

# 23. RoDo proactivo

Se desea que RoDo pueda avisar **sin que el usuario tenga que
preguntarle**, cuando exista algo relevante.

Ejemplos:

-   objetivo completado;
-   cobertura muy baja;
-   exceso de una profesión;
-   desequilibrio entre variantes;
-   varias sesiones creando casi lo mismo;
-   error repetitivo;
-   maestros sin verificar;
-   objetivos estancados;
-   diversidad insuficiente.

Ejemplo:

> Llevas 14 nuevos campesinos de Treskal en las últimas sesiones y sólo
> 2 herreros. Los objetivos de campesinos están prácticamente completos;
> herrería sigue muy por debajo.

La IA no debe contar manualmente.

**NAP/SQLite calcula los hechos. RoDo los interpreta.**

------------------------------------------------------------------------

# 24. IA ligera para supervisión cotidiana

Idea prevista:

Cada cierto número de assets o al terminar una sesión, NAP genera un
resumen pequeño:

``` text
Sesión:
8 assets nuevos

Treskal:
+ farmer male: 3
+ farmer female: 2
+ fisher female: 3

Objetivos:
farmer male: 8/10 → 10/10
fisher female: 2/10 → 5/10

Errores: 0
Duplicados: 0
```

Ese resumen se manda al modelo ligero configurado para RoDo.

Normalmente no debe molestar.

Sólo avisar si encuentra algo útil.

------------------------------------------------------------------------

# 25. Auditoría general periódica con IA potente

Además del seguimiento ligero se desea una **gran auditoría
periódica/manual**.

NAP preparará un informe estructurado con:

-   catálogo completo/resumen;
-   distribución;
-   culturas;
-   lugares;
-   profesiones;
-   sexos;
-   edades;
-   atributos;
-   objetivos;
-   cobertura;
-   últimas sesiones;
-   integridad;
-   maestros;
-   hashes;
-   errores;
-   evolución.

Ese paquete podrá enviarse a un modelo más potente disponible en ese
momento.

No fijar de forma rígida nombres/versiones de modelos futuros.

La arquitectura debe permitir cambiar proveedor/modelo mediante
configuración.

------------------------------------------------------------------------

# 26. Gemini auditor de operaciones

Separar responsabilidades:

``` text
Gemini Auditor
→ ¿es segura/coherente ESTA operación?

RoDo
→ ¿cómo evoluciona la producción?

Auditoría General
→ ¿cómo está el catálogo global?
```

Aunque técnicamente puedan usar el mismo proveedor, conceptualmente son
módulos diferentes.

**Fase 7 — Auditor IA HECHA (7.1–7.5):** NAP.Core define contratos independientes
del proveedor y NAP.AI implementa GeminiOperationAuditClient con Interactions API
estable v1, key/model externos y structured output. El request contiene solo
hechos del plan, roles, destino relativo y code/severity/disposition de NAP;
nunca paths absolutos o contenido privado de issues. STOP determinista impide
llamar a IA. Parser local estricto e invariantes validan PASS/WARNING/FAIL:
PASS no ejecuta, WARNING requiere revisión y FAIL/fallo de cliente bloquean.
Sin tools, conversación, store, filesystem writes o transición JobState.
ProcessingPlan v1 aún no define operaciones; se auditan únicamente sus hechos
actuales. Pruebas Dry Run con validadores reales y HTTP fake, sin red real.
Véase [AI_AUDIT.md](AI_AUDIT.md). Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6). Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4). Siguiente: Fase 10 — SQLite.

------------------------------------------------------------------------

# 27. La IA no debe controlar directamente los archivos

No dar acceso irrestricto a TeraBox/filesystem sólo porque sea cómodo.

Preferencia:

``` text
TeraBox ─┐
Repo ────┤
SQLite ──┤
         ↓
        NAP
         ↓
 informe estructurado
         ↓
       IA
```

NAP mantiene autoridad sobre operaciones.

La IA:

-   analiza;
-   recomienda;
-   aprueba/rechaza según contrato;
-   no borra;
-   no mueve;
-   no hace Git directamente dentro del flujo crítico.

------------------------------------------------------------------------

# 28. ProcessingPlan / Dry Run

El PLAN final contemplará producción y archive dentro de las raíces de un
`UniverseContext` explícito. El ejemplo siguiente conserva el objetivo futuro
completo, cuando existan los contratos de conversión/archive y sus outputs.
Usa el primer perfil Nimroel y su contexto histórico; no describe capacidades
ya implementadas ni sustituye la política canónica actual.

Antes de habilitar ejecución, NAP deberá construir ese plan completo.

Ejemplo:

``` text
Asset:
portrait_treskal_farmer_male_040

MASTER:
Copiar PNG a:
TeraBox\...

PRODUCTION:
PNG → WebP
768x960
Q90

DESTINATION:
...\portraits\Norgard\Treskal\farmer\male\...

FILES:
WebP
prompt.md
info.md
manifest.json
visual_identity.json

No se ha modificado ningún archivo.
```

Este modo debe desarrollarse y probarse antes de habilitar escritura
real.

**Estado real de 4.1 — ProcessingPlan HECHO:** v1 congela identidad completa,
asset type, production profile, classification validada completa, package root,
manifest path, archivos presentes por role y ProductionAssetDestination recibido.
ProcessingPlanBuilder combina package/repository/destination coherentes; el
resolver de 3.6 permanece separado. Los mappings se copian con identidad Ordinal
y se exponen read-only, con manifest separado. Sin I/O, re-routing, timestamps,
JobId ni autorización de escritura.

ProcessingPlan v1 es completo respecto a las fronteras implementadas y todavía
no es un execution graph completo. No inventa archive destination, conversión,
final production output, hashes ni execution steps. Las operaciones se añadirán
cuando existan sus contratos. Fase 4 HECHA dentro del alcance v1; 4.1–4.4 HECHOS.
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Véase
[PROCESSING_PLAN.md](PROCESSING_PLAN.md).

**Estado real de 4.2 — Dry Run HECHO:** DryRunTextRenderer.Render(ProcessingPlan)
devuelve texto humano determinista, ordenando classification/roles con Ordinal,
usando LF fijo y escaping de paths en una sola línea. Muestra exclusivamente
los hechos congelados y el destino ya calculado; OPERATIONS declara expresamente
`(not defined in ProcessingPlan v1)`. No revalida, simula filesystem ni promete
archive/conversion/output/hash. Renderizar no ejecuta ni autoriza operaciones.
4.3 valida estructuralmente el destino contra un snapshot; el objetivo futuro completo crecerá con sus
contratos. Véase [DRY_RUN.md](DRY_RUN.md).

**Estado real de 4.3 — Plan Validation HECHO:** ProcessingPlanValidator valida
estructuralmente ProductionDestination contra un ProductionRepositorySnapshot
materializado, con cero filesystem I/O. Permite directorios ausentes y ancestros
canónicos exactos; el primer File blocker, casing histórico incompatible en
Windows o Directory final existente causa Error + Stop, sin adaptación.
Un destino existente sigue causando STOP en Plan Validation. 6.2 ya distingue
contenido e idempotencia semántica por pares, sin integrarse en ese validator ni
autorizar reutilización o sobrescritura. No revalida sources ni lee contents. Un report limpio es
point-in-time y no autoriza escritura; la frontera de Production Storage debe
garantizar de nuevo las condiciones físicas. 4.4 — Logs HECHO: resumen textual privacy-safe. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Véase
[PLAN_VALIDATION.md](PLAN_VALIDATION.md).

**Estado real de 4.4 — Logs HECHO:** PlanLogTextRenderer proyecta ProcessingPlan
y NapIssueReport a string determinista privacy-safe: identidad, destino relativo,
flags y code/severity/disposition, sin I/O ni persistencia. No duplica el Dry Run
ni revalida. Fase 4 HECHA dentro del alcance v1; el plan crecerá cuando existan
los contratos de operaciones, archive, conversión y hashes. Un report limpio
no autoriza escritura. Véase [PLAN_LOGS.md](PLAN_LOGS.md).

------------------------------------------------------------------------

# 29. Hashes y duplicados

**6.1 — SHA-256 HECHO:** Sha256Digest representa 32 bytes / 256 bits mediante
64 hex lowercase ASCII estrictos, sin normalización, con igualdad por valor.
Sha256Hasher.Compute(path) abre read-only (Open/Read/Share Read) y dispone su
FileStream interno; Compute(Stream) consume desde la posición actual hasta EOF
y deja abierto el stream del caller, sin seek/restauración o dependencia de
Length/Position. SHA256.HashData(Stream) de la BCL procesa con memoria acotada,
sin cargar todo el input. Errores operativos se propagan, sin NapIssue.
El hash identifica únicamente bytes, independiente de paths, nombres, Asset ID,
universo o metadata. CRC ZIP y sus contratos permanecen intactos: CRC es
integridad de transporte ZIP y SHA-256 identidad criptográfica de contenido.
Véase [SHA256_INTEGRITY.md](SHA256_INTEGRITY.md).

**6.2 — Duplicados HECHO:** AssetContentFingerprint asocia UniverseAssetKey
y Sha256Digest; AssetDuplicateAnalyzer compara candidate/existing pairwise,
con cero I/O, hashing interno o persistencia. Dentro del mismo universo:

| UniverseAssetKey | Sha256Digest | Relation | Report |
| --- | --- | --- | --- |
| Igual | Igual | SameAssetSameContent | Semánticamente idempotente; vacío, clean, Continue |
| Igual | Distinto | SameAssetDifferentContent | COLISIÓN; asset_content_collision, Error + Stop |
| Distinto | Igual | DifferentAssetSameContent | Posible duplicado; asset_content_possible_duplicate, Warning + Continue |
| Distinto | Distinto | Distinct | Vacío, clean, Continue |

Cross-universe es una frontera inválida: ArgumentException, ParamName existing,
texto `The candidate and existing fingerprints must belong to the same universe.`
No se clasifica ninguna relación entre universos. AssetDuplicateAnalysis es
inmutable, constructor internal con invariantes de relación/fingerprints/report;
sus tres flags derivan solo de Relation. Los issues tienen SubjectPath null y
Details canónicos/direccionales independientes de cultura. Véase
[DUPLICATE_DETECTION.md](DUPLICATE_DETECTION.md) para APIs y diagnósticos exactos.

SameAssetSameContent solo demuestra el mismo asset y bytes frente al fingerprint
conocido. No prueba un Job previo/COMPLETED, una copia en TeraBox, un WebP en
producción o una actualización SQLite; no autoriza saltar procesos o escribir.
La colisión solo STOP, sin elegir contenido; el posible duplicado solo advierte,
sin borrado, merge, reutilización, rename o rechazo automático.

`hash origen == hash destino` sigue siendo requisito futuro de verificación
después de copiar/escribir. 6.1 no implementa copias ni esa verificación.
No guarda hashes en ValidatedAssetPackage, ProcessingPlan, WebP images, JSON,
manifest/profile, logs, sidecars o SQLite. 6.2 implementa solo semántica pura,
sin catálogo, colección global, Job ID, estados o recovery. Plan Validation no usa
hashes y Plan logs no los muestra; Fase 4 conserva su alcance.
La identidad completa es `UniverseAssetKey`; coincidir solo en AssetId entre
universos no permite comparar. Una futura capa de catálogo podrá agregar
análisis por pares de fingerprints conocidos, sin anticipar persistencia en 6.2.

------------------------------------------------------------------------

# 30. Jobs y estados persistentes

Cada proceso debe tener un Job ID.

**6.3 — Job ID HECHO:** JobId public sealed record ya formaliza la identidad
global, opaca y estable de proceso: `job_` + 32 lowercase ASCII hex, sin all-zero
o normalización. Value get-only, ToString exacto, Create con Guid.NewGuid() y
rehidratación mediante constructor, con igualdad por valor. No está scoped por
universo ni contiene asset, estado o timestamp. Véase [JOB_ID.md](JOB_ID.md).
UniverseAssetKey conserva UniverseId + AssetId; UniverseContext sigue explícito.

JobId por sí solo no crea modelo Job, SQLite o ejecución. Desde 6.4 existe
un journal mínimo de estados, sin ProcessingJob/orquestador. ProcessingPlan y
renderers siguen sin JobId/JobState.
**6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.**

**6.4 — Estados persistentes HECHO:** JobState fija nueve valores explícitos
0..8 y estos tokens persistentes uppercase, sin enum numérico en JSON:

``` text
DETECTED
STAGED
VALIDATED
PLANNED
AUDITED
EXECUTED
VERIFIED
COMPLETED
FAILED
```

La progresión normal sigue exactamente el orden DETECTED → STAGED → VALIDATED
→ PLANNED → AUDITED → EXECUTED → VERIFIED → COMPLETED, sin saltos, retrocesos
o same-state. Cualquier estado activo DETECTED..VERIFIED admite FAILED;
COMPLETED y FAILED son terminales, sin resurrección de FAILED en 6.4.

JobStateMachine es puro: CanTransition(from, to), Create(jobId, universeId) en
Detected y Transition(current, nextState) sin mutar, conservando referencias de
identidad/universo. JobStateRecord sealed record contiene solo JobId, UniverseId
y State get-only. Enum inválido lanza ArgumentOutOfRangeException; transición
prohibida, InvalidOperationException con tokens uppercase.

JobStateStore(context) exige UniverseContext y ofrece solo Create(jobId),
Load(jobId), Transition(jobId, nextState). Persiste el último estado únicamente
en StateRoot/<job_id>.json, sin usar AssetId o paths extraídos del contenido.
JSON v1: schema_version (entero 1), job_id, universe_id, state, en ese orden,
UTF-8 sin BOM y LF final. Load read-only rechaza corrupción, extras/ausentes/
duplicados, tipos/version/tokens inválidos y mismatch de JobId/UniverseId mediante
InvalidDataException; final ausente produce FileNotFoundException.
Create rechaza final existente sin overwrite. Transition consulta el machine
antes de escribir; un paso inválido conserva el final intacto. Publicación mediante
temp hermano exclusivo, bytes completos, Flush(true), cierre y File.Move en el
mismo directorio; no se borra el final antes de publicar. Orphans no se limpian.

Esto no habilita escrituras Inbox/Staging/Cache/Production/Archive, maestros,
WebP o TeraBox. No añade timestamps/AssetId, SQLite, schemas, NapIssueCodes,
modelo Job amplio, orquestación o cambios a PLAN/renderers. Persistir estado no
implementa recovery. Véase [JOB_STATES.md](JOB_STATES.md) para APIs, formato exacto
y límites de concurrencia/durabilidad.

El requisito histórico de recuperación contempla:

-   apagado;
-   cierre;
-   excepción;
-   bloqueo;
-   pérdida temporal de TeraBox;
-   operación incompleta.

**6.5 — Recuperación tras fallo HECHO:** JobRecoveryScanner(context).Scan()
reconstruye conocimiento durable exclusivamente desde los journals finales de
StateRoot, reutilizando Load y sin avanzar JobState. JobRecoverySnapshot separa
activos con su checkpoint exacto, COMPLETED y FAILED; conserva temps sin abrir,
promover o borrar. Journals inválidos/desaparecidos, temps sin final y reparse
points causan STOP; temp junto a final genera Warning + Continue. No se infiere
la causa histórica del cierre o sus efectos físicos. Fase 8 ya conserva el
paquete maestro en ArchiveRoot con recuperación incremental propia; no consume
ni avanza journals. Fase 9 añade reanudación idempotente en AssetExecutionCoordinator.
Snapshot point-in-time, sin lease/lock ni autorización de ejecución. Véase
[JOB_RECOVERY.md](JOB_RECOVERY.md). Fase 6 HECHA; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

------------------------------------------------------------------------

# 31. Logs

Logs útiles pero no invasivos. La futura UI debe mostrar información humana.

**Implementado en 4.4:** PlanLogTextRenderer produce el resumen textual seguro
del planning actual: identidad machine-safe, destino relativo, estado del
NapIssueReport y code/severity/disposition de cada issue en orden. Privacidad
por omisión: no incluye rutas absolutas/fuente, Message, SubjectPath, Detail,
secretos ni diagnósticos de excepciones. Sin persistencia, timestamps ni JobId.
Véase [PLAN_LOGS.md](PLAN_LOGS.md). Un resultado limpio no autoriza ejecución.

Los detalles técnicos futuros podrán incluir rutas, hashes, tiempos,
exception diagnostics, resultados IA, archivos y operaciones; también se
prevén persistencia e historial. Solo se incorporarán cuando existan los
contratos correspondientes y una política explícita de privacidad.
No están implementados en el payload v1. Los logs no deben exponer secretos.

------------------------------------------------------------------------

# 32. UI --- RobStyle UI

La identidad visual oficial se llamará:

# **RobStyle UI**

NAP debe sentirse perteneciente a la misma familia visual que RobGit.

Características:

-   fondo azul petróleo/cerúleo;
-   blanco hielo;
-   paneles/tarjetas;
-   profundidad 3D sutil;
-   diseño limpio;
-   estados claros;
-   información técnica escondida salvo que se solicite;
-   sensación profesional;
-   interfaz agradable y bonita, no sólo funcional.

------------------------------------------------------------------------

# 33. NAP no será una ventanita pequeña

NAP se diseñará para aprovechar el MSI.

Debe:

-   arrancar maximizado o ser naturalmente usable a pantalla completa;
-   aprovechar grandes resoluciones;
-   mostrar catálogo y miniaturas cómodamente;
-   utilizar espacio disponible;
-   ser visualmente atractiva.

El hecho de que la lógica sea sencilla en algunos módulos **no justifica
una UI pequeña o pobre**.

------------------------------------------------------------------------

# 34. Tecnología UI

Candidata principal:

``` text
WPF + MVVM
```

Objetivo:

-   separar UI del motor;
-   componentes RobStyle reutilizables;
-   aplicación Windows profesional;
-   async para no congelar interfaz;
-   virtualización;
-   cachés;
-   rendimiento.

La decisión se confirmará técnicamente durante el desarrollo.

------------------------------------------------------------------------

# 35. Shell visual temprano

Aunque el núcleo se desarrollará primero, no esperar hasta el final para
ver NAP como aplicación.

Una vez estabilizada la base, crear un shell RobStyle con:

``` text
Inicio
Pipeline
Catálogo
Objetivos
Estadísticas
Backups
Historial
RoDo
Ajustes
```

Las secciones se irán llenando progresivamente.

------------------------------------------------------------------------

# 36. Dashboard

Ideas:

-   assets totales;
-   maestros verificados;
-   assets producción;
-   pendientes Inbox;
-   errores;
-   objetivos activos;
-   último backup;
-   estado repo;
-   estado TeraBox;
-   estado IA;
-   actividad reciente;
-   cobertura.

No llenar de métricas inútiles sólo porque hay espacio.

------------------------------------------------------------------------

# 37. Pantalla Pipeline

Mostrar claramente el proceso:

``` text
✓ Paquete recibido
✓ Validación
✓ Hash maestro
✓ Destino calculado
● Auditoría IA
○ Archivo TeraBox
○ Conversión WebP
○ Producción
○ Catálogo
```

Botones peligrosos bloqueados mientras corresponda.

------------------------------------------------------------------------

# 38. Pantalla Catálogo

Filtros amplios + cuadrícula visual.

Filtros potenciales:

-   tipo;
-   cultura;
-   lugar;
-   profesión;
-   sexo;
-   edad;
-   pelo;
-   ojos;
-   barba;
-   complexión;
-   estado;
-   objetivo/campaña.

------------------------------------------------------------------------

# 39. Pantalla Objetivos

Debe ser visual:

``` text
Farmer male       10/10 ✓
Farmer female      8/10
Fisher male        7/10
Fisher female      2/10
```

Al pulsar un contador se muestran los assets que lo componen.

------------------------------------------------------------------------

# 40. Pantalla Estadísticas

Distribuciones útiles:

-   por cultura;
-   lugar;
-   profesión;
-   sexo;
-   edad;
-   rasgos;
-   evolución temporal;
-   cobertura;
-   diversidad.

------------------------------------------------------------------------

# 41. RoDo dentro de la UI

RoDo debe tener presencia integrada en RobStyle UI.

No debe llenar la interfaz de mascotas repetidas.

Debe poder:

-   mostrar mensajes contextuales;
-   abrir panel conversacional;
-   explicar errores;
-   consultar catálogo;
-   señalar cobertura;
-   preparar sesiones.

Tono cercano, pero serio ante riesgos.

------------------------------------------------------------------------

# 42. NAP Companion --- Android

Idea futura muy importante.

Se desea poder consultar el catálogo desde Android cuando el usuario no
está en el MSI, por ejemplo en el gimnasio.

NO hacer que Android abra/escriba directamente la SQLite activa del PC.

Arquitectura preferida:

``` text
NAP Windows
   ↓
SQLite maestra
   ↓
exportación/snapshot móvil
   ↓
sincronización
   ↓
NAP Companion Android
```

------------------------------------------------------------------------

# 43. Funciones del Companion

Inicialmente principalmente lectura:

-   buscar assets;
-   filtros;
-   miniaturas;
-   estadísticas;
-   objetivos;
-   cobertura;
-   planificación de sesiones;
-   posiblemente RoDo.

Ejemplo:

``` text
Treskal
Fisher
Female
20–30

→ 6 assets
```

------------------------------------------------------------------------

# 44. Planificación de sesiones desde Android

Idea:

``` text
PRÓXIMA SESIÓN

Treskal · Fisher · Female ×4
Treskal · Blacksmith · Female ×3
Hallheim · Farmer · Elderly Male ×2
```

Más adelante esa planificación podría sincronizarse con NAP Windows.

------------------------------------------------------------------------

# 45. Caché móvil

No sincronizar PNG maestros.

Generar thumbnails pequeños, por ejemplo WebP optimizados, junto con
catálogo móvil.

Concepto:

``` text
mobile-cache/
├── thumbnails/
└── catalog.json
```

o una SQLite secundaria de sólo lectura.

La implementación exacta se decidirá más adelante.

------------------------------------------------------------------------

# 46. Git del propio NAP

Repositorio:

``` text
robdor80/NAP
```

Público.

Rama principal:

``` text
main
```

`main` debe mantenerse estable.

Estrategia:

-   commits pequeños;
-   push frecuente;
-   ramas para tareas grandes, experimentales o arriesgadas;
-   tags para versiones;
-   GitHub Releases para versiones publicadas.

El usuario trabaja normalmente con GitHub Desktop y agradece
instrucciones explícitas de cuándo hacer commit/push/rama y el Summary
del commit.

------------------------------------------------------------------------

# 47. Seguridad del repo público

Nunca subir:

-   API keys;
-   tokens;
-   secretos;
-   credenciales;
-   rutas privadas innecesarias;
-   configuraciones personales sensibles.

Usar:

-   variables de entorno;
-   User Secrets;
-   configuración local ignorada;
-   archivos `.example`.

------------------------------------------------------------------------

# 48. Estructura inicial prevista

``` text
NAP/
├── src/
│   ├── NAP.Core/
│   └── NAP.Cli/
├── tests/
│   └── NAP.Tests/
├── docs/
├── installer/
├── test-data/
├── README.md
├── CHANGELOG.md
└── .gitignore
```

Posteriormente se añadirá la aplicación WPF.

------------------------------------------------------------------------

# 49. Codex

Codex será el programador principal.

Forma de trabajo:

``` text
Usuario + ChatGPT
→ dirección / arquitectura / revisión

Codex
→ implementación / tests / refactor
```

No pedir:

> crea NAP entero.

Dar tareas pequeñas y verificables.

Cada capítulo debe:

1.  implementarse;
2.  compilar;
3.  probarse;
4.  revisarse;
5.  commit;
6.  push;
7.  pasar al siguiente.

------------------------------------------------------------------------

# 50. Prioridad respecto al videojuego

NAP consumirá cuota/tiempo de Codex que también se necesita para el RPG.

Por ello NO se pretende terminar todo NAP antes de continuar el juego.

Prioridad inicial:

## NAP mínimo realmente útil

``` text
ZIP
↓
validación
↓
plan
↓
auditoría
↓
PNG → TeraBox
↓
WebP → repo
↓
SQLite
```

más una UI Windows sencilla pero útil.

Una vez logrado:

-   bajar prioridad de NAP;
-   volver a concentrar Codex en el RPG;
-   continuar generando assets en paralelo;
-   evolucionar NAP progresivamente.

------------------------------------------------------------------------

# 51. Objetivo estratégico

Mientras Codex desarrolla el videojuego en el MSI:

``` text
ChatGPT
→ puede seguir creando assets

NAP
→ puede gestionarlos automáticamente
```

De esta forma, cuando el videojuego llegue a fases donde necesite
grandes cantidades de contenido visual, ya existirá una biblioteca
grande y correctamente organizada.

------------------------------------------------------------------------

# 52. Instalación profesional

NAP debe terminar siendo una aplicación instalable como producto real.

Objetivo:

``` text
NAP-Setup-1.0.0.exe
```

Candidato inicial para instalador:

``` text
Inno Setup
```

La aplicación se publicará Windows x64 autocontenida cuando corresponda.

Debe incluir:

-   icono;
-   versión;
-   menú Inicio;
-   acceso directo opcional;
-   desinstalador;
-   información de versión.

------------------------------------------------------------------------

# 53. GitHub Releases

No meter todos los instaladores dentro del historial normal Git.

Usar GitHub Releases:

``` text
NAP v1.0.0
├── NAP-Setup-1.0.0.exe
├── SHA256SUMS.txt
└── Release Notes
```

------------------------------------------------------------------------

# 54. Backup de releases

Además de GitHub Releases:

``` text
TeraBox/
└── NAP/
    └── RELEASES/
        └── 1.0.0/
            ├── NAP-Setup-1.0.0.exe
            └── SHA256SUMS.txt
```

Si se formatea el MSI:

1.  descargar última release;
2.  instalar NAP;
3.  restaurar/configurar rutas;
4.  reconstruir/recuperar estado si fuese necesario.

------------------------------------------------------------------------

## Estado actual del desarrollo

La Fase 0.1 (repositorio), 0.2 (solución .NET 8), 0.3 (primer ejecutable), 0.4 (tests) y 0.5 (estrategia Git + CI) están presentes en el repositorio actual. **Fase 1 (1.1–1.6) completa:** Inbox → Detection → Readiness → Staging → Safe Extraction, probada de extremo a extremo con un paquete legítimo. **2.1 — Manifest v1 formalizado:** contrato, JSON Schema y documentación. **2.2 — Modelo C# implementado:** AssetManifestV1 con serialización System.Text.Json y classification extensible, sin normalización ni validación semántica. **2.3 — Naming v1 implementado:** reglas de forma, coherencia de prefijo y nombres canónicos. **2.4 — Validación PNG estructural implementada:** firma, IHDR, orden esencial, CRC de chunks y proporción exacta, sin decodificación de píxeles ni resolución de perfiles. **2.5 — Errores controlados implementados:** NapIssue con códigos estables, Severity y Disposition independientes, adaptadores de resultados locales y report inmutable; sin PackageValidator. Véase [CONTROLLED_ISSUES.md](CONTROLLED_ISSUES.md). **2.6.1 — Core Universe Scope implementado:** identidad fuerte de universo/asset, perfil mínimo, registry y storage/context inmutables. **2.6.2 implementado:** Manifest v2 universal, perfil Nimroel declarativo, loader genérico, reglas de clasificación, universe match y aislamiento léxico de raíces. **2.6 — Multi-Universe Foundation HECHO.** Véase [MULTI_UNIVERSE_ARCHITECTURE.md](MULTI_UNIVERSE_ARCHITECTURE.md). **2.7 — ZIP deliberadamente incorrectos para tests HECHO:** inventario, 74 casos nuevos, invariantes de filesystem, NapIssueMapper real y corrección mínima de apertura de cabeceras locales truncadas. Véase [ADVERSARIAL_ZIP_TESTS.md](ADVERSARIAL_ZIP_TESTS.md). **Fase 2 HECHA:** 2.8 — Package Semantic Validation completo. **2.8.1 HECHO:** Package Contract v1 genérico, Profile v2 y loader v1/v2, con Nimroel migrado declarativamente y Profile v1 histórico intacto. Véase [PACKAGE_CONTRACT_V1.md](PACKAGE_CONTRACT_V1.md) y [UNIVERSE_PROFILE_V2.md](UNIVERSE_PROFILE_V2.md). **2.8.2 — Package Semantic Validator HECHO:** loader Manifest v2 estricto, validación read-only del envelope/contexto/rule/archivos y ValidatedAssetPackage inmutable. Véase [PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md). Fase 3 — Routing y repo HECHA. 3.1 — Production Repository Boundary, 3.2 — Repository Scanner, 3.3 — Routing Contract v1, 3.4 — Structural Change Detection, 3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy y 3.6 — Destination Resolver HECHOS. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan, 4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Routing solo podrá consumir ValidatedAssetPackage.

# 55. Hoja de ruta acordada

## FASE 0 --- Fundación

### 0.1

Crear repo `NAP`.\
**HECHO.**

### 0.2

Crear solución .NET 8.  
**HECHO.**

### 0.3

Primer ejecutable.  
**HECHO.**

### 0.4

Tests.  
**HECHO.**

### 0.5

Estrategia Git + CI.  
**HECHO.**

------------------------------------------------------------------------

## FASE 1 --- Entrada y paquetes

**COMPLETA (1.1–1.6).** Componentes reales y prueba de integración presentes.

### 1.1

Inbox.

### 1.2

Detección.

### 1.3

Comprobar archivo completamente copiado.

### 1.4

Staging.

### 1.5

Extracción ZIP segura.

### 1.6

Primer paquete de prueba.

------------------------------------------------------------------------

## FASE 2 --- Manifest y validación

**HECHA:** todos los capítulos de la hoja de ruta vigente, 2.1–2.8, completos.

### 2.1

Diseñar manifest definitivo. **HECHO:** Manifest v1 formalizado, schema y documentación.

### 2.2

Modelo C#. **HECHO:** AssetManifestV1, mapeo JSON y tests.

### 2.3

Naming. **HECHO:** Naming v1, utilidades puras, nombres canónicos y schema.

### 2.4

Validación PNG. **HECHO:** inspección estructural y CRC, metadatos y proporción exacta, sin decoder. Véase [PNG_VALIDATION.md](PNG_VALIDATION.md).

### 2.5

Errores controlados. **HECHO:** NapIssue, códigos estables, severidad y STOP separados, mapper de resultados locales y report inmutable. Véase [CONTROLLED_ISSUES.md](CONTROLLED_ISSUES.md). Sin PackageValidator ni orquestador.

### 2.6

**Multi-Universe Foundation — HECHO (2.6.1 y 2.6.2).**

#### 2.6.1 — Core Universe Scope

**HECHO:** UniverseId, UniverseAssetKey, UniverseProfile, UniverseRegistry,
UniverseStorageConfig y UniverseContext, con tests de identidad y storage.
Véase [MULTI_UNIVERSE_ARCHITECTURE.md](MULTI_UNIVERSE_ARCHITECTURE.md).

#### 2.6.2 — Manifest v2 + Nimroel profile configuration

**HECHO:** contrato Manifest v2 universal, modelo C#, perfil Nimroel declarativo,
schema genérico de perfiles, loader, reglas de clasificación, comprobación
de universo activo y aislamiento puro de storage roots. Sin PackageValidator,
routing ni migración de v1. Véase [MANIFEST_V2.md](MANIFEST_V2.md) y
[UNIVERSE_PROFILE_V1.md](UNIVERSE_PROFILE_V1.md).

### 2.7

ZIP deliberadamente incorrectos para tests. **HECHO:** auditoría de cobertura,
fixtures sintéticos, regresiones para nombres/tipos/estructuras ZIP y límites,
invariantes de filesystem y mapeo real de Rejected/InvalidArchive a NapIssue.
Corrección mínima demostrada: EndOfStreamException al abrir una cabecera local
truncada se traduce a InvalidArchive. Véase
[ADVERSARIAL_ZIP_TESTS.md](ADVERSARIAL_ZIP_TESTS.md). Corresponde al antiguo 2.6,
desplazado por la fundación multiuniverso.

La revisión posterior a 2.7 decidió insertar validación semántica de package
antes de routing. No se permitirá que Routing reciba packages extraídos sin
validación semántica completa previa.

### 2.8 — Package Semantic Validation

**HECHO (2.8.1 y 2.8.2).** Contrato configurable y validación semántica read-only
reúnen Manifest v2, UniverseProfile, classification, Naming, PNG y universe scope
antes de Fase 3. La extracción ZIP conserva su responsabilidad genérica.

#### 2.8.1 — Package Contract v1 + Universe Profile v2

**HECHO:** AssetPackageFileRule inmutable, filenames puros y PackageFiles con
snapshot/unicidad; Profile v2 cerrado con package_files obligatorio y loader
explícito v1/v2. Nimroel migrado declarativamente a cuatro archivos required,
con clasificación intacta; manifest universal y ProductionWebP fuera de PackageFiles.
Profile v1 schema/documentación permanecen intactos como históricos. Este
subcapítulo no añadió validación de directorios ni cambios a ZIP extraction.
Véase [PACKAGE_CONTRACT_V1.md](PACKAGE_CONTRACT_V1.md) y
[UNIVERSE_PROFILE_V2.md](UNIVERSE_PROFILE_V2.md).

#### 2.8.2 — Package Semantic Validator

**HECHO:** AssetManifestV2Loader estricto, PackageSemanticValidator de solo lectura,
PackageSemanticValidationResult con invariantes y ValidatedAssetPackage inmutable.
Se exige root flat, manifest universal v2/canónico, nombre de package, universo
activo, rule conocida, clasificación permitida, required completos, ausencia
de extras y contenido soportado. png_master reutiliza PngMasterValidator y
NapIssueMapper sin aspect ratio hardcoded; validator desconocido presente causa STOP.
El objeto validado solo existe con report limpio y contiene snapshots de
metadata/paths por role, con manifest separado. Véase
[PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md).

**Fase 2 HECHA. Fase 3 — Routing y repo HECHA (3.1–3.6 HECHOS). Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1.
4.1 — ProcessingPlan, 4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS; Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Routing solo podrá consumir
ValidatedAssetPackage.** No se implementa resolución de routing, conversiones, hashes, Visual
Identity schema ni migración de Manifest v1 en este capítulo.

------------------------------------------------------------------------

## FASE 3 --- Routing y repo

**HECHA (3.1–3.6).** 2.8.2 aporta la frontera semántica previa.
Routing solo podrá consumir ValidatedAssetPackage.

### 3.1 — Production Repository Boundary

**HECHO:** ProductionRepositoryValidator valida solo los atributos de
UniverseContext.Storage.ProductionRoot. Ausencia, archivo y directorio raíz
ReparsePoint producen production_root_missing, production_root_invalid y
production_root_reparse, todos Error + Stop; otros errores operativos se propagan.
Produce ValidatedProductionRepository sellado e inmutable con constructor
internal sin I/O, UniverseId y RootPath exactos del contexto.
ProductionRepositoryValidationResult exige report limpio si y solo si
Repository no null. Es read-only, sin enumerar entries, crear carpetas, resolver
links, inspeccionar ancestros ni exigir Git. Véase
[PRODUCTION_REPOSITORY_BOUNDARY.md](PRODUCTION_REPOSITORY_BOUNDARY.md).

### 3.2 — Repository Scanner

**HECHO:** ProductionRepositoryScanner.Scan acepta exclusivamente
ValidatedProductionRepository. Revalida los atributos del root reutilizando
production_root_missing/invalid/reparse y recorre descendants iterativamente.
ProductionRepositoryEntryKind contiene Directory/File; entries y snapshot
son sellados/inmutables, con constructores internal sin I/O. Se conservan
UniverseId/RootPath exactos, nombres históricos sin Naming v1, RelativePath
con separador "/" y Entries defensivas/read-only ordenadas con Ordinal.
Los reparse internos producen repository_entry_reparse (Error + Stop), sin
traversal, con issues ordenados por RelativePath y Snapshot null. El resultado
exige report limpio si y solo si Snapshot presente. No lee contents ni
interpreta .git, profiles o assets. Errores operativos durante traversal se
propagan; la observación puntual en memoria no garantiza estabilidad concurrente
ni impone cuota explícita de entries/profundidad. Véase
[REPOSITORY_SCANNER.md](REPOSITORY_SCANNER.md).
La política canónica real de Nimroel se fija en 3.5 tras auditar su estructura
histórica. 3.2 no resuelve destinos; 3.6 añade el cálculo puro.
4.1 — ProcessingPlan añade la base inmutable y 4.2 la renderiza sin I/O.
4.3 — Plan Validation interpreta la estructura materializada; 4.4 — Logs produce
un resumen textual privacy-safe. Fase 4 HECHA dentro del alcance v1;
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

### 3.3 — Routing Contract v1

**HECHO como contrato declarativo:** AssetRouteSegmentKind contiene exactamente
Literal/Classification/AssetId; AssetRouteSegment tiene factories seguras y
AssetRoutingRule preserva orden/snapshot defensivo/read-only. Sin I/O, templates
libres ni resolución de paths. UniverseAssetRule añade Routing nullable con
compatibilidad de constructores v1/v2. Las dimensiones Classification del route
deben ser RequiredClassification; Allowed no basta.
Universe Profile v3 exige package_files + routing cerrado, con segmentos JSON
literal/classification/asset_id true y dispatch estricto v1/v2/v3. Los schemas
v1/v2 permanecen intactos. En 3.5 el perfil real Nimroel migró a v3; desde 5.4
usa v4 conservando ese routing. El loader actual soporta explícitamente 1/2/3/4.
Su JSON v2
exacto se conserva en un fixture histórico. El fixture v3 genérico sigue siendo
`test_universe`, no un segundo perfil real.
Véanse [ROUTING_CONTRACT_V1.md](ROUTING_CONTRACT_V1.md) y
[UNIVERSE_PROFILE_V3.md](UNIVERSE_PROFILE_V3.md).
3.3 todavía NO calcula destinos ni conecta las fronteras package/repository/snapshot.

### 3.4 — Structural Change Detection

**HECHO:** ProductionRepositoryStructureDiffer.Compare consume exclusivamente
dos ProductionRepositorySnapshot materializados. Comprueba mismo UniverseId por
valor y RootPath mediante OrdinalIgnoreCase en Windows, Ordinal en otras
plataformas, sin normalización ni I/O. RelativePath siempre usa Ordinal; duplicados
dentro de un snapshot se rechazan. ProductionRepositoryStructuralChange representa
Added/Removed/KindChanged con invariantes; ProductionRepositoryStructureDiff
conserva identidad/root y Changes defensivas/read-only ordenadas Ordinal.
Cada entry genera su observación, sin colapsar árboles, inferir move/rename,
usar FullPath, detectar bytes/timestamps/hashes ni aplicar routing o ignores.
Los cambios son neutrales, sin NapIssue. Mismo path/mismo kind no cambia,
aunque los bytes reales hayan cambiado. Véase
[STRUCTURAL_CHANGE_DETECTION.md](STRUCTURAL_CHANGE_DETECTION.md).

### 3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy

**HECHO:** Primer capítulo que INTERPRETA la estructura. El corte documentado
del repositorio `robdor80/Videojuego_Nimroel` observa 39 registros y 39 carpetas
bajo el ProductionRoot lógico `Worldbuilding/Direccion artistica/Assets`, con
cuatro archivos históricos por asset. Distingue infraestructura, ocho grupos,
las excepciones semánticas `children`/`elder` y el casing histórico
`Norgard/Treskal`.

Para la única regla `portrait + portrait_npc`, la política canónica queda:

```text
ProductionRoot
└── portraits
    └── {culture}
        └── {location}
            └── {role}
                └── {sex}
                    └── {asset_id}
```

Nimroel migra a Profile v3 y conserva dimensiones, allowed/required
classification y package files. `realm` y `region` no forman parte del routing.
No se infieren valores desde AssetId, no se aplica casing de display y no se
ejecuta migración. Routing Contract v1 resulta suficiente. Véase
[NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md](NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md).

### 3.6 — Destination Resolver

**HECHO:** ProductionDestinationResolver.Resolve(ValidatedAssetPackage,
ValidatedProductionRepository) calcula el directorio usando exclusivamente
package.AssetRule.Routing. ProductionAssetDestination es sellado/inmutable,
con constructor internal y exactamente AssetKey, RootPath, RelativeDirectory y
FullDirectoryPath. Aislamiento de universo por valor; routing ausente,
classification faltante o segmento inseguro causan fallo cerrado. Naming v1
defensivo, dispositivos Windows reservados en todas las plataformas y contención
léxica estricta. RelativeDirectory usa `/`, FullDirectoryPath usa
Path.Combine/Path.GetFullPath. Sin I/O, snapshot, existencia, colisiones,
filenames, creación de carpetas, escrituras ni migración. Resolver un destino
no autoriza escribirlo. Véase [DESTINATION_RESOLVER.md](DESTINATION_RESOLVER.md).

------------------------------------------------------------------------

## FASE 4 --- PLAN / Dry Run

**HECHA dentro del alcance v1.** 4.1–4.4 HECHOS. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

### 4.1 — ProcessingPlan

**HECHO:** ProcessingPlanBuilder.Build(package, repository, destination) produce
un ProcessingPlan sellado/inmutable con identidad, metadata completa, inputs y
destino recibido. Comprueba universo, AssetKey y root, copia classification y
FilesByRole a snapshots Ordinal read-only y conserva el manifest separado.
Sin I/O, re-routing, snapshot de repo, existencia, colisiones, operations list,
timestamps, JobId ni escrituras. Todavía no es un grafo de ejecución completo.
Véase [PROCESSING_PLAN.md](PROCESSING_PLAN.md).

### 4.2 — Dry Run

**HECHO:** DryRunTextRenderer.Render(ProcessingPlan) devuelve texto humano
determinista de identidad/metadata/inputs/destino congelados, con orden Ordinal,
LF fijo sin newline final y paths escapados entre comillas. Colecciones vacías
se muestran como `(none)`. OPERATIONS indica que no están definidas; SAFETY
garantiza cero filesystem I/O y que nada se ejecuta o autoriza. Sin revalidación,
snapshot, existencia, colisiones, outputs inventados, CLI o serializer.
Véase [DRY_RUN.md](DRY_RUN.md).

### 4.3 — Plan Validation

**HECHO:** ProcessingPlanValidator.Validate(ProcessingPlan,
ProductionRepositorySnapshot) devuelve NapIssueReport. Comprueba coherencia de
universo/root y rechaza duplicados bajo la semántica de plataforma. Recorre
prefijos con `/`: permite missing/ancestor Directory exacto; primer casing
conflict Windows, File blocker o Directory final existente produce Error + Stop.
Sin I/O, scanner, structural diff, source revalidation, contents ni hashes.
Un report limpio es point-in-time y no autoriza escritura. Véase
[PLAN_VALIDATION.md](PLAN_VALIDATION.md).

### 4.4 — Logs

**HECHO:** PlanLogTextRenderer.Render(ProcessingPlan, NapIssueReport) devuelve un resumen textual determinista privacy-safe de identidad, destino relativo, flags del report y code/severity/disposition en orden. Sin I/O, persistencia, timestamps, JobId ni revalidación. Véase [PLAN_LOGS.md](PLAN_LOGS.md).

------------------------------------------------------------------------

## FASE 5 --- Conversión

**HECHA.** 5.1–5.5 HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

### 5.1 — Portrait PNG → WebP

**HECHO:** PortraitPngToWebpConverter lee el PNG read-only, reutiliza validación
estructural y aplica safety pixels/ratio exacto antes del decode PNG real.
Settings explícitos, Lanczos3 sin crop, WebP lossy en memoria. Upscale permitido.
Sin output file ni integración de ejecución con ProcessingPlan. Desde 5.4,
Profile v4 declara reglas y el resolver materializa configuración. Véase
[PORTRAIT_CONVERSION.md](PORTRAIT_CONVERSION.md).

### 5.2

Validar salida — **HECHO**. PortraitWebpOutputValidator valida metadata/settings,
contenedor, decode WebP real y dimensiones. Primer fallo Error + Stop, sin I/O,
quality inference, re-encode, hashes o autorización de escritura. Véase
[PORTRAIT_OUTPUT_VALIDATION.md](PORTRAIT_OUTPUT_VALIDATION.md).

### 5.3

Scene — **HECHO**. Conversión PNG → WebP lossy y validación en memoria,
settings explícitos, sin perfil canónico Nimroel Scene. Tipos Scene separados
de Portrait; solo helper internal de contenedor compartido. Véase
[SCENE_CONVERSION.md](SCENE_CONVERSION.md).

### 5.4

Perfiles genéricos — **HECHO**. Profile v4 declara conversion required nullable;
ImageConversionResolver resuelve configuración y MaxInputPixels runtime desde
ValidatedAssetPackage, sin I/O ni ejecución. Nimroel Portrait 768×960 Q90,
sin Scene canónico. Véase [UNIVERSE_PROFILE_V4.md](UNIVERSE_PROFILE_V4.md).

### 5.5

No recorte silencioso — **HECHO** como contrato genérico full-frame de png_to_webp.
ImageConversionGeometryValidator usa HasAspectRatio exacto y devuelve clean o
Error + Stop. Portrait/Scene conservan sus APIs y errores históricos y comparten
la primitiva matemática. Sin crop, pad, letterbox o distorsión, Profile v5,
campos nuevos u orquestación. Véase [NO_SILENT_CROP.md](NO_SILENT_CROP.md).

------------------------------------------------------------------------

## FASE 6 --- Integridad

**HECHA.** 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO;
6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

### 6.1

SHA-256 — **HECHO**. Sha256Digest canónico e inmutable y Sha256Hasher streaming
sobre path/Stream; desde posición actual a EOF, stream del caller abierto,
sin dependencia de seek/Length. Sin hashes en modelos, persistencia o política
de duplicados. Véase [SHA256_INTEGRITY.md](SHA256_INTEGRITY.md).

### 6.2

Duplicados — **HECHO**. Fingerprints inmutables de UniverseAssetKey + Sha256Digest,
matriz exacta y comparación pairwise local a un universo; colisión Error + Stop,
posible duplicado Warning + Continue, sin I/O, hashing interno, catálogo o
persistencia. Idempotencia semántica sin prueba de Job completado. Véase
[DUPLICATE_DETECTION.md](DUPLICATE_DETECTION.md).

### 6.3

Job ID — **HECHO**. JobId sealed record, Value canónico `job_` + 32 lowercase
ASCII hex no all-zero, igualdad por valor y Create mediante Guid.NewGuid().
Identidad global, independiente de universo/asset/estado/timestamp. Sin Job model,
lifecycle, I/O o persistencia; ProcessingPlan/renderers intactos. Véase
[JOB_ID.md](JOB_ID.md).

### 6.4

Estados persistentes — **HECHO**. JobState, JobStateMachine y JobStateRecord
formalizan el flujo y sus terminales; JobStateStore persiste únicamente el último
estado en StateRoot con JSON estricto y publicación segura. Sin recovery o
escrituras de assets. Véase [JOB_STATES.md](JOB_STATES.md).

### 6.5

Recuperación tras fallo — **HECHO**. Discovery read-only exclusivamente bajo
StateRoot: journals finales autoritativos, checkpoints activos exactos y
terminales separados. Temporales no autoritativos e incidencias fail-closed,
sin repair, promoción, limpieza, avance de estado o ejecución de assets.
Véase [JOB_RECOVERY.md](JOB_RECOVERY.md).

------------------------------------------------------------------------

## FASE 7 --- Auditor IA

**HECHA (7.1–7.5).** Auditoría de los hechos actuales de ProcessingPlan v1,
sin inventar operaciones, ejecutar filesystem o integrar lifecycle de Jobs.
Véase [AI_AUDIT.md](AI_AUDIT.md). Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6). Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4). Siguiente: Fase 10 — SQLite.

### 7.1

Cliente IA — **HECHO**. IAiAuditClient provider-neutral y adapter Gemini REST
Interactions v1 en NAP.AI; HttpClient/key/model externos, sin SDK o nuevos paquetes.

### 7.2

Informe estructurado — **HECHO**. Request allowlisted, JSON explícito y report
inmutable; structured output remoto y parser local estricto, sin repair.

### 7.3

PASS / WARNING / FAIL — **HECHO**. Invariantes estrictas y flags derivados;
PASS no ejecuta, WARNING requiere revisión y FAIL/fallo de cliente bloquean.

### 7.4

Aislamiento de permisos — **HECHO**. Solo AiAuditRequest saneado, prioridad STOP
de NAP, sin paths absolutos, tools, conversation state o filesystem capabilities.

### 7.5

Pruebas en Dry Run — **HECHO**. Validadores reales y fake IA/HTTP offline,
con roots/checkpoints intactos y ninguna transición PLANNED → AUDITED.

------------------------------------------------------------------------

## FASE 8 --- TeraBox / Archive Storage

**HECHA (8.1–8.6).** Escritura real exclusivamente de conservación local,
con PASS de IA obligatorio, preflight determinista bajo lock y verificación
antes de publicar índice. Véase [ARCHIVE_STORAGE.md](ARCHIVE_STORAGE.md).

### 8.1

Configurar/validar ArchiveRoot del universo activo — **HECHO**. Raíz externa
existente, directorio absoluto aislado, sin reparse ni escapes. Si falta, STOP;
NAP solo crea directorios controlados debajo de una raíz validada.

### 8.2

Índice maestros — **HECHO**. Master index JSON v1 estricto, inmutable,
universe-scoped, orden Ordinal y publicación atómica con temp + Flush(true).

### 8.3

Copia incremental — **HECHA**. Paquete original completo desde FilesByRole
+ ManifestPath; CopyAndIndex, IndexExisting y AlreadyArchived. No WebP/ZIP.
Solo faltantes; recuperación parcial y segundo run exacto sin escrituras.

### 8.4

Verificación — **HECHA**. SHA-256 y tamaño de cada source, temp y final;
todos los archivos físicos exactos antes de indexar, fallo Error + Stop.

### 8.5

Colisiones — **HECHAS**. Ningún overwrite/delete de masters o auto-resolution;
inconsistencias Error + Stop, otro ID con mismo master Warning + Continue.
archive.lock de proceso cruzado y revalidación/recarga evitan lost updates.

### 8.6

Estructura definitiva — **HECHA**. ArchiveRoot/ProductionDestination.RelativeDirectory
con filenames originales; _nap/master_index.json y _nap/archive.lock. Routing
genérico, sin prefijos TeraBox/Nimroel hardcoded. Cero producción, SQLite,
transiciones JobState o ejecución global.

------------------------------------------------------------------------

## FASE 9 --- Producción repo del universo activo

**HECHA (9.1–9.4) — Production Storage + Verified Completion.** El Core
completa el MVP de producción desde ZIP validado hasta COMPLETED con archivo
original preservado y outputs físicamente verificados. Véase
[PRODUCTION_STORAGE.md](PRODUCTION_STORAGE.md). Siguiente: Fase 10 — SQLite.

### 9.1

Copiar producción — **HECHO**. ArchiveMasterResult coherente obligatorio,
plan read-only y conversión genérica desde ResolvedImageConversion. WebP +
companions/manifest exactos; PNG maestro excluido. AI PASS antes de I/O;
temporales hermanos, Flush(true) y Move sin overwrite, solo faltantes.

### 9.2

Crear carpetas permitidas — **HECHO**. Solo segmentos del destino relativo
recibido, dentro de ProductionRoot existente aislado, segmento a segmento,
con revalidación root/parent y rechazo de reparse, blockers y casing conflicts.

### 9.3

Verificar — **HECHO**. Sources, temps y todos los finales con SHA-256/tamaño;
WebP decodificable y dimensiones exactas antes/después de publicación.
Entradas inesperadas STOP; AlreadyProduced sin writes y recovery parcial
solo con evidencia durable de publicaciones del mismo Job. Fresh Job nunca
adopta finales preexistentes, aunque sean idénticos. Fase 4 conserva su blocker.

### 9.4

Completed — **HECHO**. AssetExecutionCoordinator con named mutex por Job,
AUDITED → EXECUTED → VERIFIED → COMPLETED tras verificación física. Resume
checkpoints activos, FAILED terminal, COMPLETED read-only; inconsistencia
STOP sin repair. Sin transición automática FAILED ante I/O recuperable.
Recibos estrictos en StateRoot/production-executions vinculan Job/Asset,
roots, ruta y snapshot. Solo Move no-overwrite + verificación final confieren
ownership durable; final sin recibo o ausente tras registrarse exige STOP.

------------------------------------------------------------------------

## FASE 10 --- SQLite

**SIGUIENTE.** Catálogo por universo; sin implementación ni dependencia SQLite
en Fase 9. Podrá integrarse antes del cierre sin reescribir el writer.

### 10.1

Crear un catálogo SQLite por universo en StateRoot/AssetCatalog.db.

### 10.2

Esquema.

### 10.3

Importar assets existentes.

### 10.4

Actualización automática.

### 10.5

Filtros.

### 10.6

Estadísticas.

### 10.7

Reconstrucción.

**Añadido posteriormente como requisito fuerte dentro de esta fase:** -
visual identities; - cobertura; - diversidad; - objetivos; - campañas; -
soporte RoDo.

------------------------------------------------------------------------

## FASE 11 --- Explorador visual

### 11.1

Thumbnails.

### 11.2

Grid.

### 11.3

Ficha.

### 11.4

Búsqueda avanzada.

### 11.5

Estadísticas visuales.

------------------------------------------------------------------------

## FASE 12 --- Backups

### 12.1

Backup SQLite.

### 12.2

Histórico TeraBox.

### 12.3

Retención.

### 12.4

Restauración.

### 12.5

Snapshot repo.

### 12.6

Git bundle.

------------------------------------------------------------------------

## FASE 13 --- Git del repo de producción del universo activo

### 13.1

Detectar cambios.

### 13.2

Mostrar archivos.

### 13.3

Preparar commit.

### 13.4

Commit asistido.

### 13.5

Push.

Dejar deliberadamente tarde por seguridad.

------------------------------------------------------------------------

## FASE 14 --- UI profesional

### 14.1

Ventana principal.

### 14.2

Dashboard.

### 14.3

Inbox/Pipeline.

### 14.4

Catálogo.

### 14.5

Objetivos.

### 14.6

Estadísticas.

### 14.7

Errores/Historial.

### 14.8

Ajustes.

### 14.9

Backups.

### 14.10

Bandeja sistema.

### 14.11

RoDo.

**Nota:** aunque esta fase represente el pulido completo, se quiere
introducir un shell RobStyle mucho antes.

------------------------------------------------------------------------

## FASE 15 --- Instalador y distribución

### 15.1

Publicación x64.

### 15.2

Icono.

### 15.3

Inno Setup.

### 15.4

Desinstalador.

### 15.5

GitHub Release.

### 15.6

Backup TeraBox.

------------------------------------------------------------------------

# 56. Posible revisión futura de la hoja de ruta

La hoja de ruta no es inmutable.

Especialmente:

-   SQLite se ha vuelto más importante desde que se creó la hoja
    original;
-   Objetivos y RoDo deben integrarse formalmente;
-   el shell RobStyle debería adelantarse;
-   NAP Companion se añadirá como fase posterior independiente;
-   visual identity debe formar parte del diseño de datos desde el
    principio.

Antes de programar módulos dependientes de datos conviene actualizar
formalmente la hoja de ruta.

------------------------------------------------------------------------

# 57. Próximo trabajo previsto

La Fase 1 está completa; Manifest v1 está formalizado en 2.1, su modelo C# implementado en 2.2, Naming v1 definido e implementado en 2.3 y la inspección estructural PNG implementada en 2.4. El lenguaje común de incidencias controladas está implementado en 2.5, sin acoplar los componentes existentes ni crear PackageValidator. La base de universo explícito está implementada en **2.6.1 — Core Universe Scope**, conservando Manifest v1 y los componentes existentes. **2.6.2 — Manifest v2 + Nimroel profile configuration está HECHO**, con reglas de clasificación fuera del schema universal y detección pura de storage overlap antes de futuras escrituras. **2.6 — Multi-Universe Foundation completo. 2.7 — auditoría adversarial ZIP HECHO. 2.8.1 — Package Contract v1 + Universe Profile v2 HECHO. 2.8.2 — Package Semantic Validator HECHO. Fase 2 completa.** Fase 3 — Routing y repo HECHA. 3.1 — Production Repository Boundary, 3.2 — Repository Scanner, 3.3 — Routing Contract v1, 3.4 — Structural Change Detection, 3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy y 3.6 — Destination Resolver HECHOS. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan, 4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. Routing solo podrá consumir ValidatedAssetPackage. La política canónica existe como configuración y 3.6 calcula directorios sin I/O ni escrituras, usando la regla retenida por el package validado. Resolver un destino no autoriza escribirlo. Debe partir de los contratos vigentes, comprobando el estado real del repositorio antes de afirmar su contenido.

# 58. Decisiones que NO deben olvidarse

1.  **SQLite sí o sí, un catálogo por universo.**
2.  SQLite debe ser reconstruible.
3.  Backups históricos SQLite → TeraBox.
4.  Assets maestros PNG → TeraBox.
5.  Producción → WebP Q90 según perfil.
6.  Routing basado en profile/config + metadata + estructura del repo del universo activo, limitado a sus raíces autorizadas.
7.  Dry Run antes de escritura.
8.  IA audita; NAP ejecuta.
9.  RoDo forma parte de NAP.
10. RoDo debe poder ser proactivo.
11. Objetivos y campañas de cobertura.
12. Diversidad visual, no sólo cantidad.
13. `visual_identity.json` por portrait.
14. Vocabularios normalizados para SQLite.
15. Catálogo visual con thumbnails.
16. UI grande, bonita y profesional.
17. Identidad oficial: **RobStyle UI**.
18. Futuro **NAP Companion Android**.
19. GitHub desde el primer día.
20. Instalador profesional + Releases + copia TeraBox.
21. No paralizar durante demasiado tiempo el desarrollo del RPG.
22. Conseguir primero un NAP mínimo realmente útil y luego
    evolucionarlo.
23. Identidad global = UniverseId + AssetId; ningún CurrentUniverse global.
24. Toda escritura futura de assets queda scoped por UniverseContext.

------------------------------------------------------------------------

# 59. Visión final

NAP no debe acabar siendo simplemente:

> "un conversor de PNG a WebP".

Debe convertirse en:

> **el sistema de producción, conservación, catalogación, consulta,
> cobertura, auditoría y planificación de los assets visuales de
> cada universo/perfil, comenzando por Nimroel.**

La visión completa es:

``` text
                    ┌───────────────┐
                    │   ChatGPT     │
                    │ crea assets   │
                    └───────┬───────┘
                            │ ZIP
                            ▼
                    ┌───────────────┐
                    │      NAP      │
                    │   Pipeline    │
                    └───────┬───────┘
                            │
          ┌─────────────────┼──────────────────┐
          │                 │                  │
          ▼                 ▼                  ▼
       TeraBox           Repo universo      SQLite
       maestros          producción         catálogo
          │                 │                  │
          │                 │          ┌───────┴────────┐
          │                 │          │                │
          │                 │          ▼                ▼
          │                 │      Objetivos         RoDo
          │                 │      Cobertura       Supervisor
          │                 │      Diversidad          │
          │                 │          │                │
          └─────────────────┴──────────┴────────────────┘
                                      │
                                      ▼
                               NAP Companion
                                  Android
```

Todo ello presentado mediante **RobStyle UI**, con seguridad,
trazabilidad, reconstrucción y backups.
Las tres ramas del diagrama operarán en el `UniverseContext` activo:
archive, producción y un catálogo SQLite por universo.

------------------------------------------------------------------------

# 60. Regla permanente de continuidad

Esta especificación es la referencia estable de NAP. No se debe rediseñar desde cero sin razón técnica documentada, ni perder las prioridades de seguridad, SQLite, automatización, RobStyle UI y utilidad real. Cada capítulo debe comprobar el repositorio, implementar una tarea pequeña, compilar, probar y revisar antes de pasar al siguiente.
