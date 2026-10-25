# NAP 2.0 - Correcciones UX #50, #51 y #52

25/10/2026. Correcciones preparadas para la segunda confirmación visual del responsable en Windows 11. Las tres incidencias siguen abiertas. Fase 6 no cerrada.

- Rama autorizada: `codex/nap-2-fase-6-ux`.
- Commit exacto compilado: `2f743c0fb580b8a859f5e503f6a14bf2afcf5383`.
- Referencia anterior: `fe69ba3500845c3e0f63c9336f159d0a2cda3bee`.
- `main` comprobado antes y después: `5f81950275098881fd0befac0a13cde08c9d751f`, sin cambios.
- [Ejecución final superada](https://github.com/robdor80/NAP/actions/runs/40216835055).
- [Descargar NAP-2.0-Fase6-UX-Corregida-Windows-x64](https://github.com/robdor80/NAP/actions/runs/40216835055/artifacts/12608231704). Requiere sesión de GitHub; no es una Release. Incluye runtime Windows x64, dependencias, avisos legales, CMD y manifiesto.
- SHA-256 del ZIP descargado de Actions: `e0da9f472b333b8697868d62c621bf5e26ee46558c1b4f8dcfe1acdccaf0ace9`.
- [Carpeta de Drive: NAP 2.0/Fase 6/Correcciones UX 50-51-52](https://drive.google.com/drive/folders/1_pWwOs48t99dZoPeGwnjKXJCqYbv7dDH).

## Documentación

[Ejecutivo PDF y comparativas](Informe-ejecutivo.pdf), [informe técnico](Informe-tecnico.md), [archivos modificados](Archivos-modificados.txt), [resultados](Resultados.json), [posibles regresiones](Posibles-regresiones.md) y [guía MSI](Guia-MSI.md).

Los PNG originales y la evidencia automatizada están en [runs/40216835055](../phase6-public-acceptance/runs/40216835055). Contiene 60 PNG UX anteriores, 60 posteriores y nueve capturas del ensayo previo de aceptación, además de TRX, ETW, manifiesto, procedencia y verificación de descarga. La carpeta de Drive también incluye un ZIP de estas evidencias. El PDF contiene recortes comparativos identificados; los originales no se alteran.

La referencia anterior se compiló con las vistas y estilos originales; únicamente se copiaron los dos archivos de instrumentación de aceptación para obtener las mismas superficies en ambos ensayos. WPF real sobre Windows Server 2022, cliente lógico a 96 DPI, popups renderizados separadamente y compuestos en su anclaje lógico. No se acredita renderizado físico en el MSI ni Windows 11.

## Resultados y reservas

49 pruebas focalizadas superadas, cero fallidas u omitidas y cero errores de binding. Búsqueda `001`: 1 resultado; Restablecer: 39; `farmer`: 10. Las diez comprobaciones de bordes de Herramientas pasan; la referencia anterior recortaba la izquierda en las diez. Meses y años muestran sus doce botones y ocultan la cuadrícula de días. Navegación y selección de fecha verificadas. Revisión visual de las superficies corregidas en ambos tamaños sin nuevos defectos observados.

ETW sin accesos a ubicaciones habituales de NAP, sin escrituras fuera del laboratorio, sin procesos hijos y sin eventos perdidos. Canarios intactos; comandos productivos bloqueados. Arranque directo y CMD, navegación, renovación en memoria, cambio de universo y bandeja superados. Los 491 archivos del artefacto descargado coinciden con el manifiesto. Escaneo de archivos privados y patrones de secretos superado; avisos legales incluidos. No se ejecutan pruebas de volumen ZIP ni regresión general.

Se conservan NU1902/NU1903 y las reservas de #49 (IPC timeout y StorageUnavailable); CUTOVER #47 sigue bloqueado. Sin cambios en Core, AutomationHost, contratos, imágenes, almacenamiento, Git ni backups; sin acceso al MSI o assets reales. Sin PR, fusión, instalador ni Fase 7.

Los comentarios de seguimiento están en [#50](https://github.com/robdor80/NAP/issues/50#issuecomment-6400909580), [#51](https://github.com/robdor80/NAP/issues/51#issuecomment-6400909775) y [#52](https://github.com/robdor80/NAP/issues/52#issuecomment-6400909949). Incluyen causas, cambios, archivos, pruebas, capturas y paquete; no cierran las incidencias.

## Verificación documental

Los siete archivos de Drive se comprobaron mediante lectura posterior de metadata: nombre, identificador, tamaño y carpeta coinciden con los archivos locales. Los archivos mantienen únicamente el permiso de propietario; no se han modificado permisos de ninguna carpeta. [Registro sin datos personales](Drive-readback.json). El PDF se renderizó y revisó en sus cinco páginas. La comprobación binaria completa del ZIP de la aplicación se realizó en Actions tras descargar el artefacto publicado.
