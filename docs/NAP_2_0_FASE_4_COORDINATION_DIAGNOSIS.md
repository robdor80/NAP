# Fase 4 — diagnóstico de coordinación y transición entre versiones

Fecha: 2026-10-15. PR: [#46, Draft](https://github.com/robdor80/NAP/pull/46).

**Actualización posterior:** el responsable eligió y autorizó la actualización obligatoria con control externo de permisos/arranque. Se inició un prototipo que solo está compilado y guardado como WIP remoto. A petición del responsable se pausa para apagar el ordenador; [estado y reanudación exactos](NAP_2_0_FASE_4_RESUME.md). Los apartados siguientes documentan el diagnóstico que motivó la decisión; la selección de la alternativa 1 ya está resuelta, su implementación y demostración siguen pendientes.

**Certificación bloqueada.** El responsable autorizó reforzar la coordinación independiente del CLR, manteniendo compatibilidad y exclusión frente a escritores antiguos. La reproducción confirma una incompatibilidad que impide una sustitución transparente con esas condiciones. Se detiene el cambio de coordinación antes de introducir un protocolo que los clientes antiguos puedan eludir. No se ha implementado ni certificado un coordinador nuevo.

## Reproducción independiente

El ejecutable `tools/NAP.CoordinationProbe` no referencia NAP ni abre configuraciones, activos, journals o bases reales. El driver `.github/scripts/Diagnose-Coordination.ps1` crea procesos independientes con `dotnet --fx-version`, conserva el propietario hasta que el competidor termina y registra versiones exactas. Usa exclusivamente un directorio temporal sintético. No modifica namespaces del CLR ni habilita opciones inseguras.

La primera matriz remota está en [Actions 38729934632](https://github.com/robdor80/NAP/actions/runs/38729934632), código `3effa261b0a310fd22577fbc19ea0a81884907c5`, artifacts `coordination-ubuntu-latest` y `coordination-windows-2022`, con `observations.json` y TRX. Las cuatro assertions existentes se ejecutan sin cambios.

| Entorno | PowerShell del driver | Runtime de PowerShell | Runtimes de peers forzados | SDK seleccionado / host | Git |
| --- | --- | --- | --- | --- | --- |
| Ubuntu 24.04.5 LTS, linux-x64 | 7.6.6 | .NET 10.0.12 | 8.0.31 y 8.0.32 | 10.0.401 / 10.0.13 | 2.55.0 |
| Windows Server 2022, 10.0.20348, win-x64 | 7.6.6 | .NET 10.0.12 | 8.0.31 y 8.0.32 | 10.0.401 / 10.0.13 | 2.56.0.windows.1 |
| Windows local, win-x64 | 7.6.5 | .NET 10.0.11 | 8.0.8, 9.0.8 y 10.0.11 | 10.0.400 / 10.0.11 | 2.52.0.windows.1 |

Los jobs instalan explícitamente SDK 8.0.425 y 8.0.426. No se confunde el SDK instalado con el runtime de PowerShell ni con los peers. Las pruebas originales de Windows invocan `powershell.exe`; el job de regresión completa registra también esa versión y su CLR, por separado del driver `pwsh`.

Resultados de la primera matriz, repetidos en **AutomationOwner, Job, Production y Catalog**:

| Propietario → competidor | Linux: exclusión | Windows: exclusión |
| --- | --- | --- |
| 8.0.31 → 8.0.31, mismo nombre Mutex | Sí | Sí |
| 8.0.32 → 8.0.32, mismo nombre Mutex | Sí | Sí |
| 8.0.31 → 8.0.32, mismo nombre Mutex | **No** | Sí |
| 8.0.32 → 8.0.31, mismo nombre Mutex | **No** | Sí |
| 8.0.31 → PowerShell/.NET 10.0.12, mismo nombre Mutex | Sí | Sí |
| 8.0.32 → PowerShell/.NET 10.0.12, mismo nombre Mutex | **No** | Sí |
| FileStream/FileShare.None, todas las parejas 8.0.31/8.0.32 | Sí, 16/16 | Sí, 16/16 |

En Linux las cuatro pruebas originales fallan **4/4**, con los mismos síntomas anteriores; en Windows pasan **4/4**. Es una divergencia reproducida entre namespaces de runtime, no una deducción a partir de un único fallo de timing. El caso de Job/Production llega a la API real del ejecutor mientras el otro proceso anuncia `LOCKED`: el rechazo requerido no ocurre. No se declara probado un workflow completo concurrente entre dos ejecutores NAP.

La ampliación de `426c976` ensaya además un propietario que sostiene **ambos**, el mutex original y un fichero exclusivo, frente a un cliente antiguo que intenta **exactamente ese mismo nombre de mutex** sin abrir el fichero. El propósito es verificar la transición del protocolo, no presentar el fichero diagnóstico como implementación productiva. [Actions 38730539826](https://github.com/robdor80/NAP/actions/runs/38730539826) registra **8/8 parejas cruzadas sin exclusión en Linux**, y 8/8 homogéneas con exclusión. Windows excluye las 16/16. Así se reproduce expresamente que el diseño aditivo mutex+fichero no garantiza transición segura con el cliente antiguo. La [copia de las 112 observaciones](NAP_2_0_FASE_4_COORDINATION_EVIDENCE.json), con versiones y commit, queda en el repositorio además del artifact original. En Windows local las 36 parejas de runtimes/scopes se excluyen; esa observación no demuestra compatibilidad Unix.

Microsoft documenta el cambio de ámbito/almacenamiento de mutex Unix desde .NET 8.0.32, 9.0.21 y 10.0.13 por motivos de seguridad: [dotnet/docs#56396](https://github.com/dotnet/docs/issues/56396), publicado el 14 de octubre. [dotnet/runtime#135726](https://github.com/dotnet/runtime/issues/135726) recoge también el cambio de ubicación. Se mantiene la configuración segura del runtime.

## Por qué una transición transparente no está demostrada

El código anterior solo adquiere el mutex con nombre de `ExecutionMutex.Acquire`; no consulta ningún fichero de coordinación futuro. Por tanto, conservar ese mutex y añadir un fichero excluye a escritores nuevos, pero no a escritores antiguos cuyo runtime resuelva otro namespace.

Hay además entradas manuales independientes de AutomationOwner:

- `AssetExecutionCoordinator.Execute` adquiere Job y avanza el journal v1.
- `ProductionAssetExecutor.Execute` adquiere Job cuando existe y Production. También acepta planes sin Job; no exige una política de cola.
- `CatalogBoundary.Acquire` adquiere Catalog; sus operaciones no exigen AutomationOwner.
- `JobStateStore.Transition` publica con rename y un monitor por instancia, sin epoch de la cola.

Cambiar el schema de la cola, añadir un marker o validar la versión solo en el adaptador nuevo no modifica esas rutas en binarios antiguos. Un CAS de cola protege sus transacciones; no concede fencing a un rename de producción ejecutado por un cliente que no consulta la cola. Un proceso antiguo ya situado entre validación y publicación tampoco queda revocado por un marker posterior. Enumerar procesos y observar que no hay ninguno no impide el siguiente arranque antiguo.

Se conservan los contratos anteriores, fencing/CAS, rutas, journal v1 y schemas de cola/catálogo. No se introduce una falsa garantía por heartbeat, PID, TTL, flag en memoria o locks que solo una versión conoce. Tampoco se manipulan los archivos privados del CLR ni se restaura el ámbito global inseguro mediante configuración u otro mecanismo equivalente.

## Alternativas que requieren decisión operativa/contractual

1. **Recomendada: actualización obligatoria de todos los escritores con parada verificable.** Definir una versión mínima de protocolo y una barrera externa que impida reiniciar escritores antiguos durante y después del corte: por ejemplo, identidad de servicio y permisos de escritura exclusivos junto con control del ejecutable desplegado. Drenar/parar los procesos, preservar evidencias y verificar la barrera antes de habilitar el protocolo nuevo. La coordinación nueva puede usar ficheros de identidad estable, nunca borrados/reemplazados mientras sean referencias de exclusión, con rechazo de links, orden de locks y fencing/CAS. La compatibilidad de formatos puede mantenerse, pero se cambia el contrato de despliegue: una versión antigua deja de ser un escritor admitido.
2. **Aislamiento de una generación nueva de raíces.** Migración explícita y verificable a raíces disjuntas, retirada del permiso de escritura antiguo y validación completa de bindings/evidencias antes de reautorizar. Evita coexistencia sobre los mismos destinos mediante una barrera de acceso; exige acordar migración y autoridad sobre las raíces. Copiar carpetas o cambiar un nombre sin retirar permisos/revalidar bindings no satisface la garantía.

Ninguna alternativa está aplicada a universos reales. Ambas requieren que el responsable acepte cómo se retira la capacidad antigua de escribir; la biblioteca por sí sola no puede imponer retroactivamente un protocolo a binarios ya distribuidos. Se detiene en esta incompatibilidad conforme a la condición explícita del responsable. Después de decidir la barrera, queda implementar y probar el protocolo nuevo con procesos NAP, runtimes mezclados, muerte del propietario, interrupción y recuperación, links/rutas hostiles y ausencia de doble publicación. La prueba elemental de FileShare.None no sustituye esa certificación.

## Quinto fallo: fixture Git

`GitProductionFixture.Run` lanzaba commits sin desactivar el mantenimiento automático. Un experimento aislado con Git 2.52.0.windows.1 y `GIT_TRACE2_EVENT` observó un hijo `git maintenance run --auto --no-quiet --detach` tras el commit con las opciones anteriores. Con `-c gc.auto=0 -c maintenance.auto=false` no apareció ese hijo. Una segunda observación con FileSystemWatcher sobre `.git/objects/maintenance.lock` registró **Created y Deleted a las 12:08:30 UTC** durante el commit baseline; el commit aislado no produjo esos eventos. Trace y eventos se conservan en `GitMaintenanceLocal` de la copia JSON de evidencia. Se reproduce así el actor y el fichero transitorio que invalida el snapshot, sin cambiar el motor productivo de mantenimiento. El síntoma no aparece en todas las ejecuciones: la regresión Linux completa de `3effa26`, anterior al fix, pasa ese caso y conserva cuatro fallos de mutex.

El commit `6386adf` añade solo esas dos opciones al runner del fixture temporal. El motor Git productivo ya las usa; su política de mantenimiento no cambia. No se filtra `maintenance.lock`, no se añaden esperas para ocultarlo y no se relajan los snapshots. Las pruebas originales de recuperación/push se ejecutan también en un paso independiente con `always()`, para obtener su resultado aunque fallen antes los cuatro mutex. Linux pasa **29/29**, incluidos los cuatro casos de snapshot congelado, en `426c976`; la suite GitProduction local pasa **197/197** en 3 min 57 s. Las cifras definitivas de la revisión posterior están en el informe principal.

## Incompatibilidad adicional de Git for Windows detectada por la matriz completa

El job de diagnóstico Windows de `426c976`, con Git **2.56.0.windows.1**, falla sus 29 casos de recuperación/push antes de operar: todos reportan `fatal: unable to access 'NUL': Invalid argument` desde el helper del fixture. La regresión anterior local usaba Git **2.52.0.windows.1**, donde ese valor se acepta; los jobs WPF no cubrían Git. Este fallo es distinto de maintenance.lock.

El commit `967dafd` usa `GIT_CONFIG_GLOBAL=/dev/null` en los dos helpers, en la rama de transporte local de pruebas de GitProductionProcess y en GitBackupProcess. Git documenta expresamente ese valor para [omitir la configuración global](https://git-scm.com/docs/git#Documentation/git.txt-GIT_CONFIG_GLOBAL); Git for Windows también lo entiende. Se conserva `GIT_CONFIG_NOSYSTEM=1` y la omisión de configuración global; no se heredan credenciales/configuración del usuario para hacer pasar los tests. La modificación de GitProductionProcess está limitada al seam de tests existente; el cambio de backup es una corrección de compatibilidad necesaria en su entorno aislado. No se alteran allowlists, staging, selección de rutas, receipts, timeouts, autenticación productiva ni operaciones de publicación.

La revisión `967dafd` pasa **313/313 pruebas locales de GitProduction y Backup**, 0 fallidas/omitidas, 4 min 14 s (`git-backup.trx`), y RecoveryPush remoto **29/29 tanto en Linux como en Windows/Git 2.56**. El job Windows de diagnóstico pasa completo, incluidos los cuatro mutex originales. El job Linux continúa fallando exclusivamente en esas cuatro assertions de coordinación. Las regresiones completas se registran al finalizar en el informe principal.

## Reproducción

```powershell
dotnet build tools/NAP.CoordinationProbe/NAP.CoordinationProbe.csproj -c Release
./.github/scripts/Diagnose-Coordination.ps1 -ProbePath tools/NAP.CoordinationProbe/bin/Release/net8.0/NAP.CoordinationProbe.dll -RuntimeVersions '8.0.31,8.0.32' -OutputPath artifacts/coordination/observations.json
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --filter 'FullyQualifiedName~SeparateProcessObservesExclusiveNamedOwnerMutex|FullyQualifiedName~SystemNamedMutexCoordinatesIndependentProcess|FullyQualifiedName~CatalogMutexCoordinatesAnotherProcessWithoutLockFiles'
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --filter 'FullyQualifiedName~GitProductionRecoveryPushTests'
```

El driver requiere ambos runtimes instalados y falla si no están disponibles. Los artifacts de Actions retienen las observaciones completas, versiones y TRX durante 14 días. Linux continúa fallando de forma visible hasta resolver la incompatibilidad; los diagnósticos observacionales no son un gate de certificación verde.
