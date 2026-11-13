# NAP05 — Auditoría de robustez, recuperación y correcciones focalizadas

**Fecha:** 13 de noviembre de 2026  
**Repositorio:** `robdor80/NAP`  
**Rama:** `feature/nap05-recuperacion-robustez-20261113`  
**Base:** `main@092520fab1b633bde0005ebcb5a2244ac541bbb0` (.NET 10, NAP03 integrada)  
**Ámbito:** código aislado, datos TEMP sintéticos; sin escritura real, CUTOVER ni NAP CI general.

## 1. Preflight y dependencia NAP04

Al abrir la fase, la [PR Draft #68](https://github.com/robdor80/NAP/pull/68), `feature/nap04-robstyle-diversidad-lab-20261113@0a557e407eac79532d88195e50a5f4878441819a`, seguía **abierta, no fusionada**. NAP05 se creó sobre `main`, sin adoptar NAP04 ni modificar su rama. NAP04 y NAP05 tocan `AutomationControlViewModel.cs`: habrá que reconciliar sus cambios al integrar la segunda de las dos PR, sin eliminar las filas de estado de NAP04 ni los mensajes de recuperación de NAP05.

La ausencia previa de la raíz `Nimroel_Maestros_PNG` generó `archive_root_missing` en una ejecución física. Según la evidencia aportada, restaurar la carpeta permitió revalidar las tres raíces y SQLite. Se clasifica como **configuración/almacenamiento ausente que falla en modo seguro**, no como pérdida de datos atribuible a NAP05. No se crean raíces maestras vacías para ocultar el problema.

## 2. NAP05-A: incidencias examinadas

| Origen | Hallazgo y resolución |
|---|---|
| [#47](https://github.com/robdor80/NAP/issues/47) | **Abierta.** Certificación MSI, autoridad escritora, permisos, leases, backup verificable y CUTOVER siguen pendientes de autorización separada. NAP05 no cambia esa puerta. |
| [#49](https://github.com/robdor80/NAP/issues/49) — IPC | **No acreditado como resuelto.** El timeout histórico `QueuePage` de 20 s tras cancelación/reinicio no tiene causa confirmada. `AutomationHostClient` y la tubería preservan la regla de no reemitir automáticamente mutaciones aceptadas. Ahora WPF distingue timeout, pérdida de conexión y rechazo de autenticación. No se altera el protocolo ni se proclama arreglada la causa de latencia. |
| [#49](https://github.com/robdor80/NAP/issues/49) — StorageUnavailable | **Tratamiento seguro confirmado en las pruebas sintéticas focalizadas.** Los fallos de preservación originales se conservan como incidencia/revisión, sin adoptar efectos, borrar maestros o cambiar Job/Attempt. No demuestra que desapareciera la anomalía física histórica. |
| [#49](https://github.com/robdor80/NAP/issues/49) — ImageSharp | Se conserva `SixLabors.ImageSharp 3.2.0` y el flujo de licencia existente. Se verificó la publicación del backport de seguridad del proveedor y la nota de advisories pendientes de corrección en NuGet; **no se silencian NU1902/NU1903 ni se declara limpia la auditoría**. Referencias: https://sixlabors.com/posts/security-support-and-license-enforcement/ y https://github.com/SixLabors/ImageSharp/releases/tag/v3.2.0 |
| NAP02 — persistencia STOP | **Defecto corregido.** En `AutomationHost.RequestStop` una excepción al confirmar STOP de un universo abortaba el bucle e impedía intentar detener duraderamente los demás. La fase separa los intentos, preserva el primer fallo y no simula éxito del STOP no confirmado. |

## 3. NAP05-B/C/D: recuperación, almacenamiento, IPC e integridad

### Mecanismos existentes conservados

- La autoridad única sigue siendo el owner del `AutomationUniverseRuntime`; las llamadas WPF pasan por IPC autenticado y no existe escritor local de reserva.
- `AutomationQueueStore`, `AutomationRuntimeStateStore`, `DurablePreparationService`, `DurableExecutionService`, `AssetExecutionCoordinator` y sus journals/receipts siguen gobernando recuperación y decisiones de publicación. No se crea otra cola ni otro repositorio.
- `JobRecoveryScanner` es informativo y de solo lectura: temporales huérfanos no se promocionan y estados `COMPLETED` inconsistentes exigen revisión. `DurableExecutionService.VerifyCompletedReadOnly` contrasta recibos, identidad, hashes, catálogo y estado terminal antes de confiar en un resultado completado.
- Los trabajos se identifican bajo el contexto y la raíz del universo; la Inbox mixta de NAP03 verifica el destino y SHA-256 antes de distribuir, sin modificar el ZIP origen. El mismo AssetId en distintos universos no se funde.
- Ninguna incidencia autoriza automáticamente reintentar un envío a Gemini, publicar un archivo de procedencia dudosa, borrar sidecars o reparar silenciosamente una SQLite inconsistente.

### Corrección acotada de parada

**Antes:** un fallo de `Controls.Apply(STOP)` para el primer universo dejaba sin intento de persistencia STOP al siguiente. El Host cerraba y exponía `stop_not_persisted`, correctamente, pero el segundo universo perdía la oportunidad de registrar su inhibición.

**Ahora:** se intenta `STOP` por cada universo dentro de la única frontera de parada, capturando el primer error. La secuencia no adopta un commit fallido y la aplicación no afirma que ese STOP quedó duradero. Si el almacenamiento común está completamente inaccesible, pueden fallar todos; la evidencia y el bloqueo se conservan.

### Escenarios no certificados de nuevo en esta fase

El defecto de latencia IPC original y el almacenamiento físico intermitente del MSI no se han reproducido bajo condiciones iguales a las del incidente. Tampoco se ha repetido la batería histórica de `Kill/Restart` de múltiples checkpoints: sus suites y pruebas existen en el repositorio y quedaron evidenciadas en fases anteriores. Reejecutarlas masivamente sin indicio nuevo incumpliría la restricción de pruebas focalizadas. Estas diferencias constan como límites, no como PASS inferidos.

## 4. NAP05-E: diagnóstico visible

`AutomationControlViewModel.RefreshAsync` diferencia:

- Consulta agotada: el Host podría seguir trabajando; consultar el estado sin repetir órdenes.
- Conexión que excede el tiempo: comprobar Host y actualizar; no relanzar procesamiento.
- Desconexión IPC: volver a consultar después de comprobar el proceso, sin replay de trabajos.
- Autenticación rechazada: revisar identidad/configuración; nunca ejecutar operaciones locales por fallback.

El incidente `StorageUnavailable` ahora presenta una acción prudente: comprobar la carpeta y revisar el trabajo **antes** de reanudar. Los cambios no alteran la funcionalidad ni el diseño general de RobStyle.

## 5. NAP05-F: validación en GitHub

**Run inicial de código:** [NAP05 Actions #43496340888](https://github.com/robdor80/NAP/actions/runs/43496340888), commit `d7b91a36752336067788d47f881888207aabc0d6`.

- Plataforma: GitHub Actions `windows-2022` x64, SDK .NET 10; build Release de toda la solución, WPF/Core/AI/Host/test.
- **Resultado: BUILD PASS, 0 errores, 20/20 tests PASS, 0 FAIL, 0 SKIP.** TRX publicado en el artefacto del run.
- Cobertura focalizada: nueva regresión del fallo parcial de STOP (dos universos, primera transacción simulada fallida); cuatro lecturas WPF fallidas sin comandos ni replay; mensaje de almacenamiento; controles durables; suspensión/reanudación; dos variantes de StorageUnavailable; aislamiento físico por universo y modo sin auditor Gemini.
- Advertencias de compilación: **dos CA2014 históricas** en `LegacyPortraitReader.cs` y `LegacyMasterEvidenceIndex.cs`; archivos no tocados. El estado de avisos NU1902/NU1903 no se deduce de esta compilación aislada.
- Fixtures limitadas a TEMP, sin redes de proveedor Gemini, datos de producción, operaciones Git productivas, ZIP masivos, estrés, cambios de ACL o servicios MSI.

El fichero `.github/workflows/nap05-recovery-validation.yml` está limitado a la rama NAP05 y `workflow_dispatch`; no se ha activado `ci.yml` general. La ejecución es evidencia de runner Windows, **no de una instalación certificada en el portátil físico**.

## 6. Riesgos y prioridades NAP06

1. **Antes de combinar PR:** comprobar el estado final de NAP04 y conciliar `AutomationControlViewModel.cs`; conservar por igual vistas/contadores RobStyle y diagnósticos NAP05.
2. **#49 continúa abierta:** cuando se obtenga una reproducción controlada del timeout IPC real, recoger duración de operaciones de solo lectura, estado del owner y sesión sin registrar secretos ni procesar un ZIP de volumen. No interpretar un timeout de lectura como orden cancelada. Si StorageUnavailable reaparece en el MSI, preservar recibos, sidecars y trazas antes de plantear corrección.
3. **En NAP06 y NAP07:** el instalador y actualizador deben coordinar STOP/drain, preservar journals, SQLite, maestros, SHA-256 y trazabilidad, y conservar el bloqueo de publicación ante resultado ambiguo. No introducir otra cola de instalación.
4. **#47 / NAP08–NAP09:** prueba física del MSI y CUTOVER siguen siendo actos separados. Sin permisos nuevos, identidad escritora, cambio de rutas reales, sustitución de executables ni cierre de incidencias por este informe.

## 7. Dictamen

**NAP05 técnicamente validada en el alcance focalizado, pendiente de auditoría de PR e integración controlada con NAP04.** El defecto de parada identificado quedó corregido y probado. La resiliencia existente se mantuvo, sin reconstruir motores ni tocar datos reales. Los fallos históricos sin reproducción suficiente **permanecen abiertos** y documentados. No hay certificación MSI ni autorización de CUTOVER.


## 8. Integración aprobada con NAP04

El propietario confirmó aceptación funcional de NAP04 y autorizó fusionar ambas fases por orden. La [PR #68](https://github.com/robdor80/NAP/pull/68) fue fusionada en `main` mediante `8e05c42f89bbb999288bcb70bbe8d6a863fdeed1` antes de integrar NAP05. Se han combinado expresamente las filas de estado RobStyle NAP04 y los mensajes de diagnóstico NAP05 en `AutomationControlViewModel.cs`, preservando ambas funcionalidades. Se ejecutará validación focalizada conjunta antes de fusionar la PR #69. Los pendientes #47/#49 y las pruebas físicas F7 no se cierran ni se convierten en PASS por esta integración.
