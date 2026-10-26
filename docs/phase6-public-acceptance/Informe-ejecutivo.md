# NAP 2.0 — listo para aceptación sintética

25/10/2026. El ZIP descargable está preparado y comprobado. Permite abrir la interfaz real de NAP con RobStyle y datos ficticios, sin activar producción ni utilizar los datos habituales del ordenador. No es el cierre de Fase 6.

[Descargar el programa desde GitHub](https://github.com/robdor80/NAP/actions/runs/40150398814/artifacts/12582294018). Inicia sesión normal en GitHub, descarga el artefacto **NAP-2.0-Fase6-Aceptacion-Windows-x64**, descomprime todo y haz doble clic en **Iniciar-prueba-NAP.cmd**. No hacen falta comandos, administrador ni herramientas de programación.

## Resultado verificado

- [Ejecución 40150398814](https://github.com/robdor80/NAP/actions/runs/40150398814), Windows Server 2022, correcta.
- Commit compilado: d35a8c60ba2318f4950edfed339692f259aed8ed.
- Windows x64, autónomo, .NET y WindowsDesktop 8.0.32 incluidos; 491 archivos.
- 49/49 pruebas focalizadas, 9 capturas WPF de 1200 × 700, cero errores de binding.
- Arranque directo y mediante CMD, cinco secciones, filtros, detalles, herramientas sintéticas, bandeja y cierre comprobados.
- 206 eventos únicos de archivos observados: cero accesos a datos habituales de NAP, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Canarios sin cambios y unión de directorios rechazada.
- ZIP descargado de nuevo desde GitHub: digest y 490 entradas del manifiesto verificados; el archivo 491 es el propio manifiesto. Procedencia consistente con el commit.
- ImageSharp PNG/WebP y SQLite 3.53.3 probados en memoria, sin archivo privado de licencia. Ocho paquetes y avisos de redistribución revisados.

## Qué queda pendiente

La aceptación visual humana en Windows 11 y las pruebas operativas reales siguen pendientes. Catálogo está vacío; Producción e Historial muestran filas ficticias; Diagnóstico e Histórico son sintéticos; los cambios de Licencias se pierden al cerrar. Publicación, normalización, borrado, restauración, importación de perfiles, AutomationHost y Git reales están bloqueados.

Siguen abiertos #49 — cinco avisos NU1902/NU1903, timeout IPC y StorageUnavailable — y #47/CUTOVER. No se declara un análisis de seguridad limpio. El ejecutable no tiene firma de editor; si Windows lo bloquea, conserva el mensaje y consúltalo con ChatGPT, sin desactivar protecciones. .NET 8 mantiene soporte hasta el 10/11/2026: hay que revisar el runtime antes de reutilizar este paquete después de esa fecha.

## Entrega y límites

[Informes y guía PDF en la carpeta existente de Drive](https://drive.google.com/drive/folders/1WMXcrTRYbjxk8hP0dm8yK_QGQ-ajZHDu). Permisos conservados; ejecutable solo en GitHub. Retención de GitHub comprobada hasta 2027-01-23T01:22:51Z. No se creó Release, instalador, updater ni PR. Main permanece en 5f81950275098881fd0befac0a13cde08c9d751f. No se accedió al MSI, no se tocaron assets reales ni se inició Fase 7.
