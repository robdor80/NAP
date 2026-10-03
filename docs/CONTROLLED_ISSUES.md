# Incidencias controladas — Fase 2 · Capítulo 2.5

Los componentes conservan sus resultados locales: readiness, staging,
extracción ZIP y validación PNG mantienen sus estados y responsabilidades.
`NapIssueMapper` traduce un resultado ya obtenido a `NapIssue?`, sin ejecutar
el componente ni realizar I/O. Un resultado correcto devuelve `null`:
el éxito no es una incidencia y no tiene código de issue.

## Modelo e identidad

`NapIssue` es un record sellado e inmutable con propiedades de solo lectura:

| Propiedad | Significado |
| --- | --- |
| `Code` | Identificador estable de máquina, centralizado en `NapIssueCodes`. |
| `Severity` | Importancia para el usuario: `Info`, `Warning` o `Error`. |
| `Disposition` | Decisión de flujo: `Continue` o `Stop`. |
| `Message` | Fallback humano breve en inglés. No identifica la incidencia. |
| `SubjectPath` | Ruta opcional del recurso afectado. |
| `Detail` | Detalle técnico opcional del resultado local. |
| `StopsProcessing` | Derivada de `Disposition == Stop`. |

El constructor exige un código con forma machine identifier de Naming v1
mediante `AssetNamingRules.IsValidMachineIdentifier`: lowercase snake_case
ASCII, máximo 64 caracteres. No normaliza ni exige pertenecer a un vocabulario
cerrado; los códigos actuales se definen como constantes. Reutilizar esta
regla de forma no convierte una incidencia en metadata de asset.
También rechaza enums no definidos y mensajes nulos/vacíos/solo whitespace.
Las propiedades no admiten cambios posteriores que eludan esas condiciones.

`Code` es la identidad para logs, tests, UI, historial y futuras automatizaciones.
`Message` puede evolucionar sin cambiar esa identidad. La futura UI podrá
localizar mensajes a partir de `Code`; futuras UI/RoDo podrán consumir código
y contexto según una política de exposición todavía pendiente.

Severidad y flujo son independientes: `Warning + Stop` indica que este intento
no puede continuar aunque el problema sea transitorio. También es válido
`Error + Continue`; `HasErrors` no determina `ShouldStop`.
No se implementan retries ni ejecución automática del STOP.

## Mapeos actuales

Todos los mapeos siguientes producen `Stop`; los estados de éxito producen
`null` y no conservan contexto dentro de una incidencia inexistente.

| Componente | Estado local | Código | Severidad | SubjectPath |
| --- | --- | --- | --- | --- |
| Readiness | `Ready` | Sin issue | — | — |
| Readiness | `Missing` | `inbox_missing` | Warning | Argumento opcional |
| Readiness | `Changing` | `inbox_changing` | Warning | Argumento opcional |
| Readiness | `InUse` | `inbox_in_use` | Warning | Argumento opcional |
| Staging | `Staged` | Sin issue | — | — |
| Staging | `NotReady` | `staging_not_ready` | Warning | `SourcePath` |
| Staging | `Collision` | `staging_collision` | Error | `FinalStagedPath` |
| ZIP | `Extracted` | Sin issue | — | — |
| ZIP | `Collision` | `zip_collision` | Error | `FinalPath ?? StagedZipPath` |
| ZIP | `InvalidArchive` | `zip_invalid_archive` | Error | `FinalPath ?? StagedZipPath` |
| ZIP | `Rejected` | `zip_rejected` | Error | `FinalPath ?? StagedZipPath` |
| PNG | `Valid` | Sin issue | — | — |
| PNG | `Invalid` | `png_invalid` | Error | Argumento opcional |
| PNG | `UnsupportedFeature` | `png_unsupported_feature` | Error | Argumento opcional |

La ruta de staging señala el origen no preparado o el destino en colisión.
En el contrato ZIP actual, los fallos no tienen destino publicado: su contexto
principal es `StagedZipPath`. La preferencia por `FinalPath` solo se aplica
cuando el resultado ya proporciona esa ruta.
El `ReadinessStatus` incluido en staging no cambia el código `staging_not_ready`.

`SubjectPath` se conserva literalmente, sin comprobar existencia, abrir,
anonimizar, relativizar ni normalizar. PNG por stream y readiness pueden
carecer de ruta. ZIP y PNG copian `Reason` a `Detail`, incluido `null`.
Readiness y staging no aportan `Reason`, por lo que su `Detail` es `null`.
El fallback no incluye el detalle técnico ni interpola rutas.
En 2.6.2 se añade `universe_storage_overlap` para raíces de distintos universos
iguales o contenidas entre sí: Error + Stop, Message genérico, SubjectPath de
una raíz y Detail técnico con ambas raíces/universos. Se produce directamente
desde el validador puro de aislamiento, sin cambiar los mapeos anteriores.
`Detail` debe contener información técnica controlada, nunca excepciones
completas, stack traces ni secretos. Este capítulo no añade un sanitizador:
el caller es responsable de los detalles que crea directamente.

```csharp
var result = new PngMasterValidator().Validate(stream);
var issue = NapIssueMapper.Map(result); // Sin ruta para este stream.
var report = new NapIssueReport(issue is null ? [] : [issue]);
bool canContinue = report.CanContinue;
```

## Report

`NapIssueReport(IEnumerable<NapIssue>)` toma una copia ordenada de la colección
recibida y expone `IReadOnlyList<NapIssue>` mediante un wrapper de solo lectura.
Modificar la colección original no modifica el report; tampoco se puede
modificar el almacenamiento desde su colección pública. Los issues son
inmutables. No deduplica ni reordena. Rechaza colección o elementos nulos.

- `IsClean`: no hay incidencias.
- `HasErrors`: alguna incidencia tiene severidad `Error`.
- `ShouldStop`: alguna incidencia tiene disposición `Stop`.
- `CanContinue`: negación de `ShouldStop`.

Un report vacío está limpio y permite continuar. El report no almacena
resultados locales ni realiza procesamiento, logging o persistencia.

## Package Semantic Validation — 2.8.2

PackageSemanticValidator crea directamente issues de contrato de package:
package_root_invalid, package_structure_invalid, package_manifest_missing,
package_manifest_ambiguous, package_manifest_invalid,
package_manifest_filename_mismatch, package_root_name_mismatch,
package_universe_mismatch, package_rule_not_found, package_classification_invalid,
package_required_file_missing, package_unexpected_file y
package_content_validator_unsupported. Todos son **Error + Stop**, centralizados
en NapIssueCodes; no se amplía artificialmente el mapper para estas decisiones.

Para png_master se reutiliza el mapeo PNG existente con el path concreto:
png_invalid/png_unsupported_feature, Reason → Detail. Los fallos de JSON/Naming
del manifest son conocidos y controlados; errores operativos de E/S siguen
propagándose. No se incluyen JSON completo, valores de clasificación ni stack
traces en issues de manifest/classification.

PackageSemanticValidationResult solo lleva ValidatedAssetPackage si el report
está limpio. El orden y SubjectPath/Detail de cada código están documentados en
[PACKAGE_SEMANTIC_VALIDATION.md](PACKAGE_SEMANTIC_VALIDATION.md).

## Production Repository Boundary — 3.1

`ProductionRepositoryValidator` crea directamente tres códigos de NapIssueCodes:

| Código | Condición | Severity | Disposition | SubjectPath |
| --- | --- | --- | --- | --- |
| `production_root_missing` | FileNotFoundException o DirectoryNotFoundException al leer la raíz | Error | Stop | ProductionRoot absoluto exacto |
| `production_root_invalid` | La raíz existe pero no es directorio | Error | Stop | ProductionRoot absoluto exacto |
| `production_root_reparse` | El directorio raíz mismo es ReparsePoint | Error | Stop | ProductionRoot absoluto exacto |

La raíz procede exclusivamente de UniverseContext.Storage.ProductionRoot.
Se lee únicamente File.GetAttributes de esa raíz, sin enumerar entries ni
exigir Git. Otros errores de acceso/I/O se propagan: no se confunden con missing.
No se cambian NapIssueMapper ni los mapeos previos.

`ProductionRepositoryValidationResult` solo lleva ValidatedProductionRepository
con report limpio; cualquier issue impide adjuntarlo. Véase
[PRODUCTION_REPOSITORY_BOUNDARY.md](PRODUCTION_REPOSITORY_BOUNDARY.md).

## Excepciones y límites

El mapper no atrapa excepciones. Resultados nulos o estados no reconocidos
son errores de programación y lanzan `ArgumentNullException` o
`ArgumentOutOfRangeException`. El modelo y el report también propagan sus
errores de argumentos. No hay `catch (Exception)` ni handler global.

Las excepciones operativas/programáticas que antes propagaban los componentes
siguen propagándose: argumentos incorrectos, cancelación, fallos de acceso/I/O
no contemplados, agotamiento de memoria y otros errores inesperados. Se
mantienen sus capturas locales existentes para resultados de dominio conocidos;
2.5 no amplía ni reinterpreta esos boundaries. El futuro orquestador decidirá
dónde manejar las excepciones globales.

2.5 todavía **no es PackageValidator**: no añade validador de manifest/asset,
orquestador, registry, routing, ProcessingPlan, conversiones, UI, RoDo,
telemetría, localización o almacenamiento. No cambia seguridad ZIP, parser PNG,
Naming ni Manifest v1. No hay nuevas dependencias ni I/O en esta capa.

Los tests cubren todos los estados actuales, códigos estables/únicos, contexto,
guardas de argumentos, independencia de severidad y flujo, orden e inmutabilidad
del report. La hoja de ruta vigente inserta **2.6 — Multi-Universe Foundation**
y desplaza los ZIP deliberadamente incorrectos para tests a **2.7**.
