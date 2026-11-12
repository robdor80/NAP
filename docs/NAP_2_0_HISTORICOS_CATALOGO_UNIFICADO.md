# NAP 2.0 — catálogo integrado del legado de Treskal

## Contrato de compatibilidad

- El catálogo de publicaciones nuevas **AssetCatalog.db** sigue intacto en esquema v1, con sus reglas estrictas de PNG maestro, archivos y auditoría.
- La importación histórica crea **LegacyPortraits.db** en `WorkspaceRoot/state` como índice SQLite separado. Al pulsar «Importar retratos históricos al catálogo» se selecciona explícitamente la carpeta Assets existente.
- El servicio de lectura del catálogo combina registros nativos y registros históricos en la misma galería, filtros y estadísticas. Los históricos llevan procedencia distintiva en su ficha; no se marca «PASS NAP» ni se inventa PNG maestro.
- 39 WebP, prompts, información e identidad visual permanecen en el repositorio sin modificarse. El índice guarda SHA-256, clasificación, dimensiones y ruta.
- La ficha lateral aumenta de 280 a 450 px; el SHA y las rutas tienen ajuste de línea.
- La importación debe verificar manualmente 39 retratos, filtros por cultura, sexo y profesión, y continuidad del retrato 901.

## Destinos definitivos (activación local en MSI)

La aplicación ya contiene las reglas para generar las subcarpetas de publicación a partir del manifiesto. Para activarla se configuran **tres raíces independientes**:

1. Workspace: carpeta local independiente de Git y TeraBox, con los jobs, inbox y SQLite.
2. Production: `C:\Users\andro\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets` si coincide con el checkout real.
3. Archive: carpeta **local** destinada a PNG maestros, cuyo respaldo automático a TeraBox debe configurarse y comprobarse en el cliente de TeraBox. NAP no afirma subidas directas a la nube.

La utilidad auxiliar `NAP.LiveBootstrap` crea el catálogo de la nueva configuración y guarda los ajustes con los validadores oficiales, sin modificar los de estreno si se indica un archivo de configuración diferente. Las raíces deben existir antes de ejecutar la herramienta.

## Importante: límites

- `LegacyPortraits.db` no sustituye ni migra `AssetCatalog.db`.
- No existe verificación automática de que TeraBox haya terminado una subida; comprobar un PNG de prueba desde la web.
- La integración aquí descrita no modifica los datos ni configura el MSI remotamente; requiere compilación y prueba en Windows antes de fusionar.
- El nuevo historial entra en el catálogo visual y sus estadísticas, pero los objetivos de automatización del Core original todavía se calculan solo sobre el catálogo certificado.
- No lanzar NAP CI, no ejecutar CUTOVER y no tocar la PR #58.
