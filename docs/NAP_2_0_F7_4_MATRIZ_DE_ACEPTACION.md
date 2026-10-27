# NAP 2.0 — F7.4 · Matriz de aceptación y auditoría focalizada

**Fecha:** 27-10-2026. **Estado:** escenarios por implementar y verificar, NO ejecutados.
**Alcance:** interfaz WPF, proyección, autorizaciones, Outbox, IPC/owner y compatibilidad F7.1–F7.3.
**Base:** main c4a053226f6f27c0cf109ef89cf1a92be1117f36. Rama chatgpt/nap-2-f7-4-git-ui.

## Reglas de ejecución

Las pruebas deben ejecutarse por filtros exactos y ser proporcionales; no lanzar suite general, stress, CI ni workflows. Fakes para ViewModel/adapters; para Git utilizar exclusivamente repos git init y remotos bare temporales locales del fixture, con identities .invalid. Un máximo de 1–2 ZIP por caso cuando un caso los precise; no incrementar artificialmente casos/ZIP. Todas las pruebas negativas deben verificar ausencia de escritura/autorización y preservación de evidencias. El SHA de código probado se fija antes de entregar evidencias.

| ID | Caso focalizado | Criterio verificable |
|---|---|---|
| U01 | Instalación/configuración ausente | Disabled visible; consulta read-only; cero grant/file/write/push |
| U02 | HMAC/ancla/archivo parcial corrupto | STOP explícito, no regenerar/default permisivo |
| U03 | Selector de cuatro modos | El cambio de selección no escribe política ni dispara operación Git |
| U04 | Permisos independientes | AllowPush sin AllowCommit rechazado; AutoCommitBatch no habilita push; Manual requiere dos confirmaciones |
| U05 | Propuesta y revisión | Captura identidad/branch/upstream/fingerprints y expectedRevision; cancelación no escribe |
| U06 | Cambios concurrentes | CAS antiguo, universo/cambios de binding/fingerprint caducan revisión y no aceptan un grant obsoleto |
| U07 | Prefijos y límite | Validación de prefijos, orden, duplicados, .git, rutas ajenas; MaxJobsPerBatch 1..20 |
| U08 | Revocación | Nueva revisión, ID nuevo, permisos false y Revoked/Disabled; Pending no reactivados; sin requerir remoto disponible |
| U09 | Autorización posterior a revocación | Antigua operación Committed no recibe push automáticamente con la revisión nueva |
| U10 | Lectura Outbox | Contadores y filas Pending/Committed/Pushed/Blocked con JobId/BatchId/OperationId/diagnóstico asociados; lectura sin writes |
| U11 | Outbox y receipt discordantes | Bloqueo; no inventar estado Pushed, CommitSha ni «adoptar» un receipt |
| U12 | Confirmaciones manuales | Commit confirmado separado de push; cancelación de cada uno no muta; estados de selección controlan disponibilidad |
| U13 | Retry/Reconcile | RetryBlocked y RetryPublication sólo con owner real, autorización original y budget; conserva IDs, Job/Queue Completed y RequireNoPending |
| U14 | Falta de owner, host activo o no disponible | Botón deshabilitado/bloqueo visible, sin crear owner, bypass ni fallback local de escritura |
| U15 | Cambio de universo/lectura obsoleta | Invalida selección/propuesta anterior y descarta respuesta tardía; cero operación en el universo previo |
| U16 | IPC mínimo | Autenticación/deduplicación/validación de universe/scope/ID/revision; no aceptar paths arbitrarios ni secretos en payloads/logs |
| U17 | Recuperación F73-A1/A2 | No cuarto push, no consultas remotas fuera del presupuesto y reconocimiento local durable sólo donde el Core lo permite |
| U18 | RobStyle/WPF | Bindings, comandos y DisabledReason correctos; sin segunda pantalla; revisión visual real Windows 11 pendiente de validación separada |

## Demostración de ausencia de mutaciones

En tests negativos inspeccionar explícitamente política/autorización/revisión anteriores, archivos y hashes relevantes, Outbox/receipts, HEAD/árbol/index y tip remoto bare cuando corresponda. Diferenciar lectura read-only de inspección remota solicitada. Un test que sólo espere InvalidDataException sin verificar el estado posterior no cubre el criterio.

## Resultado de la fase

- **Antes de Codex:** 0 tests F7.4 ejecutados; documentación no acredita implementación ni certificación.
- **Después de Codex:** registrar tests ejecutados, porcentajes y casos omitidos sin afirmar cobertura general; auditoría independiente de ChatGPT obligatoria.
- **Reservas:** Windows 11 WPF real, escalado, NTFS/ACL, MSI, Release y CUTOVER; ImageSharp 3.2.0 NU1902/NU1903 y licencia; issues #47/#49.
- **Prohibido:** NAP CI y demás workflows, tests Git contra GitHub/repositorios reales, producción, credenciales/licencias/ACL MSI, suites generales, stress y merge sin permiso.
