# Verificación de la entrega documental en Drive

25/10/2026. Se verificaron por lectura posterior 39 archivos entregados o actualizados: identificador, carpeta, tamaño y permisos de propietario único. Los tamaños coinciden con los archivos locales. No se cambiaron permisos ni se subió el ejecutable a Drive.

Además se leyeron de nuevo y compararon ocho documentos de texto: índice, guía, informes ejecutivo/técnico, aislamiento, dependencias/licencias, hash del ZIP y resultados. Coinciden con el contenido local, normalizando únicamente finales de línea. Los PDF y archivos JSON se comprobaron por metadata y los PDF también por render local; no se afirma un hash de descarga binaria de esos documentos desde Drive.

Los detalles sin información personal de permisos se conservan en Drive-readback-20261025.json. La comprobación binaria del ZIP del programa sí es completa: se descargó dentro de GitHub Actions y se validaron su SHA-256, las 490 entradas del manifiesto, el total de 491 archivos y el commit d35a8c60ba2318f4950edfed339692f259aed8ed. Véase runs/40150398814/download-verification.json.

La carpeta existente y su organización se conservan. Los documentos de cabecera y la guía se actualizaron en sus mismos identificadores. Las nuevas evidencias de C. Pruebas tienen prefijo 20261025; las anteriores se mantienen como histórico. La entrega pública del programa está autorizada; la documentación de Drive mantiene su acceso privado actual.
