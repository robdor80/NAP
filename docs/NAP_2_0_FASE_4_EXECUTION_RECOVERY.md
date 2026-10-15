# NAP 2.0 — Fase 4: ejecución y reconciliación idempotente

Rama: `codex/nap-2-fase-4-execution-recovery`. Inicio de implementación: 2026-10-15.

## P4.1 — Autorización

`Enabled` es una transición explícita CAS de la política vigente, con autorización exacta de universo, versión, hash, actor y raíces. Exige `Admit|Audit|Archive|Produce|Register`; `Normalize` continúa separado. Una definición, autorización o PASS aislados no activan nada. `Disabled` sigue siendo el default; `PreparationOnly` rechaza cualquier permiso productivo. Pausa suspende capacidades; revocación es terminal. Una versión nueva no hereda autoridad.

Enabled también permite preparar/auditar bajo los términos preproductivos existentes de esa misma política. No se amplía PreparationOnly. No se cambia DDL de cola, catálogo ni journal v1; lectores antiguos que rechazan Enabled siguen fallando de forma cerrada. No se habilitan universos reales ni se compone un host.

La evidencia de ejecución será un sidecar independiente del journal y de la preparación inmutable. El adaptador consumirá el snapshot y receipt PASS físicos, mantendrá la reserva y la terna item/intento/Job y usará los motores existentes con guardas en cada efecto. Los finales sin ownership posterior a publicación permanecen ambiguos, aunque sus bytes sean iguales.

## P4.2 — Adaptador explícito

`DurableExecutionService.Execute(owner, itemId)` usa un UniverseContext fijo. `VerifyExecution` relee admisión, bundles original/candidato/auditoría, ZIP/CRC/inventarios, semántica, perfil completo, motor, geometría/píxeles, lineage, reserva, solicitud, grant y PASS. No ejecuta Prepare, no reaudita y no asigna Job nuevo. La producción conserva el receipt nativo del mismo Job. El catálogo se compara con hechos físicos verificados, sin añadir JobId al schema existente: el binding del ledger y el receipt productivo establecen esa relación.

`ExecutionLedger.db` es independiente y liga item/intento/Job, roots/policy/snapshot/PASS/grant/autorización. SQLite DELETE/FULL, schema y metadata exactos, quick_check/FK, CAS e historial con hash previo; sidecars desconocidos bloquean el ámbito. Guarda intención antes de efectos y receipt después de verificar publicación. Hooks internos opcionales incorporan esas fronteras a los motores existentes y mantienen sus APIs manuales. No se sostiene transacción/lock de DB al llamar al coordinador.

Antes de cada efecto se revalida autoridad vigente, raíces y handoff físico. El archivo exige receipt posterior a publicación para cada fichero existente; IndexExisting ajeno se rechaza. Las entradas temporales ambiguas se conservan y aparcan. La cola registra Execution/Verification/Catalog y solo cierra después del journal y catálogo verificados.

Release Windows: 0 errores y 0 advertencias (build incremental). P4.2: 8/8 pruebas aprobadas, con y sin derivación autorizada, repetición sin cambios de bytes/catálogo, gates Disabled/PreparationOnly/Paused/Revoked y WARNING/FAIL. Un defecto inicial al comparar el identificador de lineage se corrigió usando el NormalizationId existente y se repitió la tanda completa.

## Validación en curso

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

## Límites conservados

Sin assets reales, claves IA reales, watcher, scheduler, UI, Git productivo, retención, adopción de perfiles ni merge a main. StateRoot conserva originales y lineage; no constituye un backup externo certificado. La configuración real permanece apagada.
