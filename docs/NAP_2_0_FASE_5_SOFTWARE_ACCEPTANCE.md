# NAP 2.0 — aceptación técnica de Fase 5

Fecha: 2026-10-20, Europe/Madrid. **Alcance vigente aprobado por el responsable: volumen histórico de 100 suficiente; gate de 1000 retirado.** No se permiten nuevas cargas de 20/50/100/1000 ZIP ni repetir baterías de volumen. El fallo histórico permanece Failed y ya no bloquea P5.10 por volumen. El cierre integral queda condicionado a la revisión de seguridad de ImageSharp y las evidencias funcionales indicadas abajo. PR #48 permanece Draft, sin fusión ni producción.

**Actualización posterior de ImageSharp:** 3.2.0 configurada, licencia Community oficial aceptada por el validador para `NAP.Core`, Release correcto y **220/220 pruebas exclusivamente de imágenes Passed**, sin omisiones. El bloqueo local de licencia queda resuelto. Archivo privado/ignorado, sin publicación ni subida como secreto. Los cinco avisos NuGet continúan visibles; el proveedor confirma que los backports están corregidos y sus metadatos pendientes. No se suprimen. Ver `NAP_2_0_IMAGESHARP_LICENSE_SETUP.md` y `NAP_2_0_FASE_5_IMAGESHARP_320_EVIDENCE.json`. CI completo anterior pertenece a 3.1.12, no se repitió; un CI nuevo necesita provisión de licencia autorizada. La observación IPC local sigue pendiente, fuera de esta actualización.

## Versión y contrato conservado

Repositorio `robdor80/NAP`, rama existente `codex/nap-2-fase-5-automation-host`. Los resultados históricos siguientes corresponden al producto recuperado en `08894c03ac6738abab1eec705c9326fae52ec141`, con sus DLL identificadas en `NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_EVIDENCE.json`. La corrección actual `1bef7ecd1c1234ba0f4dbd014d7eef4bef93589a` cambia CI, selección de tests y fixture funcional; no cambia `src/`. No se atribuye la revisión embebida de un binario anterior a un commit documental posterior.

Se conserva por decisión expresa la validación global mediante hashes frescos completos, con pruebas de validación pura vinculadas a contenido, ruta y raíces exactas. No se introdujo validación incremental autenticada, no se ampliaron límites y no se eliminaron comprobaciones de ownership, política, originales, receipts, CAS o no-overwrite.

Entorno: Windows x64, SDK 10.0.400, .NET y WindowsDesktop 8.0.8. Los resultados Linux anteriores son históricos. Main sigue en `cd7532f011eea8a72e89848234b259f1f5293e15`; GitHub informa **sin protección (`protected=false`, reglas=[])**. No se cambió esa configuración; queda pendiente antes de fusionar.

## Corrección final sin nuevas cargas

La ejecución previa de CI `39468522189` quedó cancelada antes de lanzar pruebas nuevas o publicar. Se revisaron los dos workflows; CUTOVER no se lanzó. Se excluyen AutomationVolume y AdmissionVolume de la configuración general y de CI, con deshabilitación explícita de ambas teorías como barrera adicional frente a filtros CLI. La selección por defecto se verificó mediante discovery sin ejecutar volumen. El caso de prioridad entre páginas pasó con tres elementos y páginas de dos, conservando sus barreras y assertions.

Regresión funcional local: **3784 ejecutadas, 3778 Passed / 6 Failed / 0 omitidas**. Cinco son falta de privilegio symlink; el sexto es un timeout de consulta QueuePage (20 s, límite sin ampliar) en recuperación tras cancelación. Selección posterior de inhibiciones **4/4 Passed**, sin demostrar causa ni corrección del timeout. No se declara fallo ambiental por suposición ni se oculta el TRX fallido. Los trece fallos históricos de software vuelven a pasar.

CI [39474539947](https://github.com/robdor80/NAP/actions/runs/39474539947), sobre fuente `1bef7ec`: **Windows 3784/3784 Passed, cero omisiones; Linux 3777 Passed / 0 Failed / 7 NotExecuted** (Windows). Los cinco symlinks reales pasan en Windows, recuperación 22/22 y los dieciocho casos originalmente fallidos 18/18. WPF separado **4/4**, coordinación/Git recovery **4/4 + 19/19 + 29/29** en cada plataforma; son selecciones solapadas. Los cinco jobs terminaron success. Ninguna batería nueva incluye volumen. El collector conserva los contadores crudos del TRX y añade la cantidad real de resultados NotExecuted, porque Linux informa cero en el contador pese a siete omisiones individuales. Ver `NAP_2_0_FASE_5_FINAL_REVIEW.md`, su evidencia JSON/ZIP y `NAP_2_0_FASE_5_IMAGESHARP_REVIEW.md`.

## Resultados históricos efectivamente ejecutados (antes del cambio de alcance)

| Batería | Resultado |
|---|---|
| Build completo Release del HEAD recuperado | 0 errores/0 avisos, sin restaurar; no resuelve advisories históricos |
| Dirigidas de recuperación/almacenamiento/cleanup/ledger/hashing | **56/56**, cero fallos/omisiones |
| Windows manual/aceptación específica/WPF real | **16/16**, cero fallos/omisiones |
| JSON Schema | **436 comprobaciones correctas** |
| Regresión Windows completa | **3792 ejecutadas: 3786 Passed, 6 Failed, 0 omitidas**; terminada normalmente, no anulada |
| Admisión independiente 1/10/100/1000 | **4/4** en la regresión final; 1000: 841,121 s de medición, 14m43,552s de caso completo |
| Host integral 1/10/100/1000 | 1, 10 y 100 **Passed**; 1000 **Failed por duración** |
| Los 18 fallos históricos | **13 de software Passed; 5 de privilegio symlink Failed**, cotejados por nombre en el TRX final |

TRX final: `artifacts/phase5/resume-20261020/resume-complete-windows.trx`. Manifests e inventarios: `NAP_2_0_FASE_5_TEST_EVIDENCE.json`, `NAP_2_0_FASE_5_RESOURCE_EVIDENCE.json`, `NAP_2_0_FASE_5_REGRESSION_COMPARISON.json` y `NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_EVIDENCE.json`. El archivo `docs/evidence/NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_20261020.zip` conserva TRX/logs/métricas, muestras, fuente del auditor y copia sintética detenida, con inventario interno y hashes verificados. No incluye launch configs ni claves IPC. Las selecciones se solapan y no se suman como cobertura distinta.

La regresión final incluye 22/22 escenarios de recuperación de proceso, 3/3 de etapa IA, 7/7 de aceptación específica, 4/4 de rutas manuales, 5/5 WPF, 4/4 de diagnóstico de almacenamiento/cleanup, 8/8 de ledger y 19/19 de coordinación entre procesos. Los cinco symlinks fallaron al crear los enlaces por falta de privilegio, antes de probar la guarda correspondiente; no se omiten ni se presentan como superados. Se requieren ejecuciones satisfactorias en Windows con el privilegio apropiado; no se cambiaron permisos para ello.

## Volumen y recursos

| Host | Completados | Tiempo medido | Pico privado muestreado | Handles máx. |
|---|---:|---:|---:|---:|
| 1 | 1/1 | 28,25 s | 41.992.192 B | 317 |
| 10 | 10/10 | 139,30 s | 61.636.608 B | 339 |
| 100 | 100/100 | 793,53 s | 75.280.384 B | 374 |
| 1000 | **280/1000 — Failed** | límite 3300 s; 3309,38 s con conservación | 159.006.720 B | 413 |

El caso 1000 falla exactamente con `Only 280/1000 in 00:55:00.0524028`. No es un fallo de infraestructura ni un éxito parcial de la aceptación. La excepción es la assertion temporal de la prueba; no es un timeout remoto ni un aborto del runner. No se repitió otra tanda extensa ni se elevó el límite para obtener verde.

Última muestra de la prueba: 280 Completed (140/140), CPU acumulada 3193,56 s; 23 hilos de pico, máximo 3 candidatos pendientes y mayor intervalo de progreso 42,65 s. Última muestra externa viva (19:30:10 Madrid): CPU 3197,64 s y **1.585.201.936.351 bytes de lectura lógica**, con 344 handles/17 hilos en ese instante. El campo `io` del collector del caso fallido queda null porque drena antes de leerlo; las 51 observaciones externas conservan esos contadores antes de la salida y no se rellenó ese null por inferencia.

Ocupación sintética medida: de 1.261.926 B/1013 archivos a 110.941.153 B/9758 archivos. Los ledgers de preparación llegan a unos 21,5 MB y los de ejecución a unos 26,8 MB por universo. Estos datos son tamaño de archivos y transferencias lógicas, **no E/S física de disco**, y excluyen inventarios de datos reales o del directorio global de coordinación. Es una carga sintética durante regresión, con consultas adicionales de diagnóstico por IPC espaciadas y declaradas. No es un benchmark productivo.

Los contadores históricos muestran amplificación de lectura y aumento del tiempo por item. El perfil CPU Linux conservado señala validación de ledgers, deserialización y comprobación de rutas; no es un perfil nuevo de Windows ni permite atribuir todo el coste actual al SHA. El límite observado se conserva como limitación histórica fuera del alcance exigido. No se autoriza investigar throughput mediante nuevas cargas ni repetir esta regresión extensa.

Los presupuestos del bridge (128 MiB de handles con buffers ocultos, 128 MiB independientes de blobs) limitan datos retenidos, no toda la memoria residente. El ensayo conserva su envelope de 1 GiB privado, menos de 2048 handles/128 hilos. Los valores observados del lote fallido no sustituyen las assertions finales que no alcanzó.

## Recuperación, manual y garantías

Los 22 escenarios de proceso verifican mismas identidades Job/Attempt y sidecars, originales/receipts, autorización persistida o de sesión, pausa/cancelación/revocación, resultados IA vinculados o inciertos y Completed inconsistente sin reparación automática. Las pruebas IA cubren respuestas tardías y timeouts sin liberar ciegamente presupuesto o autoridad. No certifican pérdida eléctrica ni exactly-once de un proveedor remoto.

Las cuatro rutas manuales verifican preparación/auditoría/publicación, catálogo/documentos/objetivos/estadísticas, normalización, thumbnails, backup/restore/retención y Git explícito contra bare local desechable, incluyendo recuperación/retry de commit y replay sin otro Job. WPF se ejecutó y renderizó realmente, sin errores de binding; la captura fue inspeccionada. El cierre del cliente mantiene el host independiente. Cache Windows: pinning de ancestros, comparación de identidad, conservación de reemplazos y eliminación por handle; presupuesto de buffers ocultos verificado. No se certifica una limpieza insegura por rutas ni se borran leases persistentes.

Durante el ensayo prolongado, un peer independiente con el mismo SHA de Core recibió BUSY para AutomationOwner en ambos universos. Solo intentó el lease nativo; no adquirió otro owner ni modificó epochs/Jobs. Esto no sustituye la barrera externa frente a escritores antiguos u otras identidades de CUTOVER.

Tras fallar el lote, el fixture drenó mediante ShutdownHost autenticado y verificó salida 0 antes de copiar. La copia conserva 280 Completed, 199 Queued y 1 Running; controles Stopped/Paused en ambos universos. El recuento offline de 11 SQLite, abiertas read-only/immutable, da integridad estructural y FK correctas y hashes completos sin cambios antes/después de observarlas: 480 intentos/480 JobId distintos, intento máximo 1, 281 llamadas/281 RequestId distintos y ningún registro con más de una llamada. Las publicaciones WebP observadas son 140 por universo. **No son aceptación nativa del lote 1000 ni autorización para rebind, reparar o reanudar la copia.** Las comprobaciones completas de originales y efectos posteriores al bucle del caso fallido no llegaron a ejecutarse.

## Fallos y pendientes de aceptación

1. **Volumen aceptado según decisión expresa:** 100/100 histórico es suficiente. El gate 1000 se retira, su resultado 280/1000 sigue Failed y no se intentará mejorarlo ni repetirlo.
2. **Symlinks verificados:** cinco Passed reales en CI Windows, con guard obligatorio, sin omisiones ni cambios de privilegios del MSI. Los fallos locales históricos y actuales se conservan como limitación de creación de enlaces en ese entorno.
3. StorageUnavailable histórico: no se reprodujo en el 100 actual ni en la porción ejecutada del 1000; stderr de ese host vacío. Su causa original no está demostrada y no se declara solucionada. Diagnóstico original y conservación de sidecars permanecen disponibles.
4. ConnectionReset histórico: no hay fallos por reset en la regresión final. El filtro acotado de reintento y cleanup se compilaron y ejecutaron; no se presume la causa del incidente Linux anterior ni se reintentan mutaciones.
5. **ImageSharp actualizado y probado localmente:** 3.2.0, validador oficial de licencia satisfactorio, Release y 220/220 imágenes. Los cinco avisos de metadatos se conservan, con confirmación oficial de backports corregidos. No se declara scan limpio ni se extrapolan las pruebas a una nueva regresión general. Pendiente la provisión autorizada de licencia para CI; no se publica el archivo privado.
6. **Nueva observación local pendiente:** timeout de consulta durante recuperación tras cancelar en la regresión general. La selección posterior 4/4 pasa sin cambios; el fallo original permanece Failed. Falta una causa demostrada; no se ha ampliado el timeout ni reintentado una mutación para obtener verde.

## Reservado para CUTOVER y auditoría

Conforme a los contratos de Fase 4: distribución/DLL/configuración protegidas, identidad definitiva de escritor, arranque administrado, retirada de escritores/handles anteriores, permisos/ownership de raíces y padres, pruebas de denegación entre identidades, backup/inventarios reales y autorización productiva expresa. El canal de laboratorio del mismo principal no certifica esa barrera. El workflow de cuentas/ACL permanece manual y no se ejecutó.

No se modificaron activos reales de Nimroel, main, cuentas/permisos Windows ni servicios; no hubo producción, CUTOVER, merge, force-push ni ramas nuevas. La entrega queda en Draft para auditoría humana. No autoriza fusión ni aceptación integral de Fase 5.
