# NAP04 — RobStyle, diversidad y laboratorio visual
**Fecha:** 13/11/2026  
**Rama:** `feature/nap04-robstyle-diversidad-lab-20261113`  
**Base auditada:** `main` `092520fab1b633bde0005ebcb5a2244ac541bbb0`

## 1. Inventario y decisiones de diseño
- NAP01, 01-B, 02 y 03 constan integradas; NAP03 PR #67: 85/85 PASS Windows x64 en su validación focalizada anterior. Esto **no** certifica NAP04.
- WPF ya contenía Shell RobStyle, selector de universo, bandeja (tray), fichas, catálogo SQLite de solo lectura, miniaturas diferidas, inspección de detalle y el comparador de normalización original/candidato.
- El AutomationHost de NAP02/03 es el único dueño de la cola. NAP04 **no** crea otro watcher, ejecución paralela, writer de catálogo ni almacenamiento de diversidad.
- Se opta por **ajustes puntuales** de inicio/estados, un auditor reutilizado y un laboratorio modal ligado al catálogo; no por rediseño ni laboratorio permanente.
- `main` y producción no se alteran; el trabajo se conserva en rama independiente. Sin Codex.

## 2. RobStyle / operaciones comprensibles
- Inicio muestra estado propio de los universos **configurados en el Host**, su actividad de admisión y los recuentos ya existentes: pendientes, procesando, completados y en revisión. Los recuentos proceden del snapshot del Host, no se recalculan con otra base.
- Estado de bandeja mixta y paquetes ya presentes, sin presentar un paquete existente como producción nueva.
- Los avisos de cola se mantienen con lenguaje humano: colisión, maestro duplicado, almacenamiento, autorización, etc. Los controles existentes de iniciar, detener universo, pausar, reanudar y procesar pendientes conservan su autoridad y restricciones.
- La minimización a tray y el comportamiento real del Host son previos y se conservan; no se introduce servicio privilegiado.
- El modo WPF sin Host no inventa estados: la sección automatizada permanece vacía/no configurada.

## 3. Auditor C04 y evaluación independiente PR #62
**Veredicto técnico: incorporar los módulos útiles por adaptación en esta rama, no fusionar PR #62.**

Revisión de la PR histórica Draft #62 (base anterior a NAP03):
- Se verificó el diff completo y los cambios sobre App, Shell, Presentation, catálogo y pruebas. Las modificaciones textuales eran aplicables limpiamente sobre la base NAP03. No se empleó el merge de esa rama obsoleta.
- Se recuperaron agregador por universo `CatalogDiversityAnalyzer`, lector SQLite ReadOnly `CatalogDiversityReader`, índice diferenciado de retratos históricos, panel WPF **Herramientas → Auditor IA** y cliente `GeminiCatalogDiversityClient`.
- Los recuentos del informe proceden del catálogo y su índice histórico **sin** alterar sus schemas ni ficheros. Una publicación nativa de la misma identidad prevalece sobre el retrato histórico. Las carencias representan **comparaciones de categorías observadas**, no cuotas obligatorias ni imágenes sobrantes.
- Sólo consulta Gemini bajo acción manual explícita, credenciales `NAP_GEMINI_API_KEY` y `NAP_GEMINI_MODEL`, y sesión local compatible. En sesión remota Host está deshabilitada. El proveedor recibe pregunta del usuario y resumen agregado; no recibe imágenes, SQL, ruta física, prompts almacenados ni documentos de assets. Su respuesta contiene IDs de evidencias contrastados con cifras SQLite. `store=false`, sin herramientas.
- **Hallazgo de seguridad corregido durante la adaptación:** #62 filtraba ciertos rasgos visuales, pero podía conservar valores arbitrarios de clasificación, tipo y perfil en JSON enviado al proveedor. NAP04 aplica también `SafeTraitValue` a esas facetas y acota el tamaño admitido, excluyendo rutas y cadenas con símbolos incompatibles. La reducción sintáctica no sustituye la revisión del usuario antes de enviar consultas, ni garantiza detectar todo secreto incrustado en un identificador corto.
- Se añade regresión con ruta Windows y correo simulados en metadata. Se conservan los tests existentes de C04 sobre brechas relativas, procedencia, cifras/IDs, límites, mocks Gemini y SQLite no mutado.
- El documento histórico `CATALOG_DIVERSITY_AI_V1.md` conserva la trazabilidad de #62; prevalecen las correcciones y los resultados verificables de esta acta.
- **Estado de #62:** sigue Draft sin fusionar; su integración se sustituye por la PR NAP04, condicionada a aceptación independiente.

## 4. Laboratorio visual C05
- Desde **Catálogo**, seleccionar un recurso y pulsar **Fijar referencia**; después seleccionar otro recurso del mismo universo y pulsar **Comparar selección**.
- Se abre una ventana modal RobStyle con dos vistas previas, título/tipo/perfil, desplazamiento y ampliación ajustable, reutilizando `AssetTileViewModel`, `ThumbnailView` y la caché regenerable de miniaturas.
- La referencia se libera al salir del catálogo. Se bloquea el cruce de `UniverseId`; no compara la misma identidad consigo misma. La vista no escribe originales, SQLite, archivos ni estado de publicación.
- Los documentos y procedencia quedan en la ficha existente; la comparación original/candidato del pipeline sigue en `ComparisonWindow`. No se crea un segundo motor de thumbnails ni un laboratorio experimental perpetuo.
- Las imágenes son **miniaturas**, no inspección pixel-perfect de maestros; usar el comparador de normalización existente cuando la cuestión sean bordes generados.

## 5. Pruebas y límites de certificación
- Workflow **exclusivo**: `.github/workflows/nap04-wpf-diversity-validation.yml`, Windows Server 2022 x64, SDK .NET 10 LTS.
- Build Release de solución WPF/Core/AI/Host con la configuración de licencia ya existente y limpieza de secretos; pruebas seleccionadas: `CatalogDiversityTests`, `ProfessionalUiTests`, `AutomationManagedWpfTests`, `AutomationWpfTests`, `VisualExplorerWindowsSmokeTests`, `Nap03AssetFlowTests`, `CatalogExplorerTests`, `LegacyPortraitIndexTests`. TRX y artefacto de ejecución.
- Solo se puede declarar PASS con enlace a una ejecución final concluida correctamente. Ver registro de la PR para el commit certificado.
- Ni esa compilación Windows ni los mocks Gemini equivalen a prueba física, consulta real al proveedor o certificación de CUTOVER.
- `NAP CI` general desactivada; #47, #49 y CUTOVER no se intervienen. No se modifica esquema de SQLite ni se habilita procesamiento sin políticas.

## 6. Guía de validación física MSI — **pendiente de aceptación expresa**
**Entorno:** MSI Raider GE78 HX / Windows 11 x64, una copia en directorio de laboratorio; sin habilitar raíces productivas ni servicio nuevo.

1. En GitHub Desktop, usar la rama/PR NAP04 aprobada técnicamente, compilar Release con .NET 10 y abrir `NAP.App` bajo la configuración sintética/local de pruebas. No sustituir la aplicación productiva ni cambiar permisos.
2. Revisar la legibilidad de Inicio (1440×900 y 1200×700 aproximados, ampliación de pantalla normal), navegación, scroll, controles de Host y estados por universo. Sin Host, los datos de la automatización deben mostrar indisponibilidad, nunca cifras inventadas.
3. Con un Host **sintético** y dos universos configurados, comprobar el cambio Nimroel ↔ Star Trek; comprobar que los estados, incidencias y controles permanecen separados, sin referencias cruzadas.
4. En Catálogo sintético, fijar un asset, seleccionar otro y abrir el laboratorio. Probar zoom, scroll, cierre, ausencia de cambios de catálogo y retorno; intentar comparar uno consigo mismo.
5. En Herramientas → Auditor IA, actualizar recuentos, abrir brechas y advertencias y copiar JSON; comprobar que la ausencia de credenciales deja **Consultar IA** deshabilitado. No enviar a Gemini información real para aprobar la pantalla.
6. Probar minimizar a bandeja y restaurar, revisar que el Host sintético permanece bajo su política; cerrar la aplicación sin tareas de producción.
7. Comunicar resultado expreso, capturas si procede, resolución/escala y cualquier incidencia visual. El autor es la autoridad de aceptación WPF.

**Puerta de merge:** no fusionar NAP04 hasta Windows focalizado PASS, revisión técnica final y confirmación visual explícita del propietario. La PR permanece Draft. NAP08 y NAP09 tendrán sus autorizaciones operativas separadas.
