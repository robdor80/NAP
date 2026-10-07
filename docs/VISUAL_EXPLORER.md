# Fase 11 — Explorador visual (11.0–11.5)

Aplicación real WPF + MVVM, .NET 8, para Windows. El explorador observa el catálogo
existente; únicamente escribe configuración local y thumbnails regenerables.
No importa, reconstruye, repara, ejecuta jobs, consulta IA ni modifica estados.

## Ejecutar

```powershell
dotnet run --project src/NAP.App --configuration Release
```

Seleccionar el universo y configurar tres carpetas **existentes** en Ajustes.
El catálogo debe proceder del pipeline/import/rebuild explícito de Fase 10.
Una DB ausente se muestra como error: el explorador no llama a `Initialize`.
No hay detección de TeraBox, búsqueda de repositorios ni rutas inventadas.
Nimroel es el único perfil real distribuido; otros perfiles se registrarán en el
composition root, sin añadir conceptos de lore a la lógica genérica.

## Dependencias y CI

```text
NAP.App (net8.0-windows, WPF) → NAP.Presentation (net8.0) → NAP.Core (net8.0)
NAP.Cli → NAP.Core; NAP.AI → NAP.Core; NAP.Tests → Core + AI + Presentation
```

Core no referencia WPF. Presentation contiene servicios/adapters y ViewModels
portables; `INotifyPropertyChanged`, comandos y cancelación sin framework MVVM.
App contiene vistas, recursos, panel virtualizado y bitmap adapter visual.
No se añade ningún paquete NuGet. ImageSharp y Microsoft.Data.Sqlite mantienen
las versiones de Fase 10.

`EnableWindowsTargeting=true` en App descarga los reference packs Windows para
compilar en Ubuntu ([Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100)).
WPF se **ejecuta en Windows**. La CI `ubuntu-latest` y sus tres comandos no cambian.
NAP.Tests sigue siendo net8.0, sin WindowsDesktop framework reference; las dos
pruebas de render subprocess WPF se omiten expresamente fuera de Windows.
Todos los tests portables del explorador se ejecutan también en Linux.

## Bootstrap y selección

[Settings](LOCAL_UNIVERSE_SETTINGS.md) v1 guarda roots por UniverseId fuera de Git.
No persiste un CurrentUniverse global: `ExplorerViewModel` crea y pasa
`UniverseContext(profile, storage)` explícito al seleccionar un perfil configurado.
El cambio cancela la sesión UI anterior, descarta páginas, detalle, preview,
filtros, facets y estadísticas. Los resultados tardíos no pueden repoblarla.
Antes de activar comprueba estructura/aislamiento de todas las configuraciones y
disponibilidad física **solo del universo seleccionado**. Otro universo desconectado
no bloquea grid, filtros ni estadísticas del disponible. Seleccionar el ausente
da STOP/Ajustes sin crear raíces; al reconectarlo se puede reintentar sin cambiar
settings. Guardar valida físicamente el universo configurado y todas las entradas
nuevas/modificadas; conserva otros universos sin cambios aunque estén desconectados.

## Shell RobStyle

`Themes/RobStyle.xaml` centraliza petróleo/cerúleo/blanco hielo, tipografía,
espaciado, bordes, radios, profundidad, tarjetas, botones, navegación y colores
de estado. Ventana maximizada, diseño de filtros/grid/ficha para pantallas grandes.
Funcionan Catálogo, Estadísticas y Ajustes. Inicio, Pipeline, Objetivos, Backups,
Historial y RoDo están deshabilitados y señalados como futuros.
No es la implementación profesional completa de Fase 14.

Loading/Empty/Error/Ready son estados explícitos. Los errores detienen la lectura
y muestran un mensaje sin stack trace/rutas privadas, con código técnico plegable
o tooltip. Reintentar es explícito mediante Aplicar/actualizar. No se reparan roots,
catálogo ni assets. Las rutas configuradas se muestran únicamente en Ajustes;
no se envían a IA ni se añaden al catálogo/manifests/loggers del pipeline.

## Grid y lectura de catálogo

`CatalogExplorerReader` añade Page/Detail/Statistics/Planning de **solo lectura**,
con el mismo predicado AND parametrizado que Fase 10, sobre schema v1 intacto.
Page proyecta identidad, tipo/perfil, ruta relativa y fingerprint del WebP,
ordenados por AssetId BINARY; límite máximo 240, sin SELECT de BLOBs/documentos.
No escanea ProductionRoot ni ArchiveRoot. Statistics utiliza GROUP BY en Core;
Planning reutiliza objetivos/campañas y modelos Coverage/Diversity existentes.

Cada lectura adquiere el mismo mutex de catálogo, abre ReadOnly con pooling=false
y comprueba paths/reparse, metadata/universo, user_version, schema exacto y DELETE
journaling. `CatalogSchema.ValidateContract` contiene estas comprobaciones de
identidad/schema; `CatalogSchema.Validate` sigue añadiendo **los mismos**
integrity_check y foreign_key_check. Todos sus consumidores de Fase 10 mantienen
validación fuerte, y su validación lógica anterior queda intacta; schema v1 no cambia.

El adapter conserva una única sesión de `CatalogExplorerReader`, sin conexión ni
handle abiertos entre consultas. La primera lectura al abrir/seleccionar un
universo hace validación fuerte. También se repite tras cambio físico, invalidación
explícita (`InvalidateValidation` / `BeginSessionAsync`), cualquier error de lectura
o cinco minutos desde la última validación fuerte, con reloj monotónico. La
paginación normal de esa sesión no repite ambas verificaciones globales por página.

Antes y después de cada operación se observa el archivo de forma acotada: tamaño,
fechas y **solo los 100 bytes de cabecera SQLite**, más identidad física y tiempo
de cambio obtenidos del sistema. Windows utiliza volumen/FileId128/ChangeTime;
Linux utiliza dispositivo/inode/ctime con nanosegundos mediante statx. Así se
detectan reemplazos incluso de bytes idénticos y escrituras que restauren LastWriteTime,
sin hash de DB, watchers ni recorrido de documentos. Referencias del contrato:
[cabecera SQLite](https://sqlite.org/fileformat.html),
[Windows FileId](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info),
[Windows ChangeTime](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_basic_info),
[Linux statx UAPI](https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h).
Si el sistema/filesystem no proporciona esos metadatos nativos, se desactiva la
reutilización y cada operación valida fuertemente; no se presume una identidad
fiable usando solo tamaño/mtime. Un cambio durante la consulta da STOP y descarta
el resultado. Corrupción, universo/schema incompatible y errores SQLite siguen
siendo STOP. El mutex coordina las publicaciones de NAP.

La proyección valida las relaciones y fingerprints del output mostrado.
**No constituye evidencia de publicación o recovery**.
Detail valida el snapshot seleccionado, incluidos SHA/tamaño de todos sus BLOBs,
reutilizando la misma `CatalogAssetData.ValidateAsset` que la validación anterior.
Así un BLOB ajeno corrupto no se materializa para pintar el grid; seleccionar ese
asset da STOP. Para comprobar todo el catálogo sigue existiendo CheckIntegrity.

`PagedAssetCollection` expone Count + indexer sobre SQLite: páginas de 60 summaries,
LRU de cuatro páginas (240 placeholders), cancelación de páginas expulsadas y
de toda la colección al cambiar filtros/universo. No enumera todos los assets en
el flujo UI. Si cambia el total durante paging, STOP y actualización explícita.
No hay filtros/sort WPF sobre la colección: se aplican en SQLite.
El reader guarda un único COUNT para la instancia de filtro inmutable de la
colección y hasta cuatro anclas AssetId. Scroll secuencial consulta desde el último
AssetId con LIMIT (keyset), sin recalcular COUNT ni recorrer las páginas previas.
Un filtro nuevo recalcula su COUNT; un cambio/invalidation del catálogo borra
total y anclas. Un salto aleatorio sin ancla usa OFFSET: su coste puede crecer con
la distancia y selectividad del filtro, pero no materializa documentos ni snapshots
de todos los assets. No se promete acceso por índice arbitrario en tiempo constante.

`VirtualizingTilePanel` implementa IScrollInfo: calcula filas visibles y usa
IRecyclingItemContainerGenerator. Genera solo controles de filas intersectando
el viewport; el ancho determina columnas, sin WrapPanel no virtualizado.
Keyboard BringIndexIntoView y scroll usan índices. ThumbnailView activa la carga
en Loaded y cancela/libera la imagen en Unloaded/DataContextChanged. Bitmaps
congelados se decodifican fuera del dispatcher, con dos workers y cancelación al reciclar. La caché de página es apropiada
para ventanas convencionales y pantallas 4K; un viewport extremo con más de 240
tiles simultáneos necesitaría ampliar/ajustar el presupuesto de páginas.

## Ficha, búsqueda y estadísticas

Ficha: preview WebP→thumbnail, UniverseAssetKey, AssetId, tipo/perfil, clasificación
key/value y traits tipados, lifecycle catalogado y auditoría real nullable. Secciones
plegables para prompt/info/manifest/visual identity originales, rutas relativas,
archivos, roles/kinds/localización/tamaños/SHA-256, master y producción.
No se abre el PNG maestro. No se inventan fechas, Job original, modelos, historia
ni auditorías ausentes. Los documentos solo se cargan al seleccionar un asset.

Filtros: AssetId/tipo/perfil exactos, múltiples dimensiones de clasificación y
múltiples traits (JSON Pointer + valor + tipo escalar). Facets salen del catálogo,
sin propiedades Nimroel. Una elección por dimensión; todo combinado por AND;
vacío = sin filtro. Aplicar actualiza grid y estadísticas; Restablecer limpia.
No SQL de usuario, fragmentos ni lenguaje de consulta.

Estadísticas: tarjetas total/tipos/perfiles y barras por type/profile/classification/
trait. Porcentaje = count / total filtrado, cobertura con filtro propio de cada
objetivo catalogado; muestra actual/target/remaining. No objetivos = ausencia
explícita. No hay edición de objetivos, tendencias temporales ni juicios creativos.

## Concurrencia y límites

Catálogo/I/O se ejecutan en Task.Run; adapter serializa leases locales. Tokens
cancelan esperas, iteración y resultados tardíos. Microsoft.Data.Sqlite realiza
sus llamadas nativas síncronas: una consulta/integrity_check ya en curso acaba
antes de liberar el worker/lease; no se cierra la conexión desde otro hilo.
Integrity_check puede recorrer muchas páginas de DB (incluido almacenamiento de
BLOBs) a nivel SQLite, sin materializarlos como documentos ni snapshots de UI.
La validación fuerte se paga al inicio/cambio/expiración de sesión, no por cada
página normal; statistics/planning agregan las filas necesarias de sus filtros.
La lectura caliente añade comprobaciones acotadas de archivo/contrato y consulta
la página con sus filtros, sin ValidateLogical global ni SELECT de documentos.
No se mantiene un lock o transacción de lectura durante la vida de la ventana.

La vista refleja lecturas actuales por operación, no un snapshot temporal global
entre página/estadísticas. Cambios operativos simultáneos requieren actualizar;
el mutex del pipeline provoca error busy y reintento explícito. Archivos externos
modificados se rechazan incluso si existe thumbnail en caché.

## Validación

```powershell
dotnet restore NAP.sln
dotnet build NAP.sln --configuration Release --no-restore
dotnet test NAP.sln --configuration Release --no-build
.\scripts\Test-MultiUniverseSchemas.ps1
git diff --check
git status --short
```

Tests nuevos: LocalUniverseSettingsTests, ProductionThumbnailTests,
CatalogExplorerTests, CatalogExplorerSessionTests, ExplorerViewModelTests,
ExplorerUniverseAvailabilityTests y VisualExplorerWindowsSmokeTests.
Las pruebas de sesión verifican validación inicial/reutilización/expiración,
reemplazos y modificaciones con mtime restaurado, STOP por universo/schema/
corrupción y validación fuerte de Fase 10. Las de disponibilidad usan catálogos
reales temporales para apertura/filtros/stats, STOP del universo ausente, reconexión,
guardado con otro offline y rechazo de roots propios inválidos/overlap/cambios inválidos.
VisualExplorerWindowsSmokeTests lanza la App real con settings/catalog/roots temporales y verifica
ficha/documentos/filtros/reset/estadísticas y render WPF. Un stress separado usa
30.000 enteros exclusivamente para medir el panel/reciclaje; no sustituye los
assets reales del primer recorrido. Settings reales de AppData no se usan.
También se valida la colección portable con 30.000 summaries y cuatro páginas.

Modo diagnóstico explícito (termina tras render; no modifica assets):

```powershell
dotnet src/NAP.App/bin/Release/net8.0-windows/NAP.App.dll --smoke-test --settings=C:\ruta\local\settings.json
```

`--settings=` permite inyectar una ubicación local controlada para diagnósticos;
aplican las mismas restricciones de paths, Git, formato y aislamiento. No es un
modo demo, no crea catálogo ni assets. Fases 12–15 permanecen sin implementar.
