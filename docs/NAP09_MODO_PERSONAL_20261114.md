# NAP09 — Cierre técnico del modo personal de NAP (14/11/2026)

**Decisión del propietario:** aplicación personal. No cuentas nuevas de Windows, servicios, instalación en Program Files, permisos especiales ni redespliegue empresarial. No reprogramar NAP ni los 39 retratos.

## Qué se ha implementado

1. `AutomationHostLaunchConfiguration.PersonalMode`: opción explícita; nunca combina auditoría sintética ni inyección de fallos con el procesamiento personal.
2. `AutomationHost`: conserva el modo TEMP que ya existía para pruebas; permite carpetas reales de Nimroel/Star Trek en modo personal, desde un Host local disjunto de esas carpetas. Las políticas aprobadas, el procesamiento, control de trabajos, mutex y registro de resultados existentes se reutilizan intactos. El Host comienza pausado, no procesa archivos por sorpresa.
3. `NAP.AutomationHost --personal-config`: misma aplicación, mismo usuario Windows, mismo IPC `CurrentUserOnly` autenticado. No hay servicio adicional.
4. `ManagedAutomationHost`: la misma WPF arranca o recupera su Host personal cuando existe configuración en `%LOCALAPPDATA%\NAP\Host\launch.json`. Cerrar WPF no mata deliberadamente el Host que procesa un trabajo.
5. `RemoteDesktopService`: combina el catálogo nativo con los retratos históricos, sus vistas previas y estadísticas, evitando que el cambio a Host oculte los 39 WebP y el índice de PNG maestros.
6. `NAP09.ActivarHostPersonal.ps1`: incluido dentro del **instalador normal NAP 2.0.3** (subcarpeta `updater`). Lee `%LOCALAPPDATA%\NAP\settings.json`, respeta carpetas existentes, localiza perfiles incluidos o importados y prepara la bandeja común `Documentos\NAP\Entrada`. No inventa Star Trek si todavía no está configurado. Sin `-Aplicar`, solo muestra; con `-Aplicar`, crea el archivo de conexión una única vez y no sobrescribe configuraciones previas.
7. `NAP06` vuelve a empaquetar la misma aplicación personal para Windows x64, versión 2.0.3; no hay un segundo instalador ni cuenta de servicio.

## Qué queda fuera

No se han ejecutado comandos en el MSI del propietario. Hasta instalar 2.0.3, aplicar la configuración y ejecutar un pequeño procesamiento real bajo sus permisos, **no declarar NAP09 físicamente terminada**. Los 39 retratos históricos ya incorporados no se modifican.

Es necesario que cada universo tenga ya su carpeta de trabajo, producción, archivo y perfil correctamente configurados. La bandeja mixta reconoce paquetes por su manifiesto, no inventa identidades ni modifica ZIP de origen. Gemini solamente actúa si está configurado y la política correspondiente está autorizada.

## Uso en el MSI una vez disponible el instalador

- Cerrar NAP, instalar **NAP-Setup-2.0.3.exe** desde el artefacto de GitHub Actions NAP09 (es actualización del instalador per-user habitual).
- Ejecutar una sola vez en PowerShell **normal, sin administrador**:
  ```powershell
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\NAP\updater\NAP09.ActivarHostPersonal.ps1" -Aplicar
  ```
- Abrir NAP. El Host nace pausado. Activar un universo y su política desde la propia interfaz cuando se quiera procesar.
- Colocar un único ZIP real en la bandeja común y comprobar su aparición en la cola y el resultado, sin volver a importar históricos.
- Si el asistente informa que ya existe launch.json, no lo modifica por sorpresa: revisar de forma puntual ese archivo para distinguir configuración antigua de nueva.

**No volver al despliegue multicuenta.** La complejidad técnica debe ser proporcionada a una herramienta personal.
