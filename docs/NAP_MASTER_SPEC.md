# NAP — Master Specification

**Proyecto:** NAP\
**Nombre oficial:** Nexus Asset Platform\
**Origen histórico:** Nimroel Asset Pipeline\
**Repositorio:** `robdor80/NAP`\
**Estado actual verificado:** Fase 0, Fase 1 (1.1–1.6) y Fase 2 (2.1–2.8) HECHAS. 2.8.1 — Package Contract v1 + Universe Profile v2 y 2.8.2 — Package Semantic Validator completos. Siguiente: Fase 3 — Routing y repo, NO iniciada. Routing solo podrá consumir ValidatedAssetPackage.\
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
aislamiento de raíces antes de futuras escrituras. Routing, SQLite y TeraBox
siguen pendientes.
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
para la combinación exacta asset_type + production_profile, declarada en
[Universe Profile v2](UNIVERSE_PROFILE_V2.md). No hay requisito universal de PNG,
prompt, info ni Visual Identity.

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

`UniverseProfile` obtiene las dimensiones y reglas semánticas de la
[configuración genérica v2](UNIVERSE_PROFILE_V2.md), conservando
[Profile v1](UNIVERSE_PROFILE_V1.md) como contrato histórico intacto. El loader
aplica dispatch explícito por schema_version 1/2, sin reinterpretación silenciosa.
El primer perfil real,
[Nimroel](../config/universes/nimroel/profile.json), registra culture/realm/
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

Las decisiones Portrait/Scenes siguientes pertenecen al primer perfil
previsto, Nimroel. Las reglas futuras de producción se resolverán desde
el profile/config del universo activo; no son parámetros universales del Core.

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

## Scenes

Se definirá un perfil específico posteriormente.

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
Las reglas de routing vendrán del profile/config; ninguna metadata de asset
podrá seleccionar una raíz de otro universo. No hay routing implementado.
Routing solo podrá consumir ValidatedAssetPackage, tras la frontera semántica de 2.8.2.
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

Ejemplo conceptual:

``` text
portrait
→ portraits/{culture}/{location}/{role}/{sex}/{asset_id}
```

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

NAP deberá automatizar la incorporación incremental.

Si hoy se archivan 5 assets y mañana aparecen 2 nuevos:

``` text
NO volver a copiar 7
SÍ detectar y copiar sólo los 2 nuevos
```

Para ello se usarán:

-   Asset ID;
-   hashes;
-   índice;
-   SQLite;
-   verificación origen/destino.

Nunca sobrescribir silenciosamente un maestro con otro distinto.

------------------------------------------------------------------------

# 10. Índice de maestros

Debe existir un registro de los maestros archivados.

Datos posibles:

-   Asset ID;
-   hash SHA-256;
-   ruta;
-   fecha;
-   tamaño;
-   tipo;
-   estado de verificación.

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

El plan futuro recibirá un `UniverseContext` explícito: producción y archive
se calcularán dentro de sus raíces. Este ejemplo usa el primer perfil Nimroel.

Antes de ejecutar, NAP construye un plan completo.

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

------------------------------------------------------------------------

# 29. Hashes y duplicados

Usar SHA-256.

Casos:

``` text
mismo Asset ID + mismo hash
→ ya procesado / idempotente

mismo Asset ID + distinto hash
→ COLISIÓN / STOP

distinto Asset ID + mismo PNG
→ posible duplicado / revisar
```

También verificar copias después de escribir.
Estos casos se evalúan dentro del mismo universo; la identidad global es
`UniverseAssetKey`. Coincidir solo en AssetId entre universos no es colisión.

------------------------------------------------------------------------

# 30. Jobs y estados persistentes

Cada proceso debe tener un Job ID.

Estados conceptuales:

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

NAP debe saber recuperarse tras:

-   apagado;
-   cierre;
-   excepción;
-   bloqueo;
-   pérdida temporal de TeraBox;
-   operación incompleta.

------------------------------------------------------------------------

# 31. Logs

Logs útiles pero no invasivos.

La UI debe mostrar información humana.

Los detalles técnicos podrán incluir:

-   rutas;
-   hashes;
-   tiempos;
-   excepciones;
-   resultado IA;
-   archivos;
-   operaciones.

Los logs no deben exponer secretos.

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

La Fase 0.1 (repositorio), 0.2 (solución .NET 8), 0.3 (primer ejecutable), 0.4 (tests) y 0.5 (estrategia Git + CI) están presentes en el repositorio actual. **Fase 1 (1.1–1.6) completa:** Inbox → Detection → Readiness → Staging → Safe Extraction, probada de extremo a extremo con un paquete legítimo. **2.1 — Manifest v1 formalizado:** contrato, JSON Schema y documentación. **2.2 — Modelo C# implementado:** AssetManifestV1 con serialización System.Text.Json y classification extensible, sin normalización ni validación semántica. **2.3 — Naming v1 implementado:** reglas de forma, coherencia de prefijo y nombres canónicos. **2.4 — Validación PNG estructural implementada:** firma, IHDR, orden esencial, CRC de chunks y proporción exacta, sin decodificación de píxeles ni resolución de perfiles. **2.5 — Errores controlados implementados:** NapIssue con códigos estables, Severity y Disposition independientes, adaptadores de resultados locales y report inmutable; sin PackageValidator. Véase [CONTROLLED_ISSUES.md](CONTROLLED_ISSUES.md). **2.6.1 — Core Universe Scope implementado:** identidad fuerte de universo/asset, perfil mínimo, registry y storage/context inmutables. **2.6.2 implementado:** Manifest v2 universal, perfil Nimroel declarativo, loader genérico, reglas de clasificación, universe match y aislamiento léxico de raíces. **2.6 — Multi-Universe Foundation HECHO.** Véase [MULTI_UNIVERSE_ARCHITECTURE.md](MULTI_UNIVERSE_ARCHITECTURE.md). **2.7 — ZIP deliberadamente incorrectos para tests HECHO:** inventario, 74 casos nuevos, invariantes de filesystem, NapIssueMapper real y corrección mínima de apertura de cabeceras locales truncadas. Véase [ADVERSARIAL_ZIP_TESTS.md](ADVERSARIAL_ZIP_TESTS.md). **Fase 2 HECHA:** 2.8 — Package Semantic Validation completo. **2.8.1 HECHO:** Package Contract v1 genérico, Profile v2 y loader v1/v2, con Nimroel migrado declarativamente y Profile v1 histórico intacto. Véase [PACKAGE_CONTRACT_V1.md](PACKAGE_CONTRACT_V1.md) y [UNIVERSE_PROFILE_V2.md](UNIVERSE_PROFILE_V2.md). **2.8.2 — Package Semantic Validator HECHO:** loader Manifest v2 estricto, validación read-only del envelope/contexto/rule/archivos y ValidatedAssetPackage inmutable. Véase [PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md). Siguiente: Fase 3 — Routing y repo, no iniciada. Routing solo podrá consumir ValidatedAssetPackage.

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

**Fase 2 HECHA. Siguiente: Fase 3 — Routing y repo. Routing solo podrá consumir
ValidatedAssetPackage.** No se implementa routing, conversiones, hashes, Visual
Identity schema ni migración de Manifest v1 en este capítulo.

------------------------------------------------------------------------

## FASE 3 --- Routing y repo

**NO INICIADA, siguiente.** 2.8.2 ya aporta la frontera semántica previa.
Routing solo podrá consumir ValidatedAssetPackage.

### 3.1

Configurar production repository / storage del universo activo.

### 3.2

Escanear estructura.

### 3.3

Routing.

### 3.4

Detectar carpetas nuevas.

### 3.5

Clasificar cambios.

### 3.6

Calcular destino.

------------------------------------------------------------------------

## FASE 4 --- PLAN / Dry Run

### 4.1

`ProcessingPlan`.

### 4.2

Dry Run.

### 4.3

Validar plan.

### 4.4

Logs.

------------------------------------------------------------------------

## FASE 5 --- Conversión

### 5.1

Portrait.

### 5.2

Validar salida.

### 5.3

Scene.

### 5.4

Perfiles genéricos.

### 5.5

No recorte silencioso.

------------------------------------------------------------------------

## FASE 6 --- Integridad

### 6.1

SHA-256.

### 6.2

Duplicados.

### 6.3

Job ID.

### 6.4

Estados.

### 6.5

Recuperación tras fallo.

------------------------------------------------------------------------

## FASE 7 --- Auditor IA

### 7.1

Cliente IA.

### 7.2

Informe estructurado.

### 7.3

PASS / WARNING / FAIL.

### 7.4

Aislamiento de permisos.

### 7.5

Pruebas en Dry Run.

------------------------------------------------------------------------

## FASE 8 --- TeraBox

### 8.1

Configurar ArchiveRoot del universo activo.

### 8.2

Índice maestros.

### 8.3

Copia incremental.

### 8.4

Verificación.

### 8.5

Colisiones.

### 8.6

Estructura definitiva.

------------------------------------------------------------------------

## FASE 9 --- Producción repo del universo activo

### 9.1

Copiar producción.

### 9.2

Crear carpetas permitidas.

### 9.3

Verificar.

### 9.4

Completed.

------------------------------------------------------------------------

## FASE 10 --- SQLite

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

La Fase 1 está completa; Manifest v1 está formalizado en 2.1, su modelo C# implementado en 2.2, Naming v1 definido e implementado en 2.3 y la inspección estructural PNG implementada en 2.4. El lenguaje común de incidencias controladas está implementado en 2.5, sin acoplar los componentes existentes ni crear PackageValidator. La base de universo explícito está implementada en **2.6.1 — Core Universe Scope**, conservando Manifest v1 y los componentes existentes. **2.6.2 — Manifest v2 + Nimroel profile configuration está HECHO**, con reglas de clasificación fuera del schema universal y detección pura de storage overlap antes de futuras escrituras. **2.6 — Multi-Universe Foundation completo. 2.7 — auditoría adversarial ZIP HECHO. 2.8.1 — Package Contract v1 + Universe Profile v2 HECHO. 2.8.2 — Package Semantic Validator HECHO. Fase 2 completa.** Siguiente: Fase 3 — Routing y repo, no iniciada. Routing solo podrá consumir ValidatedAssetPackage. La frontera semántica es read-only; no se implementan routing ni escrituras. Debe partir de los contratos vigentes, comprobando el estado real del repositorio antes de afirmar su contenido.

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
