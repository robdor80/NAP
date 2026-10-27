# NAP 2.0 — F7.5 · Auditoría integral y acta de cierre técnico de Fase 7

**Fecha:** 27 de octubre de 2026  
**Repositorio:** `robdor80/NAP`  
**Base inmutable auditada:** `main` = `f29dc943718b8070c29931ee95ec20d2261a8231`  
**Rama documental:** `chatgpt/nap-2-f7-5-technical-closure`  
**Naturaleza:** auditoría independiente de código, configuración, relaciones entre componentes y evidencia histórica. **No incluye nuevas ejecuciones de tests, workflows, ZIP, Git sobre repositorios reales ni certificación productiva.**

## 1. Dictamen ejecutivo

**Dictamen F7.5: favorable para aceptación del cierre técnico del desarrollo de Fase 7, con reservas operativas explícitas y sin autorización de producción.** No se ha identificado en las rutas inspeccionadas un defecto nuevo demostrado que obligue a cambiar código crítico o a repetir pruebas. Las garantías se limitan a los contratos conservadores del motor, a escenarios históricos concretos y al modelo de confianza local documentado. No son garantía absoluta contra actores externos, pérdida física de durabilidad, hardware ni CUTOVER.

F7.5 **no realiza una integración adicional**: F7.1 está incorporada documentalmente; F7.2, F7.3, F7.4 y F74-A1 ya están en `main` a través de merges previos. El cierre definitivo requiere **aceptación del responsable y fusión expresa** de la PR documental; esta acta, por sí misma, no declara fusionada F7.5.

La **certificación funcional/visual Windows 11 y MSI**, el despliegue y la activación Git automática real **NO** están aprobados. Las incidencias **#47 y #49 permanecen abiertas**.

## 2. Estado de GitHub contrastado

| Elemento | Hecho observado |
|---|---|
| `main` | HEAD `f29dc943718b8070c29931ee95ec20d2261a8231`, rama marcada `protected` |
| PR #54 / F7.2 | CLOSED/MERGED 26-10-2026; merge `809fe00b5397c38ff6031987fbc7058f9b238d5b` |
| PR #55 / F7.3 | CLOSED/MERGED 27-10-2026; merge `c4a053226f6f27c0cf109ef89cf1a92be1117f36` |
| PR #56 / F7.4 y F74-A1 | CLOSED/MERGED 27-10-2026 03:42:59 UTC; merge `f29dc943718b8070c29931ee95ec20d2261a8231` |
| PR abiertas al inicio | Ninguna |
| Ramas remanentes anteriores | `codex/nap-2-fase-3-preparation-audit`, `codex/nap-2-fase-4-coordination-checkpoint`, `lab/imagesharp-320-release-fd7c4ff-20261020`; no borradas ni modificadas |
| Issue #47 | OPEN: certificación MSI y CUTOVER |
| Issue #49 | OPEN: IPC, StorageUnavailable y ImageSharp |

**Histórico de F7.1:** arquitectura, contrato, matriz y acta documentales presentes en `main`, sin atribuirle una PR independiente que no se haya acreditado. Los documentos históricos redactados antes de cada merge retienen legítimamente su estado «pendiente» de aquella fecha; **el estado vigente lo determina GitHub**, no la portada de esos informes.

**Workflows por YAML real en `main`:** `.github/workflows/ci.yml` (`NAP CI`) solo `workflow_dispatch`; `cutover-laboratory.yml` solo `workflow_dispatch` y autorización explícita; `phase6-review.yml` solo `workflow_dispatch`; `phase6-msi-preparation.yml` admite `workflow_dispatch` y un `push` exclusivamente en `codex/nap-2-fase-6-ux` con allowlist de paths. La rama y el fichero documental F7.5 quedan fuera de dicho disparador. El listado histórico de runs no acredita nuevas pruebas F7.5. **La habilitación administrativa en GitHub no se ha certificado mediante el endpoint administrativo**; la prohibición de ejecutar cualquier workflow permanece.

## 3. Fuentes y auditoría efectivamente realizada

**Contratos/documentación cotejados:**
- `docs/NAP_2_0_F7_1_GIT_ARCHITECTURE.md`, `NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md`, `NAP_2_0_F7_1_CIERRE.md`.
- `docs/NAP_2_0_F7_2_IMPLEMENTACION.md` y `NAP_2_0_F7_2_PRUEBAS.md`.
- `docs/NAP_2_0_F7_3_IMPLEMENTACION.md`, `NAP_2_0_F7_3_PRUEBAS.md`, `NAP_2_0_F7_3_F73_A1.md`, `NAP_2_0_F7_3_F73_A2.md`.
- `docs/NAP_2_0_F7_4_ARQUITECTURA_Y_AUDITORIA.md`, `NAP_2_0_F7_4_IMPLEMENTACION.md`, `NAP_2_0_F7_4_PRUEBAS.md`, `NAP_2_0_F7_4_MATRIZ_DE_ACEPTACION.md`, `NAP_2_0_F7_4_F74_A1.md`.
- `docs/GIT_PRODUCTION.md` y textos de #47/#49.

**Código contrastado directamente en `main`** (lecturas completas o fragmentos relevantes según archivo): `GitPublicationPolicyStore`, `GitPublicationManagementService`, `GitPublicationManualService`, `GitPublicationFiles`, `GitOutboxStore`, `GitBatchPublicationService`, `GitBatchPublicationPush`, `GitProductionService`, `GitProductionInspector`, `GitProductionProcess`, `AutomationHost`, `AutomationProtocol`, `GitManagementUiService`, `GitManagementViewModel`, `ShellGitManagement`, `UniverseOperationDispatcher`, `AutomationDesktopBridge`, `RemoteDesktopService` y `GitView.xaml`. Se cotejaron además los blobs de fuentes críticas con los manifiestos de F7.4/F74-A1. La auditoría es de los puntos de integración y seguridad, **no una inspección formal exhaustiva línea por línea de todo NAP**.

## 4. Arquitectura efectiva y flujo de autoridad

```text
Job.Completed + Queue.Completed [autoridad del procesamiento; no depende de Git]
  -> paginación por evento Completed / cursor Outbox durable
  -> VerifyCompletedReadOnly [evidencia original, receipt, catálogo PASS, hashes]
  -> intent Outbox autenticado y ligado al universo/repo/revisión/autorización
  -> batch determinista por IDs, como máximo MaxJobsPerBatch
  -> reserva durable OperationId + PlanHash
  -> GitProductionService [único motor nativo, RequireNoPending, staging exacto]
  -> receipt Prepared -> Committed
  -> [solo autorización independiente AllowPush + modo compatible]
  -> verificación tip remoto / push normal o AlreadyPushed
  -> receipt Pushed -> Outbox batch+intents Pushed
```

Autoridad: `AutomationQueueOwner`/hilo host para `RunOnce` y retry, acceso `AutomationManualAccess`, mutex de política y mutex Production nativo. La GUI no fabrica el owner, ni reemplaza los receipts, ni compone comandos Git arbitrarios. El dispatcher alojado solo admite mantenimiento con host pausado/detenido y sin Queue Running. IPC usa operaciones tipadas, autenticación/alcance, handles retenidos en servidor y deduplicación, no rutas libres ni políticas remitidas por el cliente. `_verified` de UI no constituye fuente durable de ownership Outbox.

## 5. Matriz consolidada de contratos de seguridad

| ID | Contrato | Contraste de código/evidencia | Dictamen |
|---|---|---|---|
| S01 | Disabled predeterminado, sin inferir consentimiento ZIP/GCM | `GitPublicationPolicyStore.LoadLocked`; `ReadConfiguration`; F72/F74 TRX | Acreditado |
| S02 | Cuatro modos y `AllowCommit`/`AllowPush` independientes; push exige ambos | `GitPublicationModels`, `PolicyStore.Require/Validate`, editor y `GitUiF74Tests` | Acreditado |
| S03 | Políticas cerradas, HMAC, directorio protegido, CAS, revisión, vínculo universo/raíces/repo | `GitPublicationFiles`, `PolicyStore.Save/Load/Require`, ancla `GitOutboxStore`; negativos F72/F74 | Acreditado dentro del modelo de confianza local |
| S04 | Revocación no remota; no se reactivan intenciones antiguas ni se concede permiso por nueva revisión | `ManagementService.Revoke`, `OriginalAuthorization`, F73/F74 | Acreditado |
| S05 | Completed sigue independiente de Git; reconciliación paginada sin reprocesar ZIP | `AutomationHost`, `Reconcile`, `VerifyCompletedReadOnly`; F72 | Acreditado en escenarios seleccionados |
| S06 | Selección/ownership nativo, SHA-256, staging exacto, rechazo índice/dirty ajenos | `GitProductionService` y `GIT_PRODUCTION.md`; F72/F73 | Acreditado; no se relaja el motor |
| S07 | Un único motor Git; no force, reset, pull, merge, runner alternativo ni URL libre | `GitProductionProcess`/servicio, adaptadores, diff F74 | Acreditado en rutas inspeccionadas |
| S08 | `RequireNoPending` y segundo lote bloqueado | `GitProductionService.RequireNoPending`; regresión F72/F73/F74 | Acreditado |
| S09 | Reserva antes de Prepared, mismo OperationId/SHA, recuperación sin commit duplicado | `GitBatchPublicationService`, `GitProductionService.Recover/RetryCommit`; F72 | Acreditado mediante checkpoints/reinicio simulado |
| S10 | Publicación separada, verificación SHA remoto, no push automático sin permiso original | `GitBatchPublicationPush`, `GitProductionService.PushLocked`; F73 | Acreditado en bare ficticio local |
| S11 | Presupuesto durable de 3 pushes y hasta 3 comprobaciones agotadas; no cuarto push | `GitBatchPublicationPush`; F73-A1 | Acreditado |
| S12 | Receipt Pushed durable recupera Outbox por verificación local sin sondeo remoto al límite | `VerifyDurablePushedBound`; F73-A2 | Acreditado |
| S13 | Batch/intent/receipt coherentes, IDs/PlanHash/SHA exactos, sin borrar evidencias | `GitOutboxStore.RequireReceipt/RecordPushedLocked`, `ManagementService.ReadOutbox` | Acreditado |
| S14 | WPF revisa y confirma por separado autorizar, guardar, revocar, commit y push | `ShellGitManagement`, `GitManagementViewModel`, `GitView.xaml`; F74 | Acreditado en código/tests, pendiente ejecución visual Windows |
| S15 | IPC, respuestas tardías, scope, host legítimo y cero fallback local en retry | `AutomationDesktopBridge`, `RemoteDesktopService`, `GitManagementUiService`, dispatcher; F74 | Acreditado en prueba focalizada; #49 no cerrado |
| S16 | Diagnósticos sin mensajes hostiles, intervención clara y repositorio humano saneado | `GitOutboxGuidance`, `GitRepositoryIdentity`, `GitView.xaml`; F74-A1 | Acreditado en 2 pruebas sin Git ni ZIP |

Interpretación estricta: **«acreditado» significa revisión de implementación más evidencias históricas identificadas**, no que se hayan repetido tests en F7.5. Un bloqueo conservador ante incertidumbre puede exigir intervención humana: no debe reinterpretarse como recuperación automática completa.

## 6. Evidencias históricas reutilizadas y comprobaciones nuevas de F7.5

| Fuente final | Resultado constatado en el TRX almacenado | Contexto y reserva |
|---|---|---|
| `docs/f7-2-evidence/f72-final-core.trx` + `f72-final-guards.trx` | 14/14 + 5/5 Passed | Linux x64, .NET 8.0.427, fixtures temporales; sin Windows/MSI |
| `docs/f7-3-evidence/f73-final.trx` | 27/27 Passed | Remotos bare ficticios; correcciones posteriores auditadas aparte |
| `docs/f7-3-a1-evidence/f73-a1.trx` | 5/5 Passed | Dos casos nuevos de presupuesto y regresiones mínimas |
| `docs/f7-3-a2-evidence/f73-a2-final.trx` | 5/5 Passed | Incluye reproducción fallida previa conservada en `f73-a2-before.trx` |
| `docs/f7-4-evidence/f74-delivery.trx` | 17/17 Passed | Contratos UI/IPC y regresiones, compilación WPF cruzada; no WPF visual |
| `docs/f7-4-a1-evidence/f74-a1-final.trx` | 2/2 Passed | Solo diagnóstico y etiqueta; cero ZIP y cero procesos Git de test |

En los siete TRX finales relacionados arriba se leyó directamente `Counters` y se contrastaron outcomes `UnitTestResult`: **75 ejecuciones Passed, 0 Failed, 0 NotExecuted**. **No significa 75 escenarios únicos:** algunos casos F72/F73 se repitieron como regresión en F74 y las ejecuciones iniciales fallidas siguen preservadas.

**Verificación de integridad por objetos Git:** se cotejó el manifiesto `docs/f7-4-a1-evidence/final-source-manifest.json` con el árbol real `main`: **13 entradas individuales coincidentes y los 3 árboles `src/NAP.Core`, `src/NAP.AutomationHost` y `.github` coincidentes**. El manifiesto F7.4 inicial tiene 5 diferencias de presentación respecto a `main`, exactamente las cinco rutas modificadas después por F74-A1; los restantes **22 de 27** blobs esperados coinciden. Se inspeccionó presencia de los manifiestos `SHA256SUMS` y fuentes históricas. **Esta comparación de SHA de blobs/árboles Git no constituye recalcular independientemente todos los SHA-256 de los archivos saneados ni reproducir los ejecutables de test**. Los resúmenes preservan tanto outcomes correctos como fallos históricos, sin fingir una CI general nueva.

**F7.5 ha ejecutado 0 pruebas nuevas, 0 ZIP, 0 comandos de motor Git sobre repos reales, 0 workflows y 0 operaciones productivas.** No se repiten suites/volumen/estrés, de acuerdo con la autorización.

## 7. Riesgos, reservas y puertas que permanecen cerradas

1. **#47 — BLOQUEO PRODUCTIVO:** falta certificación/operación real en MSI Raider, instalación y CUTOVER autorizado; no tocar raíces reales, ACL, cuentas Windows, servicios ni ejecutables productivos; no activar Git automático.
2. **#49 — ABIERTA:** timeout/cancelación IPC, StorageUnavailable histórico sin causa cerrada y advertencias NU1902/NU1903 de ImageSharp 3.2.0. Ni la compilación ni la presente auditoría resuelven estos problemas.
3. **Entorno y UX:** Linux x64/Debug y build cruzado WPF no prueban Windows 11/NTFS, escalado 125–200 %, navegación teclado, capturas reales, MSI, instalación o Release con licencia.
4. **Resistencia a interrupciones:** checkpoints y servicios reconstruidos no equivalen a matar proceso en un punto arbitrario, fallo eléctrico, fallo de disco, fsync portable de directorios ni transacción física única entre `policy.json`, ancla Outbox y receipts. La discordancia entre finales debe BLOQUEAR y requerir revisión administrativa; no se borra/recrea estado.
5. **Concurrencia externa y Git:** el mutex solo coordina escritores compatibles; herramientas externas y administradores del servidor Git no están bajo él. El push normal sin force no proporciona CAS remoto absoluto. Sin autenticación HTTPS/SSH/GCM real ensayada.
6. **Evidencia/ownership incompletos:** si faltan fuentes retenidas, snapshots, catálogo PASS, permisos actuales de procesamiento o proofs locales, la intención queda Blocked; no hay adopción por carpeta ni reparación silenciosa.
7. **Límites y recuperación humana:** Outbox 2 MiB/4096 registros y lotes 1–20, presupuestos de retry acotados; agotamiento, grants históricos, `RequireNoPending` y receipts corruptos pueden bloquear nuevas operaciones hasta revisión/acción explícita. No se ensayó capacidad de volumen.
8. **Integridad histórica:** no confundir bitácoras/TRX con certificación autónoma de todo el sistema. Preservar fallos anteriores, logs, manifests y receipts para trazabilidad.

## 8. Contratos de preservación y prohibiciones permanentes

No modificar, limpiar ni «corregir» `main`, receipts, Outbox, historiales de grants, resultados históricos, GitProductionService, middleware de seguridad ni infraestructura previa por facilidad documental. No ejecutar `NAP CI`, CUTOVER ni workflows, no nuevos ZIP ni pruebas Git contra remotos reales, no credenciales/licencias/ACL, no force/pull/rebase/reset, no nuevas funcionalidades ni cambio de política de producción. La única propuesta F7.5 es **este fichero Markdown** en rama aislada.

## 9. Conclusión y condiciones de aceptación

**Conclusión independiente:** para el **cierre técnico del desarrollo F7** existen continuidad contractual, código de integración y evidencias focalizadas consistentes. No se constata una regresión técnica nueva en las rutas auditadas que obligue a Codex ni a pruebas nuevas. **Codex no es necesario para F7.5 en el alcance autorizado.**

Se recomienda **aceptar técnicamente F7.1–F7.4 + correcciones**, con reservas explícitas, y fusionar la PR documental **solo cuando lo autorice el responsable**. Hasta ese momento, F7.5 queda **preparada para aceptación, no fusionada**. Incluso tras su fusión, **NAP 2.0 no estará certificado ni autorizado para producción**; #47, #49, revisión visual Windows/WPF, MSI/CUTOVER y advisories continuarán pendientes.

**No se ha efectuado activación productiva ni ninguna mutación Git de los repositorios gestionados por NAP.**
