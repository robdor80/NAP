# Configuración local de universos v1

Ubicación Windows: `%LOCALAPPDATA%\NAP\settings.json`, resuelta mediante
Environment.SpecialFolder.LocalApplicationData; **fuera del checkout Git**.
`LocalUniverseSettingsStore(path)` permite ubicación absoluta temporal en tests.
El formato cerrado contiene solo `schema_version: 1` y `universes`, cada entrada
con universe_id, workspace_root, production_root, archive_root. No claves API,
secretos, universo activo global, settings IA ni rutas en SQLite.

Constructor UniverseStorageConfig valida rutas absolutas sin normalización
inventada. `ValidateStructure(configurations)` comprueba todos los IDs y el
aislamiento de todas las raíces, tanto entre universos como dentro de cada uno,
sin requerir dispositivos conectados. Load aplica esta validación global y el
formato cerrado, incluso cuando hay raíces temporalmente ausentes.

`ValidateAvailable(storage)` valida físicamente solo esa configuración:
WorkspaceRoot local existente y ProductionStorageRootValidator/ArchiveRootValidator
existentes, conservando aislamiento/reparse/containment de sus contratos.
La selección combina estructura global y disponibilidad del universo elegido.
A disponible funciona con B desconectado; seleccionar B da STOP/Ajustes. Al
reconectar B basta reintentar o seleccionarlo nuevamente, sin reconfigurar.
No crea ninguna raíz y no se crea un Context utilizable antes de validar el elegido.

La UI guarda con `Save(configurations, configuredUniverse)`: estructura global,
lectura de la configuración previa bajo mutex y validación física del universo
configurado, aunque sus rutas no cambien. También valida físicamente cada entrada
nueva o modificada; solo los otros universos previamente guardados y **sin cambios**
pueden permanecer desconectados. Guardar A no exige disponibilidad de B sin cambios.
No admite introducir raíces inválidas de B ni overlap, duplicados, rutas relativas
o configuración corrupta. La variante `Save(configurations)` comprueba disponibilidad
de todas las entradas nuevas/modificadas; los callers que guardan un universo
concreto deben pasar su ID para comprobarlo incluso sin cambios. La API anterior
`Validate(configurations)` conserva la comprobación física de todos los universos.

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
