# NAP 2.0 — F7.1: contrato de Git configurable
Fecha: 2026-10-26. Estado: especificación para implementación, no funcionalidad instalada.

## Invariantes
- Job COMPLETED se consolida antes de generar una intención Git durable. Ningún error Git revierte COMPLETED.
- Git está Disabled por defecto, también tras migración, configuración ausente o inválida. No inferir autorización desde credenciales Git existentes.
- Políticas por UniverseId y repositorio canónico; nunca existe repositorio global implícito.
- Modos: Disabled (ninguna operación), Manual (acciones explícitas), AutoCommitBatch (commit local autorizado), AutoCommitAndPushBatch (commit y push autorizados por separado).
- La autorización de commit y la de push son independientes, explícitas, revocables y versionadas. Deben vincular universo, ruta canónica del repositorio, identidad Git, rama local, remoto, URL remota, upstream, fingerprint de destino, prefijos de rutas permitidas, y política de lote.
- Fingerprint de remoto: identificador estable derivado de la URL normalizada y la identidad esperada; nunca sustituye la verificación del destino actual. Rechazar redirecciones o cambios silenciosos de URL.
- No guardar tokens, claves, contraseñas ni licencias en configuración, receipts o logs.

## Outbox y barreras
- Persistir por repositorio y JobId un intent idempotente con hashes SHA-256 de outputs, rutas relativas, ownership probado, receipt de publicación, autorización versionada y snapshot del HEAD/base.
- Estados internos Pending, Committed, Pushed, Blocked. UI puede mostrar Disabled cuando no existe autorización. Guardar intent, commit SHA, push confirmado y causa del bloqueo mediante escritura atómica y recuperación ante caída.
- Bloqueo exclusivo por repositorio entre procesos; recuperar leases caducados solo con comprobación de propietario vivo y estado durable. Un único escritor de índices/refs por repositorio.
- Cada ruta debe ser relativa al worktree canónico y estar bajo prefijos permitidos; rechazar rutas absolutas, .., symlinks/reparse points, submódulos y rutas fuera de alcance.
- Comparar hashes actuales con receipts y ownership; cualquier diferencia o cambio ajeno bloquea esa ruta/lote, sin sobrescribir ni apropiarse de cambios.
- Stage únicamente mediante lista explícita de rutas validadas, idealmente índice Git temporal aislado, y comprobar árbol/diff resultante antes de crear commit. Prohibido git add ., -A, force, reset, clean, pull/rebase automáticos y alteración de ficheros ajenos.
- No alterar el índice de usuario ni incluir cambios preexistentes; si no puede garantizarse, bloquear y pedir intervención.
- Política de lotes determinista y acotada; no crear commits vacíos ni duplicados. Un lote bloqueado no arrastra otros repositorios.
- Revocar autorización impide nuevas operaciones incluso sobre intents antiguos; reautorizar requiere validación completa y versión nueva.

## Push
- Confirmar identidad, rama, upstream, remoto y fingerprint inmediatamente antes de push.
- Contrato conservador RemoteTip == HEAD: si el tip remoto no coincide con HEAD local, no ampliar automáticamente el permiso; bloquear o requerir nueva autorización expresa según el estado. No implementar sincronización automática ni reconciliación remota.
- Commit local y push son fases independientes: un fallo de red o remoto avanzado conserva Committed y deja reintento controlado. Nunca force push.
- El caso de commit local nuevo requiere contrato explícito para identificar el HEAD anterior y el estado remoto esperado antes de autorizar un fast-forward; no reinterpretar RemoteTip == HEAD para permitir otros casos sin aprobación.
- Tras interrupción, inspeccionar el commit/refs y remoto antes de repetir; nunca duplicar commit ni declarar Pushed sin confirmación.

## Aceptación
Solo repositorios temporales ficticios y ZIP sintéticos: escenarios de 1, 5, 10 o 20 ZIP como máximo, sin baterías masivas. No ejecutar NAP CI, CUTOVER, tests generales ni publicar en remotos reales. #47 y #49 siguen abiertos.
