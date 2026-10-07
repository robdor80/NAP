# Configuración local de universos v1

Ubicación Windows: `%LOCALAPPDATA%\NAP\settings.json`, resuelta mediante
Environment.SpecialFolder.LocalApplicationData; **fuera del checkout Git**.
`LocalUniverseSettingsStore(path)` permite ubicación absoluta temporal en tests.
El formato cerrado contiene solo `schema_version: 1` y `universes`, cada entrada
con universe_id, workspace_root, production_root, archive_root. No claves API,
secretos, universo activo global, settings IA ni rutas en SQLite.

Constructor UniverseStorageConfig valida rutas absolutas sin normalización
inventada. UniverseStorageIsolationValidator rechaza IDs duplicados y overlap
entre cualquier root de distintos universos. Guardado valida WorkspaceRoot local
existente y ProductionStorageRootValidator/ArchiveRootValidator existentes, con
aislamiento/reparse/containment de sus contratos. No crea ninguna raíz.
Un archivo ya guardado puede cargar roots temporalmente ausentes para editarlas
explícitamente; no se crea un Context utilizable hasta validar su disponibilidad.

Guardar publica UTF-8 determinista ordenado por UniverseId: mutex, temp hermano
CreateNew, Flush(true), Move tras cierre. Solo crea la carpeta de settings. Rechaza
settings en cualquier root de universo o dentro de Git, symlinks/reparse y archivos
no regulares. Archivo corrupto/versión desconocida/campos secretos o duplicados
son STOP: nunca los sobreescribe como si fueran configuración nueva. Un fallo de
validación conserva bytes previos; cleanup se limita al temp controlado propio.
El directorio de configuración pertenece al usuario del equipo (permisos del
perfil del sistema); no se añaden logs con rutas absolutas.

Nimroel se registra con su profile.json real distribuido en App. Multiuniverso
está soportado en store/composición/ViewModels y probado con perfiles genéricos;
no se distribuyen perfiles futuros ficticios. La selección activa es estado de UI,
sin singleton Core y sin persistir un root implícito. Al cambiar, se pasa otro
UniverseContext a todas las consultas, caché y servicios.
