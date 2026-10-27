# NAP 2.0 · F8.1 · Entrega Cloud

2026-10-27. Base: `ae6aa5b05a47917e7c53f854c4506725cc7c1bf8`. Desarrollo en `chatgpt/nap-2-f8-safe-msi-lab`. **Pendiente ejecución MSI y auditoría independiente.**

## Cambios

- `NAP.App.csproj`: variante explícita `Phase81GitLabOnly=true`, ensamblado `NAP.GitVisualLab`, manifiesto asInvoker existente, sin perfiles instalados de producción. Rechaza combinación con `Phase6AcceptanceOnly=true`.
- `App.xaml.cs`: composición exclusiva F8.1 mediante preprocesador; el binario normal rechaza `--f81*` antes de construir servicios. No modifica sus rutas ordinarias.
- `Phase81GitLab`: verifica contrato antes de modelos; `--f81-verify-isolation` verifica y sale. Ventana contenedora con selector de escenarios y banner; un `ContentControl` recibe el **GitViewModel productivo**. El DataTemplate RobStyle existente crea el **GitView.xaml productivo**, sin copia ni imagen simulada. La ventana no es un sustituto de la pantalla Git: aloja sus controles originales con un adaptador para los bindings del Window.
- `GitVisualLabContract`: argumentos cerrados, consentimiento exacto, root temporal directo GUID, app dentro, marcador, rechazo de repos/enlaces/rutas persistentes/datos inesperados; sin escribir ni crear carpetas.
- `GitVisualLabService`: cerrado/sellado, solo memoria; reutiliza proyecciones `AutomationReadViews` marcadas no nativas. No hereda el servicio productivo ni contiene runners, red, IPC, disco escritor o coordinadores. Expone la interfaz de gestión existente y deniega sus mutaciones aun por invocación directa. No existe adaptador nativo alternativo para commit/push/recovery.
- `GitVisualLabViewModel`: adapta comandos y propiedades del Window. Actualizar, editar provisionalmente y seleccionar/ver recibo solo usan memoria; diez comandos operativos tienen permiso permanentemente falso y delegado denegador. No usan comandos productivos con permiso condicionado por fixtures. El ViewModel Git y su lógica de selección/estado/guía son los existentes.
- `Start-F81GitVisualLab.ps1`: prepara en **Windows**, desde clone temporal limpio/pinado, un root nuevo; compila Release licenciado para win-x64, sin F6; hashes de binarios, preflights positivos/negativos, redirección de evidencias en root y lanzamiento exclusivo. No limpia archivos ni cambia servicios/tareas/ACL. `-PrepareOnly` permite observar antes del arranque interactivo.
- `GitVisualLabF81Tests`: pruebas focalizadas portables. `tools/NAP.GitVisualLabProbe`: prueba separada de IL de la aplicación compilada, sin cargar ni ejecutar WPF. Separarla evita exigir un build WPF previo a las suites normales del proyecto de tests.

**Sin cambios en Core, dependencias, licencias, workflows, motor, vista Git, recursos RobStyle, IPC ni esquemas.** No se implementa F8.2–F8.6.

## Catálogo determinista

| Escenario visual | Datos de los contratos existentes |
|---|---|
| Disabled | Modo Disabled, revisión 0, sin autorización ni registros. |
| Manual | Modo Manual; permisos commit/push ficticios; 24 lotes, 24 intents y 24 receipts. Ningún permiso habilita una acción del laboratorio. |
| AutoCommitBatch | Commit autorizado ficticio, AllowPush=false; mismos estados para mostrar mensajes de permiso independiente. |
| AutoCommitAndPushBatch | Ambos permisos ficticios, modo oficial exacto; continúa sin capacidad operativa. |
| Revoked | Disabled, Revoked=true, revisión 2 y AuthorizationId nuevo; historial 1/2; operaciones antiguas conservan revisión/autorización original. |
| Recovery | AutoCommitAndPushBatch, Prepared/Committed/Pushed; filas Blocked por política/evidencia, Committed con `git_push_retry_3`; guía original de intervención/agotamiento. No ejecuta recuperación. |
| Empty | Sin recibos/Outbox y mensaje vacío original. Es un escenario visual, no un nuevo modo Git. |
| Error | STOP sintético de integridad; limpia selección, configuración y evidencia anteriores sin fallback. Banner y EmptyMessage muestran el error. |

Outbox solo Pending/Committed/Blocked/Pushed. Receipts solo Prepared/Committed/Pushed. IDs de universo, Job, QueueItem, Attempt y operación usan formatos canónicos; SHA/fingerprints/dates son ficticios y deterministas. No hay URL ni repositorio real; no se presentan estas proyecciones como archivos durables autenticados. La colección de 24 elementos permite scroll/virtualización/selección; el botón de consultar recibo vincula OperationId en memoria. No existen imágenes prefabricadas de la ventana.

## Verificaciones ejecutadas en Cloud

Linux x64; .NET SDK 8.0.427 instalado en `/tmp/nap-dotnet`. Sin licencia privada. Comandos desde raíz del repo, binario dotnet de ese SDK:

```sh
dotnet build src/NAP.App/NAP.App.csproj -c Debug \
  -p:Phase81GitLabOnly=true --artifacts-path /tmp/nap-f81-build --verbosity minimal

dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Debug \
  --filter FullyQualifiedName~GitVisualLabF81Tests \
  --artifacts-path /tmp/nap-f81-tests --results-directory docs/f8-1-evidence \
  --logger 'trx;LogFileName=f81-cloud-delivery.trx' --verbosity minimal

dotnet build tools/NAP.GitVisualLabProbe/NAP.GitVisualLabProbe.csproj -c Debug \
  --artifacts-path /tmp/nap-f81-probe
dotnet /tmp/nap-f81-probe/bin/NAP.GitVisualLabProbe/debug/NAP.GitVisualLabProbe.dll \
  /tmp/nap-f81-build/bin/NAP.App/debug/NAP.GitVisualLab.dll

dotnet build src/NAP.App/NAP.App.csproj -c Debug \
  --artifacts-path /tmp/nap-f81-normal-build --verbosity minimal
```

| Verificación | Resultado y evidencia |
|---|---|
| WPF lab Debug cruzada | **0 errores**, 7 avisos reportados; `build-lab-debug.log`. Incluye avisos oficiales de licencia ausente y CS1998/CS0649 por ramas excluidas. No se suprimen. |
| Tests finales | **7/7 Passed, 0 Failed, 0 Skipped**; `f81-cloud-delivery.trx`, `tests-delivery.log`. |
| IL de arranque compilado | PASS, `probe-il.log`; llama Phase81GitLab y valida contrato, sin composición productiva en los métodos inspeccionados. No ejecuta el ensamblado WPF. |
| Binario normal Debug | **0 errores**, `build-normal-debug.log`; compila la rama ordinaria y guarda F8.1. No se abre NAP normal. |
| Propiedades F6+F81 simultáneas | Error esperado `RejectConflictingLaboratories`, `conflicting-build.log`; no es una compilación aprobada. |
| PowerShell 7.4.7 | Parser y construcción de argumentos con espacios PASS, `powershell-parse.log`. Guard no-Windows rechaza ejecución Linux antes de crear directorios/compilar/lanzar (`nonwindows-rejection.log`). No acredita Start-Process/Win32 en MSI. |
| Git diff / integridad | `git diff --check`; sin diferencias en vista/temas/Core/.github. Manifiesto SHA256 de fuentes y evidencias junto a los resultados. |

Cobertura de los siete tests: activación exacta/localización; falta/corrupción del contrato y entradas extra; ancestros Git y symlink Linux real; fixtures/ViewModel/selección/refresh/denegación y ausencia de archivos nuevos; cuatro modos/permisos/revocación; llamadas directas/cancelación/contexto extraño/dependencias nulas; composición sin dependencias operativas, rama de arranque y compatibilidad de todas las propiedades Window referidas desde GitView. La prueba IL complementa el último test sobre el binario real.

El test de symlinks solo crea enlace real en Linux; en Windows no solicita privilegios ni cambia políticas. NTFS/junctions quedan para observación Local. La inspección de fields/fuentes/IL no es una prueba formal de todos los efectos transitivos ni de confinamiento del SO.

## Fallos intermedios conservados

Primera compilación de tests: acceso indebido al plan interno del receipt y overload xUnit síncrono para Task; corregidos antes de ejecutar tests. Primera ejecución: 3 Passed/4 Failed por ID sintético con guion incompatible con Naming v1 y búsqueda de fuentes fuera del checkout; conservada en `f81-cloud-before.trx`. Después 7/7 (`f81-cloud.trx`), y 8/8 con la comprobación IL entonces integrada (`f81-cloud-final.trx`). La entrega separa ese chequeo como herramienta portátil: **7 tests + probe IL**, no ocho escenarios finales de xUnit. El primer `dotnet run --artifacts-path` del probe buscó el executable en ruta predeterminada y falló sin ejecutar NAP (`probe-launch-before.log`); se corrigió invocando explícitamente el DLL construido. No se borran estos resultados ni se agregan como cobertura independiente.

## Límites y bloqueos

No se ha abierto una ventana WPF en Cloud, compilado Release F8.1, usado licencia privada, ejecutado NAP en MSI, procesado ZIP, creado repos Git para tests ni arrancado host/IPC/GCM/IA. No se repiten suites/estrés históricos. Los avisos históricos ImageSharp y los problemas IPC/StorageUnavailable de #49 siguen abiertos; el restore/build actual no certifica su resolución.

La composición no escribe archivos; el launcher y las herramientas de build sí escriben el nuevo laboratorio y sus evidencias. No se afirma confinamiento físico ni protección frente a un proceso externo que altere el ejecutable o la carpeta tras validación. No se impide actividad del SO/antivirus fuera de NAP. Observación Windows, render, DPI, ruta de licencia privada y validador Release quedan para Codex Local siguiendo el runbook. Ante cualquier incertidumbre de aislamiento, detener, conservar evidencias y no cambiar al arranque normal.

Publicación: revisar nuevamente main/.github antes de push/PR. Los cuatro YAML de base tienen tres triggers solo manuales y un push exclusivo en `codex/nap-2-fase-6-ux`; ningún trigger pull_request. Esta rama queda fuera. No se cambia YAML, protección ni habilitación administrativa; **cero workflows invocados**. PR únicamente Draft, sin merge, sin cerrar #47/#49.
