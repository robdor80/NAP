# NAP 2.0 — aceptación técnica de Fase 5

Fecha: 2026-10-20, Europe/Madrid. **P5.10 PARCIAL — NO ACEPTADA INTEGRALMENTE.** El procesamiento integral de 1000 no cumple el presupuesto temporal. La entrega queda preparada para auditoría mediante PR Draft, sin fusión ni producción.

## Versión y contrato conservado

Repositorio `robdor80/NAP`, rama existente `codex/nap-2-fase-5-automation-host`. Recuperación limpia y sincronizada en `08894c03ac6738abab1eec705c9326fae52ec141`; se compiló ese HEAD. Después solo cambiaron documentación/evidencia y un probe externo de contadores. `src/` y `tests/` siguen con las mismas fuentes; las DLL ejecutadas están identificadas por SHA256 y versión en `NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_EVIDENCE.json`. No se atribuye su revisión embebida a un commit documental posterior.

Se conserva por decisión expresa la validación global mediante hashes frescos completos, con pruebas de validación pura vinculadas a contenido, ruta y raíces exactas. No se introdujo validación incremental autenticada, no se ampliaron límites y no se eliminaron comprobaciones de ownership, política, originales, receipts, CAS o no-overwrite.

Entorno: Windows x64, SDK 10.0.400, .NET y WindowsDesktop 8.0.8. Los resultados Linux anteriores son históricos. Main sigue en `cd7532f011eea8a72e89848234b259f1f5293e15`; GitHub informa **sin protección (`protected=false`, reglas=[])**. No se cambió esa configuración; queda pendiente antes de fusionar.

## Resultados efectivamente ejecutados

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

Los contadores muestran amplificación de lectura y aumento del tiempo por item. El perfil CPU Linux conservado señala validación de ledgers, deserialización y comprobación de rutas; no es un perfil nuevo de Windows ni permite atribuir todo el coste actual al SHA. El cuello de escalabilidad sigue abierto. Cualquier mejora debe conservar el contrato global y demostrar su efecto mediante un reproducer acotado antes de otra regresión extensa.

Los presupuestos del bridge (128 MiB de handles con buffers ocultos, 128 MiB independientes de blobs) limitan datos retenidos, no toda la memoria residente. El ensayo conserva su envelope de 1 GiB privado, menos de 2048 handles/128 hilos. Los valores observados del lote fallido no sustituyen las assertions finales que no alcanzó.

## Recuperación, manual y garantías

Los 22 escenarios de proceso verifican mismas identidades Job/Attempt y sidecars, originales/receipts, autorización persistida o de sesión, pausa/cancelación/revocación, resultados IA vinculados o inciertos y Completed inconsistente sin reparación automática. Las pruebas IA cubren respuestas tardías y timeouts sin liberar ciegamente presupuesto o autoridad. No certifican pérdida eléctrica ni exactly-once de un proveedor remoto.

Las cuatro rutas manuales verifican preparación/auditoría/publicación, catálogo/documentos/objetivos/estadísticas, normalización, thumbnails, backup/restore/retención y Git explícito contra bare local desechable, incluyendo recuperación/retry de commit y replay sin otro Job. WPF se ejecutó y renderizó realmente, sin errores de binding; la captura fue inspeccionada. El cierre del cliente mantiene el host independiente. Cache Windows: pinning de ancestros, comparación de identidad, conservación de reemplazos y eliminación por handle; presupuesto de buffers ocultos verificado. No se certifica una limpieza insegura por rutas ni se borran leases persistentes.

Durante el ensayo prolongado, un peer independiente con el mismo SHA de Core recibió BUSY para AutomationOwner en ambos universos. Solo intentó el lease nativo; no adquirió otro owner ni modificó epochs/Jobs. Esto no sustituye la barrera externa frente a escritores antiguos u otras identidades de CUTOVER.

Tras fallar el lote, el fixture drenó mediante ShutdownHost autenticado y verificó salida 0 antes de copiar. La copia conserva 280 Completed, 199 Queued y 1 Running; controles Stopped/Paused en ambos universos. El recuento offline de 11 SQLite, abiertas read-only/immutable, da integridad estructural y FK correctas y hashes completos sin cambios antes/después de observarlas: 480 intentos/480 JobId distintos, intento máximo 1, 281 llamadas/281 RequestId distintos y ningún registro con más de una llamada. Las publicaciones WebP observadas son 140 por universo. **No son aceptación nativa del lote 1000 ni autorización para rebind, reparar o reanudar la copia.** Las comprobaciones completas de originales y efectos posteriores al bucle del caso fallido no llegaron a ejecutarse.

## Fallos y pendientes de aceptación

1. **Bloqueo de rendimiento:** completar el host integral de 1000 dentro del presupuesto sin debilitar seguridad o pruebas. El lote terminó fallido; P5.10 sigue parcial.
2. **Entorno Windows:** cinco symlinks necesitan ejecución satisfactoria donde exista el privilegio, sin cambiar este equipo. No se extrapolan los cinco éxitos Linux históricos.
3. StorageUnavailable histórico: no se reprodujo en el 100 actual ni en la porción ejecutada del 1000; stderr de ese host vacío. Su causa original no está demostrada y no se declara solucionada. Diagnóstico original y conservación de sidecars permanecen disponibles.
4. ConnectionReset histórico: no hay fallos por reset en la regresión final. El filtro acotado de reintento y cleanup se compilaron y ejecutaron; no se presume la causa del incidente Linux anterior ni se reintentan mutaciones.
5. Advisories históricos de ImageSharp siguen pendientes de valoración separada. La compilación sin restaurar no acredita su resolución.

## Reservado para CUTOVER y auditoría

Conforme a los contratos de Fase 4: distribución/DLL/configuración protegidas, identidad definitiva de escritor, arranque administrado, retirada de escritores/handles anteriores, permisos/ownership de raíces y padres, pruebas de denegación entre identidades, backup/inventarios reales y autorización productiva expresa. El canal de laboratorio del mismo principal no certifica esa barrera. El workflow de cuentas/ACL permanece manual y no se ejecutó.

No se modificaron activos reales de Nimroel, main, cuentas/permisos Windows ni servicios; no hubo producción, CUTOVER, merge, force-push ni ramas nuevas. La entrega queda en Draft para auditoría humana. No autoriza fusión ni aceptación integral de Fase 5.
