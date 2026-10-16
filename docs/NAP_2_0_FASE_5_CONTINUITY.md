# NAP 2.0 — continuidad de Fase 5

Estado actual: **P5.10 REANUDADA por autorización expresa de 2026-10-16; aceptación final en curso.** Las correcciones dirigidas están verificadas; la regresión completa Release, incluyendo ambos grupos de volumen, aún no ha terminado. No se declara aceptación integral ni se ha creado todavía la Draft PR. No se ha modificado main ni realizado merge.

Actualización 2026-10-17: commit de estabilización `1a049c0` publicado. Regresión posterior de software/presentación **3766/3771, cinco fallos de privilegio symlink, cero omisiones**, con **22/22** escenarios nuevos de recuperación aprobados. Cierre WPF/diagnóstico de parada **6/6**. La presentación indica explícitamente la parada durable no confirmada; smoke ya no lee claves del entorno. El Core y el scheduler no han cambiado desde `1a049c0`; la regresión completa Release y sus volúmenes siguen en ejecución. Pendiente registrar los resultados reales de 100/1000 antes de cerrar el informe y crear la Draft PR.

La verificación inicial encontró la rama local/remota limpia y coincidente en `20106c7f876ff488ea362bd6f709b643c3b63b5e`, y main aún en `cd7532f011eea8a72e89848234b259f1f5293e15`. **GitHub informa main SIN protección (`protected=false`, reglas efectivas vacías, endpoint de protección 404).** Se conserva esta discrepancia sin cambiar la configuración de GitHub; necesita resolución antes de cualquier futura fusión.

Avance verificable: reproducción y corrección de pérdida de una admisión diferida durante recepción HTTP (2 de 3 ZIP antes, 3 de 3 después); prioridad de continuación de Jobs preparados/respuestas aceptadas; pinning de caché por identidad, presupuestos del bridge y rutas largas de retención. Selección Host/Batch/aceptación 12/12; rutas manuales remotas ampliadas 4/4. La regresión amplia intermedia ejecutó 3768: 3760 pasan, 5 fallan por privilegio symlink y 3 por una expectativa nueva incorrecta sobre `Completed`, ya corregida según el contrato de Fase 4. Los trece fallos históricos restantes se han cotejado individualmente como Passed en ese TRX. Se exige la regresión final posterior a las últimas correcciones.

Los primeros lotes end-to-end de 1 y 10 pasan; 100 y 1000 fallaron el límite de cinco minutos sin nuevos completados antes de las correcciones del scheduler. No se consideran resueltos hasta terminar la repetición. Sus métricas y fallos se conservan. CUTOVER sigue prohibido: su ensayo heredado con cuentas/ACL se conserva en un workflow manual de confirmación explícita, sin ejecutarlo al abrir esta PR ni durante P5.10.

## Histórico de la parada original en 20106c7 — no es el estado actual

La parada original por cuota fue levantada por la orden de reanudación. El registro siguiente conserva el punto de partida y las garantías pendientes entonces; el log de implementación y el manifest de evidencia registran las ejecuciones posteriores.

## Repositorio y conservación

- Repositorio: `C:\Users\andro\Documents\GitHub\NAP`; origin: `https://github.com/robdor80/NAP.git`.
- Rama: `codex/nap-2-fase-5-automation-host`.
- Base: `cd7532f011eea8a72e89848234b259f1f5293e15`, merge PR #46; origin/main seguía en ese SHA al consultar el remoto durante la parada.
- Último bloque completo previamente registrado: `98cbca5` (P5.9). El commit de conservación posterior contiene las correcciones parciales P5.10 y este informe; su SHA se entrega en la confirmación final y se obtiene con `git log -1`.
- Durante la inspección no quedaban procesos dotnet/testhost/AutomationHost/WPF en ejecución; no hubo que forzar una parada pendiente. No se iniciaron nuevas pruebas tras la orden.
- Los universos y Git remotos utilizados por las pruebas son sintéticos, temporales y aislados. No se activó producción, instalaron servicios, cambiaron cuentas/ACL ni modificaron archivos reales de Nimroel.
- La publicación será un push normal de la rama, sin force. El resultado verificable y SHA remoto se comunican al finalizar; este texto no presume éxito remoto antes de comprobarlo.

## Bloques y nivel de validación

| Bloque | Implementación conservada | Evidencia y pendiente |
|---|---|---|
| P5.1 | Hilo propietario por universo y mailbox acotado; drenaje en hilo original. | 3/3 inicial; últimos cambios de retry/dispose incluidos en selección final 8/8. Aceptación global pendiente. |
| P5.2 | Control durable versionado independiente, CAS, receipts idempotentes, sesiones/grants y cancelaciones. | 6/6 y 4/4 dirigidas; reinicio real y matriz completa de autorización pendientes. |
| P5.3 | Observación asíncrona de readiness y continuación con validaciones originales en owner. | 2/2 nuevas; selección histórica 43/44, fallo de privilegio symlink. Volumen pendiente. |
| P5.4 | Intención/auditoría/transporte/completado separados usando el pipeline original. Timeout conserva incertidumbre remota y presupuesto. | 53/53 y selección final 5/5; caída prolongada y concurrencia integrada pendientes. |
| P5.5 | Host, scheduler multiuniverso, aislamiento, un permiso pesado y una auditoría, límites conservadores. | 3/3; alternancia/backpressure añadidos en P5.10 con selección final 8/8, sin ensayo de carga/fairness prolongado. |
| P5.6 | Suspensión en fronteras durables seguras; mismo claim/Job y control persistido. | 9/9 y control 7/7; matar/reiniciar proceso, cancelación durable end-to-end y resultados remotos ambiguos pendientes. |
| P5.7 | Captura de lotes durable, reconciliación y proyecciones paginadas. | 5/5 tras corregir starvation de recepción HTTP. Recuperación real con corte de proceso pendiente. |
| P5.8 | Ejecutable independiente y named pipes autenticados, tipados, versionados y acotados. | 2/2 protocolo y build correcto; identidad distinta/instalación protegida son requisitos futuros de CUTOVER, no certificados. |
| P5.9 | WPF remoto, operaciones manuales centralizadas, referencias opacas y proyecciones sin autoridad. | 84/84 selección amplia; 5/5 final incluyendo host/WPF reales y rutas manuales. Cambios posteriores CurrentControl/availability requieren repetir integración WPF. |
| P5.10 | PARCIAL: correcciones de regresión y del scheduler; callback de laboratorio para futuras inyecciones. | 101/101 y 8/8 dirigidas. Sin regresión completa final, ensayos de interrupción/volumen completos, aceptación ni PR. |

“Implementado” no equivale a aceptación integral: P5.1–P5.9 tienen commits y pruebas específicas; todas las garantías transversales dependen todavía de P5.10.

## Código y archivos

Inventario completo de archivos de implementación respecto a la base en `NAP_2_0_FASE_5_CHANGED_FILES.txt`. Componentes principales:

- Core: `AutomationUniverseRuntime`, `AutomationHost`, `AutomationHostControlStore`, `AutomationRuntimeStateStore`, `AutomationProtocol`, `AutomationHostLaunchConfiguration`, `AutomationManualAccess`, `AutomationReadViews`, `DurableAuditDispatch`, `WorkflowRunControl`.
- Pipeline original adaptado: admisión e inventario retenido, preparación/auditoría/ejecución durable, queue ledger y ownership; coordinador, publicación archive/production, catálogo, backups/restore/retention, Git, normalización y miniaturas. No se pretende crear otro pipeline de reglas.
- Presentación: `UniverseOperationDispatcher`, `AutomationDesktopBridge`, `RemoteDesktopService`, `AutomationControlViewModel`, servicios manuales/modelos, Explorer/Shell/normalización.
- Proceso: proyecto `src/NAP.AutomationHost`, añadido a solución y compilación de tests.
- WPF: App/MainWindow/Tray y `AutomationSmokeProbe`, conexión explícita por configuración de laboratorio sin fallback local.
- Tests: familias AutomationRuntime/HostControl/AdmissionStage/AuditStage/Host/Suspension/Batch/Protocol/Desktop/Wpf; fixture de proceso y adaptación de fixtures sintéticos y helper de reflexión.

La parada conserva 23 archivos fuente/de pruebas con cambios pendientes respecto a P5.9, más documentación de continuidad. Son correcciones parciales autorizadas, no una entrega P5.10 terminada.

## Pruebas reales y evidencia

Registro detallado por bloque: `NAP_2_0_FASE_5_IMPLEMENTATION.md`. Manifest de todas las ejecuciones TRX existentes, incluidos intentos fallidos, contadores/fechas/hashes/nombres de fallos: `NAP_2_0_FASE_5_TEST_EVIDENCE.json`. No sumar selecciones superpuestas como pruebas únicas.

- Baseline seleccionado antes de refactors: 98 ejecutadas, 94 pasan / 4 fallan por privilegio de symlinks.
- Última regresión amplia, previa a correcciones P5.10, sin volumen: **3742 ejecutadas, 3724 pasan / 18 fallan / 0 omitidas**, 5 min 38 s. TRX: `artifacts/phase5/preacceptance/phase5-preacceptance-regression.trx`.
- Cinco de esos fallos son symlinks (tres LinkedBoundaryArtifacts, LinkedQueueDirectoryAndWalArtifacts, SymlinkZipAndSymlinkStaging). No se consideran superados por inferencia.
- Trece fallos restantes: excepciones de dominio/IOException históricas en producción, catálogo y backup; helper con dos constructores no públicos; consulta externa durante arranque del host; deadline funcional 30 s con carga paralela. Cambios aplicados, selección de corrección **101/101** pasada. La selección incluye las familias ProductionSafety, ProductionConversionBoundary, BackupSafety, CatalogConcurrency, Host y Runtime.
- Último test ya finalizado al recibir la parada: Host/Runtime/Batch **8/8**; confirma compilación y esa selección tras los últimos cambios. No equivale a verificar controles WPF ni recuperación tras muerte de proceso.
- P5.9: **84/84** selección amplia; final **5/5** desktop/WPF, con proceso host separado, cierre de cliente dejando host vivo, backups/restore, normalización y commit/push solo contra bare local sintético. PNG WPF inspeccionado visualmente.
- Builds Release previos correctos. Se observaron cinco avisos históricos NU1902/NU1903 de ImageSharp 3.1.12 en restauraciones; no se actualizaron dependencias. Build incremental con cero avisos no implica resolución de advisories.
- Se conservan también ejecuciones fallidas y sus diagnósticos, descritos en el log. No se ha repetido la batería completa tras las correcciones. No hay resultados nuevos de volumen, CI remoto o certificación CUTOVER.

Los TRX completos y PNG están en `artifacts/phase5` (ignorados por Git) y permanecen en este workspace. GitHub conserva el código, informes y manifest; **no los binarios/artefactos completos de pruebas**. El manifest permite identificarlos por hash si se trasladan más adelante.

## Pendientes, riesgos y límites

1. No está verificada globalmente la ausencia de duplicados de Jobs/publicaciones/respuestas tras caída de proceso. Hay mecanismos y pruebas de fronteras in-process; faltan escenarios reales de kill/restart con mismo Job y receipts.
2. El callback `AutomationHostOptions.LaboratoryCheckpoint` es scaffolding: no está conectado a launch configuration/Program ni existen las nuevas pruebas de kill. La verificación inicial de Completed requiere pruebas dirigidas de corrupción, política y suspensión sin reparación automática.
3. Ensayar reinicio automático solo con autorización persistida aún válida; pausa/cancelación durable, control revocado, desconexión y respuesta IA tardía/timeout. Nunca interpretar timeout como cancelación remota confirmada ni reintentar ciegamente un efecto incierto.
4. Ensayos de fairness bajo bloqueo HTTP, permiso único manual/automático, fallos aislados y límites de backlog/memoria/disco pendientes; los límites configurados no están certificados mediante volumen prolongado.
5. Canal de laboratorio restringido al mismo principal Windows. Identidad definitiva, servicio, ACL y canal entre identidades distintas quedan sin autorización y pendientes de CUTOVER.
6. Revisar presupuestos en bytes de objetos retenidos por handles del bridge (hay límites de cantidad/buffers, sin caracterización completa de memoria) y seguridad del borrado del cache temporal del cliente ante reparse/reemplazo de ancestros.
7. Revisar diagnóstico de stop con fallo de persistencia y resultado remoto aún desconocido; preservar drenaje, nunca introducir kill como sustituto de parada pendiente.
8. Repetir WPF tras CurrentControl y disponibilidad de botones. Revisar que el smoke antiguo tampoco cargue claves reales de entorno; la ruta remota explícita de laboratorio ya evita settings/credenciales reales.
9. Cinco pruebas symlink sin privilegio: pendientes en entorno autorizado adecuado; no cambiar permisos del equipo para hacerlas pasar. Advisories ImageSharp históricos pendientes de evaluación, fuera de una afirmación de aceptación completa.
10. Generador sintético actual repite patrón después de 256 seeds; antes de probar 1000 masters únicos ajustar fixture conservando contratos y evitando falsos MasterDuplicate. Ningún generador nuevo implementado todavía.

## Punto exacto de reanudación — requiere nueva autorización

Primero comprobar rama/estado limpio, commit conservado, igualdad con rama GitHub y cambios posteriores de main. Leer este informe, log y manifest; no repetir pruebas dirigidas ya superadas salvo cambios o necesidad de integración.

**Siguiente tarea concreta:** completar exclusivamente la aceptación P5.10 pendiente empezando por pruebas deterministas de interrupción del proceso en laboratorio. Conectar la inyección de checkpoint de pruebas a la configuración explícita de laboratorio, extender `AutomationHostProcessFixture` para reiniciar sobre los mismos sidecars, y comprobar mismo Job, conservación de originales/receipts y Completed o NeedsReview según la frontera. La muerte deliberada es solo inyección de fallo de un fixture sintético, nunca mecanismo de cierre del host.

Después: matriz de pausa/cancelación/reinicio/autoridad; bloqueo y timeout HTTP entre universos; cargas 1/10/100 y 1000 con métricas reales de memoria/disco/tiempo; regresión histórica y volumen disponible; WPF; revisión de riesgos arriba; informe de aceptación honesto y Draft PR solo al terminar técnicamente lo autorizado. No considerar pendientes como superados; no fusionar ni activar producción.

Comando de la última selección (ya terminada; no repetir al pausar):

```powershell
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AutomationHostTests|FullyQualifiedName~AutomationRuntimeTests|FullyQualifiedName~AutomationBatchTests' --logger 'trx;LogFileName=p510-scheduler-corrections.trx' --results-directory artifacts/phase5/p510
```

Tras la orden de parada solo se inspeccionó el estado, se recopiló evidencia y se conservó trabajo. No hay nuevas funcionalidades ni pruebas extensas iniciadas por la parada.
