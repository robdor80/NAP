# Resultados de la entrega 40150398814

- Estado Actions: correcto; [ejecución](https://github.com/robdor80/NAP/actions/runs/40150398814). Commit d35a8c60ba2318f4950edfed339692f259aed8ed.
- Compilación Release win-x64 autónoma: correcta. .NET/WindowsDesktop 8.0.32; SDK 10.0.402.
- Pruebas focalizadas: 49 aprobadas, cero fallos y cero omitidas.
- Capturas: nueve, 1200 × 700; cero errores de binding.
- ETW: 206 eventos únicos; cero accesos a NAP habitual, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Control positivo de escritura observado.
- Canarios intactos; comandos productivos y argumentos inseguros rechazados; unión de directorios del laboratorio rechazada.
- Arranque directo, iniciador CMD, cinco secciones, cinco herramientas, filtros, detalles, licencia en memoria, histórico de 39 referencias, bandeja y cierre: comprobados.
- ImageSharp PNG/WebP sin pérdida y SQLite 3.53.3 en memoria: correctos, sin licencia privada disponible en ejecución.
- Ocho paquetes revisados; bibliotecas nativas y recursos obligatorios presentes; controles de nombres y patrones de secretos correctos.
- Artefacto publicado y descargado de nuevo: 491 archivos; 490 entradas de manifiesto y el propio manifiesto; todos los hashes coinciden.
- SHA-256 del ZIP final: f15626624e0a88b3d7d796f83690fbffca1f21ca8f86687547f232d86e64ea7e.
- Caducidad observada: 2027-01-23T01:22:51Z. Requiere cuenta GitHub normal para descargar en navegador.
- No se ejecutaron pruebas de volumen de assets, estrés, benchmarks ni regresiones generales; no hubo acceso al MSI.

Las capturas y trazas son del runner, no del MSI. Las reservas de #49 y el CUTOVER de #47 siguen abiertos. El soporte de .NET 8 termina el 10/11/2026; la retención del ZIP no amplía esa vigencia.
