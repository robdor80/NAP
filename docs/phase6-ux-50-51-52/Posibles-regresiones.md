# Posibles regresiones y límites

No se han observado defectos visuales nuevos en las superficies corregidas en ambos tamaños de captura. La primera iteración reveló una cuadrícula incorrecta al cambiar de modo de calendario: se corrigió antes de esta entrega y se añadió una aserción para meses y años. Se revisaron las capturas finales.

Confirmar en MSI la entrada manual y elección de fecha, meses/años, hover, hoy, deshabilitados y foco con el escalado habitual. La prueba remota usa Windows Server 2022 y cliente lógico WPF a 96 DPI; no acredita Windows 11 ni el renderizado físico de todos los DPI.

No se modifica Core, Presentation, AutomationHost, almacenamiento, contratos, imágenes, Git ni backups. La instrumentación solo se ejecuta en --phase6-self-test dentro del laboratorio. La edición conserva bloqueo productivo y sesiones en memoria.

Permanecen los avisos NU1902/NU1903 de ImageSharp y las reservas #49 (IPC timeout y StorageUnavailable). CUTOVER #47 sigue bloqueado. No se ocultan ni se declaran resueltos por esta corrección de interfaz. No se inicia Fase 7 ni se cierra Fase 6. #50, #51 y #52 quedan abiertas hasta confirmación del responsable.


## Entrega verificada

Commit compilado: `2f743c0fb580b8a859f5e503f6a14bf2afcf5383`.

Ejecución: https://github.com/robdor80/NAP/actions/runs/40216835055

Descarga: https://github.com/robdor80/NAP/actions/runs/40216835055/artifacts/12608231704

SHA-256 del ZIP descargado de Actions: `e0da9f472b333b8697868d62c621bf5e26ee46558c1b4f8dcfe1acdccaf0ace9`. El ZIP interno del ensayo usa otra compresión; este es el hash de la descarga del usuario.

Caducidad indicada por GitHub: 2027-01-23T17:30:55Z. Requiere sesión en GitHub. No es una Release.

49 pruebas focalizadas superadas; cero errores de binding. Búsqueda 001: 1; Restablecer: 39; farmer: 10. Diez comprobaciones geométricas del submenú superadas; las diez fallaban en la referencia anterior. Cuadrículas de meses y años comprobadas con doce botones visibles y días ocultos. Selección de fecha y navegación verificadas.

ETW: cero accesos a ubicaciones habituales de NAP, cero escrituras fuera del laboratorio, cero procesos hijos y cero eventos perdidos. 491 archivos verificados por manifiesto tras descargar el artefacto. Arranque directo y CMD, argumentos y junctions inseguros, renovación en memoria, cambio de universo y bandeja superados. Sin licencias privadas ni patrones de secretos en el paquete. Avisos legales conservados.
