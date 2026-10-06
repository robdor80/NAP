# Multi-Universe Foundation, validación semántica y repositorio de producción — 2.6, 2.8, 3.1 y 3.2

NAP es un sistema genérico de producción, conservación, catalogación,
auditoría y planificación de assets organizado por universos/perfiles.
El nombre oficial es **NAP — Nexus Asset Platform**.
Nimroel es el primer Universe Profile real soportado, no una dependencia del núcleo.

La separación `CoreRPG + Universe Pack` (consola + cartucho) tiene su
equivalente conceptual en `NAP Core + Universe Profile`. El núcleo contiene
capacidades genéricas; las reglas, vocabularios y destinos específicos
proceden de perfiles/configuración. Star Trek y Star Wars son solamente
ejemplos futuros: no se implementa contenido, configuración ni clases para
esos universos, ni un framework de plugins.

## Identidad fuerte

`UniverseId` es un record sellado con un único `Value` de solo lectura.
Su constructor reutiliza exactamente
`AssetNamingRules.IsValidMachineIdentifier`: lowercase snake_case ASCII,
máximo 64 caracteres. No hay trim, conversión a lowercase ni corrección;
las entradas inválidas lanzan `ArgumentException`. La igualdad y el hash
son por valor; `ToString()` devuelve ese valor. El runtime representa los
universos con `UniverseId`, sin sustituirlo por strings desnudos.

`UniverseAssetKey` es un record sellado e inmutable que combina
`UniverseId` y `AssetId`. Exige universo no nulo y un ID válido mediante
`AssetNamingRules.IsValidAssetId`. La identidad global es el par:

```text
(nimroel, portrait_example_001)
(star_trek, portrait_example_001)
```

Son assets distintos. Dos claves con el mismo universo y asset son iguales,
incluido su uso como claves de diccionario o conjunto. El universo no se
añade al asset_id ni al filename: Naming v1 permanece intacto.
La representación `universe::asset_id` de `ToString()` sirve exclusivamente
para diagnóstico humano; no es filename, asset_id ni contrato parseable.
No se implementan migraciones ni copias entre universos.

## Perfil y registry

`UniverseProfile` contiene `UniverseId Id`, `string DisplayName`,
`ClassificationDimensions` y `AssetRules`, todos de solo lectura. Rechaza
ID nulo y nombre nulo/vacío/solo whitespace.
El nombre humano puede contener espacios, acentos y puntuación: no sirve
como identidad, ruta ni clave de búsqueda.

Las dimensiones y reglas tienen snapshot defensivo; Naming v1 comprueba sus
identificadores. Cada `UniverseAssetRule` describe una combinación exacta
AssetType/ProductionProfile y dimensiones Allowed/Required, con required
como subconjunto de allowed. En 2.8.1 añade PackageFiles: reglas inmutables
de role/suffix/extension/required/content validator, con snapshot defensivo,
roles únicos y filenames sin colisión. El constructor histórico conserva
PackageFiles vacío. Véase [PACKAGE_CONTRACT_V1.md](PACKAGE_CONTRACT_V1.md).
Las combinaciones son únicas y sus dimensiones
deben estar registradas por el perfil. No hay significado especial de culture,
location, role o sex en el Core. `TryGetAssetRule` usa lookup ordinal.
El constructor mínimo sigue creando listas vacías.

`UniverseRegistry` recibe perfiles, toma un snapshot ordenado y expone
`IReadOnlyList<UniverseProfile>` mediante una colección de solo lectura.
Cambiar la colección original no cambia el registry. Rechaza colección
nula, perfiles nulos y IDs duplicados por igualdad de `UniverseId`, aunque
los nombres humanos difieran. Puede estar vacío.
`TryGet(UniverseId, out UniverseProfile?)` busca por identidad, devuelve
`false` y `null` si no encuentra el perfil y rechaza un ID nulo.
El futuro selector de UI podrá consumirlo; no hay discovery, watchers,
reload, service locator ni framework de DI.

En 2.6.2 el primer perfil real se carga explícitamente desde
`config/universes/nimroel/profile.json` con `UniverseProfileLoader`.
El contrato [Universe Profile configuration v1](UNIVERSE_PROFILE_V1.md) se
conserva intacto como histórico. En 2.8.1 Nimroel pasó a
[Universe Profile v2](UNIVERSE_PROFILE_V2.md), que exige package_files en cada
asset rule; 3.5 conserva ese estado como fixture histórico y migra el perfil
real a [Universe Profile v3](UNIVERSE_PROFILE_V3.md) con routing. Desde 5.4,
Nimroel usa [Universe Profile v4](UNIVERSE_PROFILE_V4.md), que conserva esos
contratos y añade conversion required nullable. El loader
detecta versión y aplica estrictamente v1, v2, v3 o v4; admite
stream y ruta en solo lectura con guardas de configuración. No hay reglas
Nimroel-specific hardcodeadas ni carga global; no valida packages.

## Raíces físicas autorizadas

`UniverseStorageConfig` contiene un `UniverseId` y tres raíces de solo lectura:

| Raíz | Función futura |
| --- | --- |
| `WorkspaceRoot` | Datos operativos locales del universo. |
| `ProductionRoot` | Producción/repositorio del universo. |
| `ArchiveRoot` | Conservación y backups del universo, por ejemplo TeraBox. |

Las raíces provienen de configuración confiable ya resuelta por el caller.
El constructor rechaza universo nulo y rutas nulas/vacías/solo whitespace
o que no sean fully qualified según la plataforma .NET actual. En Windows,
`C:workspace` y `\workspace` no bastan; rutas absolutas con unidad o UNC
pueden utilizarse. `Path.GetFullPath` realiza normalización léxica, sin
consultar existencia ni resolver enlaces. No se crean carpetas o archivos.

Se derivan mediante `Path.Combine`:

| Propiedad | Convención |
| --- | --- |
| `InboxRoot` | `<WorkspaceRoot>/inbox` |
| `StagingRoot` | `<WorkspaceRoot>/staging` |
| `StateRoot` | `<WorkspaceRoot>/state` |
| `CacheRoot` | `<WorkspaceRoot>/cache` |
| `CatalogPath` | `<WorkspaceRoot>/state/AssetCatalog.db` |

`AssetCatalog.db` es un nombre genérico: la futura arquitectura tiene un
catálogo SQLite por universo. SQLite todavía no está implementado.
La conservación local de [Archive Storage — Fase 8](ARCHIVE_STORAGE.md) y el
repositorio de producción quedan scoped por el contexto del universo. Fase 8
valida ArchiveRoot, mantiene índice/lock propios por raíz y nunca escribe
ProductionRoot/WorkspaceRoot ni avanza JobState.

El objeto individual no comprueba permisos, existencia, enlaces ni
exclusividad de raíces entre configuraciones. En 2.6.2
`UniverseStorageIsolationValidator.Validate(configurations)` comprueba
solapamientos léxicos entre universos distintos antes de futuras escrituras.
No constituye una barrera de seguridad de filesystem por sí solo: los
componentes que escriban deberán mantener controles de contención y enlaces.

El validador compara las nueve combinaciones de Workspace/Production/Archive
de cada par de universos. Igualdad o contención en cualquier dirección produce
un `NapIssue` por par de raíces conflictivo: `universe_storage_overlap`,
Error + Stop. El `NapIssueReport` conserva orden de configuraciones/raíces.
El Message es genérico, sin rutas; SubjectPath señala la primera raíz y Detail
incluye ambos universos, tipos de raíz y paths. Ese detalle técnico no se
debe exponer externamente sin la futura política de contexto.

Se normalizan rutas en UniverseStorageConfig y se comparan con boundary de
segmento, eliminando separadores finales salvo los de raíz del volumen.
Windows usa OrdinalIgnoreCase; otras plataformas, Ordinal. Siblings y nombres
con prefijos similares no son ancestros. No hay I/O ni resolución de symlinks,
junctions, aliases de volumen o nombres físicos alternativos.
No se introduce política para raíces del mismo universo. Dos configuraciones
con el mismo UniverseId son un uso ambiguo del API y producen ArgumentException;
colección nula o elementos nulos también son errores de argumentos.

## Contexto explícito y separación de storage

`UniverseContext` contiene `Profile` y `Storage`, ambos no nulos e inmutables,
y expone `Id => Profile.Id`. Su constructor exige igualdad exacta por valor
entre `Profile.Id` y `Storage.UniverseId`; una mezcla falla inmediatamente
con `ArgumentException`. No depende de igualdad de referencias.

No existe `CurrentUniverse`, singleton mutable ni contexto global. Cada caller
deberá pasar explícitamente el contexto a las capas que lo necesiten; varios
contextos pueden coexistir sin cambiar una selección global del Core.

Toda futura operación de escritura de assets debe estar scoped por un
`UniverseContext`. Conceptualmente, workspace y archive podrán separar datos
en `.../universes/nimroel/...` y `.../universes/star_trek/...`, pero esas rutas
no se hardcodean: las raíces autorizadas ya vienen de `Storage`.
Ningún routing podrá seleccionar una raíz de otro universo mediante metadata
del asset. El resolver de 3.6 recibe la regla de profile/config conservada en
package.AssetRule.Routing y calcula destinos dentro de la raíz validada.
En 2.6.1 esta regla se documentó; ese capítulo no implementó routing ni cambió la
API de los componentes existentes que reciben rutas explícitas.

## Frontera read-only de producción — 3.1

`ProductionRepositoryValidator.Validate(UniverseContext)` obtiene únicamente
`context.Storage.ProductionRoot` y lee los atributos de esa raíz. Ausencia,
archivo y directorio raíz ReparsePoint generan respectivamente
production_root_missing, production_root_invalid y production_root_reparse,
todos Error + Stop. Solo FileNotFoundException/DirectoryNotFoundException se
tratan como ausencia; los demás errores operativos se propagan.

Un directorio normal produce `ValidatedProductionRepository`, sellado e
inmutable, con constructor internal sin I/O y UniverseId/RootPath exactos del
contexto. `ProductionRepositoryValidationResult` exige report limpio si y solo
si Repository no null. Las responsabilidades de UniverseStorageConfig y
UniverseContext permanecen intactas.

No se enumeran entries, inspeccionan ancestros reparse, resuelven links ni crean
carpetas. No se exige `.git`. **3.2 — Repository Scanner** consume esta raíz
validada y controla las entries internas. La política canónica Nimroel se fijó
en 3.5 y 3.6 calcula directorios sin I/O. Véase
[PRODUCTION_REPOSITORY_BOUNDARY.md](PRODUCTION_REPOSITORY_BOUNDARY.md).

## Fotografía raw del repositorio — 3.2

`ProductionRepositoryScanner.Scan(ValidatedProductionRepository)` revalida los
atributos actuales del root y recorre descendants con un Stack explícito.
Produce un `ProductionRepositorySnapshot` inmutable con identidad/raíz exactas,
files/directories incluidos los vacíos, RelativePath con `/`, nombres originales
y orden Ordinal. No aplica Naming v1 ni semántica de perfiles y no lee contents.

Cada descendiente ReparsePoint causa repository_entry_reparse (Error + Stop),
sin traversal ni resolución de targets; varios issues se ordenan por RelativePath.
Cualquier issue deja Snapshot null. `.git` y otras entries de infraestructura
se incluyen normalmente, sin interpretación ni ignores. La observación es
puntual, materializada en memoria, sin garantía transaccional ni cuota explícita
de entries/profundidad. Véase [REPOSITORY_SCANNER.md](REPOSITORY_SCANNER.md).

## Routing declarativo — 3.3

AssetRouteSegmentKind define exactamente Literal, Classification y AssetId.
AssetRouteSegment y AssetRoutingRule son sellados/inmutables, sin I/O ni
resolución de paths. Segments preserva orden mediante snapshot defensivo
read-only. Literal y nombre de dimensión usan Naming v1 sin corrección;
AssetId lleva Value null. No hay templates libres.

UniverseAssetRule añade Routing nullable; constructores y profiles v1/v2
conservan Routing null. Toda Classification usada por routing debe estar en
RequiredClassification de esa regla, no solo Allowed. Profile v3 exige
package_files + routing, con parsing estricto. El loader actual aplica dispatch
explícito v1/v2/v3/v4; los contratos históricos siguen intactos.
En 3.5 Nimroel migra a Profile v3 con su política canónica declarada; v2 queda
preservado como fixture histórico. Los destinos se calculan en 3.6. Véanse
[NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md](NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md),
[ROUTING_CONTRACT_V1.md](ROUTING_CONTRACT_V1.md) y
[UNIVERSE_PROFILE_V3.md](UNIVERSE_PROFILE_V3.md).

## Diff estructural puro — 3.4

ProductionRepositoryStructureDiffer.Compare(before, after) consume únicamente
snapshots materializados. Exige mismo UniverseId por valor y RootPath comparado
con OrdinalIgnoreCase en Windows, Ordinal en otras plataformas, sin normalización
ni I/O. RelativePath se compara siempre Ordinal, también en Windows; duplicados
dentro de un snapshot se rechazan como ArgumentException.

ProductionRepositoryStructureDiff conserva identidad/raíz de before y Changes
defensivas/read-only ordenadas Ordinal. Los únicos cambios son Added, Removed y
KindChanged; mismo path/mismo kind no cambia. No usa FullPath como identidad,
colapsa árboles, infiere moves/renames, detecta contenido, aplica ignores o usa
semántica de routing. No genera NapIssue: son observaciones neutrales.
Véase [STRUCTURAL_CHANGE_DETECTION.md](STRUCTURAL_CHANGE_DETECTION.md).

## Auditoría histórica y política canónica Nimroel — 3.5

El corte auditado de `robdor80/Videojuego_Nimroel` documenta 39 registros y 39
carpetas bajo `Worldbuilding/Direccion artistica/Assets`, las excepciones
semánticas `children`/`elder` y el casing histórico `Norgard/Treskal`. Para la
regla `portrait + portrait_npc`, la política canónica queda fijada como
`portraits/{culture}/{location}/{role}/{sex}/{asset_id}`, relativa a
ProductionRoot y con machine identifiers. No se infieren classifications desde
AssetId ni se ejecuta migración. 3.6 calcula destinos usando la regla retenida
en el package validado.

## Cálculo puro del directorio de producción — 3.6

`ProductionDestinationResolver.Resolve(ValidatedAssetPackage, ValidatedProductionRepository)`
produce `ProductionAssetDestination` sellado/inmutable: AssetKey, RootPath,
RelativeDirectory portable con `/` y FullDirectoryPath native normalizado.
La única autoridad es package.AssetRule.Routing, sin recarga de perfiles ni
snapshot. Exige identidad de universo por valor; routing ausente, dimensiones
faltantes o segmentos inseguros fallan cerrado. Comprueba Naming v1, dispositivos
Windows reservados en todas las plataformas y contención léxica estricta bajo
ProductionRoot. No infiere valores desde AssetId ni transforma casing.
No comprueba existencia/colisiones ni realiza I/O o escrituras. Resolver no
autoriza escribir. Véase [DESTINATION_RESOLVER.md](DESTINATION_RESOLVER.md).

## Base inmutable del plan — 4.1

`ProcessingPlanBuilder.Build(ValidatedAssetPackage, ValidatedProductionRepository,
ProductionAssetDestination)` comprueba universo, AssetKey completo y equivalencia
léxica del root, sin I/O ni re-routing. Produce ProcessingPlan sellado/inmutable
con identidad, tipo/perfil, classification completa, package root, manifest path,
archivos presentes por role y la misma instancia de destino recibida.
Classification y FilesByRole son snapshots Ordinal realmente read-only; el
manifest permanece separado. No hay operations list, timestamps, JobId,
existencia, colisiones ni escrituras. Es la base del plan, todavía sin contratos
de conversión/archive/hash ni grafo de ejecución. Véase
[PROCESSING_PLAN.md](PROCESSING_PLAN.md).

## Representación humana del plan — 4.2

`DryRunTextRenderer.Render(ProcessingPlan)` devuelve un string humano
determinista: identidad, classification completa, package/manifest, inputs por
role y destino congelado. Ordena dimensions/roles con StringComparer.Ordinal,
usa LF fijo sin newline final y escapa paths entre comillas en una sola línea.
No revalida fronteras, consulta filesystem ni recalcula routing. OPERATIONS
declara que las operaciones no están definidas; SAFETY no ejecuta ni autoriza
escrituras. Sin CLI, serializer, outputs inventados o DTO adicional de report.
Véase [DRY_RUN.md](DRY_RUN.md).

## Validación estructural del plan — 4.3

`ProcessingPlanValidator.Validate(ProcessingPlan, ProductionRepositorySnapshot)`
es la frontera pura de 4.3: exige universo/root coherentes y paths únicos según
plataforma. Recorre prefijos canónicos con `/`, permite directorios ausentes y
ancestors exactos; el primer casing conflict Windows, File blocker o Directory
final existente devuelve Error + Stop. Conserva FullPath observado como SubjectPath.
Sin scanner, diff, re-routing, source revalidation, contents, hashes o escrituras.
El report limpio es point-in-time y no autoriza ejecución. Véase
[PLAN_VALIDATION.md](PLAN_VALIDATION.md).

## Plan Logs v1 — 4.4 HECHO

PlanLogTextRenderer recibe exclusivamente ProcessingPlan + NapIssueReport y
devuelve un string determinista. Incluye identidad del universo/asset, destino
relativo y flags/code/severity/disposition, sin rutas absolutas/fuente ni
Message/SubjectPath/Detail. Sin I/O, persistencia, timestamps, JobId ni
revalidación; no añade contratos de ejecución. Véase [PLAN_LOGS.md](PLAN_LOGS.md).

## Portrait Conversion — 5.1 HECHO

Primitive independiente de universo, profile, ProcessingPlan y repository.
PortraitPngToWebpConverter recibe sourcePath + PortraitConversionSettings;
ImageSharp 3.1.12 managed/cross-platform decodifica PNG tras estructura,
pixel safety y ratio exacto, resizea sin crop con Lanczos3 y devuelve WebP lossy
en memoria. El converter recibe settings explícitos; desde 5.4 Profile v4
declara reglas genéricas y el resolver produce un snapshot de configuración,
sin ejecutar conversión. Nimroel declara 768×960 Q90 en config. Véase
[PORTRAIT_CONVERSION.md](PORTRAIT_CONVERSION.md).

## Perfiles genéricos — 5.4 HECHO

[Universe Profile v4](UNIVERSE_PROFILE_V4.md) añade conversion required nullable.
ImageConversionRule declara PNG → WebP, source role required `.png` +
`png_master`, output dimensions y quality. MaxInputPixels sigue siendo runtime.
ValidatedAssetPackage retiene AssetRule.Conversion; ImageConversionResolver
produce ResolvedImageConversion con path exacto, sin I/O ni ejecución y sin
branching por universo, asset type o production profile. v1/v2/v3 conservan
Conversion null. Nimroel solo añade Portrait master → 768×960 Q90; no Scene.
ProcessingPlan v1 no congela la regla y Dry Run v1 no muestra conversión;
una evolución futura podrá consumirla cuando se formalicen operaciones.

## No recorte silencioso — 5.5 HECHO

png_to_webp conserva el frame completo: ratio source/output idéntico, resize
proporcional, sin crop/pad/BoxPad/letterbox/distorsión. ImageConversionGeometryValidator
consume PngImageInfo + ResolvedImageConversion y usa exclusivamente HasAspectRatio
con productos long. Devuelve clean o image_conversion_aspect_ratio_mismatch
Error + Stop, sin I/O, decode, ejecución, quality o pixel budget. Portrait/Scene
comparten la primitiva matemática y mantienen errores históricos y Stretch
condicionado al ratio exacto. Las integraciones prueban Nimroel 4:5 y fixture
genérico 3:2, con STOP para ratios incompatibles. Profile v4/config intactos;
sin v5 ni campos de crop. Cualquier crop futuro exige contrato explícito
versionado. Véase [NO_SILENT_CROP.md](NO_SILENT_CROP.md).

## SHA-256 — 6.1 HECHO

[Sha256Digest / Sha256Hasher](SHA256_INTEGRITY.md) aportan identidad de bytes
genérica, sin Asset ID, universo, path, metadata o formato. Digest inmutable:
64 hex lowercase ASCII estrictos y value equality. Hasher stateless: BCL SHA-256
streaming con memoria acotada; path Open/Read/Share Read y stream desde posición
actual a EOF, caller-owned y no seekable permitido. No modifica source ni
traduce excepciones a issues. CRC ZIP sigue siendo integridad de transporte,
sin cambios. La primitiva se prueba sobre master validado y WebP en memoria,
sin almacenarla en modelos/plan o escribir sidecars. JobId ya existe desde 6.3;
6.4 añade estados y journal mínimo separado bajo StateRoot, sin acoplar hashes.
Orquestación y recovery siguen sin implementar.

## Duplicados — 6.2 HECHO

[AssetContentFingerprint / AssetDuplicateAnalyzer](DUPLICATE_DETECTION.md)
asocian UniverseAssetKey + Sha256Digest y comparan candidate/existing pairwise.
Solo el mismo UniverseId permite comparación: cross-universe produce
ArgumentException (existing), incluso con AssetId y digest iguales; no existe
universo global ni relaciones implícitas entre universos.
SameAssetSameContent es semánticamente idempotente y clean, sin demostrar Jobs
completados; SameAssetDifferentContent genera asset_content_collision Error + Stop;
DifferentAssetSameContent genera asset_content_possible_duplicate Warning + Continue;
Distinct es clean. Resultado sealed/inmutable, constructor internal con invariantes
de relación y report; SubjectPath null y diagnósticos canónicos/direccionales.
Cero I/O, hashing interno, catálogo, colección detectora, persistencia o SQLite.
No altera roles, clasificación, perfiles, routing, package/plan o storage roots.
Una futura capa podrá agregar comparaciones por pares dentro de cada universo;
no hay resolución automática ni autorización de escritura.

## Job ID — 6.3 HECHO

[JobId](JOB_ID.md) es la identidad global, opaca y estable de proceso: sealed
record con Value get-only, ToString exacto y Create mediante Guid.NewGuid().
Formato estricto `job_` + 32 lowercase ASCII hex, no all-zero, igualdad por valor
e independencia de cultura. No está scoped por universo y no contiene UniverseId,
AssetId, estado o timestamp. Dos universos no reutilizan intencionadamente el
mismo JobId. UniverseAssetKey sigue siendo UniverseId + AssetId; ambas identidades
son distintas. JobId no elimina UniverseContext explícito ni crea un «current
universe». Un Job futuro podrá asociarse a un universo sin embebido en su ID.
No crea Jobs persistidos, modelo/lifecycle, I/O, SQLite, schemas o dependencias;
no cambia ProcessingPlan o renderers. Desde 6.4 existe un journal de estados
separado; [recovery 6.5](JOB_RECOVERY.md) HECHO mediante discovery read-only.
Fase 6 está HECHA. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.

## Estados persistentes — 6.4 HECHO

[JobState / machine / record / store](JOB_STATES.md) fijan los nueve estados
canónicos, progresión normal sin saltos/retrocesos/same-state y FAILED desde
estados activos. COMPLETED/FAILED son terminales. JobStateRecord asocia JobId
global con UniverseId y estado inmutables; no redefine JobId o UniverseAssetKey.
JobStateStore exige UniverseContext y solo usa context.Storage.StateRoot,
con journal <job_id>.json y JSON v1 estricto. JobId interno y universo deben
coincidir con solicitud/contexto; mismatch causa InvalidDataException + STOP.
Nunca busca en otros universos, infiere universo del ID o crea CurrentUniverse.
Create no sobrescribe; Transition valida antes de publicar temp hermano
completo/flushed/cerrado mediante move. No escribe assets u otros storage roots,
no guarda hashes/fingerprints y no cambia PLAN/renderers, schemas o config.
[6.5 — Recovery](JOB_RECOVERY.md) enumera únicamente StateRoot, reutiliza Load
y clasifica checkpoints activos/COMPLETED/FAILED del contexto. Cada temp se
registra sin abrirlo; ambigüedades producen STOP. No hay reconciliación física,
promoción/limpieza de temps o avance automático de estado.

## Portrait Output Validation — 5.2 HECHO

PortraitWebpOutputValidator consume únicamente PortraitWebpImage + settings,
sin universo, source PNG, plan, repository o routing. Valida metadata, contenedor,
decode WebP forzado y dimensiones reales, con el primer fallo Error + Stop y
SubjectPath null. Quality es metadata contractual, sin inferencia del bitstream.
Sin dependencias nuevas, I/O, re-encode, repair, hashes o autorización de escritura.
Véase [PORTRAIT_OUTPUT_VALIDATION.md](PORTRAIT_OUTPUT_VALIDATION.md).

## Scene Conversion — 5.3 HECHO

SceneConversionSettings/SceneWebpImage/SceneConversionResult y converter/validator
propios, sin dependencia de tipos Portrait, universo, plan o routing.
Parámetros explícitos: PNG validado, pixel safety/ratio exacto antes de decode,
resize Lanczos3 sin crop, WebP lossy y validación en memoria. Solo se comparte
el helper internal de contenedor RIFF/WEBP, extraído sin cambios observables
de Portrait. No existe perfil canónico Nimroel Scene; ejemplos técnicos no
deciden resolución, ratio, quality, production_profile, routing o clasificación.
Config intacta, sin filesystem output ni hashes. Véase [SCENE_CONVERSION.md](SCENE_CONVERSION.md).

## Continuidad y alcance

NAP.Core no añade conceptos, clases de perfiles, vocabularios o rutas propios
de Nimroel. Detector, readiness, stager, extractor ZIP, PNG, NapIssue y Naming
siguen genéricos y no necesitan depender del universo para su función local.
NAP nació originalmente como Nimroel Asset Pipeline. Tras evolucionar a una
arquitectura multiuniverso, el nombre oficial pasa a ser Nexus Asset Platform.
Nimroel permanece como el primer Universe Profile soportado. Esta decisión
de marca no introduce cambios técnicos de arquitectura.

Manifest v1, su modelo y schema permanecen intactos como contrato histórico;
no se añade `universe_id` a v1 ni se vincula automáticamente a un universo.
`NAP_CONTINUIDAD_NAP1.md` tampoco cambia. [Manifest v2](MANIFEST_V2.md) es el
contrato vigente para nuevos assets, con `universe_id` obligatorio y sin
requisitos de clasificación específicos del primer universo en su schema.
El DTO conserva un string de transporte para universe_id; el runtime usa
UniverseId/UniverseAssetKey. `ManifestUniverseScope.Matches` comprueba el
boundary contra el contexto activo; un mismatch futuro deberá causar STOP.
El helper no es un PackageValidator. En 2.8.2, PackageSemanticValidator lo
reutiliza tras carga estricta de manifest: el mismatch detiene la validación
antes de aplicar rules del perfil activo. No se integra un pipeline global.
Los ejemplos específicos de Nimroel de la documentación anterior conservan
su valor como ejemplos del primer universo, no como reglas universales.

La hoja de ruta vigente es:

- **2.6 — Multi-Universe Foundation: HECHO.**
- **2.6.1 — Core Universe Scope: HECHO.** Los seis tipos y sus tests.
- **2.6.2 — Manifest v2 + Nimroel profile configuration: HECHO.**
  Contratos genéricos, loader, perfil Nimroel, classification y aislamiento.
- **2.7 — ZIP deliberadamente incorrectos para tests: HECHO.**
  Auditoría genérica documentada en [ADVERSARIAL_ZIP_TESTS.md](ADVERSARIAL_ZIP_TESTS.md).

- **2.8 — Package Semantic Validation: HECHO.** Insertado tras 2.7.
- **2.8.1 — Package Contract v1 + Universe Profile v2: HECHO.** Contrato
  genérico de package flat; manifest universal externo a PackageFiles; Nimroel
  portrait_npc exige PNG master, prompt, info y Visual Identity, sin WebP de entrada.
- **2.8.2 — Package Semantic Validator: HECHO.** Manifest v2 estricto,
  validación read-only con contexto explícito y ValidatedAssetPackage inmutable.
  Véase [PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md).

**Fase 2 HECHA (2.1–2.8). Fase 3 — Routing y repo HECHA (3.1–3.6).**

- **3.1 — Production Repository Boundary: HECHO.** Validación read-only de raíz.
- **3.2 — Repository Scanner: HECHO.** Fotografía raw de una raíz validada.
- **3.3 — Routing Contract v1: HECHO.** Configuración pura, sin calcular destinos.
- **3.4 — Structural Change Detection: HECHO.** Diff puro de snapshots.
- **3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy: HECHO.** Primera interpretación de estructura y política declarativa real.
- **3.6 — Destination Resolver: HECHO.** Cálculo puro de directorios con la regla retenida en el package validado.
- **Fase 4 — PLAN / Dry Run: HECHA dentro del alcance v1.**
- **4.1 — ProcessingPlan: HECHO.** Base inmutable de hechos validados y destino calculado.
- **4.2 — Dry Run: HECHO.** Representación textual humana determinista del plan, sin I/O.
- **4.3 — Plan Validation: HECHO.** Validación estructural pura del destino contra un snapshot materializado.
- **4.4 — Logs: HECHO.** Resumen textual privacy-safe.
- **Fase 5 — Conversión: HECHA.**
- **5.1 — Portrait: HECHO.** Conversión PNG → WebP en memoria, settings explícitos.
- **5.2 — Validar salida: HECHO.** Metadata/settings, decode WebP real y dimensiones, sin I/O.
- **5.3 — Scene: HECHO.** Conversión y validación en memoria; sin canon Nimroel.
- **5.4 — Perfiles genéricos: HECHO.**
- **5.5 — No recorte silencioso: HECHO.** Invariante full-frame genérica y validación geométrica exacta.
- **Fase 6 — Integridad: HECHA.** 6.1–6.5 completos.
- **6.1 — SHA-256: HECHO.** Digest canónico y cálculo streaming, sin persistencia.
- **6.2 — Duplicados: HECHO.**
- **6.3 — Job ID: HECHO.** Identidad global de proceso, sin lifecycle o persistencia.
- **6.4 — Estados persistentes: HECHO.** Journal mínimo solo bajo StateRoot.
- **6.5 — Recuperación tras fallo: HECHO.** Discovery durable read-only.
- **Fase 7 — Auditor IA: HECHA.** 7.1–7.5; contratos Core provider-neutral y adapter Gemini aislado, sin writes/lifecycle.
- **Fase 8 — TeraBox / Archive Storage: HECHA.** 8.1–8.6; paquete original completo, índice v1, copia incremental verificada, colisiones y estructura derivada del routing.
- **Fase 9 — Production Storage + Verified Completion: HECHA (9.1–9.4).** Writer genérico con archive prerequisite, WebP/documentación verificados, recovery físico y cierre COMPLETED.
- **Fase 10 — SQLite: SIGUIENTE.**

Routing solo podrá consumir ValidatedAssetPackage, después de validación semántica
completa, sin inferencias de universo ni fallback de rules.

Quedan pendientes schemas completos de assets/metadata, persistencia de logs,
vocabularios de valores, SQLite, backups generales y selector UI.
[Fase 9](PRODUCTION_STORAGE.md) consume routing/conversión declarativos con
preflight físico y escritura verificada. 5.1 añade únicamente
ImageSharp 3.1.12 y lee el source PNG; su WebP queda en memoria. Las capas
previas conservan su alcance de solo lectura: el loader solo lee configuración y 3.1 solo lee
los atributos de la raíz de producción; 3.2 enumera estructura sin leer contents.
