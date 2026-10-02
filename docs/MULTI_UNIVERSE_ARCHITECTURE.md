# Multi-Universe Foundation — 2.6.1 · Core Universe Scope

NAP es un sistema genérico de producción, conservación, catalogación,
auditoría y planificación de assets organizado por universos/perfiles.
Nimroel es el primer universo real previsto, no una dependencia del núcleo.
El nombre del producto sigue siendo NAP; el significado histórico de las
siglas no se redefine en este subcapítulo.

La separación `CoreRPG + Universe Pack` (consola + cartucho) tiene su
equivalente conceptual en `NAP Core + Universe Profile`. El núcleo contiene
capacidades genéricas; las reglas, vocabularios y destinos específicos
llegarán desde perfiles/configuración. Star Trek y Star Wars son solamente
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

`UniverseProfile` contiene únicamente `UniverseId Id` y `string DisplayName`,
ambos de solo lectura. Rechaza ID nulo y nombre nulo/vacío/solo whitespace.
El nombre humano puede contener espacios, acentos y puntuación: no sirve
como identidad, ruta ni clave de búsqueda.

`UniverseRegistry` recibe perfiles, toma un snapshot ordenado y expone
`IReadOnlyList<UniverseProfile>` mediante una colección de solo lectura.
Cambiar la colección original no cambia el registry. Rechaza colección
nula, perfiles nulos y IDs duplicados por igualdad de `UniverseId`, aunque
los nombres humanos difieran. Puede estar vacío.
`TryGet(UniverseId, out UniverseProfile?)` busca por identidad, devuelve
`false` y `null` si no encuentra el perfil y rechaza un ID nulo.
El futuro selector de UI podrá consumirlo; no hay discovery, JSON, watchers,
reload, service locator ni framework de DI.

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

El objeto no comprueba permisos, existencia, solapamientos, enlaces ni
exclusividad de raíces entre configuraciones. No constituye una barrera de
seguridad de filesystem por sí solo. La configuración futura debe asignar
raíces separadas por universo; los componentes que escriban deberán mantener
sus controles de contención y seguridad al usar esas raíces.

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
no se añade `universe_id` ni se vincula automáticamente un manifest v1 a un
universo. `NAP_CONTINUIDAD_NAP1.md` tampoco cambia.
Los ejemplos específicos de Nimroel de la documentación anterior conservan
su valor como ejemplos del primer universo, no como reglas universales.

La hoja de ruta vigente es:

- **2.6 — Multi-Universe Foundation: en curso.**
- **2.6.1 — Core Universe Scope: HECHO.** Los seis tipos y sus tests.
- **2.6.2 — Manifest v2 + Nimroel profile configuration: PENDIENTE, siguiente.**
  Formalizará `universe_id` y trasladará requisitos específicos al perfil.
- **2.7 — ZIP deliberadamente incorrectos para tests: PENDIENTE.**
  Corresponde al antiguo 2.6.

Quedan pendientes carga JSON de perfiles, schemas de clasificación/metadata,
reglas de producción/routing, perfil Nimroel completo, PackageValidator,
SQLite, TeraBox, conversiones y selector UI. No hay dependencias nuevas ni
I/O nuevo fuera de las APIs puras de Path.
