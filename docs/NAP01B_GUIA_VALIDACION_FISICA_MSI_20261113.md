# NAP01-B — Guía de comprobación física en MSI Raider (posterior a la PR)
Fecha: 2026-11-13. No es certificado de ejecución: el propietario realizará esta comprobación en Windows 11 x64.

## Precauciones obligatorias
Mantener servicio/autoridad del host en estado no productivo. No hacer CUTOVER, no tocar ACL/cuentas, no leer ni migrar la biblioteca real de assets, no registrar una clave Gemini real durante el smoke, no instalar servicios ni editar secretos/archivos de licencia. Usar raíz de laboratorio y configuración local temporal sin raíces de producción. No sustituir .NET 8 global ni borrar instalaciones previas.

## Preparación
1. En GitHub Desktop, tras una fusión **validada**, sincronizar `main`; comprobar el commit que se indique en el informe B04.
2. Instalar/confirmar **SDK .NET 10 x64** y el **Windows Desktop Runtime .NET 10 x64** desde Microsoft, si faltan. Abrir PowerShell en la raíz del repositorio.
3. Ejecutar:
```powershell
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
dotnet restore .\NAP.sln
dotnet build .\NAP.sln -c Release --no-restore
```
La compilación Release de Core/ImageSharp requiere una licencia **válida ya configurada localmente** mediante `SixLaborsLicenseFile` / `sixlabors.lic`, sin mostrarla, moverla ni enviarla a GitHub. Si falta, detener la compilación y registrar un fallo de licencia, no desactivar el control.

## Pruebas focalizadas (solo fixtures de test)
```powershell
$filter='(FullyQualifiedName~NAP.Tests.AutomationHostTests|FullyQualifiedName~NAP.Tests.AutomationHostControlTests|FullyQualifiedName~NAP.Tests.AutomationProtocolTests|FullyQualifiedName~NAP.Tests.AutomationWpfTests|FullyQualifiedName~NAP.Tests.CatalogQueryTests|FullyQualifiedName~NAP.Tests.CatalogSafetyTests|FullyQualifiedName~NAP.Tests.GeminiOperationAuditClientTests|FullyQualifiedName~NAP.Tests.VisualExplorerWindowsSmokeTests)&FullyQualifiedName!~RealCatalogFiltersDetailStatisticsAndThirtyThousandRecycledItemsRender'
dotnet test .\tests\NAP.Tests\NAP.Tests.csproj -c Release --no-build --no-restore --filter $filter --settings .\tests\functional.runsettings
```
Los tests de Gemini usan respuestas simuladas; no activan consultas reales ni acreditan acceso al proveedor.

## WPF visual en MSI — comprobación humana aislada
Crear/seleccionar únicamente un perfil local **sintético** del laboratorio. Abrir `dotnet run --project .\src\NAP.App\NAP.App.csproj -c Release --no-build` solo tras verificar que no se conecta a Host o raíces de producción. Confirmar que arranca, se presenta RobStyle, navega por Inicio/Ajustes/Herramientas y cierra, que no aparecen excepciones WPF ni diálogos de migración de bases reales. No iniciar procesamiento, operaciones de escritura, publicación ni conexión Gemini real.

## Anotar acta
Fecha, Windows build, CPU x64, SDK y Desktop Runtime 10.x, HEAD exacto, resultado de restore/build/test, incidencias, capturas WPF sin secretos, confirmación de raíces sintéticas y confirmación explícita de que no hubo operaciones productivas. Resultado físico: **PENDIENTE** hasta ejecución del propietario.
