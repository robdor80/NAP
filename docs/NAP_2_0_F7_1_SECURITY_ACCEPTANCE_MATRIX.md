# NAP 2.0 · F7.1 — Matriz de seguridad y aceptación focalizada
**Fecha:** 26-10-2026. **Estado:** escenarios prescritos, NO ejecutados en F7.1.
**Restricción inviolable:** no ejecutar `NAP CI`, suites generales, carga, estrés, 100/1000 ZIP ni subdivisiones ficticias para eludir el límite. Máximo 20 ZIP sintéticos por ejecución/escenario. No tocar raíces reales, secretos, MSI productivo ni repositorios reales.

## Precondiciones obligatorias
1. Directorios temporales seguros y locales con perfiles/universos ficticios, Git init y remotos **bare locales** ficticios; no `robdor80/NAP` ni repos de Nimroel. La facilidad Git.AllowLocalTestTransport solo en fixture de test.
2. Política nueva = Disabled y sin secretos. No asumir que contraseña o SSH preexistente son consentimiento.
3. Al ejecutar una prueba con Git, guardar identidad/branch/upstream/remoto/fingerprint/autorización exactos y recopilar únicamente logs saneados.
4. Recibir/verificar por cada Job sintético outputs de NAP con hashes SHA-256 + ownership/receipt + catálogo PASS; no usar `_verified` de UI como fuente.
5. Para cada caso comprobar que los estados de Job y Queue siguen Completed al fallar Git; no volver a archivar/producir, no alterar otras rutas, nunca resetear HEAD.

## Matriz focalizada para F7.2–F7.3
| ID | Escenario de fixture | ZIP máximos en ese escenario | Aceptación |
| --- | --- | ---: | --- |
| T01 | Instalación/config ausente, migración v1 y reinicio | 1 | Disabled; cero commit/push |
| T02 | Política corrupta/desconocida o raíz distinta | 1 | STOP sin adoptar defaults permisivos ni sobrescribir config |
| T03 | Manual: confirmación de commit y confirmación separada de push | 1 | No mutación sin consentimientos respectivos |
| T04 | AutoCommitBatch: Job completado, outputs propios y repo limpio | 1 | Un commit local verificado; no push |
| T05 | AutoCommitAndPushBatch con remoto bare ficticio | 1 | Commit + push y comprobación SHA real |
| T06 | 5 Jobs completados del mismo repo, un lote determinista | 5 | Selección exacta, sin duplicados ni commit parcial |
| T07 | Dirty path ajeno en working tree, index staged previo, output alterado | 1 | Blocked, no staging ni apropiación |
| T08 | Remoto avanzado o diferente BaseHead y upstream/remoto/branch cambiado | 1 | Blocked; sin pull/rebase/force ni modificación de config |
| T09 | Revocación tras Pending y antes de commit; tras Committed y antes de push | 1 | No nueva mutación; receipt/HEAD intactos |
| T10 | Caída tras Completed antes del registro Outbox | 1 | Reconciliación genera una sola intención, no repite producción |
| T11 | Caída tras Prepared; tras mover HEAD antes de guardar Committed | 1 | Usa Recover/RetryCommit nativo solo donde proceda, un único commit |
| T12 | Push exitoso antes de escribir estado Pushed (crash) | 1 | Reintento AlreadyPushed sin segundo push |
| T13 | Red/credenciales fallan en push | 1 | Mantiene Committed, Job Completed; reintento condicionado |
| T14 | Receipt huérfano/corrupto, temp huérfana, ownership ambiguo | 1 | Blocked, preserva evidencia, revisión humana |
| T15 | Dos intents simultáneos mismo repositorio, dos procesos | 2 | Una barrera exclusiva, sin carreras/duplicaciones |
| T16 | Cambios de Git identity, fingerprint, filtros, LFS o symlink | 1 | STOP antes de escritura |
| T17 | AutoCommitBatch con segundo lote y receipt previo Committed sin push | 2 | Segundo espera o Blocked, sin saltarse RequireNoPending |
| T18 | Manual WPF y host automático concurrentes | 1 | Coordinación/STOP, sin comprometer cambios ajenos |
| T19 | Batch con 10 ZIP ficticios, mismo repo y condición nominal | 10 | Aceptación limitada, sin volumen |
| T20 | Batch con máximo 20 ZIP ficticios, solo si aporta evidencia concreta y autorizado | 20 | No generalizar a stress/performance |

**No ejecutar T01–T20 en cascada**: son un catálogo de casos disponibles, no una batería masiva autorizada. Codex seleccionará el conjunto mínimo de casos focalizados pertinente al cambio técnico, con máximo 20 ZIP por ejecución, sin multiplicar ejecuciones para simular carga. Los escenarios de 10/20 no son preceptivos. Respetar prohibición de pruebas generales de cientos/miles de tests aunque cada fixture use 1 ZIP.

## Evidencia requerida cuando se autoricen pruebas
- Identidad del fixture y condiciones de aislamiento; SHA del código probado.
- Comando y filtro exacto de test (prohibido `dotnet test` sin `--filter` focalizado), número real de casos y ZIP.
- Antes/después: Job/Queue, Outbox, receipt Git, HEAD/parent/árbol, tip remoto, índice/working tree ajeno.
- Resultado factual y limitaciones, sin atribuir CI general verde.

## Criterios fail-closed
Sin evidencia suficientemente reconstruible, política vigente o sincronía validada: `Blocked`; un error Git nunca transiciona Job/Queue de Completed ni borra originals. Ante fallos irreconciliables de estado compartido, STOP del subsistema Git y revisión explícita; no borrar/recrear recibos para continuar.
