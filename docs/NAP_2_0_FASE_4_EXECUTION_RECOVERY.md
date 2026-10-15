# NAP 2.0 — Fase 4: ejecución y reconciliación idempotente

Rama: `codex/nap-2-fase-4-execution-recovery`. Inicio de implementación: 2026-10-15.

## P4.1 — Autorización

`Enabled` es una transición explícita CAS de la política vigente, con autorización exacta de universo, versión, hash, actor y raíces. Exige `Admit|Audit|Archive|Produce|Register`; `Normalize` continúa separado. Una definición, autorización o PASS aislados no activan nada. `Disabled` sigue siendo el default; `PreparationOnly` rechaza cualquier permiso productivo. Pausa suspende capacidades; revocación es terminal. Una versión nueva no hereda autoridad.

Enabled también permite preparar/auditar bajo los términos preproductivos existentes de esa misma política. No se amplía PreparationOnly. No se cambia DDL de cola, catálogo ni journal v1; lectores antiguos que rechazan Enabled siguen fallando de forma cerrada. No se habilitan universos reales ni se compone un host.

La evidencia de ejecución es un sidecar independiente del journal y de la preparación inmutable. El adaptador consume el snapshot y receipt PASS físicos, mantiene la reserva y la terna item/intento/Job y usa los motores existentes con guardas en cada efecto. Los finales sin ownership posterior a publicación permanecen ambiguos, aunque sus bytes sean iguales.

## P4.2 — Adaptador explícito

`DurableExecutionService.Execute(owner, itemId)` usa un UniverseContext fijo. `VerifyExecution` relee admisión, bundles original/candidato/auditoría, ZIP/CRC/inventarios, semántica, perfil completo, motor, geometría/píxeles, lineage, reserva, solicitud, grant y PASS. No ejecuta Prepare, no reaudita y no asigna Job nuevo. La producción conserva el receipt nativo del mismo Job. El catálogo se compara con hechos físicos verificados, sin añadir JobId al schema existente: el binding del ledger y el receipt productivo establecen esa relación.

`ExecutionLedger.db` es independiente y liga item/intento/Job, roots/policy/snapshot/PASS/grant/autorización. SQLite DELETE/FULL, schema y metadata exactos, quick_check/FK, CAS e historial con hash previo; sidecars desconocidos bloquean el ámbito. Guarda intención antes de efectos y receipt después de verificar publicación. Hooks internos opcionales incorporan esas fronteras a los motores existentes y mantienen sus APIs manuales. No se sostiene transacción/lock de DB al llamar al coordinador.

Antes de cada efecto se revalida autoridad vigente, raíces y handoff físico. El archivo exige receipt posterior a publicación para cada fichero existente; IndexExisting ajeno se rechaza. Las entradas temporales ambiguas se conservan y aparcan. La cola registra Execution/Verification/Catalog y solo cierra después del journal y catálogo verificados.

Release Windows: 0 errores y 0 advertencias (build incremental). P4.2: 8/8 pruebas aprobadas, con y sin derivación autorizada, repetición sin cambios de bytes/catálogo, gates Disabled/PreparationOnly/Paused/Revoked y WARNING/FAIL. Un defecto inicial al comparar el identificador de lineage se corrigió usando el NormalizationId existente y se repitió la tanda completa.

## Evidencia incremental de validación

Primera tanda P4.1 Windows: 71 aprobadas, 4 fallidas al intentar crear symlinks sin privilegio del sistema (`phase4-p41.trx`). Son pruebas preexistentes de AutomationQueueTests; no se omiten ni se relajan. Resultados definitivos de fase pendientes.

P4.1 políticas: 28/28 aprobadas (`phase4-p41-policy.trx`). Los resultados focalizados se solapan y no se suman.

## P4.3 — Reconciliación por evidencia

La reapertura usa el owner exclusivo de un epoch nuevo. Tras verificar binding, handoff, reserva, journal y outputs, se transfiere solo el claim Running obsoleto mediante CAS y evento ClaimRecovered. No se usa TTL, no se compite con otro owner y no se adquiere un segundo mutex Job alrededor del coordinador. Un journal avanzado necesita su intención previa del ledger. Un claim sin evidencia válida se aparca.

| Ventana | Recuperación |
| --- | --- |
| Antes/después del intent de ejecución o claim | Mismo item/intento/Job; no nueva preparación ni auditoría. |
| Antes del copy de archivo, o después de su receipt | Completar únicamente ficheros faltantes; verificar cada final propio. |
| Temporal de archivo o rename sin receipt de fichero | NeedsReview; conservar bytes y temporales. |
| Índice publicado, receipt de índice pendiente | Revalidar índice y todos los recibos propios de fichero; no adoptar archivo ajeno. |
| Antes/después de conversión o receipt inicial de producción | Reconstruir plan con perfil/motor idénticos; publicación pendiente conserva sus límites. |
| WebP/final publicado sin receipt productivo nativo | NeedsReview, incluso con hash igual. |
| Receipt nativo de fichero productivo publicado | Revalidar mismo Job/snapshot y escribir solo faltantes. |
| AUDITED/EXECUTED/VERIFIED con intención propia | Revalidación nativa y continuación del mismo journal. |
| Commit catálogo con journal VERIFIED | Insert-or-verify de los mismos hechos, después COMPLETED. |
| Job COMPLETED y cola Running | Verificar archivo, producción y catálogo; cerrar cola sin repetir efectos productivos. |
| COMPLETED con fichero/entrada de catálogo ausente o alterada | Evento CompletedInconsistent y NeedsReview, journal conservado COMPLETED; sin rebuild. |

CompletedInconsistent es una API interna restringida de diagnóstico, no una transición genérica que reabra ejecución. La cola no sustituye a la evidencia del ledger. Cambios en roots, índice compartido o bases compartidas bloquean el universo; daños individuales se aíslan. Una pausa/revocación en una frontera deja los efectos ya publicados y residuos intactos, con revisión explícita.

Primera tanda P4.3: 49/49 aprobadas en Windows (3 min 34 s, `phase4-p43.trx`): 30 cortes recuperables, 7 ambiguos conservados, 3 corrupciones de COMPLETED, rollback del primer intent y 8 casos P4.2. Los cuatro smoke WPF existentes pasaron 4/4 (13 s) y el gate Assert-WpfTestResults confirmó las cuatro identidades. Revalidación final tras reforzar boundaries y presupuesto de conversión en curso.

## P4.4 — Integridad, regresión y CI

Todos los workflows nuevos usan PNG/ZIP/manifest, universos y presupuestos sintéticos, temporales privados y proveedor simulado. Se comparan inventarios SHA de archivos/productivos, Inbox y universos ajenos; en recuperación se comparan también timestamps de cada fichero ya publicado para detectar reescrituras. Ninguna prueba nueva lee settings productivos ni llama al proveedor real.

ExecutionIntegrityTests añade alteraciones de receipt/control, originales/ZIP preservado, candidato/ZIP, perfil, política y reserva; destinos ajenos con bytes idénticos; pausa/revocación antes del rename; corrupción de queue/preparation/execution/history/sidecar; mismo AssetId en dos universos; raíces reparse y presupuesto de memoria insuficiente. Se conserva la cobertura anterior de colisiones AssetKey, contratos manuales, ownership, archivo, catálogo, normalización, autorización y ZIP hostil.

Se corrigieron dos problemas detectados por regresión: las conexiones SQLite de preparación/ejecución se disponen también si falla la configuración inicial de una DB corrupta; las firmas internas usadas por los ensayos existentes de publicación se mantienen mediante wrappers que delegan en métodos controlados. Las pruebas existentes conservan assertions de seguridad. La expectativa del código de error al intentar Enabled sin operaciones productivas pasa de Disabled a PolicyMismatch, conforme al nuevo gate.

La verificación repetida de un COMPLETED correcto no añade revisiones idénticas al ledger ni reescribe archivo, producción o catálogo. El histórico no se consume por verificaciones sin cambios. Las pruebas de reparse usan el helper de junctions existente y eliminan exclusivamente el enlace antes del teardown del fixture.

CI conserva triggers, permisos y los cuatro smoke WPF obligatorios. Linux sigue ejecutando la suite completa, incluidos los cuatro AdmissionVolume; se añade TRX y artifact durante 14 días para que fallos/omisiones sean auditables. Windows/WPF conserva su gate estricto y artifact existentes. No se sustituye una ejecución real por compilación.

Entorno local: Windows x64, SDK 10.0.400, runtime .NET 8.0.8 y Windows Desktop 8.0.8. Release de la solución completa: 0 errores, 0 advertencias (3,26 s en la última compilación registrada). La tanda inicial final de 75 casos pasó 73 y falló 2 por handles SQLite retenidos; corregidos, ExecutionIntegrityTests pasó sus 19 casos entonces existentes en 45 s. Se añadieron posteriormente cinco casos de roots/índice/catálogo/memoria. Las ejecuciones focalizadas se solapan: no se suman.

Los cinco casos existentes de symlinks de AutomationQueueTests/InboxAdmissionTests fallan en esta sesión por falta de privilegio de creación. Se reprodujeron los mismos cinco fallos en una exportación aislada de `dbdadc4`, anterior a la implementación, conservada solo como artifact ignorado. No se desactivan ni se cambian esas pruebas, ni se modifican privilegios del equipo.

Regresión amplia local inicial: 3.679 aprobadas, 8 fallidas, 0 omitidas, 3.687 total, 5 min 51 s (`phase4-windows-regression-final.trx`). Cinco fallos son los symlinks preexistentes reproducidos en la base; los otros tres fueron dos teardowns de junctions sintéticas y la expectativa histórica Disabled/PolicyMismatch. Corregidos estos últimos, la tanda de correcciones y compatibilidad anterior pasa **170/170**, 0 fallidas/omitidas, en 59 s (`phase4-corrections-final.trx`). Incluye las 24 pruebas nuevas de integridad y dos recorridos de completado repetido. Build Release posterior: 0 errores/advertencias, 2,56 s. Resultados consolidados finales y CI pendientes de publicación; no se declaran aprobados por anticipado.

Comandos de verificación:

```powershell
dotnet build NAP.sln -c Release --no-restore
dotnet test NAP.sln -c Release --no-build --no-restore --filter 'Category!=AdmissionVolume' --logger 'trx;LogFileName=phase4-windows-regression-final.trx'
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ExecutionIntegrityTests' --logger 'trx;LogFileName=phase4-integrity-final.trx'
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~VisualExplorerWindowsSmokeTests' --results-directory artifacts/phase4-wpf --logger 'trx;LogFileName=windows-wpf.trx'
./.github/scripts/Assert-WpfTestResults.ps1 -ResultsPath artifacts/phase4-wpf/windows-wpf.trx
git diff --check
```

## Límites conservados

Sin assets reales, claves IA reales, watcher, scheduler, UI, Git productivo, retención, adopción de perfiles ni merge a main. StateRoot conserva originales y lineage; no constituye un backup externo certificado. La configuración real permanece apagada.

Los cortes ensayados son excepciones deterministas con reapertura de stores/owner; no certifican pérdida eléctrica ni muerte del proceso en cualquier instrucción. Un hot journal o temporal ambiguo requiere revisión, sin limpieza automática. No hay garantía exactly-once remota del proveedor. Los checksums locales no son firmas contra un administrador que reescriba toda la evidencia. Las estimaciones de memoria son conservadoras, no una certificación del working set del proceso. La revalidación completa antes de cada efecto prioriza integridad; no se ha medido throughput de lotes productivos y el host pertenece a Fase 5. Los bloqueos compartidos se expresan mediante AutomationException tipada; no se crea un estado de host/UI nuevo. Backup/retención y parámetros reales por perfil siguen pendientes de decisión operativa, sin activar valores de fixtures.
