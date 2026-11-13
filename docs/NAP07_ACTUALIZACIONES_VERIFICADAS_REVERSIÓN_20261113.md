# NAP07 — Actualizaciones verificadas y reversión controlada (Windows x64)

Fecha: 2026-11-13. Rama: `feature/nap07-verified-updates-rollback-20261113`. Base: `main@021368061570064668be138e4019bc9f7aac01d0` (NAP06 fusionada). Responsable: ChatGPT, sin Codex.

## 1. Alcance y autoridad

NAP07 incorpora una herramienta PowerShell sin privilegios, distribuida como `updater/NAP07.Update.ps1` en el instalador de NAP06, y acceso de **solo consulta** en Ajustes de WPF. El código conserva el instalador Inno Setup x64 por usuario en `%LOCALAPPDATA%\Programs\NAP` y no edita CoreRPG, maestros, SQLite, settings, journals, perfiles de universos, repos Git ni rutas productivas.

**No se ha publicado ninguna GitHub Release productiva ni existe certificado de firma NAP.** El canal es explícito: `stable` requiere `draft=false`, `prerelease=false`, tag `vX.Y.Z`; `preview` requiere `prerelease=true`. La fuente está fijada a `https://github.com/robdor80/NAP/releases`; el nombre del fichero debe ser exactamente `NAP-Setup-X.Y.Z.exe`. No hay carga de URLs suministradas por metadatos ni repositorios configurables al arbitrio del servidor.

La comprobación en Ajustes abre una ventana PowerShell para **Check**: no descarga ni instala ni desactiva Host. El script permite operaciones explícitas Stage, Apply, Recover y Rollback fuera del proceso WPF. Sin releases aún, Check indicará que no hay una versión candidata verificada.

## 2. Modelo de confianza

- El instalador sigue **sin firma del producto**; la validación del compilador Inno de NAP06 no es una firma de NAP ni elimina avisos SmartScreen.
- **El SHA-256 esperado ha de obtenerse por un canal independiente y fiable** de la misma página de Release, por ejemplo un inventario fijado y verificado fuera del enlace de descarga. Los hashes publicados como assets junto al binario *no prueban por sí mismos* su autenticidad frente a una cuenta de Releases comprometida.
- Stage y Apply exigen `-TrustedSha256`; Apply exige también `-PreviousSha256` y el instalador anterior local. Cualquier hash distinto, firma Authenticode inválida o ejecutable no firmado sin consentimiento `-AllowUnsigned` aborta antes de iniciar el instalador.
- Los metadatos recibidos nunca son argumentos de proceso: URL Github/repo/tag/nombre del asset están compuestos a partir de constantes y versión numérica validada. Los redirects HTTPS de GitHub/CDN no se convierten en confianza; el pin independiente es obligatorio.
- No hay actualización silenciosa ni comando disparado por GitHub Actions en el MSI real. El consentimiento de Apply es `-Confirm APLICAR-X.Y.Z`. Las pruebas de rutas sintéticas e inyección de fallos quedan bloqueadas fuera de Actions.
- El staging y las copias de Setup antiguo se ubican bajo `%LOCALAPPDATA%\NAP\Updates` y rechazan reparse points, instancias simultáneas y hashes inconsistentes.

## 3. Operativa del propietario (cuando exista una Release autorizada)

Abrir WPF → Ajustes → **Consultar actualizaciones (solo lectura)**, o ejecutar:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\NAP\updater\NAP07.Update.ps1" -Action Check -Channel stable
```

Tras verificar por separado el SHA de la nueva Release y el SHA de un **Setup anterior conservado**:

```powershell
# 1. Descarga verificada a staging, sin ejecución.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\NAP\updater\NAP07.Update.ps1" -Action Stage -Channel stable -Version 2.0.1 -TrustedSha256 "<HASH_NUEVO_64_HEX>" -AllowUnsigned

# 2. En WPF: Ajustes > Detener automatización (drena TODOS los universos).
# Comprobar que el Host ha terminado; cerrar NAP completamente, incluida la bandeja.
# 3. Lanzar el actualizador desde su copia externa al directorio de instalación.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\NAP\Updates\tools\NAP07.Update.ps1" -Action Apply -Version 2.0.1 -TrustedSha256 "<HASH_NUEVO_64_HEX>" -PreviousSetup "C:\RUTA_VERIFICADA\NAP-Setup-2.0.0.exe" -PreviousSha256 "<HASH_ANTERIOR_64_HEX>" -Confirm APLICAR-2.0.1 -AllowUnsigned
```

Los placeholders no son hashes reales. No reutilizar el SHA del servidor como único anclaje de confianza. Los comandos fallan si la versión no es superior a la instalada, no está el instalador antiguo o permanecen procesos NAP instalados en ejecución. **Nunca se mata a la fuerza al Host.** Si el drenaje no termina, posponer Apply.

El actualizador crea un journal de transacción: `prepared → applying → committed`; ante fallo durante la aplicación: `recovery_needed → restoring → rolled_back`. Para una interrupción recuperable, cerrar procesos NAP y ejecutar:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\NAP\Updates\tools\NAP07.Update.ps1" -Action Recover -Confirm RECUPERAR-2.0.0 -AllowUnsigned
```

La reversión manual de una instalación **ya confirmada** exige `-Action Rollback -Confirm REVERTIR-2.0.0 -AcknowledgeSchemaRisk -AllowUnsigned` y comprobación humana de compatibilidad de datos antes de abrir la versión antigua. El actualizador **no rebaja schemas SQLite, no restaura snapshots de catálogos ni arranca ninguna versión por sí mismo**. Una reversión de binarios después de que la versión nueva haya corrido puede ser incompatible con sus datos: hacer backup verificable y evaluar antes de autorizarla. En cambio, si la instalación nueva falla y nunca se arranca, el rollback temprano restaura el Setup antiguo verificado.

## 4. Criterios de seguridad de la transacción

1. No se permite cambiar el destino fijo de Inno ni emplear carpetas con junctions/symlinks/reparse.
2. La escritura se serializa con lock local de archivo, los recibos JSON se reemplazan mediante operación de sistema de archivos y un fallo no se considera éxito.
3. Espacio libre reservado conservadoramente antes de staging y aplicación; los errores de descarga y el fallo de disco lleno abortan sin ejecutar el Setup.
4. Se compara versión declarada de **NAP.App.exe y NAP.AutomationHost.exe** después de instalar o recuperar, y se deja constancia de la transacción.
5. No se reabre WPF automáticamente, para evitar migraciones de esquema antes de comprobar la versión y decidir si procede recuperar.
6. No se toca `%LOCALAPPDATA%\NAP` salvo el subárbol exclusivo `Updates`, que no se elimina ni se confunde con los datos históricos del usuario.

## 5. Pruebas y límites

Workflow focalizado: `.github/workflows/nap07-verified-update.yml` (`push` solo a rama NAP07 y `workflow_dispatch`). Compila dos instaladores sintéticos `2.0.0` y `2.0.1` en runner Windows 2022, con Inno oficial verificado y Six Labors mediante secreto existente. Ejecuta `scripts/Test-Nap07Update.ps1` en un usuario TEMP, sin publicar Release ni usar carpetas productivas.

La matriz pretende verificar: instalación N, rechazo de descarga alterada, disco lleno, SHA independiente, consentimiento obligatorio, fallo pre-Setup recuperable, fallo tras instalar con rollback, actualización N+1, gate explícito de esquema para revertir instalación ya confirmada, recuperación a N y conservación de marcador de datos. Registrar resultados concretos de CI antes de cerrar la fase. Un fixture local no prueba ni la disponibilidad real de GitHub Releases ni autenticidad de una futura publicación; requieren prueba separada cuando exista Release.

**NAP08**: certificación Windows 11 física/SmartScreen y experiencia de actualización real; **NAP09/#47**: autorización separada para CUTOVER. Incidencia **#49**: no modificar sin evidencia específica de sus defectos. No activar `NAP CI` general, no ejecutar cargas ZIP masivas y no consumir Codex por defecto.
