# NAP08 — Acta de comprobaciones físicas del MSI Windows 11

**Fecha:** 14/11/2026. **Equipo:** MSI Raider GE78 HX, Windows 11 x64.
**Repositorio:** `robdor80/NAP`; PR #72, rama `feature/nap08-certificacion-fisica-msi-20261113`.
**Alcance:** certificación de instalación/interfaz y gestión sintética de AutomationHost. **No acredita NAP09/CUTOVER**.
**Fuentes de comprobación:** capturas del propietario en Windows 11, salida PowerShell del MSI y GitHub Actions acotadas.
**Criterio:** declarar PASS únicamente aquello comprobado; distinguir Windows físico de runner CI.

## Evidencia acreditada en el MSI

| Prueba | Resultado | Evidencia |
| --- | --- | --- |
| Extracción de paquetes originales 2.0.0/2.0.1 y SHA-256 | PASS | PowerShell del MSI verificó los dos hashes contra Actions #43517155816 |
| Instalación 2.0.0 y creación de acceso directo RoDo | PASS físico | Capturas del instalador y acceso directo |
| Arranque y navegación WPF (Inicio, Ajustes, Catálogo, Producción, Historial y Auditor IA) | PASS visual | Capturas de pantallas; sin atribuir operaciones no ejecutadas |
| Catálogo real de Nimroel | PASS lectura visual | Catálogo disponible, miniatura de mapa de Treskal mostrada |
| Bandeja: minimizar y restaurar | PASS informado por usuario | Confirmación expresa tras la prueba física |
| Actualización Inno 2.0.0 → 2.0.1 | PASS físico | `NAP.App.exe` ProductVersion: `2.0.1+74aeac2e2bc165c9d56b72de4e8a80765744dd7a`, con arranque posterior |
| Conexión WPF ↔ AutomationHost en laboratorio TEMP | PASS físico | Captura con universo Nimroel (laboratorio NAP08), estado «Sin activar» y contadores obtenidos del Host |
| ShutdownHost 2.0.1 y desaparición del proceso | PASS físico | Capturas del Administrador de tareas: sin `NAP.AutomationHost.exe` en Procesos ni Detalles |
| NAP 2.0.2 instalado en el MSI | PASS visual | Captura normal de Inicio, pie `RobDev · NAP 2.0.2` |
| Corrección de UX tras ShutdownHost en NAP 2.0.2 | **PASS físico visual** | Captura del propietario de Ajustes: «Automatización detenida», botones de control deshabilitados, sin timeout de Host |

## Evidencia de compilación y pruebas en GitHub Windows

- [Prueba focalizada del apagado y estado WPF, SUCCESS](https://github.com/robdor80/NAP/actions/runs/43531073054).
- [Paquete NAP 2.0.2, SUCCESS](https://github.com/robdor80/NAP/actions/runs/43532238801):
  `NAP-Setup-2.0.2.exe` SHA-256 `6bfd138e115a7b4d12bf1353e26b0fb179bdd99254d75a57274fe74ff64457d6`.
  683 archivos inventariados; binarios WPF y Host con ProductVersion
  `2.0.2+8bc335523e17a37757840690a5e4b3d8c064a51d`;
  instalación, arranque WPF sintético, desinstalación y preservación de marca de datos en Windows Server 2022 PASS.
- El instalador del producto no está firmado digitalmente; la verificación de origen de Inno y los hashes no equivalen a firma del producto.

## Observaciones, distinciones y límites

1. Al abrir desde el acceso directo normal aparece «Automatización sin configurar»:
   ese modo no incorpora `--automation-managed-config` y no conecta Host por sí solo.
   La conexión verificada fue expresamente en laboratorio TEMP. Activación normal es NAP09.
2. En laboratorio temporal se muestra advertencia roja de lectura de catálogo:
   `workspace/state/AssetCatalog.db` está ausente. El lector no reconstruye ni crea
   bases de datos durante consultas. El catálogo real de Nimroel se leyó en modo normal.
   No interpretar esta advertencia como corrupción de datos o como fallo de shutdown.
3. En 2.0.1, tras shutdown confirmado, WPF consultaba de nuevo el endpoint ya cerrado
   y presentaba erróneamente timeout de Host. Código corregido y regresión probada
   en 2.0.2; captura de MSI confirma el texto «Automatización detenida».
4. **No acreditado físicamente:** reinstalación tras desinstalación en el MSI;
   NAP07 Stage/Apply/Recover/Rollback (una actualización por instalador Inno no lo verifica);
   Host con dos universos físicos; procesamiento de assets/Gemini de producción;
   aceptación visual sistemática a 1200×700 o diversas escalas DPI; recibos persistentes
   de shutdown 2.0.2. La desinstalación y preservación sí pasaron en CI, no en el MSI.
5. No se ha autorizado NAP09 ni CUTOVER. Mantener abiertas #47 y #49; su cierre requiere
   comprobaciones propias. La prueba no modificó intencionadamente activos de producción;
   las capturas acreditan visualización, no una auditoría forense de todos los archivos.

## Dictamen

**NAP08 supera las comprobaciones físicas principales de instalación, actualización,
interfaz y conexión/parada de Host de laboratorio.** El defecto observado de proyección
post-shutdown está corregido y acreditado físicamente en la versión 2.0.2.

**Certificación integral sin reservas: NO emitida**, por las pruebas no ejecutadas
enumeradas arriba. La PR #72 puede evaluarse para cierre técnico **con excepciones
explícitas**, sin convertirlas en PASS ni confundir la fase con NAP09. La decisión de
fusionar debe respetar las reglas actuales del repositorio y su estado de PR.
