# NAP01-B — B04 Auditoría final, aceptación técnica y pendientes físicos

**Fecha:** 13 de noviembre de 2026  
**Proyecto:** NAP 2.0 — Nexus Asset Platform  
**Base inalterada hasta el cierre:** `main@1f52f14d18dce8e5579686e65a490e5885d12e9d`  
**Rama:** `feature/nap01b-dotnet10-lts-20261113`  
**PR:** https://github.com/robdor80/NAP/pull/64  
**HEAD de código auditado:** `0a7a72339ca8c4fbcc8c5e6c9dc99b6a949681dc`

## 1. Alcance y diff revisado

Se ha inspeccionado el diff íntegro de la PR hasta el commit de código auditado (24 archivos), contra la base indicada. Los cambios se concentran en:
- 11 archivos `.csproj`: TFM `net10.0`, `net10.0-windows` en WPF; `Microsoft.Data.Sqlite` 10.0.15. No se cambian entidades, procedimientos SQL, schemas, ni serializaciones.
- `global.json`: SDK 10.0.100 mínimo con `latestFeature`; sin prereleases.
- Corrección de rutas de salida de prueba y laboratorio que apuntaban al TFM `net8.0`.
- `src/NAP.App/Views/WorkflowView.xaml` y `src/NAP.App/Views/GitView.xaml`: `ToolTip` enlazados a `DisabledReason` **ya existente** en los comandos; no se cambian autorizaciones, callbacks ni política de Git.
- `src/NAP.App/VisualSmokeProbe.cs`: expansión explícita de sección manual en modo smoke, búsqueda de controles en árbol visual anidado y diagnóstico; no interviene en ejecución normal. `MainWindow.xaml.cs` imprime excepción completa únicamente en el catch del modo smoke.
- Workflow **nuevo y restringido a rama NAP01-B**, documentación y README.
- No se modifica `main` directamente, `ci.yml`, workflows históricos de CUTOVER, servicios, secretos, ni evidencia antigua.

**Decisión sobre trabajos históricos:** Los scripts de test y publicación Phase 6 y los workflows manuales antiguos conservan anclajes `net8.0` y runtimes explícitos 8.0.31/8.0.32. Se preservan por ser evidencia/laboratorios de otra fase; **no se han ejecutado ni validan builds de .NET 10**. Antes de reutilizarlos en NAP06, deben revisarse y adaptar sus rutas/licencias de empaquetado, sin afirmar aquí una publicación de distribución.

## 2. Pruebas reales en GitHub Actions Windows 2022 x64

Workflow: `.github/workflows/nap01b-net10-validation.yml`.  
Run **final de código**: https://github.com/robdor80/NAP/actions/runs/43459164301  
Run ID: `43459164301` · Job ID: `132001158414` · Commit: `0a7a72339ca8c4fbcc8c5e6c9dc99b6a949681dc` · **SUCCESS**.  
Artefacto de evidencia (TRX, .NET info, runtimes, listado NuGet y diagnóstico): https://github.com/robdor80/NAP/actions/runs/43459164301/artifacts/14054554412  
SHA-256 del ZIP de artifact según log Actions: `787713297778eb3e1865c0d176de4caf651eac8c2294156f65e8111cc7d06425`.

| Puerta | Resultado comprobado |
| --- | --- |
| Windows Server 2022 x64, .NET SDK 10 | PASS |
| `dotnet restore NAP.sln` | PASS |
| `dotnet build NAP.sln -c Release --no-restore` con licencia Six Labors oficial | PASS, 0 errores |
| `NAP.AcceptanceIoProbe` independiente | PASS |
| `NAP.LiveBootstrap` independiente | PASS |
| `NAP.PrivatePackageProbe` independiente | PASS |
| Tests filtrados Core/AutomationHost/Protocol/WPF/SQLite/Gemini | **100 PASS, 0 FAIL, 0 SKIP** (33 s de ejecución) |
| Inventario de paquetes/advisories generado en el runner | Paso PASS; salida conservada en artifact, sin atribuir por ello ausencia de vulnerabilidades |
| Limpieza del archivo temporal de licencia en el runner | PASS |
| NAP CI general / CUTOVER / ZIP de volumen | **NO EJECUTADOS** (deliberadamente) |
| Windows 11 WPF en MSI del propietario | **NO REALIZADO** |

**Advertencias:** Dos `CA2014` (stackalloc en bucle) en `src/NAP.Core/LegacyPortraitReader.cs:91` y `src/NAP.Core/LegacyMasterEvidenceIndex.cs:82`, sin errores de compilación; registrar para tratamiento independiente, no modificar algoritmos históricos durante la migración. Los riesgos NU1902/NU1903 históricos de ImageSharp del issue #49 no se declaran resueltos por esta compilación.

### Registro de iteraciones y correcciones

| Ejecución | Commit | Resultado | Aprendizaje |
| --- | --- | --- | --- |
| [43456730730](https://github.com/robdor80/NAP/actions/runs/43456730730) | `0ea4d78` | Build solución PASS; job FAIL | Helper seguro consume y borra la variable temporal en cada invocación, dividir builds auxiliares en pasos |
| [43457136293](https://github.com/robdor80/NAP/actions/runs/43457136293) | `b506cf6` | Build completo + herramientas PASS; **97/100** tests | 3 fallos WPF sin detalles suficientes |
| [43457780587](https://github.com/robdor80/NAP/actions/runs/43457780587) | `d7fed94` | **97/100** tests | Diagnóstico de ToolTip ausente y búsqueda por Namescope de imágenes |
| [43458556769](https://github.com/robdor80/NAP/actions/runs/43458556769) | `c16ee56` | **97/100** tests | Normalización resuelta; detectadas ayudas ausentes en botones Git |
| [43459164301](https://github.com/robdor80/NAP/actions/runs/43459164301) | `0a7a723` | **100/100 PASS**, build + 3 herramientas PASS, run SUCCESS | Aceptación técnica focalizada |

## 3. Compatibilidad y dependencia de seguridad

- SQLite 10.0.15 integrado en Core, comprobado en pruebas temporales sintéticas. **No se ha abierto, convertido ni migrado ninguna base productiva.**
- Gemini: únicamente respuestas HTTP simuladas/test, sin claves ni llamadas reales.
- WPF: smoke Windows runner en `net10.0-windows`, sin equivaler a aceptación visual/hardware Windows 11 del MSI.
- ImageSharp: versión 3.2.0 conservada, comprobación de licencia del build activo; no se modifica, copia ni revela el secreto. La revisión de avisos en #49 sigue independiente.
- Las dependencias xUnit y TraceEvent se mantienen sin actualizar por inercia, con sus respectivos assemblies compilados.

## 4. Puertas de seguridad y autorización

- PR #62 sigue **Draft abierta**, no rebasada ni fusionada.
- Issues [#47](https://github.com/robdor80/NAP/issues/47) y [#49](https://github.com/robdor80/NAP/issues/49) siguen **abiertos** al auditar.
- Los scripts auxiliares, CUTOVER, `NAP CI` general, permisos/ACL/servicios de MSI y raíces reales de assets permanecen fuera de alcance.
- No se ha añadido updater, cuenta de servicio, privilegio ni migración de datos.
- No se ha ejecutado Codex.

## 5. Decisión de B04 y puerta posterior

**Aceptación técnica B01–B04: FAVORABLE PARA FUSIÓN**, exclusivamente para el código migrado auditado si la última comprobación del HEAD y las protecciones de GitHub también pasan. La validación del MSI **no está certificada** y es tarea explícita del propietario conforme a [guía de validación física](NAP01B_GUIA_VALIDACION_FISICA_MSI_20261113.md).

Antes de iniciar NAP02 sobre `main`, el propietario deberá comprobar SDK/Windows Desktop Runtime .NET 10 x64, build local con licencia ya configurada, tests focalizados y arranque WPF con perfiles **sintéticos**; sin CUTOVER, procesos/host productivos, publicaciones ni acceso a bases reales. Documentar resultado y fecha separados de esta fusión.

> La aceptación técnica de la migración no abre la puerta operativa de #47. No extrapolar al MSI el PASS de un runner.
