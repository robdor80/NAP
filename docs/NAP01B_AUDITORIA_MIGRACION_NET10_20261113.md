# NAP01-B — Auditoría integral y migración a .NET 10 LTS
Fecha: 2026-11-13 | Fuente: `main@1f52f14d18dce8e5579686e65a490e5885d12e9d`

## Ámbito y política
Migración exclusiva de framework, SDK, dependencias estrictamente justificadas, rutas de ejecución y pruebas. Sin cambios en protocolos, bases de datos de producción, entidades, permisos, claves privadas, CUTOVER ni NAP CI general. La PR #62 continúa Draft; #47 y #49 siguen abiertas. No confundir validación de runner con MSI físico.

## B01 — Inventario completo del árbol
Se han localizado **11 proyectos C#**, todos `net8.0` o `net8.0-windows` antes de migrar:
- `src/NAP.Core`: biblioteca de dominio, persistencia y SQLite.
- `src/NAP.AI`: integración de auditoría Gemini por HTTP.
- `src/NAP.Presentation`: presentación reutilizable y modelos de UI.
- `src/NAP.App`: ejecutable WPF + Windows Forms, Windows-targeting.
- `src/NAP.AutomationHost`: proceso independiente de automatización.
- `src/NAP.Cli`: herramienta de comandos.
- `tests/NAP.Tests`: xUnit, smoke tests WPF y pruebas de coordinación.
- `tools/NAP.CoordinationProbe`: proceso de observación entre runtimes.
- `tools/NAP.AcceptanceIoProbe`: observador de E/S (TraceEvent).
- `tools/NAP.LiveBootstrap`: inicialización de laboratorio.
- `tools/NAP.PrivatePackageProbe`: verificador de paquete.
La solución `NAP.sln` incluye ocho proyectos; tres herramientas (`AcceptanceIoProbe`, `LiveBootstrap`, `PrivatePackageProbe`) exigen compilación independiente. No había `global.json` de raíz. La prueba `CopyCoordinationPeer` y tres archivos de pruebas WPF/AutomationHost contienen rutas codificadas de `net8.0`.

## Dependencias y decisión técnica B02
| Paquete | Antes | Después | Razón |
| --- | --- | --- | --- |
| Microsoft.Data.Sqlite | 8.0.31 | **10.0.15** | Alinear con la línea soportada de .NET 10; no cambiar esquema SQLite |
| SixLabors.ImageSharp | 3.2.0 | **3.2.0** | Preservar licencia oficial, API y correcciones de seguridad ya implantadas; auditar advisories del feed, sin ocultarlos |
| Microsoft.NET.Test.Sdk | 17.12.0 | 17.12.0 | Sin cambio preventivo de infraestructura de pruebas; someter a ejecución real |
| xunit | 2.9.2 | 2.9.2 | Sin cambio funcional; verificar descubrimiento/ejecución |
| xunit.runner.visualstudio | 2.8.2 | 2.8.2 | Sin cambio funcional; verificar descubrimiento/ejecución |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.1.21 | 3.1.21 | Herramienta auxiliar; verificar compilación en .NET 10 |

.NET 10 LTS, publicado en noviembre de 2025, está soportado hasta noviembre de 2028 (Microsoft). Se exige SDK 10 mediante `global.json` con mínimo 10.0.100 y `latestFeature`, sin usar SDK 11 por defecto.

## Riesgos controlados
1. **ImageSharp**: conservar `SixLaborsLicenseFile`, el helper `Invoke-LicensedDotnet.ps1` y el secreto ya existente; no imprimir contenido ni añadir licencia al repositorio. La base de advisories puede arrastrar avisos de 3.2.0 corregidos en origen; revisión explícita en lugar de supresión global.
2. **SQLite**: cambio mayor a 10.0.15; comprobar consultas y persistencia sobre bases **temporales sintéticas**. No migrar datos productivos ni formato.
3. **WPF**: `net10.0-windows` con `UseWPF`, `UseWindowsForms` y `EnableWindowsTargeting` intactos. El runner Windows compila y prueba, pero no certifica al MSI.
4. **Proyectos fuera de NAP.sln**: compilación independiente necesaria.
5. **Workflows históricos**: `ci.yml` general, `cutover-laboratory.yml`, `phase6-msi-preparation.yml`, `phase6-review.yml` y scripts de certificación histórica conservan versiones .NET 8 y contratos de experimentos antiguos. **No se ejecutan ni se adaptan a la nueva línea en esta fase**: en especial las pruebas de convivencia `8.0.31/8.0.32` y el manifest legal de publicación Phase 6. Antes de reutilizarlos en NAP06 requieren revisión específica; no son evidencia válida de NAP01-B.
6. **PR/ramas ajenas**: ninguna rama funcional se rebasa, modifica ni fusiona. El trabajo se hace contra el `main` verificado.

## B03 — Matriz de validación
Workflow focalizado nuevo: `.github/workflows/nap01b-net10-validation.yml`, únicamente `push` de la rama NAP01-B o `workflow_dispatch` explícito. Windows Server 2022 x64, SDK 10, restore, solución Release, tres proyectos auxiliares, pruebas sintéticas limitadas de Core/AutomationHost/WPF/SQLite/Gemini, inventario de dependencias y artifact TRX con retirada garantizada de licencia. **No invoca NAP CI ni CUTOVER**, no hace regresión completa ni procesa ZIP masivo.

**Estado inicial de evidencias**: implementación publicada; no presentar compilaciones, tests o software acceptance como PASS hasta leer un run terminado de GitHub Actions. El equipo MSI aún no ha sido verificado físicamente.

## B04 — Cierre
Fusionar solo con diff auditado y Windows build + test realmente satisfactorios. Sin estas evidencias, mantener PR Draft y documentar el impedimento. `#47`, `#49`, `#62`, secretos, licencias históricas y producción permanecen intocados.

## Referencias oficiales
- https://dotnet.microsoft.com/en-us/platform/support/policy
- https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15
- https://sixlabors.com/posts/security-support-and-license-enforcement/
