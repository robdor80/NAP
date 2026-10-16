# NAP 2.0 — continuidad de Fase 5

## PARADA SEGURA POR CUOTA — 2026-10-17, Europe/Madrid

**Desarrollo suspendido por orden expresa del usuario. P5.10 sigue PARCIAL y sin aceptación integral. No iniciar desarrollo ni más pruebas hasta nueva autorización.** No se ha creado PR, realizado merge, modificado main ni activado producción.

### Repositorio y conservación

- Repositorio `robdor80/NAP`, workspace `C:\Users\andro\Documents\GitHub\NAP`, origin `https://github.com/robdor80/NAP.git`.
- Rama `codex/nap-2-fase-5-automation-host`. Verificación inicial limpia, local/remoto en `20106c7f876ff488ea362bd6f709b643c3b63b5e`.
- Último commit de código: **`4549c48d07017a5a97f9f77a4df711d14763e8cb`**, publicado y coincidente con GitHub antes de guardar este informe. Anteriores: `1a049c0290539b9d6e84544df28301792bf2e623` (recuperación/scheduler/bridge) y `8fd0f10c3b6e0ab05abad9bd5fb1aa5e8ce00f1a` (WPF/diagnóstico de parada).
- El commit de parada introduce este informe, manifests actualizados y archivo de evidencia. Su SHA se obtiene con `git log -1`; la confirmación final comunica su coincidencia remota. No se atribuye ese SHA a los binarios ya probados.
- Main continúa en `cd7532f011eea8a72e89848234b259f1f5293e15`. **GitHub informa `protected=false` y reglas efectivas `[]`**, confirmado de nuevo al parar. La protección no se cambió; resolver antes de cualquier futura fusión.
- Los fuentes, pruebas y decisiones están versionados. Los TRX completos, métricas JSON y PNG retenidos se conservan también en `docs/evidence/NAP_2_0_FASE_5_QUOTA_STOP_20261017.zip`, con inventario/hash en `NAP_2_0_FASE_5_QUOTA_STOP_EVIDENCE.json`. Los originales locales siguen en `artifacts/phase5` (ignorados). No incluye launch configs, claves ni credenciales.

### Estado P5.1–P5.10

| Bloque | Estado al parar |
|---|---|
| P5.1 | Owner dedicado/mailbox implementados; exclusión y recuperación probadas específicamente. Aceptación transversal pendiente. |
| P5.2 | Control durable/CAS/receipts y autorizaciones implementados; pausa/cancelación/reinicio cubiertos. Aceptación transversal pendiente. |
| P5.3 | Admisión asíncrona implementada; corregida pérdida de observación diferida. Volumen 1000 no aceptado. |
| P5.4 | Auditoría durable/transport separado implementados; respuestas tardías, timeout y presupuesto incierto cubiertos específicamente. |
| P5.5 | Scheduler multiuniverso implementado; prioridad de continuación corregida. Escalabilidad/fairness de lotes grandes pendientes. |
| P5.6 | Suspensión y recuperación implementadas; 22/22 escenarios de reinicio/corrupción pasan incluso en la última regresión parcial. |
| P5.7 | Captura/proyecciones durables implementadas; backpressure corregido. Cierre bajo volumen prolongado pendiente. |
| P5.8 | Host separado/IPC autenticado implementados; laboratorio del mismo principal. Servicio/identidades/ACL reservados a CUTOVER. |
| P5.9 | WPF remoto y rutas manuales implementados y probados; diagnóstico de stop durable no confirmado visible. |
| P5.10 | **PARCIAL**: correcciones y pruebas ampliadas conservadas; fallos de almacenamiento/volumen y regresión integral final pendientes. |

Implementado no equivale a aceptación integral. Los contratos y CUTOVER de Fase 4 fueron leídos y conservados; no se repiten bloques anteriores sin defecto demostrado.

### Últimos cambios y pruebas efectivamente realizadas

- Kill/restart determinista sobre los mismos sidecars sintéticos: JobId/AttemptId, originales, receipts, journal/cola, resultado IA bound/unbound, autorizaciones persistidas o de sesión, pausa/cancelación/revocación y Completed inconsistente. Las inyecciones matan exclusivamente el proceso hijo del fixture; nunca sustituyen la parada normal.
- Scheduler conserva admisión diferida también al cosechar HTTP y prioriza la continuación durable del Job. Mantiene gates, round-robin y exclusión global pesada/HTTP.
- Presupuestos de buffers ocultos/handles del bridge, liberación de tareas completadas, caché temporal limitada con pinning por identidad y eliminación segura por handle; pinning Windows admite rutas largas. PNG sintéticos únicos más allá de 256 seeds.
- Manual remoto: preparación/auditoría/ejecución, catálogo/documentos/objetivos/estadísticas, thumbnails, normalización, backup/restore/retención y recuperación/retry/push Git solo contra bare local desechable. Selección final **4/4**.
- WPF y parada no persistida: **6/6**, render real y cero errores de binding registrados; smoke no lee claves del entorno. Regresión software/presentación anterior a la optimización: **3771 ejecutadas, 3766 pasan, 5 symlinks fallan, 0 omitidas**.
- Última optimización de Core: hashes frescos de bytes completos del sidecar y pruebas de validación pura vinculadas a cada historial/raíces. Cache de pruebas global 8 MiB/4096; parsing de cola global 32 MiB de texto/32768 entradas. No cachea autoridad ni observaciones del filesystem. Dirigidas **139/139**, adversariales **7/7**, cola/admisión **93/98**, solo cinco fallos de privilegio symlink. Su aceptación integral sigue pendiente.
- Histórico de intentos fallidos y correcciones: `NAP_2_0_FASE_5_IMPLEMENTATION.md`. No sumar selecciones superpuestas. Advisories históricos ImageSharp no resueltos; build incremental con cero avisos no los certifica.

### Regresiones y fallos de volumen que NO están resueltos

1. Regresión completa **Release anterior a la optimización**, finalizada antes de la parada: `artifacts/phase5/final/p510-final-complete-regression.trx`, **3779 ejecutadas, 3772 pasan / 7 fallan / 0 omitidas**. Fallan cinco symlinks por privilegio, admisión histórica de 1000 por timeout de 20 minutos y host de 1000 por `NeedsReview/StorageUnavailable` tras 48 completados. El host de 100 sí pasó: 100/100 en 1839,5 s de medición, pico privado 115,4 MiB, 386 handles, 22 hilos. Es evidencia del Core anterior, no del último.
2. Regresión completa optimizada **INTERRUMPIDA POR ORDEN DE CUOTA**: `p510-final-optimized-complete.trx`, counters parciales **3759 ejecutadas, 3753 pasan / 6 fallan / 0 omitidas**. VSTest registra serie anulada; `notExecuted=0` NO significa cobertura completa. Cuatro symlinks fallaron antes de parar; el quinto no se certifica en esa ejecución. Los 22 casos de recuperación registrados pasan.
3. En esa última ejecución, host 1 y 10 pasan. Host **100 falla realmente**: 36 completados, `NeedsReview/StorageUnavailable`, medición 376,65 s, pico privado 65.716.224 bytes. No es un fallo de privilegio symlink ni se presume causa ambiental. La explicación interna original de la excepción aún no está capturada.
4. Host **1000 optimizado interrumpido voluntariamente**: último muestreo 12 completados en 86,41 s; cierre autenticado confirmado Stopped y posterior interrupción del testhost. Su `Process has exited` procede de la parada ordenada; no demuestra un defecto nuevo ni un éxito del lote. La serie quedó anulada, sin certificar admisión 1000 ni regresión final completa.
5. Los primeros fallos de progress timeout de 100/1000 se conservan. No se declaran resueltos por el éxito histórico de un lote 100 ni por pruebas dirigidas.
6. Los **13 fallos históricos de software** se cotejan individualmente como Passed en la regresión completa Release conservada; **5/5 fallos de symlinks** continúan Failed. `NAP_2_0_FASE_5_REGRESSION_COMPARISON.json` identifica pruebas y hashes. Al recopilar evidencia se corrigió el uso accidental de la variable automática PowerShell `$Matches`; el cotejo regenerado confirma 13 Passed/5 Failed, sin blancos.

### Parada de procesos y seguridad

Se capturó el estado parcial por IPC de solo lectura. Después, el host del laboratorio `nap-host-9db5c7e1b45548aa89255993e13e69ef` devolvió **Stopped para ambos universos, sin diagnóstico de fallo de persistencia**, mediante ShutdownHost autenticado; se verificó su salida. Solo entonces se interrumpió el testhost propio P510Review (PID observado 15348, padre 12224), para impedir más trabajo. El runner salió con código 1/serie anulada y produjo TRX. Se cerraron los build servers. La comprobación posterior no encontró host, WPF ni test runner de NAP activos.

No hubo producción/CUTOVER, cambios de servicios/cuentas/permisos Windows ni acceso de pruebas a originales reales de Nimroel. Las cuentas/ACL del antiguo ensayo CUTOVER quedaron en workflow manual con autorización booleana explícita; no se despachó. No se creará PR durante esta parada.

Los fixtures limpian normalmente sus universos sintéticos al terminar; no se promete conservar los sidecars de casos ya eliminados. Se conservan fuentes, TRX, métricas, capturas y respuesta de parada. No queda operación Git de merge/rebase/cherry-pick pendiente según la comprobación final.

### Punto exacto de reanudación — requiere nueva autorización

1. Verificar rama/HEAD remoto, árbol limpio y main/protección; leer continuidad, implementación, manifests, comparación y evidencia de parada. El código a diagnosticar está en `4549c48`, más la corrección del script de evidencia del commit de parada. No repetir las selecciones ya superadas sin cambios que lo justifiquen.
2. **Primera operación técnica recomendada:** reproducir y capturar la excepción interna que acaba en `StorageUnavailable` en `DurablePreparationService.Prepare` (catch IOException/UnauthorizedAccessException) con 40–60 o 100 masters sintéticos, conservando sidecars antes del teardown. Revisar el item en revisión y su checkpoint/receipts. No se ha demostrado todavía si el origen es lock, path, IO u otra condición; no inferirlo ni relajar gates/deadlines.
3. Corregir solo el defecto demostrado y ejecutar su regresión correspondiente. Revisar por separado el timeout real de admisión 1000. No hay aceptación de lotes 100/1000 con el último Core.
4. Completar volumen 1/10/100/1000 con métricas CPU/memoria/disco/handles/hilos/ledgers, fairness, ownership y ausencia de duplicados; después regresión completa tras las últimas correcciones y WPF/manual pertinente. No elevar timeouts para fabricar resultados positivos.
5. Actualizar evidencia y publicar commits validados. Crear Draft PR hacia main únicamente cuando la nueva orden de reanudación lo autorice y el cierre técnico esté documentado con sus limitaciones. No merge/producción sin aprobación. Identidad definitiva, instalación, cuentas/ACL y CUTOVER permanecen fuera de autorización.
