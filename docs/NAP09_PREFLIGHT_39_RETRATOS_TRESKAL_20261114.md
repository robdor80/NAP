# NAP09 — Preflight y adopción controlada de 39 retratos históricos de Treskal

**Fecha:** 2026-11-14  
**Estado:** INICIADA (solo preparación documental y comprobación humana guiada, sin CUTOVER).  
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
