# NAP03 — Cadena de assets Nimroel / Star Trek (13/11/2026)

## Inventario antes de cambiar

- Existían Inbox watcher/reconciliación, separación por `UniverseId + AssetId`, colas SQLite por universo, admisión y CRC ZIP, manifest v2, reglas de perfiles v4, preparación y normalización, conversión WebP, archivo de maestros PNG y publicación de derivados, transacciones catálogo, ledger de reintentos, idempotencia por ZIP y protección de duplicados.
- Existían controles Host/WPF de activar, pausar, reanudar, detener y cancelar/reactivar items; fichas de revisión y contadores globales, pero no un disparador inmediato de nueva captura desde el escritorio.
- `NAP.AutomationHost` solo seleccionaba el auditor de pruebas explícito o ninguno. La app local conocía Gemini real, pero el Host real no lo componía; sin auditor no había PASS automático.
- Los legados de NAP 1.0 ya usan `LegacyPortraits.db` y `LegacyMasterEvidence.db` separados del `AssetCatalog.db` nativo, con comprobación hash y sin inventar publicaciones. Se conservan sus reglas y pruebas; **no se migran ni reescriben originales históricos**.

## Cambios de NAP03

1. El Host definitivo conecta el cliente `GeminiOperationAuditClient` existente mediante `NAP_GEMINI_API_KEY` y `NAP_GEMINI_MODEL` del proceso, sin escribir claves en configuración ni GitHub. La selección rechaza credenciales parciales o mezcla sintético/real. Si no hay configuración, no emite PASS ni produce WebP/catálogo por omisión. La autorización persistida de la política, la comprobación de grant y sus presupuestos siguen siendo preceptivos.
2. `ScanPending` ofrece la acción WPF «Procesar pendientes» para el universo activo, mediante IPC autenticado. Solo solicita una reconciliación; el owner existente hace todas las verificaciones, sin escritor UI ni tareas paralelas. El control requiere universo activado y concesión; la repetición no duplica items.
3. El selector de lotes informa cantidades resueltas y pendientes desde un snapshot SQLite de la cola, sin nueva persistencia ni doble conteo de observaciones de un mismo paquete.
4. Se añaden ensayos focalizados de ambos universos y mismo AssetId, archivo PNG y WebP publicados, catálogo SQLite, invariancia del ZIP original, reescaneo idempotente, ausencia de auditor, y selección Gemini sin red. Se ejecuta un workflow exclusivo Windows x64; **NAP CI general queda apagada**.

## Límites preservados

- Host/acciones automáticas solo en TEMP sintético y con autorización explícita: producción real/CUTOVER (#47), instalación MSI (NAP06–NAP08), reparación #49 y aceptación visual final (NAP04) permanecen separados.
- No se habilitan servicios privilegiados, reglas ACL, reescritura de catálogos NAP 1.0, sincronización TeraBox ni Git productivo.
- Los tests del proveedor Gemini son mocks: no acreditan una invocación comercial con una clave privada. La integración real requiere configuración expresa y autorización preproductiva.

## Evidencia

Primera ejecución Windows x64: [Actions #43473121749](https://github.com/robdor80/NAP/actions/runs/43473121749) compila Release (0 errores; dos CA2014 históricos y aviso xUnit2031 nuevo), 23 PASS y 2 FAIL. El nuevo caso mixto leyó la cola SQLite directamente mientras el Host la poseía; se corrige el test para consultar mediante el owner remoto. El otro fallo es `AutomationDesktopTests.RemoteGitCommitAndPushUseNativeServerPlanAndExplicitLocalBareFixture` (plan Git manual, fuera de NAP03) con `InvalidDataException`; no se marca reparado ni se modifica Git. Se aíslan las otras tres pruebas del mismo grupo y se incluyen las regresiones de IPC, auditor Gemini y UI en la matriz NAP03.

Validación revisada pendiente del próximo run; NAP CI general permanece desactivada.
