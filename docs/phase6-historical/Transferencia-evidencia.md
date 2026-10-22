# Transferencia de evidencias P6.7

Los primeros intentos de `gh run download` y `gh run view --log` no pudieron seguir los redirects de artefactos/logs porque la política de red del executor devolvió Forbidden. No se alteraron proxy, TLS, credenciales, rutas ni política. Se utilizaron las operaciones autorizadas del conector GitHub para descargar los artefactos y obtener los logs decodificados. Los ZIP conservados son archivos originales de evidencias de Actions (PNG, TRX y reportes de texto), no paquetes ZIP de assets creados por NAP.

Una consulta posterior de estado mediante CLI devolvió HTTP 503 temporal. La lectura por conector confirmó el estado del job. Estas incidencias son de transferencia/consulta; no se convierten en resultados de pruebas del producto ni se ocultan fallos de tests. Las tres ejecuciones de revisión se describen con su resultado real en evidence.json.

Los logs se conservan como texto decodificado, con saltos de línea normalizados a LF. Los bytes originales de PNG, TRX, stdout/stderr, reportes y archivos de hashes se conservan sin modificación. Los digest de los ZIP recibidos se contrastan con los declarados por Actions.

La primera comprobación documental de la galería PDF detectó 51 páginas en vez de 34 por salto de pies de captura. Se conserva ese PDF preliminar. Se corrigió exclusivamente el tamaño de colocación en el documento, sin modificar PNG originales; la galería final se verifica con una captura por página. No fue un fallo de producto ni de pruebas WPF.

El control documental Git detectó una línea vacía adicional al final de Galeria-WPF.md; se corrigió antes del commit de entrega. No cambia el contenido de revisión ni los artefactos originales.
