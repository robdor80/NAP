# NAP 2.0 — Biblioteca de 39 retratos históricos de Treskal

## Alcance de este parche de estreno
- Añade en **Catálogo** el botón «Consultar retratos históricos (legado)».
- Solicita seleccionar explícitamente la carpeta \`Assets\` del checkout de \`Videojuego_Nimroel\`.
- Lee \`portraits/norgard/treskal\`, incluidas las ramas históricas \`children\` y \`elder\`.
- Descubre un WebP, prompt, información e identidad visual por asset; comprueba estructura, identidad, formato decodificable, dimensiones y SHA-256. Muestra una lista con selección, vista previa, clasificación, documentos y rutas a copiar/abrir.
- Reconoce los originales del repositorio tal como están, sin moverlos, transformarlos ni sobrescribirlos.
- No toca el catálogo SQLite v1, no genera PNG maestros ni inventa manifiestos, índices o auditorías PASS. Es una **vista integrada de solo lectura**, no un importador certificado a SQLite. Los 39 retratos son utilizables directamente por el videojuego a partir de sus WebP existentes.
- No sincroniza con TeraBox ni busca el PNG histórico en la nube.

## Constatación del repositorio, 12-11-2026
- 39 WebP, 39 \`_prompt.md\`, 39 \`_info.md\`, 39 \`_visual_identity.json\` — 156 archivos.
- PR de Nimroel #57 fusionada, rutas normalizadas a minúsculas \`norgard/treskal\`, sin alterar bytes.
- Doce retratos siguen las categorías \`children\` y \`elder\`: el lector consulta su identidad verdadera de cada JSON, sin inferir \`role\` a partir de la carpeta.
- El nuevo NAP no permite importar estos 39 en su SQLite v1 por falta de \`_manifest.json\`, PNG maestro, master_index y las reglas canónicas de procedencia; el importador nativo existente seguiría dando STOP. No modificar ese contrato para fingir interoperabilidad.

## Validación local imprescindible en el MSI
1. Compilar rama \`feature/nap-legacy-portrait-library-20261112\`, configuración Release con la licencia Six Labors original (no subir licencia a Git).
2. Abrir el NAP normal, entrar en Catálogo y pulsar «Consultar retratos históricos (legado)».
3. Elegir \`C:\Users\andro\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets\` (o su ubicación real).
4. Contar 39, abrir varios de los grupos farmer, children, elder, comprobar la clasificación leída del JSON y comprobar una vista previa y el contenido del prompt/info. Confirmar que los 156 archivos originales y sus hashes permanecen idénticos.
5. Comprobar que la publicación nueva y el catálogo SQLite del retrato 901 siguen intactos.
6. No ejecutar GitHub Actions NAP CI ni CUTOVER. No fusionar sin validación Windows.

## Limitación expresa
Esta entrega prioriza que los 39 retratos **se puedan consultar y localizar desde NAP ahora mismo** para pruebas de la aldea. Para combinarlos en las estadísticas y filtros del catálogo SQLite certificado hace falta un modelo de procedencia histórica separado y su migración versionada, o disponer de originales+manifiestos para importación estricta. No es una migración SQLite completada.
