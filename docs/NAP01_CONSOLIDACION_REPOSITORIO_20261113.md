# NAP01 — Consolidación del repositorio
**Fecha:** 2026-11-13  
**Producto:** NAP 2.0 — Windows 11, C#/.NET 8, WPF/RobStyle  
**Repositorio:** `robdor80/NAP`  
**Base auditada:** `main@9c286c3f87bcc9833f35b4f0af576115df15a7c3`

## 1. Objeto y decisión
Cerrar la dispersión de trabajos antes de afrontar las cinco fases funcionales restantes, sin reconstruir el producto ni confundir una fusión con una instalación operativa. NAP01 es **consolidación de ramas, PR y documentación**, no una nueva versión de ejecución, ni un cambio de motores, ni certificación del MSI.

El producto debe conservar el flujo de gestión de assets: Inbox / admisión → validación y auditoría IA → conversión → publicación/archivo → catálogo SQLite, con separación por universo. Mantener el legado de NAP 1.0 donde corresponda a la compatibilidad funcional de NAP 2.0; no resucitar dos productos.

## 2. Estado comprobado en GitHub antes de NAP01
- Rama principal protegida `main@9c286c3`. Las integraciones recientes incluyen el cierre de 39 maestros históricos / catálogo legado y el perfil `scene_environment` para Nimroel (PR #60).
- Core NAP, AutomationHost, cola durable, admisión Inbox, GeminiOperationAuditClient, ejecución/conversión, SQLite, interfaz WPF/RobStyle y Git configurable existen en el árbol principal. **Existir en el repositorio no certifica que la composición de extremo a extremo esté funcionando en el MSI.**
- GitHub Actions: `ci.yml`, `cutover-laboratory.yml` y `phase6-review.yml` son exclusivamente manuales. `phase6-msi-preparation.yml` también permite `push`, pero solo en `codex/nap-2-fase-6-ux` y con rutas restringidas. NAP01 no activa, modifica ni despacha workflows.
- `#47` sigue abierto: instalación/autoridad escritora/CUTOVER/productivo real no certificados. `#49` sigue abierto: IPC tras cancelación, StorageUnavailable histórico y avisos NU1902/NU1903 ImageSharp. Mantener sus hechos y reservas, sin declararlos arreglados.
- El código actual está en **.NET 8**. NAP01 no realiza migración de framework ni introduce un instalador, un updater o un servicio privilegiado.

## 3. Decisiones sobre PR
| PR | Situación NAP01 | Motivo |
| --- | --- | --- |
| [#58](https://github.com/robdor80/NAP/pull/58) — Laboratorio F8.1 | **Cerrada sin merge** | Prototipo/laboratorio aislado y no certificación física; ruta sustituida por cierre funcional NAP02–06. Historial y rama intactos. |
| [#61](https://github.com/robdor80/NAP/pull/61) — Auditor diversidad, primera variante | **Cerrada sin merge** | Dos implementaciones alternativas solapaban archivos y finalidad; esta variante no tenía compilación real acreditada. Historial y rama intactos. |
| [#62](https://github.com/robdor80/NAP/pull/62) — Auditor diversidad, variante vigente | **Conservar Draft, sin merge en NAP01** | Compilación Release MSI y 111/111 pruebas focalizadas registradas sobre `74bc259`; revisión visual WPF y consulta Gemini real pendientes. No hace falta fusionarla para la consolidación. |

La #62 aporta consulta de diversidad sobre datos agregados locales SQLite y un panel adicional. No sustituye al motor principal de producción ni se debe mezclar automáticamente con la #61. Evaluar la aceptación de pantalla en NAP04.

## 4. Estado de ramas y límites
- `main`: única base de referencia para las fases nuevas.
- `chatgpt/nap-2-f8-safe-msi-lab`: rama histórica retenida, no fusionar por arrastre.
- `feature/nap2-catalog-diversity-ai-20261113`: rama histórica de la #61, no fusionar.
- `feature/nap-auditor-diversidad-visual-20261113`: rama activa de la #62, no rebasar ni fusionar en NAP01.
- `feature/nap-legacy-portrait-library-20261112` y `feature/nap-nimroel-scene-environment-20261113`: ramas de trabajo históricas; su comparación de commits con `main` muestra bifurcación por integraciones posteriores. **No interpretar sus contadores ahead/behind como cambios pendientes para mergear.** Conservar sin manipular; contrastar el árbol solo si surge una necesidad concreta.
- `chatgpt/nap01-consolidacion-repositorio-20261113`: rama documental independiente de NAP01.

No se borran ramas ni evidencia de pruebas. El cierre de una PR no destruye sus commits ni equivale a integrar código.

## 5. Plan de cierre: NAP01–NAP06
Los nombres operativos concretan un plan mínimo y secuencial; no autorizan saltar barreras de producción.

| Fase | Resultado de producto exigido | Fuera de alcance |
| --- | --- | --- |
| **NAP01 — Consolidación del repositorio** | Fuente única (`main`), PR obsoletas cerradas, PR vigente identificada, inventario y continuidad documental. | Refactor global, CI general, instalación. |
| **NAP02 — Host operativo conectado a WPF** | Componer el AutomationHost existente en el arranque normal; control de iniciar/pausar/reanudar y autoridad de ejecución coherente, sin reinventar el motor. | Servicio privilegiado o escrituras productivas sin #47. |
| **NAP03 — Flujo real de procesamiento** | Inbox → validar/auditar con Gemini configurado → convertir → archivar/publicar → SQLite; lotes y progreso, sin requerir acciones técnicas del usuario en cada paso. | TeraBox remoto directo, modelos IA nuevos, alterar contratos de assets. |
| **NAP04 — Interfaz RobStyle definitiva** | Pantalla principal clara, procesamiento minimizable, pendientes/completados/duplicados/revisión y estados comprensibles; decidir la aceptación visual de #62 por separado. | Rediseño integral sin necesidad. |
| **NAP05 — Correcciones y recuperación focalizadas** | Corregir defectos reproducidos durante uso aislado (incluidos los de #49 cuando proceda), recuperación y duplicados; evidencias mínimas sobre rutas sintéticas. | Baterías de estrés, ZIP masivos, certificar como resuelto lo no reproducido. |
| **NAP06 — Release Windows y puesta en servicio** | Compilar distribución x64 definitiva, aceptación visual y funcional sobre MSI; preparar instalación/recuperación y decidir la puerta #47 con evidencia real antes de escrituras productivas. | Actualizador automático, cambios de datos o ACL sin procedimiento y autorización específica. |

Las fases posteriores parten de `main`, en ramas propias y con PR revisables. Priorizar una ruta funcional, evitando ampliar indefinidamente los laboratorios.

## 6. Regla operativa para las siguientes fases
1. No duplicar componentes ya existentes ni incorporar ramas antiguas completas por inercia.
2. Mantener `AssetCatalog.db` y el índice legado `LegacyPortraits.db` con sus procedencias respectivas. La interfaz puede mostrar ambos; no declarar auditable/publicado por NAP lo que es histórico.
3. Las claves Gemini no entran al repositorio; el cliente actual usa configuración externa y la auditoría nunca debe inventar un PASS. Mantener segregación por universo.
4. La publicación real de assets y los cambios de permisos/servicios/arranque del MSI siguen supeditados a la resolución operativa de #47. Las fusiones de código y pruebas sintéticas no levantan esa condición.
5. CI general desactivada: no activarla ni ejecutar suites globales por rutina. Usar verificaciones focalizadas cuando se cambie código, con resultados y límites explícitos.
6. TeraBox es un respaldo gestionado fuera de NAP; no afirmar sincronización nube directa. No incluir actualización automática en este ciclo.

## 7. Verificación y acta de NAP01
**Comprobaciones efectuadas:** lectura directa de repositorio, ramas, PR, árbol de fuentes, directorio de documentación y cuatro YAML de Actions; comparación Git entre cinco ramas de trabajo y `main`; clasificación de archivos superpuestos en las PR #61/#62; cierre de #58 y #61 **sin merge** con comentario justificativo.

**No se ha ejecutado:** `dotnet build`, interfaz WPF, Gemini real, test funcional de Windows, NAP CI, CUTOVER ni procesamiento de assets reales. NAP01 solo altera documentación y metadatos de PR.

**Criterio de cierre:** PR NAP01 documental fusionada a `main`; #58 y #61 cerradas sin merge; #62 abierta Draft; #47 y #49 preservadas. **Conclusión: cierre de consolidación documental, no aceptación funcional ni despliegue.**
