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

## Límites conservados

Sin assets reales, claves IA reales, watcher, scheduler, UI, Git productivo, retención, adopción de perfiles ni merge a main. StateRoot conserva originales y lineage; no constituye un backup externo certificado. La configuración real permanece apagada.
