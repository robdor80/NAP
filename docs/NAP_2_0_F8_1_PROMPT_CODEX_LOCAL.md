# Prompt de continuación para Codex Local

Copiar el bloque completo siguiente en la sesión **local del MSI Raider GE78 HX**, sin acceso remoto desde Cloud:

```text
NAP 2.0 · F8.1 · EJECUCIÓN FÍSICA VISUAL AISLADA · 27/10/2026

Estás en Codex Local sobre el MSI Raider GE78 HX con Windows 11. Tienes autorización únicamente para completar las comprobaciones físicas pendientes del laboratorio visual F8.1. No certifiques cierre de F8.1: ChatGPT debe auditar las evidencias y aprobarlo después.

Código exacto autorizado: 9f15b796f7d6ae597e813f725091e5fc00eeab2e del repositorio https://github.com/robdor80/NAP. No ejecutar main, el arranque normal de NAP.App, la antigua carpeta F7, CLI ni AutomationHost. No usar Phase6AcceptanceOnly=true. La variante contiene el GitView WPF y GitViewModel originales de F7.4 con datos ficticios; la ventana contenedora y el selector sirven para navegar estos controles reales, no para sustituirlos.

Restricciones: no Actions, merge, instalación/sustitución de NAP, NAP 1.0, CUTOVER, servicios/cuentas/ACL/tareas/secretos, Git desde NAP, repos/assets/ZIP reales ni automatizaciones. No borrar carpetas, journals, Outbox, recibos, backups o evidencias. No cerrar #47/#49. No modificar el código durante este ensayo; reportar defectos y detener. No instalar drivers/herramientas que exijan cambiar servicios. No asumir que la carpeta histórica del laboratorio sigue existiendo.

Trabaja sin elevación. Verifica físicamente modelo, Windows 11, PowerShell 7 x64, Git, SDK .NET 8 y Windows Desktop Runtime 8 x64. La licencia Six Labors ImageSharp 3.2.0 Community original ya fue validada en Fase 5: no pedir nueva licencia ni leer/copiar/mostrar/subir la clave. Si su archivo original existente está disponible privadamente, selecciónalo en la variable de este proceso SixLaborsLicenseFile sin imprimir contenido o registrar secretos. No usar SixLaborsLicenseKey ni helper de Actions ni desactivar validadores. Si no puedes seleccionar la original, registra bloqueo de Release y detente; no la busques en Cloud ni inventes ruta.

Crear clone nuevo bajo TEMP, previamente verificando que TEMP y todos sus padres existen, no son reparse points y no están dentro de un repo. No tocar TEMP/HOME/USERPROFILE. En PowerShell 7:

$pin = '9f15b796f7d6ae597e813f725091e5fc00eeab2e'
$source = Join-Path ([IO.Path]::GetTempPath()) ('NAP-F81-Source-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $source) { throw 'Destino debe ser nuevo.' }
for ($dir = [IO.DirectoryInfo]::new([IO.Path]::GetTempPath()); $null -ne $dir; $dir = $dir.Parent) {
    if (-not $dir.Exists -or ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -or (Test-Path (Join-Path $dir.FullName '.git'))) { throw 'TEMP no aislado.' }
}
git clone --no-checkout https://github.com/robdor80/NAP.git $source
if ($LASTEXITCODE -ne 0) { throw 'Clone fallido; no usar repo productivo.' }
git -C $source checkout --detach $pin
if ($LASTEXITCODE -ne 0 -or (git -C $source rev-parse HEAD) -ne $pin) { throw 'SHA incorrecto.' }
if (git -C $source status --porcelain) { throw 'Checkout debe estar limpio.' }

Leer los documentos incluidos docs/NAP_2_0_F8_0_AUDITORIA_Y_CONTRATO_DE_AISLAMIENTO.md y docs/NAP_2_0_F8_1_IMPLEMENTACION_Y_PRUEBAS_CLOUD.md. El runbook físico y este prompt están en la entrega documental posterior; no necesitas cambiar el commit de ejecución para leerlos. No repetir pruebas Cloud ni suites/ZIP/Git históricos.

Con la selección privada de licencia preparada, ejecutar:
& (Join-Path $source 'scripts/Start-F81GitVisualLab.ps1') -ExpectedCommit $pin -PrepareOnly

El preparador crea un root TEMP/NAP-F81-Lab-<GUID> nuevo y compila Release win-x64 con Phase81GitLabOnly=true y Phase6AcceptanceOnly=false, validador original activo, artefactos y evidencias en root. Debe emitir NAP.GitVisualLab.exe; no abrir otro exe. Verifica exit codes y evidencias de preflight: contrato positivo exit 0 y F81_ISOLATION_CONTRACT_OK; activación incorrecta y opción --settings prohibida exit 1 sin ventana. Si cualquier comprobación falla/timeout, detener; no usar Debug/F6/arranque normal como fallback. No limpiar la carpeta. No subir archivos .lic ni logs que revelen información privada.

Asignar $lab exclusivamente a la ruta nueva indicada por el preparador. Antes de abrir: verificar SHA/hashes, padres ordinarios, app sin perfiles ni licencia; registrar procesos/servicios/tareas NAP existentes y metadatos de ubicaciones NAP persistentes conocidas mediante consultas de solo lectura, sin escanear proyectos/ZIP ni abrir configuraciones privadas. No debe haber host/IPC/coordinador NAP en la composición F8.1. Diferenciar aislamiento lógico probado en código de confinamiento del SO, que no existe. Mantener observación read-only por PID con herramientas ya disponibles; no iniciar servicios de monitorización.

Abrir exactamente una sesión dedicada con evidencia/PID:
$exe = Join-Path $lab 'app/NAP.GitVisualLab.exe'
$options = @('--f81-git-visual-lab=NAP-F81-SYNTHETIC-READONLY-v1', "--f81-root=`"$lab`"")
$session = Start-Process -FilePath $exe -ArgumentList $options -PassThru -RedirectStandardOutput (Join-Path $lab 'evidence/local-session.stdout.txt') -RedirectStandardError (Join-Path $lab 'evidence/local-session.stderr.txt')
$session.Id

No sobrescribir archivos de una sesión anterior. Debe aparecer título F8.1/DATOS FICTICIOS/SOLO LECTURA y banner de denegación. Si aparece shell productivo/tray/picker/credenciales, cerrar solo el PID dedicado y bloquear.

Navegar con controles WPF reales: Disabled, Manual, AutoCommitBatch, AutoCommitAndPushBatch, Revoked, Recovery, Empty y Error. No son modos nuevos: el catálogo usa los cuatro modos oficiales. Verificar configuración, permisos independientes, historial, lotes/intents, diagnósticos/intervenciones, recibos Prepared/Committed/Pushed y ausencia de datos anteriores tras Error. Las listas tienen 24 elementos en escenarios poblados: probar scroll, selección lote/intent mutuamente excluyente, expanders, Tab/Shift+Tab, flechas y actualización. Seleccionar una fila y usar 'Seleccionar recibo nativo' para comprobar vínculo OperationId en memoria. La edición provisional no guarda ni concede permisos. Los diez comandos operativos (incluida inspección remota, propuesta, guardar, revocar, commit, recovery, retry y push) deben permanecer deshabilitados en todos los estados; no intentar habilitarlos ni seguir las recomendaciones ficticias con herramientas externas.

Probar pantalla física 2560x1600, maximizado y ventanas aproximadamente 1440x900, 1200x700 y 1000x650. Registrar DPI real y distinguir unidades WPF/píxeles. No cambiar ajustes del equipo si afecta otras sesiones: documentar DPI adicionales pendientes. Comprobar RobStyle, contraste, clipping, wraps/IDs, scroll hasta el final y accesibilidad de controles. Capturas reales, nunca simulaciones.

Observar durante la sesión que NAP no crea host/Git/GCM/servicios/hijos ni contacta red/IPC/coord NAP; no usa settings, ProgramData o repos/ZIP/assets productivos. No escribe fixtures ni estado persistente; solo launcher/build/evidencia dentro de lab. Si una herramienta no permite acreditar todo el ciclo, indicar explícitamente qué queda basado en código, qué se observó físicamente y qué no se pudo observar. No presentar una consulta vacía de sockets o una foto como prueba completa. Si no puedes sostener el aislamiento lógico o aparece actividad inesperada, detener inmediatamente, conservar evidencia y bloquear. No corregir ACL/rutas/servicios como atajo.

Guardar en lab/evidence con nombres nuevos: SO/modelo/runtime/DPI saneados, commit/hashes, Release/licencia aprobado sin secretos, preflights, PID/observaciones, capturas de modos y tamaños y matriz de navegación. No subir otros datos del escritorio, rutas personales, binarios ni carpeta build automáticamente.

Cerrar normalmente la ventana dedicada; esperar $session.WaitForExit(); $session.Refresh(); comprobar ExitCode=0 y F81_WPF_BINDING_ERRORS=0 en stderr. Un error de binding bloquea aceptación. Si se cuelga, terminar solo ese PID identificado, nunca otro NAP. Verificar después ausencia del PID/hijos y cambios inesperados en binarios/metadatos conocidos. Preservar lab y clone. No borrar ni modificar datos reales.

Entregar informe preciso: qué se ejecutó físicamente, qué pasó/falló, capturas/evidencias y hashes, límites de observación y pendientes. Ningún resultado sintético Cloud equivale a ejecución MSI. No cerrar F8.1 ni #47/#49: ChatGPT debe auditar favorablemente el aislamiento, la UI auténtica, pruebas negativas, licencia Release, observación física y cierre antes de aceptar la fase visual. Esto nunca concede producción o CUTOVER.
```
