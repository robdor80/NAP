# Punto seguro de pausa — Fase 4

Pausa solicitada por el responsable para apagar el ordenador, 2026-10-15, aproximadamente 14:20 Europe/Madrid. **Fase 4 NO certificada. PR #46 abierta en Draft. Sin merge ni producción real.**

## Trabajo guardado

- Rama de la PR: `codex/nap-2-fase-4-execution-recovery`. Código validado de esta reanudación hasta `967dafdac89a6312958570e03f0863249052a3a2`, más la consolidación documental de esta pausa.
- Prototipo incompleto, guardado también en GitHub: `codex/nap-2-fase-4-coordination-checkpoint`, commit **`d5495c4ed84f09456c240cb7af6ad4b4484a8254`**. No está incorporado a la rama de la PR ni tiene otra PR. No debe considerarse listo para uso.
- Diagnóstico: `NAP_2_0_FASE_4_COORDINATION_DIAGNOSIS.md`; copia permanente de las 112 observaciones y del experimento maintenance.lock en `NAP_2_0_FASE_4_COORDINATION_EVIDENCE.json`.
- No hay procesos locales de pruebas que deban permanecer vivos. Los builds/testhosts ya finalizaron. Las ejecuciones pendientes de Actions son remotas y no necesitan este ordenador.

## Decisión ya autorizada — no volver a preguntarla

El responsable aprobó **actualización obligatoria de todos los escritores, con parada y control externo de permisos y arranque**, manteniendo rutas, datos, assets y evidencias. La compatibilidad de datos se conserva; los binarios antiguos deben perder de forma verificable la capacidad de escribir/arrancar antes del nuevo despliegue.

No se han modificado permisos ni arranque del MSI, ni se ha aplicado una barrera sobre raíces reales. Si se necesita esa intervención Windows, debe explicarse antes al responsable. La garantía frente a escritores antiguos aún no está implementada ni demostrada: no basta declarar una parada, añadir un marker o publicar un lock que los binarios antiguos ignoran. Mantener el bloqueo de certificación mientras esta barrera y sus pruebas estén pendientes.

## Resultados comprobados

- Fixture Git: `6386adf` desactiva auto maintenance únicamente en el helper. GIT_TRACE2_EVENT y FileSystemWatcher reproducen el hijo detach y creación/eliminación de `.git/objects/maintenance.lock` en el baseline, ausentes en el caso aislado. No se filtran snapshots ni se añaden sleeps a los tests.
- Compatibilidad Windows Git 2.56: `967dafd` sustituye NUL por /dev/null en los dos helpers, la rama test-only de GitProductionProcess y el runner GitBackupProcess. Conserva aislamiento de configuración; no cambia publicación, allowlists ni receipts.
- GitProduction local: **197/197**, 3 min 57 s. GitProduction+Backup local tras el ajuste null: **313/313**, 0 fallos/omisiones, 4 min 14 s.
- [Actions 38731153045](https://github.com/robdor80/NAP/actions/runs/38731153045), código `967dafd`: RecoveryPush **29/29 en Linux y 29/29 en Windows** con Git 2.56; cuatro WPF **4/4**, TRX descargado y gate de identidades repetido correctamente.
- Ese mismo run, Linux completo terminado: **3.683 aprobadas, 4 fallidas, 4 WPF omitidas; 3.691 total**. Los cuatro fallos son exclusivamente AutomationOwner, Job, Production y Catalog con el protocolo antiguo. El quinto fallo Git ya no aparece. Sigue siendo CI fallida, no certificada.
- Windows completo de ese run **seguía en ejecución al pausar**: consultar su resultado y descargar `windows-regression-results` antes de afirmar aprobación. Runs anteriores `38729934632` y `38730539826` quedan como evidencia histórica; el segundo contiene fallos NUL anteriores al ajuste.
- Build del prototipo WIP: `dotnet build NAP.sln -c Release --no-restore`, **0 errores y 0 advertencias**, aproximadamente 4 s. No se ejecutó la batería de coordinación del prototipo. La compilación de la solución construyó el peer en Debug mientras su copia a tests busca Release: resolver esa discrepancia al retomar; el build verde no certifica que se esté ejecutando el peer correcto.

## Contenido del prototipo WIP

`ExecutionFileLease` usa CreateFileW share-none / flock no bloqueante, con archivos persistentes que nunca se borran al liberar. Inspecciona identidad del handle, archivo vacío, enlaces, owner/permisos y rutas no reparse. La ubicación es por identidad de sistema, independiente de HOME/TMPDIR/XDG y del namespace del CLR: ProgramData en Windows y /tmp más euid en Linux. `ExecutionMutex` añade esa guarda, conserva el mutex como protección adicional y exige afinidad de hilo.

El peer añade core-hold/core-try/core-race mediante el Core real y legacy-write para ensayos sintéticos de denegación. Hay un helper de procesos y cableado inicial en el csproj de tests. **Faltan su revisión y validación, la integración con los tests y el contrato efectivo de despliegue.** El caché Windows del prototipo crea ACL privadas si se ejecuta: antes de ensayarlo localmente, revisar el alcance y respetar el aviso previo sobre intervención Windows; preferir CI desechable para los ensayos de permisos. Hasta esta pausa solo se compiló ese código, no se ejecutó esa creación de ACL.

## Orden exacto para retomar

1. Ejecutar `pwd`, `git branch --show-current`, `git status --short` y comunicar carpeta/rama/cambios antes de trabajar. Leer este documento y los dos informes. No empezar otra fase ni reautorizar la decisión ya aceptada.
2. Consultar el estado final del run `38731153045` y de la CI documental posterior; descargar/consolidar Windows completo. Los artifacts locales anteriores están en `artifacts/phase4/` (ignorados); la evidencia permanente está en docs y GitHub.
3. Desde la rama de la PR, recuperar el prototipo con `git cherry-pick d5495c4ed84f09456c240cb7af6ad4b4484a8254`. Alternativamente inspeccionarlo primero con `git show` o en su rama de respaldo. No usar git reset ni borrar datos para recuperarlo.
4. Revisar/corregir la implementación nativa de locks y sus supuestos de plataforma/permisos; fijar configuración/copia del peer para que use el Core y binario de la compilación actual. Cerrar el diseño fail-closed de la barrera externa y demostrar que un binario antiguo no puede escribir bajo ella. Si no puede garantizarse, parar y presentar alternativa segura. No sustituir esa prueba por una bandera o un documento firmado sin control efectivo de acceso/arranque.
5. Integrar los cuatro tests originales con peers del protocolo aprobado manteniendo rechazo Busy, ausencia de efectos y retry, conservando evidencia explícita de que el peer antiguo sin barrera puede eludir el fichero. Añadir pruebas del Core real con propietarios/competidores 8.0.31/8.0.32, scopes AutomationOwner/Job/Production/Catalog, competición simultánea, muerte del propietario y reapertura, fencing/CAS, links/hardlinks/permisos/identidad, rutas, abandono y recuperación. **No relajar assertions ni usar UnsafeDefaultMutexScopeGlobal.**
6. Ensayar retirada de permisos y arranque de escritores antiguos solo sobre identidades/raíces sintéticas en CI desechable. Cualquier intervención necesaria en el MSI se comunica antes; no activar producción ni alterar activos reales.
7. Commit separado para coordinación y para pruebas/compatibilidad. Ejecutar regresiones completas Windows y Linux, cuatro smoke WPF reales con gate y matriz de procesos/runtimes. Corregir fallos, actualizar informe y PR #46. Declarar certificación únicamente tras superar toda la validación exigida y demostrar la transición segura.

La rama de la PR queda limpia y publicada al terminar esta pausa. Main conserva `30a0b199fcb3200ce9e4de97944d2ff9fd0c642c`. La rama WIP es una copia de recuperación remota, no una entrega aprobada del coordinador.
