# NAP 2.0 — continuidad de Fase 5

## REVISIÓN FINAL VIGENTE — cambio de alcance aprobado el 2026-10-20

**Actualización posterior vigente:** ImageSharp 3.2.0 instalada directa/transitivamente en los ocho proyectos. El validador oficial acepta la licencia Community para `NAP.Core`, Release termina correctamente y 220/220 pruebas dirigidas de imágenes pasan. Ninguna prueba de volumen, regresión general o CI nueva. Licencia local ignorada; contenido/clave/firmas fuera de Git y de evidencia. Condiciones de distribución revisadas en `NAP_2_0_IMAGESHARP_LICENSE_SETUP.md`: las assembly-scoped pueden compartirse, pero la prohibición expresa de publicarla sigue vigente. Resuelto el bloqueo local de licencia; CI necesitará provisión autorizada. NuGet mantiene cinco avisos de backports que el proveedor confirma corregidos, sin supresiones. La observación IPC previa no se investigó en esta tarea.

El responsable prohíbe nuevas cargas de 20/50/100/1000 ZIP y repeticiones de volumen. Uso previsto 1–20; **100/100 histórico suficiente; 1000 retirado como gate**. Su fallo 280/1000 se conserva Failed. Las antiguas instrucciones de repetir/optimizar ese lote quedan expresamente supersedidas.

Recuperación inicial limpia/sincronizada en `b1bf7a0b816303bf40fa87da0625aad0d03e471d`; referencia `20106c7f876ff488ea362bd6f709b643c3b63b5e` presente y ancestro. Rama existente `codex/nap-2-fase-5-automation-host`, PR [#48 Draft](https://github.com/robdor80/NAP/pull/48). Main permanece `cd7532f011eea8a72e89848234b259f1f5293e15`, protected=false/reglas=[]; no se cambiaron ajustes.

Primera tarea completada antes de pruebas/commits: revisión de ambos workflows, cancelación controlada de CI `39468522189` (completed/cancelled), conservación de artefactos. Commit publicado `1bef7ecd1c1234ba0f4dbd014d7eef4bef93589a`: filtro general y CI sin AutomationVolume/AdmissionVolume, ambas teorías deshabilitadas por orden expresa; caso funcional entre páginas reducido de 64 a tres ZIP con páginas de dos y mismas assertions. Discovery confirma exclusión por defecto; caso reducido 1/1 Passed. Sin cambios de producto.

P5.1–P5.9 siguen implementados. **P5.10: CI funcional previo superado y volumen acreditado; persisten provisión de licencia para CI e IPC local sin causa demostrada.** CI `39474539947`, anterior a la actualización de ImageSharp, terminó success: Windows 3784/3784 (symlinks reales 5/5, recuperación 22/22, dieciocho fallos originales 18/18); Linux 3777 Passed y siete omisiones Windows. Local: 3778/3784, cinco fallos de privilegio y un timeout QueuePage de 20 s; selección posterior de inhibiciones 4/4 sin cambios ni causa demostrada. TRX fallido conservado. Sin cargas nuevas, simulaciones ni omisiones contadas como éxitos. Este MSI no tiene el privilegio requerido; no se han modificado cuentas ni permisos.

El bloqueo previo de ImageSharp en 3.1.12 se conserva como histórico en `NAP_2_0_FASE_5_IMAGESHARP_REVIEW.md`; la actualización vigente está indicada arriba. StorageUnavailable histórico sigue no reproducido, sin causa verificable ni corrección especulativa; diagnóstico original y parking seguro conservados. Sin nuevas cargas para provocarlo.

Reanudación/revisión humana: consultar primero `NAP_2_0_FASE_5_FINAL_REVIEW.md` y el dictamen de aceptación actualizado. No repetir volumen, activar CUTOVER ni fusionar. Cualquier actualización posterior de dependencia necesita resolver licencia y validación funcional pequeña; las garantías y barreras de Fases 1–4 permanecen.

El registro siguiente se conserva como **histórico**, incluidas sus instrucciones ya supersedidas por esta orden.

---

## CIERRE TÉCNICO PARCIAL PARA AUDITORÍA — 2026-10-20, Europe/Madrid

**P5.10 NO ACEPTADA INTEGRALMENTE: bloqueo de rendimiento en host 1000 y cinco pruebas Windows de symlinks sin privilegio.** La autorización de reanudación se ejecutó hasta obtener la regresión completa real; no se rebajaron límites ni contratos. Entrega destinada a PR Draft hacia main, sin merge ni producción. El informe vigente es `NAP_2_0_FASE_5_SOFTWARE_ACCEPTANCE.md`; el historial de suspensión siguiente no representa el trabajo actual.

### Recuperación, fuentes y Git

- Repo `robdor80/NAP`; rama existente `codex/nap-2-fase-5-automation-host`, recuperada limpia/sincronizada en `08894c03ac6738abab1eec705c9326fae52ec141`. Sin ramas nuevas, reset, force-push ni merge.
- Compilado ese HEAD: Release, 0 errores/0 avisos sin restaurar. Código de producto y tests sin cambios posteriores; nuevas entregas documentales y probe externo Windows. SHA/versiones de las DLL efectivamente ejecutadas en `NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_EVIDENCE.json`. No atribuir revisión embebida anterior a un commit documental posterior.
- Checkpoints publicados: `1d49dd2` (reanudación/dirigidas), `86fec8e` (matriz/procedencia) y `36dfadd` (probe externo). El commit de cierre incorpora evidencia final; su SHA y sincronía se comprueban con `git log -1` y origin y se entregan en la confirmación final.
- Main intacta en `cd7532f011eea8a72e89848234b259f1f5293e15`. **GitHub sigue indicando protected=false y reglas=[]**. No se cambió su configuración; resolver antes de cualquier fusión.

### Estado de los bloques

| Bloque | Estado actual |
|---|---|
| P5.1 | Owner/mailbox implementados; 19/19 coordinación de procesos en regresión, peer BUSY durante volumen. |
| P5.2 | Control durable/autorizaciones/CAS/receipts implementados; pausa/cancelación/reinicio cubiertos. |
| P5.3 | Admisión asíncrona implementada; 1/10/100/1000 independientes pasan en la versión actual. |
| P5.4 | Auditoría/timeout/resultados tardíos e inciertos implementados; 3/3 etapa IA y recuperación específica pasan. |
| P5.5 | Scheduler multiuniverso implementado; 1/10/100 pasan; 1000 no cumple 55 minutos. |
| P5.6 | Suspensión/recuperación implementadas; 22/22 escenarios reales de proceso pasan. |
| P5.7 | Cohortes/backpressure/proyecciones implementados; pendientes de aceptación integral a 1000. |
| P5.8 | Host/IPC autenticado implementados; laboratorio del mismo principal, CUTOVER no certificado. |
| P5.9 | Manual remoto y WPF Windows reales pasan; 4/4 manuales y 5/5 WPF en regresión final. |
| P5.10 | **PARCIAL, no aceptada**. Regresión completa terminada y evidencia preservada; bloqueo de rendimiento, symlinks pendientes. |

### Resultado final verificable

- Dirigidas del fuente final conservado: **56/56**, Windows manual/aceptación/WPF **16/16**, JSON Schema **436**. Selecciones superpuestas, no sumarlas.
- Regresión completa Windows: **3792 ejecutadas, 3786 Passed / 6 Failed / 0 omitidas**, terminada normalmente. TRX `artifacts/phase5/resume-20261020/resume-complete-windows.trx`. Cinco fallos de creación symlink por privilegio; uno de host 1000 por duración. Los trece fallos históricos de software pasan individualmente en ese TRX; comparación con nombres/hashes actualizada.
- Admisión independiente 1000: Passed, 841,121 s de medición / 14m43,552s del caso. No sustituye el pipeline integral.
- Host 100: Passed, 100/100 en 793,53 s; pico privado 75.280.384 B, 374 handles; 197.882.443.413 B lógicos leídos. Host 1/10 también pasan.
- Host 1000: **Failed: Only 280/1000 in 00:55:00.0524028**. Última muestra 140/140 Completed, CPU 3193,56 s, pico privado 159.006.720 B, 413 handles, 23 hilos, gap máximo 42,65 s y máximo 3 candidatos pendientes. Último contador vivo externo: 1.585.201.936.351 B lógicos leídos. Disco del laboratorio: 110.941.153 B/9758 archivos al cierre. No medirlo como E/S física ni presentarlo como lote aceptado.
- Fallo real de aceptación por rendimiento, no infraestructura. No se elevó el límite ni se repitió una tanda extensa sin una corrección. El contrato de hashes frescos completos permanece por decisión expresa del responsable.
- Ningún StorageUnavailable nuevo en el lote actual; stderr vacío. Causa histórica Windows no demostrada. Sin fallos ConnectionReset en la regresión final; no inferir causa del incidente Linux anterior.

### Parada y conservación

El fixture drenó el host mediante IPC autenticado y verificó salida 0; el runner terminó con código 1 por los seis casos fallidos, no por aborto. El sampler externo terminó al salir ese host. Build servers cerrados; comprobación posterior sin hosts, WPF ni test runners de NAP activos. No cambios reales de Nimroel, cuentas/permisos Windows, servicios, producción ni CUTOVER.

Copia sintética detenida: 280 Completed, 199 Queued, 1 Running; controles Stopped/Paused para ambos universos. Recuento offline read-only de 11 SQLite, integridad/FK y hashes antes/después sin cambios: 480 intentos/480 Jobs distintos, intento máximo 1; 281 llamadas/281 RequestId distintos, ninguno con más de una llamada; 140 WebP por universo. Son observaciones estructurales de copias, **no aceptación nativa de 1000 ni permiso para rebind/reparación**. Las assertions de originales/efectos posteriores al bucle del caso fallido no se alcanzaron.

Archivo nuevo `docs/evidence/NAP_2_0_FASE_5_WINDOWS_ACCEPTANCE_20261020.zip`, con inventario interno `evidence-inventory.json` y hashes verificados. Conserva TRX, logs, métricas/muestras externas, capturas y copia de sidecars sintéticos; excluye launch configs/claves. Los tres archivos históricos permanecen intactos. Los manifests de pruebas/recursos se amplían sin borrar entradas históricas. Inventario completo frente a Fase 4 en `NAP_2_0_FASE_5_CHANGED_FILES.txt`.

### Siguiente operación recomendada tras auditoría

1. Verificar HEAD/origin, árbol limpio y main/protección; leer aceptación, manifiesto Windows y fallo exacto 280/1000. No repetir selecciones ya verificadas sin cambios que lo justifiquen.
2. Perfilar/reproducir el coste de escalabilidad en un ensayo acotado; evaluar lecturas/validaciones/deserialización redundantes conservando todas las fronteras y hashes globales frescos. El perfil CPU Linux es histórico, los contadores Windows no atribuyen todo al SHA. No está autorizado reemplazar el contrato por validación incremental autenticada; pedir autorización si se necesitara un cambio fundamental.
3. Corregir solo un defecto demostrado y demostrar mejora/integridad con pruebas dirigidas; entonces repetir 1000 y la regresión final pertinente. La admisión 1000 ya pasó y no debe confundirse con procesamiento completo.
4. Ejecutar los cinco symlinks en Windows con privilegio apropiado sin alterar este equipo. Revisar el CI de la Draft; no considerar sus resultados pendientes como éxitos. Mantener StorageUnavailable histórico sin causa atribuida y advisories ImageSharp abiertos.
5. CUTOVER sigue fuera de alcance: identidad/arranque/distribución protegidos, ACL/padres, exclusión de escritores antiguos, preservación/backup y autorización productiva separada. No fusión sin aprobación humana.

## Historial conservado — las instrucciones de suspensión siguientes han sido levantadas

## PARADA CONTROLADA VIGENTE — 2026-10-18, UTC

**Desarrollo suspendido por orden expresa del usuario. P5.10 PARCIAL, sin aceptación integral. No continuar código ni pruebas hasta nueva autorización.** Esta sección sustituye las indicaciones de trabajo en curso que se conservan debajo como historial. La breve autorización intermedia de reanudación fue revocada; no se inició ninguna prueba ni desarrollo entre ambas órdenes.

### Estado, commits y decisión del responsable

- Rama existente `codex/nap-2-fase-5-automation-host`, repositorio `robdor80/NAP`. Recuperación inicial verificada exactamente en `63a50cc6e381221f66caa68bc28632545e2d338a`. Main permanece en `cd7532f011eea8a72e89848234b259f1f5293e15`; no se alteraron configuración, producción, CUTOVER, servicios, cuentas, permisos ni archivos reales de Nimroel.
- Último commit publicado antes de esta conservación: `836ba1a02b38df2b999bfc9beb7ed98eaeb0bcf7`. El trabajo parcial de polling se conserva en `b9ddeba`; el commit documental posterior contiene este cierre y su SHA se obtiene con `git log -1`. La comprobación final de publicación comunica su coincidencia con origin; no se atribuye ese commit a binarios anteriores.
- Decisión explícita recibida: **conservar el contrato actual y auditar P5.10 parcial**. No está autorizado reemplazar validación global por validación incremental autenticada. Se conservan hashes frescos completos, ownership, originales, receipts y gates existentes.

| Bloque | Estado exacto al parar |
|---|---|
| P5.1 | Owner/mailbox implementados; pruebas específicas conservadas. Aceptación transversal final pendiente. |
| P5.2 | Control durable/CAS/receipts, pausa/cancelación/reinicio implementados y cubiertos específicamente. |
| P5.3 | Admisión asíncrona implementada; admisión sintética 1000 pasó en versión intermedia. Última versión no certificada a 1000. |
| P5.4 | Auditoría/transport, resultados inciertos, timeout y respuestas tardías implementados; recuperación específica conservada. |
| P5.5 | Scheduler multiuniverso, continuación durable y exclusiones implementados; lotes 1/10/100 completados. Fairness/recursos finales de 1000 pendientes. |
| P5.6 | Recuperación implementada; 22/22 escenarios pasan en la última selección completada, con la salvedad de versión del fixture indicada abajo. |
| P5.7 | Proyecciones/backpressure implementados; aceptación prolongada y cierre bajo 1000 pendientes. |
| P5.8 | Host/IPC autenticado implementados; laboratorio sintético del mismo principal. Instalación/ACL/identidades reales fuera de alcance. |
| P5.9 | WPF remoto y operaciones manuales implementados; rutas manuales portables verificadas. Nueva ejecución WPF real pendiente en Windows. |
| P5.10 | **PARCIAL**: diagnóstico y optimizaciones conservados; regresión final y 1000 sin aceptación, causa histórica StorageUnavailable no demostrada. |

### Última tarea completada y trabajo interrumpido

Última selección terminada antes de la parada: `recovery-and-stop-final.trx`, **26/26** (22 recuperación y 4 diagnóstico de almacenamiento). Sus binarios FixtureReview incluían un filtro de reintento de consultas más amplio que el fuente final; el último estrechamiento a `IOException` con `SocketError.ConnectionReset` no se compiló/ejecutó después por orden de parada. No se certifica el HEAD final mediante esa selección.

Regresión Release más reciente, aplicación `37578e2`, **INTERRUMPIDA por la parada**: `final-buffered-complete.trx`, **3764 resultados registrados: 3755 Passed / 2 Failed / 7 omitidos**; counters `executed=3757`, serie anulada en mensajes. `notExecuted=0` no significa cobertura completa. Los dos fallos registrados se conservan: cleanup externo con ConnectionReset (corregido posteriormente en `836ba1a`, caso aislado y tres repeticiones superadas), y QueuePage de recuperación `audit-response-received` con reset durante handshake (sin causa exacta demostrada; estabilización parcial de polling conservada). No se borran ni reclasifican como éxitos.

La regresión portable anterior terminó: **3773 Passed / 0 Failed / 7 omitidos**, total 3780; versión intermedia, no aceptación del último código. Las cinco pruebas históricas de symlinks se ejecutaron aquí y pasaron **5/5 Linux**, sin cambios de privilegios. No certifican Windows. Contrato de schema: **436 comprobaciones superadas**. Selecciones dirigidas y repeticiones se conservan individualmente; no sumar ejecuciones superpuestas.

Volumen sobre aplicación `37578e2`: 1/1 en 6,12 s, 10/10 en 28,91 s, **100/100 en 628,56 s**, pico privado 402.964.480 bytes/RSS 290.095.104 bytes, 139 handles, 23 hilos, mayor intervalo de progreso 35,55 s. Se observaron 197.659.925.710 bytes lógicos leídos y 24.200.903 lecturas: persiste coste de validación completa. La admisión separada de 1000 pasó antes en 878,23 s de admisión (15m48s de caso completo); no equivale al procesamiento host de 1000 ni a una prueba final del HEAD.

El host de 1000 fue drenado mediante **ShutdownHost autenticado**, Stopped en ambos universos, diagnóstico nulo y salida de proceso verificada. Se copiaron sus sidecars antes de terminar el testhost: **52 Completed (26 por universo), 115 Queued; resto de entradas aún no admitidas**. No hay aceptación de 1000 ni fallo StorageUnavailable nuevo demostrado en ese lote. El testhost se suspendió para impedir nuevos casos durante el drenaje y después se terminó; el runner registra aborto como crash de testhost, consecuencia de esta parada deliberada. La selección paralela de recuperación ya había terminado normalmente.

### Cambios y evidencias conservados

Se conservan diagnóstico interno de IO del laboratorio (sin exponer claves en IPC/receipts), copias sintéticas al fallar volumen, lecturas de hechos agrupadas, caché débil de perfiles inmutables y pruebas de ledger vinculadas a ruta/raíces/SHA-256 fresco completo dentro del presupuesto original. El buffer de hashing de 128 KiB reduce lecturas pequeñas sin saltar bytes ni cambiar autoridad. El benchmark aislado de 64 MiB confirma hashes idénticos y aproximadamente 16.387 frente a 515 lecturas; no demuestra escalabilidad de 1000. Cleanup del fixture exige salida real con código 0 y el test verifica stop durable por separado. La estabilización parcial solo reintenta Status/QueuePage hasta dos veces, autenticando de nuevo dentro del mismo límite de 20 s; nunca reproduce mutaciones ni extiende su deadline.

- Archivo histórico de 73 entradas y progreso cloud de 33 entradas conservados sin sustitución.
- Nuevo archivo `docs/evidence/NAP_2_0_FASE_5_CLOUD_STOP_20261018.zip`, inventario/hash por entrada en `docs/NAP_2_0_FASE_5_CLOUD_STOP_EVIDENCE.json`: TRX, logs, métricas, fuente del probe, procedencia de binarios, respuestas de parada y sidecars sintéticos. Excluye launch configs y claves IPC. Las trazas crudas del profiler quedan solo locales ignoradas con hashes; su resumen CPU sí se publica. No se promete conservar sidecars ya eliminados por teardown de casos anteriores.
- `NAP_2_0_FASE_5_TEST_EVIDENCE.json` y `NAP_2_0_FASE_5_RESOURCE_EVIDENCE.json` incorporan nuevas entradas sin eliminar las históricas. `NAP_2_0_FASE_5_CHANGED_FILES.txt` contiene el inventario frente a main. La conservación parcial final modifica dos fixtures, continuidad, implementación, manifests e inventario; el archivo nuevo conserva los datos generados.
- Todos los hosts/test runners propios terminaron y se cerraron build servers. No PR, merge, force-push, reset destructivo ni eliminación de ramas. Comprobación final de árbol limpio, ausencia de operación Git incompleta y sincronía remota tras publicar.

### Pendientes y punto exacto de continuación

1. **Primera acción tras nueva autorización:** verificar branch/HEAD remoto y árbol, leer este cierre y el manifest de parada; compilar el fuente conservado y validar el filtro final estrecho de polling y cleanup. No repetir análisis o versiones ya documentadas sin un cambio que lo justifique.
2. Revisar por separado los dos resets documentados. El polling es solo una mejora del fixture, no una explicación demostrada de la causa del reset ni una corrección del StorageUnavailable histórico.
3. StorageUnavailable histórico Windows no se reprodujo en los lotes Linux 100; causa pendiente de captura de excepción en ese entorno. Los logs de laboratorio ya permiten conservarla. No atribuirlo automáticamente al entorno ni relajar permisos, integridad o receipts.
4. Conservar el contrato global por decisión del responsable. Ensayo final de admisión/procesamiento 1000, fairness/ownership/deduplicación/recursos y regresión completa siguen pendientes. Coste de lectura de DB completa limita escalabilidad; no elevar límites para fabricar aceptación.
5. Validación real Windows/WPF y dos casos nativos de pinning/cache pendientes: las siete omisiones Linux no son éxitos. Los resultados Windows anteriores son históricos. Advisories existentes ImageSharp 3.1.12 pendientes, no resueltos por este trabajo.
6. Actualizar aceptación con resultados reales y preparar Draft PR solo cuando se reautorice el trabajo y corresponda al cierre. **Ahora detenerse por completo.** No fusionar ni activar producción.

## REANUDACIÓN CLOUD — 2026-10-18, Europe/Madrid (en curso)

La nueva autorización expresa del responsable levanta la parada de cuota únicamente para continuar P5.10 en universos sintéticos. Se mantiene prohibido producción, CUTOVER, cambios de cuentas/permisos/servicios y merge.

- Comandos y lectura GitHub verificados. Checkout inicial limpio en `work`, SHA de main `cd7532f011eea8a72e89848234b259f1f5293e15`; recuperada la rama existente `codex/nap-2-fase-5-automation-host` exactamente en `63a50cc6e381221f66caa68bc28632545e2d338a`, sin commits posteriores al recuperar. Main remoto conserva la misma SHA; GitHub sigue informando `protected=false`. No se cambió su configuración.
- Leídos los seis documentos exigidos, inventario y manifests adicionales. Verificados SHA-256 del archivo histórico y sus **73 entradas**; contiene **58 TRX**, no 73 ejecuciones independientes. Los resultados anteriores siguen siendo históricos.
- Runtime cloud Linux x64: SDK .NET 8.0.426/runtime 8.0.32 y PowerShell 7.4.13. Sin servicios/proveedor real. Las cinco pruebas históricas de symlinks se ejecutaron y pasaron **5/5** aquí; no se extrapola a permisos Windows.
- Primer lote separado de 100, sobre el Core recuperado con diagnóstico añadido: **100/100**, 628,49 s, dos universos, máximo 3 candidatos, 45,80 s de mayor intervalo de progreso. Pico RSS 302.485.504 bytes/privado 423.600.128 bytes, 138 handles, 24 hilos. No reprodujo `StorageUnavailable`; no demuestra una corrección del fallo Windows. Evidencia `artifacts/phase5/cloud-resume/volume100-diagnostic-v2.trx` y métrica `automation-100-072355.json`.
- Diagnóstico original de IO preservado exclusivamente por callback del laboratorio y stderr; los fallos del fixture de volumen drenan y conservan sidecars sin copiar `launch.json` ni su clave IPC. Se cubren preservación y publicación de respuesta IA sin crear intentos alternativos ni convertir el fallo en éxito.
- Optimizaciones en validación: regla inmutable por identidad débil; lectura conjunta de hechos de una frontera; pruebas de DB completa compartidas entre stores únicamente por SHA-256 fresco, ruta y raíces exactas. No hay aprobación automática de bytes escritos, autoridad cacheada ni cambios de schemas/gates. Pruebas de corrupción y regresión finales siguen en curso.
- Dirigidas iniciales 9/9; siguiente selección de autoridad/ejecución/diagnóstico 59/59. Son selecciones superpuestas y versiones intermedias; no sustituyen la regresión final. Admisión 1000, recuperación y regresión transversal siguen ejecutándose. Las pruebas Windows/WPF y el fallo Windows de IO no están certificados por esta máquina.

**Estado actual: P5.10 aún no aceptado.** El cierre final actualizará esta sección y conservará las invocaciones fallidas y resultados pendientes. No se ha creado PR ni publicado este nuevo cierre todavía.

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

Actualización 2026-10-20: dirigidas del HEAD **56/56** y Windows/manual/WPF **16/16**, cero omisiones; regresión completa Windows con ambos grupos de volumen iniciada. Pendiente su resultado real. Fuente de aplicación conservado de 08894c0, sin cambios posteriores de código hasta este punto.
