# Thumbnails v1

Fuente única: archivo **generated_webp de Production** identificado por
CatalogAssetSummary. UniverseId debe coincidir con el Context; ruta relativa
canónica debe terminar en AssetId.webp. El decoder específico WebpDecoder rechaza
PNG aunque se renombre .webp. No hay argumento de Archive/master ni fallback PNG.

Destino único: `CacheRoot/thumbnails/<SHA-256-key>.png`. Clave determinista:
JSON UTF-8 de versión de perfil, límite 320, UniverseAssetKey, ruta relativa,
SHA-256 catalogado del output y tamaño. Cambiar fingerprint produce otra entrada;
universos con igual AssetId/hash conservan identidad/cache aislados.

Perfil: PNG lossless, compresión nivel 6, un frame, lado mayor ≤320 píxeles,
sin upscale. WPF decodifica PNG nativamente; conserva alpha y evita un codec
externo. La calidad no es un parámetro de PNG: se preservan los píxeles de la
reducción Lanczos3. La fuente es el WebP ya producido, nunca el PNG maestro.
Resize proporcional full-frame con redondeo al entero más próximo (mínimo 1):
sin crop, deformación deliberada, padding ni ratio universal 4:5.

Creación lazy/on-demand async, dos workers globales como máximo. Fuente ≤128 MiB,
≤64 millones de píxeles; no File.ReadAllBytes del source. SHA streaming async y
tamaño se verifican **también en cache hit**. WebP Identify precede al decode y
un segundo SHA tras decode rechaza cambios durante lectura. Lectura con
FileShare.Read; otros procesos no controlados siguen sujetos a los límites TOCTOU
del filesystem, igual que las fronteras existentes (no sandbox del sistema).

Caché hit: archivo regular sin reparse, ≤2 MiB, decoder PNG, dimensiones exactas
esperadas, decode completo para detectar corrupción. Entradas corruptas se
regeneran exclusivamente en CacheRoot. Publicación: mutex por clave, temp hermano
CreateNew + encoder + Flush(true) + token/paths + Move. Cancelación conserva
assets y limpia solo el temp propio comprobando otra vez reparse/containment.
Un source ausente/modificado/corrupto da error y no devuelve un preview obsoleto.

Bitmaps visibles ≤320×320; Unloaded/reciclaje cancela demanda y libera referencias.
Presupuesto del decoder acotado por píxeles × dos workers; fuentes cerca del límite
pueden requerir cientos de MiB durante conversión. No se cargan miles a la vez.
No hay cuota/evicción automática de disco: entradas antiguas son regenerables y
pueden eliminarse explícitamente fuera del explorador. No pertenecen al catálogo,
assets canónicos, archive, repositorio de producción ni backup.

Se reutilizan validadores de ProductionRoot, containment y rechazo de reparse en
todos los componentes source/cache. Workspace/cache deben estar aislados de
Production/Archive. No se sigue un junction para generar, publicar o limpiar.
No se escriben SQLite, manifest, masters, WebP ni companions. Tests usan únicamente
roots temporales e incluyen snapshots antes/después de Production/Archive.
