# Multi-Universe Foundation y Package Contract — 2.6 y 2.8.1

NAP es un sistema genérico de producción, conservación, catalogación,
auditoría y planificación de assets organizado por universos/perfiles.
Nimroel es el primer universo real previsto, no una dependencia del núcleo.
El nombre del producto sigue siendo NAP; el significado histórico de las
siglas no se redefine en este subcapítulo.

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
conserva intacto como histórico. En 2.8.1 Nimroel pasa a
[Universe Profile v2](UNIVERSE_PROFILE_V2.md), que exige package_files en cada
asset rule. El loader detecta versión y aplica estrictamente v1 o v2; admite
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
La conservación futura en TeraBox/archive y el repositorio de producción
también quedan scoped por el contexto del universo.

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
del asset. El routing futuro recibirá sus reglas desde profile/config y
resolverá destinos dentro de las raíces del contexto activo.
En 2.6.1 esta regla se documenta; no se implementa routing ni se cambia la
API de los componentes existentes que reciben rutas explícitas.

## Continuidad y alcance

NAP.Core no añade conceptos, clases de perfiles, vocabularios o rutas propios
de Nimroel. Detector, readiness, stager, extractor ZIP, PNG, NapIssue y Naming
siguen genéricos y no necesitan depender del universo para su función local.
La identidad histórica del producto se conserva sin decidir una expansión
nueva de las siglas NAP.

Manifest v1, su modelo y schema permanecen intactos como contrato histórico;
no se añade `universe_id` a v1 ni se vincula automáticamente a un universo.
`NAP_CONTINUIDAD_NAP1.md` tampoco cambia. [Manifest v2](MANIFEST_V2.md) es el
contrato vigente para nuevos assets, con `universe_id` obligatorio y sin
requisitos de clasificación específicos del primer universo en su schema.
El DTO conserva un string de transporte para universe_id; el runtime usa
UniverseId/UniverseAssetKey. `ManifestUniverseScope.Matches` comprueba el
boundary contra el contexto activo; un mismatch futuro deberá causar STOP.
No es un PackageValidator ni se integra todavía en un pipeline global.
Los ejemplos específicos de Nimroel de la documentación anterior conservan
su valor como ejemplos del primer universo, no como reglas universales.

La hoja de ruta vigente es:

- **2.6 — Multi-Universe Foundation: HECHO.**
- **2.6.1 — Core Universe Scope: HECHO.** Los seis tipos y sus tests.
- **2.6.2 — Manifest v2 + Nimroel profile configuration: HECHO.**
  Contratos genéricos, loader, perfil Nimroel, classification y aislamiento.
- **2.7 — ZIP deliberadamente incorrectos para tests: HECHO.**
  Auditoría genérica documentada en [ADVERSARIAL_ZIP_TESTS.md](ADVERSARIAL_ZIP_TESTS.md).

- **2.8 — Package Semantic Validation: EN CURSO.** Insertado tras 2.7.
- **2.8.1 — Package Contract v1 + Universe Profile v2: HECHO.** Contrato
  genérico de package flat; manifest universal externo a PackageFiles; Nimroel
  portrait_npc exige PNG master, prompt, info y Visual Identity, sin WebP de entrada.
- **2.8.2 — Package Semantic Validator: PENDIENTE, siguiente.**

**Fase 2 EN CURSO DE NUEVO; 2.1–2.7 HECHOS. Fase 3 no iniciada.** Routing
no recibirá packages extraídos sin validación semántica completa previa.
El contrato prepara esa capa; PackageValidator todavía no existe.

Quedan pendientes schemas completos de assets/metadata, reglas de
producción/routing, vocabularios de valores, PackageValidator, SQLite,
TeraBox, conversiones y selector UI. No hay dependencias nuevas ni escrituras
de filesystem en esta capa: el loader solo lee configuración.
