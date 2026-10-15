# NAP 2.0 — Fase 4: ejecución y reconciliación idempotente

Rama: `codex/nap-2-fase-4-execution-recovery`. Inicio de implementación: 2026-10-15.

## P4.1 — Autorización

`Enabled` es una transición explícita CAS de la política vigente, con autorización exacta de universo, versión, hash, actor y raíces. Exige `Admit|Audit|Archive|Produce|Register`; `Normalize` continúa separado. Una definición, autorización o PASS aislados no activan nada. `Disabled` sigue siendo el default; `PreparationOnly` rechaza cualquier permiso productivo. Pausa suspende capacidades; revocación es terminal. Una versión nueva no hereda autoridad.

Enabled también permite preparar/auditar bajo los términos preproductivos existentes de esa misma política. No se amplía PreparationOnly. No se cambia DDL de cola, catálogo ni journal v1; lectores antiguos que rechazan Enabled siguen fallando de forma cerrada. No se habilitan universos reales ni se compone un host.

La evidencia de ejecución será un sidecar independiente del journal y de la preparación inmutable. El adaptador consumirá el snapshot y receipt PASS físicos, mantendrá la reserva y la terna item/intento/Job y usará los motores existentes con guardas en cada efecto. Los finales sin ownership posterior a publicación permanecen ambiguos, aunque sus bytes sean iguales.

## Validación en curso

Primera tanda P4.1 Windows: 71 aprobadas, 4 fallidas al intentar crear symlinks sin privilegio del sistema (`phase4-p41.trx`). Son pruebas preexistentes de AutomationQueueTests; no se omiten ni se relajan. Resultados definitivos de fase pendientes.

## Límites conservados

Sin assets reales, claves IA reales, watcher, scheduler, UI, Git productivo, retención, adopción de perfiles ni merge a main. StateRoot conserva originales y lineage; no constituye un backup externo certificado. La configuración real permanece apagada.
