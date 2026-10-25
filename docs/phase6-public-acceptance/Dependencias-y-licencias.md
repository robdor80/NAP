# Dependencias y licencias — aceptación sintética

Revisión del 25/10/2026. Publicación autorizada exclusivamente para el paquete de prueba de Fase 6. No autoriza una Release definitiva, instalador ni actualización automática. No se modifica Core ni se omite su validación oficial de Six Labors.

## ImageSharp 3.2.0

La licencia Community existente de NAP.Core fue verificada oficialmente en la preparación anterior. Este empaquetado conserva Invoke-LicensedDotnet.ps1 y SixLabors_ValidateLicense en Release, con el secreto de Actions existente. Se elimina el temporal antes de cualquier prueba de ejecución o empaquetado. Se revisan nombres y contenido UTF-8/UTF-16 de todos los archivos contra claves, tokens y material privado. No se publica sixlabors.lic, su contenido ni su hash.

Las condiciones de Six Labors Split License 1.0 permiten Apache-2.0 para los supuestos Community. Se utiliza la concesión Community ya validada para este producto; no se presupone que un repositorio público sea por sí solo una licencia Open Source. Se incluyen Apache-2.0, copyright y procedencia del texto Split original. La clave se requiere al compilar la dependencia directa, no al ejecutar este binario. El sondeo PNG/WebP en memoria usa exactamente la DLL distribuida y se ejecuta sin archivos ni variables de licencia. Una distribución comercial futura debe revisar su propia elegibilidad y condiciones.

Fuentes oficiales: https://github.com/SixLabors/ImageSharp/blob/v3.2.0/LICENSE y https://docs.sixlabors.com/articles/imagesharp/index.html . Los textos públicos de origen y sus SHA-256 se conservan en notices/sources.json. No se han modificado las bibliotecas.

## Inventario revisado

- Microsoft.Data.Sqlite y Microsoft.Data.Sqlite.Core 8.0.31: MIT, Microsoft.
- SQLitePCLRaw.bundle_e_sqlite3, core, lib.e_sqlite3 y provider.e_sqlite3 2.1.12: Apache-2.0, Eric Sink y colaboradores. El motor SQLite nativo es de dominio público; no impone redistribución de código de NAP. Fuente: https://sqlite.org/copyright.html . Se incluyen e_sqlite3.dll y sus adaptadores; se comprueba la consulta sqlite_version() en base de datos exclusivamente en memoria y la coincidencia SHA-256 de la DLL nativa.
- System.Memory 4.5.3: MIT, Microsoft. Su NUSPEC histórico usa licenseUrl y carece de license expression; se ha contrastado el URL oficial corefx/LICENSE.TXT y se incluye el texto MIT. Esta ausencia de metadato moderno no queda sin revisar.
- .NET 8.0.32 autónomo para Windows x64: MIT y avisos de terceros de runtime, WPF y Windows Forms de las etiquetas oficiales v8.0.32. Se incluyen los textos completos y sus copyrights. No requiere instalar .NET ni herramientas de programación.
- WPF, RobStyle, RoDo y perfiles públicos: recursos del mismo commit NAP compilado; no se importan perfiles personales ni recursos reales de trabajo. Los perfiles JSON públicos incluidos no son descubiertos/cargados en aceptación: la sesión usa dos perfiles construidos en memoria.
- El observador ETW Microsoft.Diagnostics.Tracing.TraceEvent 3.1.21 es una herramienta de verificación exclusiva del runner. No se distribuye con NAP. Tampoco se incluyen tests, AutomationHost, Git, símbolos PDB ni carpetas obj.

La puerta de publicación compara el grafo NuGet resuelto con un inventario cerrado de ocho paquetes. Si aparece otra dependencia, falta un recurso obligatorio, no coincide un aviso o falla una comprobación, no se sube el paquete. El inventario exacto se entrega en third-party-packages.json.

## Cinco avisos conservados

- GHSA-gwg2-r3hj-4w44: NU1902, Moderate, parser ICC/CLUT.
- GHSA-wmxv-xphr-5c9g: NU1902, Moderate, decodificador BigTIFF.
- GHSA-j3p4-wp97-rph4: NU1903, High, histograma/luminancia.
- GHSA-j9gm-c75j-xc9q: NU1903, High, encoder TIFF T4.
- GHSA-jjfr-hcj7-qf5w: NU1903, High, encoder TIFF T6.

Cada identificador puede consultarse en https://github.com/advisories/ seguido del ID. El proveedor incluyó backports en https://github.com/SixLabors/ImageSharp/pull/3208 ; los registros NuGet siguen señalando estos avisos. Se conserva el inventario observado en la ejecución actual, sin supresiones y sin declarar un análisis limpio. La sesión no admite imágenes externas ni ZIP de assets; solo genera píxeles conocidos en memoria. Esto limita las entradas de esta aceptación, pero no resuelve por sí mismo los avisos para uso productivo. Sigue abierto https://github.com/robdor80/NAP/issues/49 . No se ejecutan PoC, estrés ni regresiones generales.

## Herramientas externas y alcance

Esta aceptación no necesita Git ni otros programas. Las herramientas productivas de Git necesitarán Git externo; aquí la inspección de remotos, commit, push y recuperación están bloqueadas. Backups y Objetivos muestran pantallas vacías sin escritura. Estadísticas usa el catálogo vacío sintético. RoDo continúa reservado. Licencias permite cambios administrativos ficticios en memoria, sin modificar la licencia real ni ampliar derechos de uso. Las exportaciones a archivos se rechazan antes de abrir un diálogo.
