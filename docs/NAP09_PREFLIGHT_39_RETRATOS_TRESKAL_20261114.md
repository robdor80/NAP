# NAP09 — Preflight y adopción controlada de 39 retratos históricos de Treskal

**Fecha:** 2026-11-14  
**Estado:** IMPORTACIÓN HISTÓRICA Y VINCULACIÓN DE 39 MAESTROS ACREDITADAS EN NAP 2.0.2; CUTOVER real independiente y pendiente.  
**Repositorio:** robdor80/NAP; rama separada desde `main@3edf82cd5a8035389111108a022c164eafec68d1`.  
**Equipo de comprobación:** MSI Raider GE78 HX / Windows 11 x64 / NAP instalado 2.0.2 según acta NAP08.

## 1. Lo que YA existe y lo que falta verificar

- El cierre del 13/11 documenta 39 WebP históricos auditados, 39 PNG maestros locales conciliados y 0 incidencias en los WebP, **no** 39 nuevos trabajos de la cadena productiva.
- El contrato histórico mantiene `AssetCatalog.db` (producción nativa), `LegacyPortraits.db` (índice del legado) y `LegacyMasterEvidence.db` (huellas de PNG) separados. No adjudicar un PASS Gemini ni registrar publicaciones nativas retroactivas.
- Para PNG: 14 SHA anteriores coincidentes, 1 caso `portrait_treskal_farmer_female_001` coincidente con contenido Git histórico, 24 SHA locales sin comparación remota; TeraBox remoto sin acreditación de restauración.
- En NAP08 la instalación 2.0.2 mostró la UI y el catálogo real de Nimroel; **no** se demostró que el MSI instalado muestre ya los 39 retratos en una misma sesión.
- La PR NAP08 #72 está Draft; acta NAP08 admite PASS físico de varias rutas, pero conserva excepciones y niega certificación integral. #47/#49 abiertas.
- La apertura de esta fase **no autoriza** activar Host real, conceder permisos productivos, migrar rutas, escribir en assets reales ni procesar 39 ZIP nuevos.

## 2. Primera intervención GUIADA en el MSI (sin cambios)

1. Abrir el acceso directo normal **NAP 2.0.2** (no el lanzador de Host TEMP).
2. En **Inicio**, anotar versión y estado de automatización; no pulsar Iniciar/Procesar pendientes ni cambiar configuración.
3. Entrar en **Catálogo** y seleccionar Nimroel. Registrar el contador histórico total y, si es posible, captura de pantalla sin rutas o secretos.
4. Si hay retratos: verificar de forma visual que aparecen en la galería y que un registro muestra procedencia histórica. No volver a importar de inmediato.
5. Si el contador es 0, o solo se ve el mapa de Treskal, **detener el procedimiento**, registrar evidencia y revisar bases/rutas de trabajo **en lectura** antes de usar cualquier botón «Importar retratos históricos al catálogo» o «Vincular 39 PNG maestros».
6. Confirmar en pantalla que ningún trabajo está corriendo. No emplear el Host TEMP para producción.

## 3. Puertas antes de una importación histórica

- Demostrar el estado actual de catálogo, Workspace y sus tres bases, preservando configuración existente.
- Realizar un backup externo realmente restaurable de catálogo, historial, ZIP/PNG maestros y configuración antes de habilitar escrituras, y verificar evidencia. No usar TeraBox como verificado por la sola existencia de una carpeta sincronizada.
- Verificar la carpeta de assets históricos y, cuando proceda, usar exclusivamente el botón de importación histórica en Workspace autorizado, nunca un lote de ZIP de 39 assets ficticiamente nuevos.
- La opción **Vincular 39 PNG maestros** requiere el manifiesto exacto `MAESTROS_39_CONCILIACION_20261113.json` y la carpeta local `Videojuego Nimroel - ASSETS MAESTROS PNG`; el programa compara identidad, PNG, dimensiones y SHA-256 y debe hacer STOP si divergen. Si la evidencia ya está registrada, comprobar idempotencia antes de reimportar.
- La eventual importación de índice histórico y el eventual uso productivo son **decisiones independientes**. Un índice de solo consulta no es autorización de procesamiento vivo.

## 4. Barreras del CUTOVER real

Previas y obligatorias según el plan maestro y #47: inventario de escritores antiguos/procesos/tareas, identidad y exclusión escritora única, evaluación de los 6.864 leases históricos (8 no vacíos), raíces y permisos de padres, binarios/configuración protegidos, backup externo restaurable, decisión explícita sobre frontera de datos, procedimiento de parada en frío y reversión sin ejecutar binarios antiguos en caliente.

No instalar cuentas, servicios, ACL ni migrar Documents/GitHub por inferencia. El propietario debe **aprobar por separado** cualquier intervención de instalación, identidad, permisos, migración o primera escritura sobre raíces reales. Mientras falten garantías, salida obligatoria: **CUTOVER BLOQUEADO** y producción sin activar.

## 5. Registro de ejecución (pendiente de evidencia del MSI)

| Puerta | Resultado | Evidencia |
|---|---|---|
| NAP 2.0.2 inicia desde acceso directo normal | PENDIENTE DE ESTA INTERVENCIÓN | — |
| Catálogo Nimroel y recuento visible de históricos | PENDIENTE | — |
| Fichas e identidad de los 39 WebP | PENDIENTE | — |
| Presencia/consistencia de `LegacyPortraits.db` | PENDIENTE | — |
| Presencia/consistencia de `LegacyMasterEvidence.db` | PENDIENTE | — |
| Backup externo y recuperación verificable | PENDIENTE | — |
| Inventario escritores, ACL/parents, leases, identidad | PENDIENTE | — |
| Autorización individual del CUTOVER | NO OTORGADA | — |
| Primera escritura productiva | NO EJECUTADA | — |

## Fuentes contractuales

- [Plan maestro ampliado NAP01–NAP09](NAP_PLAN_MAESTRO_AMPLIADO_SIN_CODEX_20261113.md).
- [Integración de históricos](NAP_2_0_HISTORICOS_CATALOGO_UNIFICADO.md).
- [Cierre local de 39 maestros](NAP_2_0_CIERRE_39_MAESTROS_20261113.md).
- [Contrato CUTOVER](NAP_2_0_FASE_4_CUTOVER.md).
- [Incidencia bloqueante #47](https://github.com/robdor80/NAP/issues/47).
- [Acta física NAP08 (rama PR #72)](https://github.com/robdor80/NAP/blob/feature/nap08-certificacion-fisica-msi-20261113/docs/NAP08_ACTA_FISICA_MSI_20261114.md).


## 6. Avance físico comunicado por el propietario — 14/11/2026, madrugada

La comprobación guiada de NAP 2.0.2 desde el acceso directo normal ha aportado evidencia directa de los siguientes hechos:

- Ajustes del universo Nimroel: Workspace `C:\Users\andro\Documents\NAP\Nimroel`, Production `C:\Users\andro\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets` y Archive `C:\Users\andro\Documents\Nimroel_Maestros_PNG`, marcadas válidas por la aplicación.
- Antes de importar, `state/AssetCatalog.db` existía (139264 bytes); `LegacyPortraits.db` y `LegacyMasterEvidence.db` no existían.
- En el repositorio de producción local se contaron 39 WebP, 39 prompts, 39 fichas informativas y 39 JSON de identidad visual: 156 archivos presentes. Este recuento no sustituye un nuevo hash de integridad.
- NAP se cerró y se copió `AssetCatalog.db` a `C:\Users\andro\Downloads\NAP09_Backup_20261114_015053`; el SHA-256 del original y de la copia coincidieron. Es una **copia local puntual**, no prueba de restauración externa integral.
- El usuario ejecutó la importación histórica desde Catálogo. La captura posterior muestra el mapa previo y retratos históricos (herreros, carpinteros y personaje infantil) renderizados en la galería. **Importación visible y funcional**; no se debe volver a pedir la misma demostración.
- No hay evidencia directa del número exacto indexado tras la importación ni de vinculación de las huellas PNG maestras. Estas comprobaciones solo se reabrirán si aparece un fallo operativo real, no como barrera rutinaria para ver las imágenes.
- No se han acreditado ni autorizado activación de AutomationHost en raíces reales, reconfiguración de permisos/identidades, migración ni CUTOVER completo. #47 permanece abierta.

### Próxima acción útil, sin repetición de pruebas

El paso funcional siguiente para enriquecer el legado ya visible es **«Vincular 39 PNG maestros»**, que exige el manifiesto de conciliación `MAESTROS_39_CONCILIACION_20261113.json` y la carpeta local descargada `Videojuego Nimroel - ASSETS MAESTROS PNG`. No confundir este vínculo con importación de WebP ni con habilitar procesamiento automático. No declarar la operación completada hasta ejecutarla realmente.

**Acuerdo operativo de esta intervención:** no pedir nuevas capturas de fichas, recuentos ni series de comandos sin necesidad concreta. Registrar como avance la importación visible; mantener las barreras de CUTOVER productivo como asunto distinto.


## 7. Vinculación de 39 PNG maestros — PASS físico (14/11/2026, 02:00)

**Evidencia:** captura aportada por el propietario de NAP 2.0.2 en el MSI, diálogo `NAP — Maestros históricos`:
> 39 evidencias de PNG históricos incorporadas a NAP. 14 coincidencias con SHA histórico; 1 coincidencia con PNG de Git histórico documentada; 24 líneas base nuevas. Los archivos originales y AssetCatalog.db no se han modificado. TeraBox remoto NO ha sido verificado por API.

El resultado acredita el éxito de la **importación de evidencia histórica** desde el manifiesto de conciliación en NAP 2.0.2. El catálogo WebP había sido incorporado previamente y se veía en pantalla. No pedir duplicar estas acciones ni repetir pruebas manuales sin incidencia concreta. No elevar este hecho a certificación CUTOVER: faltan garantías distintas de autoridad escritora, integridad de respaldo externo y despliegue. La base `LegacyMasterEvidence.db` es el destino esperado según el contrato y la interfaz confirmó importación; no se ha hecho inspección forense separada de ese fichero.
