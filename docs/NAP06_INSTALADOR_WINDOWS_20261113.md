# NAP06 — Distribucion Windows x64 e instalador de laboratorio

Fecha: 2026-11-13. Repositorio: robdor80/NAP. Rama: feature/nap06-windows-installer-20261113. Base: main@cf547a323236932d4c062ea188fda39b62b3c7b0, con NAP01–NAP05 fusionadas.

## Proposito y fronteras

Esta fase prepara un NAP-Setup-2.0.0.exe clasico para Windows 11 x64. No publica una GitHub Release de produccion, no activa actualizaciones (NAP07), no certifica el MSI Raider (NAP08), no cambia ACL ni licencias, no registra servicios, no configura Gemini ni autoriza CUTOVER (NAP09/#47). Las incidencias #47 y #49 siguen independientes. Los trabajos automatizados utilizan un runner temporal, no el disco del propietario.

Se eligio Inno Setup 6 frente a WiX/Burn: NAP ya cuenta con dos ejecutables .NET 10 y no requiere bootstrapper MSI, servicio Windows, asociaciones ni cambios de registro global. Se fija el compilador Inno Setup 6.7.4 desde la release oficial inmutable is-6_7_4 de jrsoftware/issrc, validando attestation de GitHub y firma Authenticode del fabricante Pyrsys B.V. Si NAP se distribuye con fines comerciales, revisar previamente las condiciones de licencia de Inno Setup y Six Labors.

## Contrato del instalador

- Instalacion exclusivamente por usuario, sin elevacion: %LOCALAPPDATA%\Programs\NAP, carpeta fija; se rechaza cualquier /DIR alternativo.
- App WPF y AutomationHost se publican como win-x64 self-contained (runtime .NET 10 incluido). El Host reside en host/ y se inicia solo mediante configuracion externa explicita ya aceptada por los controles NAP02. El codigo conserva compatibilidad con el Host lateral de los builds de desarrollo.
- Icono .ico 256x256 generado del PNG RoDo del repositorio; icono en la aplicacion, bandeja, instalador y accesos directos. Inicio por menu NAP y escritorio optativo.
- No incluye launch.json, settings.json, Catalog DB, SQLite maestros, sidecars, journal, ZIP, claves Gemini, licencia Six Labors, ficheros de firma ni secretos. Los ficheros de configuracion y estado permanecen fuera de la carpeta de binarios.
- Sin autoarranque ni Run posinstalacion. Instalacion y desinstalacion no cierran aplicaciones en marcha automaticamente. Cerrar WPF y detener de manera controlada Host antes de reinstalar o desinstalar: una version futura NAP07 coordinara el drenaje.
- Desinstalacion Inno elimina solo archivos instalados; no incluye UninstallDelete hacia datos o repositorios. El directorio %LOCALAPPDATA%\NAP, maestros y rutas de universos son externos al paquete y deben permanecer intactos.
- Sin certificado de firma del producto; NO afirmar editor verificado ni ausencia de SmartScreen. NAP06 produce artefactos de laboratorio sin firmar. La firma Authenticode comprobada de Inno Setup se refiere a la herramienta compiladora, NO al instalador NAP.

## Construccion focalizada

Workflow: .github/workflows/nap06-windows-installer.yml. Unicamente push de la rama NAP06 y workflow_dispatch. No ejecuta ci.yml, NAP CI general ni suites de ZIP masivas.

1. Se obtiene Inno Setup oficial y se comprueban procedencia y firma antes de ejecutarlo.
2. Se genera el ICO del recurso RoDo, se restauran las referencias win-x64 y se publican la App y el Host de forma autocontenida e independiente. La licencia Six Labors solo se inyecta de forma temporal mediante el helper existente en Actions; nunca queda en el paquete.
3. Se verifica el inventario por SHA-256, la presencia de ejecutables y el perfil Nimroel, y se rechazan ficheros de datos/secretos. Se compila el instalador por ISCC.
4. Se escriben inventory.json (hashes de cada fichero), provenance.json (fuente, modo y hash del instalador) y NAP-Setup-2.0.0.exe.sha256.
5. La prueba instalada en Windows 2022 valida: checksum del instalador y payload, rechazo del Host sin configuracion, rechazo de /DIR hacia los datos, instalacion por usuario, vista previa WPF sintetica, desinstalacion y conservacion del marcador de datos personales.
6. Se conserva el instalador en el artefacto de Actions con retencion limitada. Sin publicacion automatica de Releases ni copias en rutas productivas.

Para construir localmente hace falta Windows x64, SDK .NET 10, la licencia Six Labors local existente y el ejecutable ISCC.exe validado. El comando, desde la raiz del checkout, es:

    pwsh ./scripts/Build-Nap06Release.ps1 -InnoCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" -Version '2.0.0'

El directorio artifacts/nap06 debe estar vacio/no existir al inicio; no se sobreescriben artefactos anteriores. No introducir rutas reales de produccion en estas pruebas.

## Aceptacion y pendientes

La CI del runner Windows x64 es una validacion tecnica de empaquetado, no una prueba fisica del MSI. Para aceptar NAP06, verificar run Windows y test-results.json PASS, SHA-256, listado de artefactos y ausencia de ficheros privados. Si falla algun gate, mantener PR Draft y no proclamar generado el .exe. 

NAP07 incorporara la actualizacion verificada, drenaje y rollback. NAP08 comprobara la instalacion, WPF, host, iconos, SmartScreen, alta/baja en el MSI con datos sinteticos bajo instrucciones separadas. NAP09 y #47 exigiran autorizacion especifica del propietario antes de tocar raiz, permisos o escritores reales.
