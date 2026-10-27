# F8.1 · Procedimiento físico para Codex Local · MSI Raider GE78 HX

**Código exacto que ejecutar: `47b76af46b266795647618aa34ee5eee190bc33e`.** Base oficial de partida: `ae6aa5b05a47917e7c53f854c4506725cc7c1bf8`. Los documentos de entrega posteriores no cambian ese código. No usar main por aproximación ni la antigua carpeta F7. No es instalación, CUTOVER ni autorización productiva.

## 1. Precondiciones y límites

Windows 11 real del MSI, sesión interactiva **sin elevación**, PowerShell 7 x64, Git, SDK .NET 8 y Windows Desktop Runtime .NET 8 x64 disponibles. Verificar localmente modelo/SO y versiones; no instalar/reemplazar NAP, drivers ni herramientas de monitorización que cambien servicios/ACL/tareas. Si falta un requisito, registrar bloqueo. No suponer acceso remoto desde Cloud.

Leer la auditoría F8.0 y la implementación Cloud incluidas en el commit. Verificar que el SHA es ancestro de la rama Draft entregada. #47 y #49 deben seguir abiertas; no ejecutar Actions. Esta actividad solo valida navegación visual del GitView original. No arrancar `NAP.App.exe`, AutomationHost, CLI, herramientas F6 ni ejecutables de la carpeta histórica. No cambiar flags para sortear un fallo.

La licencia original ImageSharp 3.2.0 Community ya fue validada en Fase 5. **No pedir una nueva clave**, leerla, copiarla, mostrarla ni subirla. Verificar privadamente su archivo original existente y seleccionarlo en la variable de proceso `SixLaborsLicenseFile`, con su ruta local privada; la documentación no conoce ni necesita esa ruta. No colocar la licencia en el checkout/app/evidencias. Si no se puede localizar sin examinar contenido privado, detener la compilación Release y reportar ausencia de selección, sin buscar secretos en Cloud ni cambiar el validador. No usar `SixLaborsLicenseKey`, binlogs, verbosidad diagnóstica, transcript de secretos ni helper de Actions.

## 2. Clone independiente pinado

En PowerShell 7, sin administrador, crear únicamente un destino temporal nuevo. No alterar variables HOME/USERPROFILE/TEMP ni usar un checkout productivo. Estos son comandos Git de desarrollo del laboratorio, nunca operaciones desde NAP:

```powershell
$pin = '47b76af46b266795647618aa34ee5eee190bc33e'
$source = Join-Path ([IO.Path]::GetTempPath()) ('NAP-F81-Source-' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $source) { throw 'El destino debe ser nuevo.' }
# Antes de clonar: verificar que TEMP y sus padres existen y no son reparse points ni están dentro de un repo.
for ($dir = [IO.DirectoryInfo]::new([IO.Path]::GetTempPath()); $null -ne $dir; $dir = $dir.Parent) {
    if (-not $dir.Exists -or ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Test-Path (Join-Path $dir.FullName '.git'))) { throw 'TEMP no aislado; detener.' }
}
git clone --no-checkout https://github.com/robdor80/NAP.git $source
if ($LASTEXITCODE -ne 0) { throw 'No se pudo clonar; no usar un repo real como alternativa.' }
git -C $source checkout --detach $pin
if ($LASTEXITCODE -ne 0) { throw 'Commit no disponible.' }
if ((git -C $source rev-parse HEAD) -ne $pin) { throw 'SHA incorrecto.' }
git -C $source status --short
```

No hacer push, merge ni cambios en GitHub. No reutilizar ni borrar carpetas anteriores. Los accesos a GitHub/NuGet de clone/restore son herramientas de preparación; el proceso NAP visual no contacta con red.

## 3. Preparación y contrato antes del arranque interactivo

Una vez seleccionada privadamente la licencia original en el proceso, ejecutar:

```powershell
& (Join-Path $source 'scripts/Start-F81GitVisualLab.ps1') -ExpectedCommit $pin -PrepareOnly
```

El script se niega ante plataforma, ruta, checkout, .NET o licencia no seleccionados. Crea un root **nuevo** `%TEMP%\NAP-F81-Lab-<GUID>` con `app`, `build`, `evidence` y marcador. No admite root arbitrario. Ejecuta exactamente:

```powershell
dotnet publish '<source>\src\NAP.App\NAP.App.csproj' -c Release -r win-x64 --self-contained false `
  -p:Phase81GitLabOnly=true -p:Phase6AcceptanceOnly=false `
  --artifacts-path '<lab>\build' -o '<lab>\app' --verbosity minimal -nodeReuse:false
```

Los placeholders de esta explicación los resuelve el script con las rutas nuevas. Release debe terminar con **cero errores** y validación oficial Six Labors satisfecha. Los avisos preexistentes se conservan y no cierran #49. Si falla, no ejecutar Debug como sustitución para certificar Release ni añadir `ContinueOnError`, claves inventadas u otras versiones.

Preflights que ejecuta el preparador sobre el exe dedicado:

- Activación + root + `--f81-verify-isolation`: exit 0 y `F81_ISOLATION_CONTRACT_OK`; no crea modelo/ventana ni lee settings.
- Activación incorrecta: exit 1 sin ventana.
- Argumentos correctos más `--settings=forbidden`: exit 1 sin fallback.
- Timeout, `.lic` en app, perfiles instalados o ejecutable dedicado ausente: detener. El timeout solo puede finalizar el PID de preflight recién creado.

Comprobar los logs `contract.*`, `rejected-activation.*`, `rejected-settings.*`, `source-commit.txt` y `binary-hashes.json` dentro de evidence. No subir logs del build si revelan rutas/datos privados; publicar solo resumen saneado de validador aprobado, versiones y resultado. No exponer licencia ni su hash/contenido.

Antes de abrir: comprobar que solo existen las carpetas esperadas, todos sus padres son ordinarios, app no está dentro de un repo productivo y el comando usa **NAP.GitVisualLab.exe**. Comparar hashes de app con el manifiesto inmediatamente antes y después de la sesión. Los binarios contienen dependencias Core transitivas; el contrato exige la composición sintética, no ausencia de todas las DLL productivas.

## 4. Observación y comando de apertura exacto

Anotar `$lab` con la ruta recién indicada por el preparador; no elegir otra carpeta. Registrar antes de abrir, por medios ya disponibles y sin modificar el equipo: procesos NAP existentes y sus PID, servicios/tareas NAP conocidos y metadatos básicos de ubicaciones NAP persistentes conocidas (sin leer secretos ni inventariar proyectos/ZIP). La ausencia de procesos adicionales en una foto no demuestra todo el ciclo. Si se dispone de observación de archivos/handles ya activa, usarla filtrada al nuevo PID; no instalar/iniciar servicios o drivers para obtenerla.

Comando que abre únicamente la variante correcta:

```powershell
& "$lab\app\NAP.GitVisualLab.exe" '--f81-git-visual-lab=NAP-F81-SYNTHETIC-READONLY-v1' "--f81-root=$lab"
```

Para conservar diagnóstico y PID durante la navegación, usar esta forma equivalente (con `$lab` ya verificado):

```powershell
$exe = Join-Path $lab 'app/NAP.GitVisualLab.exe'
$options = @('--f81-git-visual-lab=NAP-F81-SYNTHETIC-READONLY-v1', "--f81-root=`"$lab`"")
$session = Start-Process -FilePath $exe -ArgumentList $options -PassThru `
  -RedirectStandardOutput (Join-Path $lab 'evidence/local-session.stdout.txt') `
  -RedirectStandardError (Join-Path $lab 'evidence/local-session.stderr.txt')
$session.Id
```

Solo una sesión; no sobrescribir evidencia de una sesión previa. Para repetir usando el preparador sin `-PrepareOnly`, se creará otro lab y se lanzará con captura/validación automática al cierre. No reutilizar ni borrar las evidencias anteriores.

La ventana debe tener título F8.1, DATOS FICTICIOS y SOLO LECTURA; el banner deniega operaciones. No debe haber shell de producción, icono tray, selector de proyectos, diálogos de rutas o solicitudes de credenciales. Si aparece cualquiera de ellos, cerrar solo esa ventana/PID y bloquear. Verificar que no nace AutomationHost/Git/GCM ni un proceso hijo de NAP, que no hay conexiones de ese PID ni endpoints/handles NAP productivos. Las consultas nativas `Get-CimInstance Win32_Process` y `Get-NetTCPConnection`/`Get-NetUDPEndpoint` pueden ayudar de modo read-only; ausencia de filas en una consulta aislada no prueba ausencia histórica. Si las herramientas disponibles no permiten demostrar todo el ciclo, explicitar qué garantía sigue basada en código y no certificar observación completa.

## 5. Navegación real y tamaños

Sin acciones operativas ni datos reales:

1. Disabled: comprobar resumen sin autorización, listas vacías y mensajes. Actualizar solo reconstruye memoria.
2. Manual, AutoCommitBatch y AutoCommitAndPushBatch: comprobar modo efectivo y permisos independientes. En AutoCommitBatch push no está concedido ni en el fixture. Los diez botones operativos deben permanecer deshabilitados en todos los escenarios, también con actor/prefijos/máximo provisionales válidos.
3. Expandir inspección factual e historial; seleccionar lotes e intents (la selección de uno limpia la otra); recorrer 24 filas con ratón/teclado/scroll. Expandir detalle técnico. Seleccionar fila y pulsar **Seleccionar recibo nativo**: debe seleccionar el receipt del mismo OperationId, sin recovery ni disco.
4. Revisar Prepared/Committed/Pushed en la lista real de recibos. Cambiar selección y actualizar; no debe haber bindings rotos ni selección residual incorrecta.
5. Revoked: revisión 2/grant nuevo, filas históricas revisión 1, guía de revisión/revocación. No se renuevan permisos.
6. Recovery: estados bloqueados, evidencia no disponible, RequireNoPending y agotamiento de publicación; todos los botones de recuperar/retry/push siguen bloqueados.
7. Empty y Error: mensaje vacío / STOP y banner de error, sin datos anteriores; volver a un escenario válido debe restaurar las listas ficticias. Probar Tab/Shift+Tab, flechas, Home/End, selección y expanders sin ejecución productiva.

La interfaz Git contiene texto operativo original (incluida explicación de inspección remota); en el laboratorio su botón permanece bloqueado y la observación factual es ficticia. No pulsar herramientas externas para seguir recomendaciones del fixture.

En la pantalla física **2560 × 1600**, registrar resolución, DPI/escalado actual y tamaño lógico de ventana; maximizar y también probar ventana 1440×900, 1200×700 y 1000×650 aproximadamente, respetando mínimo 800×600. No confundir píxeles físicos con unidades WPF. Si otros DPI 125/150/200 % no pueden probarse sin cambiar ajustes de la sesión, registrar pendiente en vez de alterar el equipo. Comprobar títulos, contraste RobStyle, wraps de SHA/IDs, scroll vertical hasta el final, listas, foco/teclado, botones legibles y ausencia de clipping; no sustituir controles ni crear capturas simuladas. Una limitación de diseño se reporta, no autoriza modificar producción.

## 6. Evidencias, cierre seguro y aceptación

Guardar únicamente en `lab/evidence`, con nombres nuevos: identificación saneada del MSI/SO/runtime/DPI, SHA de código y hashes de binarios, resultados Release/licencia (sin secretos), preflights, PID y observaciones temporales de procesos/red/archivos/IPC, capturas reales de los cuatro modos, Revoked/Recovery/Empty/Error, selección→receipt y tamaños. Anotar inicio/fin y resultados de navegación en una matriz. No publicar rutas personales ni capturas con otros datos del escritorio. No subir binarios ni toda la carpeta build/restores automáticamente.

Detención normal: cerrar la ventana dedicada. No hay host, tray, timers operativos ni estado que drenar. Esperar al PID exacto, comprobar exit 0 y `F81_WPF_BINDING_ERRORS=0` en stderr. Con la forma Start-Process: `$session.WaitForExit(); $session.Refresh(); $session.ExitCode`. Si hay bloqueo/indicios de efecto productivo, cerrar o finalizar **solo el PID dedicado identificado** y preservar datos; no matar NAP 1.0 ni limpiar archivos, journals/Outbox/backups.

Después: comprobar que no queda el PID ni hijos y que los binarios y metadatos observados no cambiaron inesperadamente. Los fixtures nunca se guardan en disco y las ubicaciones ficticias workspace/production/archive no deben existir; solo preparador/build/evidence tienen archivos. No borrar el laboratorio ni el clone al acabar.

**Aceptar físicamente solo si**: commit y hashes correctos; Release oficial aprobado; preflights seguros; interfaz original realmente abierta en el MSI; navegación y tamaños acreditados; cero bindings erróneos; todas las mutaciones/red/IPC NAP ausentes según el alcance observado; ningún efecto productivo; cierre seguro. Distinguir observación física de inferencia de código y de limitaciones del SO.

**Bloquear** ante dependencia/configuración faltante, licencia no seleccionada/válida, ruta/enlace sospechoso, ventana/binario equivocado, botón operativo habilitado, credenciales/red/IPC/host inesperados, escritura fuera de lab, error de binding o imposibilidad de sostener aislamiento lógico. No resolverlo cambiando servicios/ACL/tareas/secretos, usando datos reales ni lanzando arranque ordinario. Entregar informe con límites a ChatGPT. **Codex Local no cierra F8.1: se requiere auditoría favorable de ChatGPT. #47/#49 siguen abiertas.**
