# NAP 2.0 — P6.1 / P6.2, revisión visual 1

Fecha: 21-10-2026. Rama: `codex/nap-2-fase-6-ux`.
Estado: implementación para revisión; aceptación visual y funcional del responsable pendiente. No autoriza P6.3 ni CUTOVER.

## Auditoría previa

- HEAD real de main contrastado mediante API y `git ls-remote`: `5f81950275098881fd0befac0a13cde08c9d751f`. Es el merge de PR #48, integrado el 21-10-2026 a las 08:09:16 UTC. Padre de Fase 5: `d83c6036ddf8ca45a75bb371e1dec051829005cb`. Checkout limpio; rama nueva creada sobre ese SHA sin modificar main.
- Leídos Fase 0 (especialmente sección 8, Fase 6 y 11.8), aceptación y auditoría final de Fase 5, fuente de Shell/ViewModels/vistas/RobStyle, host, protocolo, controles, cola y decisiones excepcionales. La decisión posterior en #49 y el commit de integración prevalecen sobre los párrafos históricos que aún describen la PR Draft.
- #47 permanece abierta y bloquea despliegue real/CUTOVER. #49 permanece abierta: timeout IPC, StorageUnavailable histórico y cinco advisories ImageSharp no resueltos. No se alteran sus evidencias ni se investigan mediante nuevas cargas.
- Main informa `protected=true`. Ruleset activo `25531793`, «Protección de main»: aplica a rama por defecto, exige PR (0 revisiones obligatorias), impide deletion y non-fast-forward, sin bypass actors ni status checks requeridos declarados. El endpoint clásico de branch protection devuelve 403 por permisos de la integración; no se infiere ausencia de protección de ese error.
- `.github/workflows/ci.yml`: push a main y PR hacia main ejecutan regresiones generales y WPF/coordinación. No se activan. `cutover-laboratory.yml` solo por dispatch y booleano explícito, no ejecutado.

## Reutilización y alcance

Se conservan paleta petróleo/hielo, RobTitledFrame, radios, tipografía, botones, foco y navegación MVVM RobStyle. Se reutilizan cola, policies, controles y fronteras de operaciones manuales del host. No se cambia motor, normalización, auditor IA, receipts, permisos, ownership, límites ni validación por bytes.

La nueva operación de lectura `Overview` se añade al final del enum de protocolo. El host devuelve un resumen validado y acotado del universo solicitado, su policy/control y un snapshot del ámbito del host. No mantiene contadores persistentes alternativos.

La lectura de la cola usa un único snapshot SQLite de solo lectura y conserva sus comprobaciones existentes. Agrupación por estados:

| Contador | Estados durables |
| --- | --- |
| Pendientes | Observed, WaitingStable, Queued, RetryScheduled |
| Procesando | Running |
| Completados | Completed |
| Necesitan revisión | NeedsReview, Rejected, MissingSource |
| Duplicados (contrato, sin tarjeta adicional) | Duplicate |

Son elementos únicos durables de todo el universo, sin limitar a lote, día ni primera página. Observaciones repetidas no multiplican contadores. Los sondeos de archivos aún no admitidos del host no se presentan como elementos de cola; cuando existen, el texto indica «Comprobando archivos», sin afirmar «Todo al día». Processing cuenta Running durable, incluso durante suspensión; el texto de pausa tiene prioridad. No hay porcentaje estimado ni se contabilizan duplicados como Completed.

## Inicio y navegación

Cinco accesos principales: Inicio, Catálogo, Producción, Historial y Ajustes. Herramientas agrupa Objetivos, Estadísticas, Backups, Git y RoDo, con selección visible al abrir una herramienta. Las funciones manuales originales permanecen accesibles mediante sus pantallas existentes.

Inicio muestra cuatro contadores y estado natural. Solo cuando existen casos que revisar aparece el acceso desplegable a las ocho incidencias más recientes por orden de ingreso; si hay más, indica cuántas quedan fuera de esta vista breve. Muestra nombre de archivo, problema y fecha, sin rutas internas, IDs, códigos, logs ni acciones de aprobación. La lista completa y la resolución corresponden a los incrementos posteriores. Este acceso inicial no sustituye el flujo de P6.4.

Los contadores corresponden al universo seleccionado en la cabecera. «En marcha: N universos» cuenta los scopes que el host informa Running/Pausing/Stopping; no cambia por seleccionar otro universo. Pausar/Reanudar afecta únicamente al seleccionado y no pide confirmación rutinaria. El host revalida siempre la orden. Activación inicial y parada de todos los universos se conservan en Ajustes, con sus confirmaciones existentes por alcance/autoridad.

El refresco, cada cinco segundos y sin consultas solapadas, pertenece a la ventana y sigue al navegar o minimizar a bandeja. Nunca inicia ni cancela el trabajo del host. Cambiar contexto borra inmediatamente las proyecciones, cancela la lectura anterior, invalida respuestas anteriores y bloquea controles obsoletos. Cada consulta visual tiene un plazo de 20 segundos; no se amplía ningún timeout del protocolo ni se reintentan mutaciones. Esto no demuestra una solución del incidente IPC de #49. Una consulta fallida deja `—` y acciones deshabilitadas, sin fingir éxito ni recurrir a escrituras locales.

### Arranque ordinario frente a laboratorio

`App.OnStartup` conserva dos composiciones explícitas. El arranque ordinario usa los servicios manuales originales; no conecta ni instala AutomationHost ni convierte configuraciones reales en laboratorio. Inicio indica «Automatización sin configurar» y no usa sus métricas manuales como contadores automáticos. El read model local previo se conserva por compatibilidad de los adaptadores manuales; sus mensajes técnicos no se muestran en Inicio.

Solo `--automation-config=…` usa el cliente autenticado y servicios remotos. El host de Fase 5 sigue limitado a raíces `nap-host-*` bajo TEMP. No se elimina esa restricción para dar apariencia de automatización normal. La composición productiva y el arranque administrado definitivos siguen bloqueados por #47; es una limitación real pendiente de CUTOVER, no resuelta por esta UI.

Core dispone de `ProposeException`, decisiones persistidas, preview/regla efectiva y reanudación verificable. El puente de escritorio aún no expone íntegramente ese flujo durable de cola. Los comandos manuales de normalización existentes no equivalen a resolver una incidencia de automatización. P6.4 deberá exponer los contratos correctos antes de habilitar acciones; no se añaden botones ficticios ni se cambia WARNING/FAIL a PASS.

## Verificación acotada

- XML/XAML bien formado y `git diff --check` locales.
- Build local intentado con SDK .NET 8.0.426: bloqueado por ausencia de licencia en el target oficial de ImageSharp. No se altera esa validación ni se lee/provisiona una licencia local. Persisten cinco avisos NU1902/NU1903; no se declaran corregidos.
- Workflow específico `phase6-review.yml`: exclusivamente rama de Fase 6, paths de producto/tests focalizados y dispatch explícito. Reutiliza el helper privado existente para validación oficial en runner desechable; cleanup antes de artefactos. Solo ejecuta filtro `Phase6Overview` y el caso existente de navegación sin configuración: proyecciones, metadatos durables y endpoint autenticado contra host vacío, sin activar políticas ni ejecutar assets. No ejecuta suites generales, pruebas de volumen, procesamiento ZIP, benchmark ni CUTOVER. Abrir esta rama no autoriza abrir PR.
- La vista previa `--phase6-preview=active|paused|review|offline|local --phase6-output=<directorio>` construye datos en memoria con adaptadores sin escritor y sin conectar al host, leer settings locales ni crear/procesar ZIP. Renderiza la misma MainWindow y DashboardView de producto mediante el árbol visual WPF y cierra. El layout fuera de pantalla fija el área cliente lógica para independizarla del escritorio pequeño del runner; no es una captura del MSI ni una maqueta HTML. Título explícito «Vista previa sintética P6.2». `--compact` comprueba 1200×700; tamaño habitual 1440×900. Solo escribe las capturas en el destino explícito.

Los resultados y las capturas definitivas se documentan en la evidencia de revisión. Ningún test WPF ni renderizado constituye aprobación del responsable en el MSI.

## Pendiente tras esta revisión

P6.3–P6.8 no desarrollados. Producción/Historial y funciones manuales conservan sus vistas previas; su simplificación visual está pendiente de P6.3. Diagnóstico, licencias y simulación histórica Treskal también quedan pendientes. No se han tocado retratos reales, otros repositorios, arranques/servicios/cuentas/ACL del MSI ni producción.

Se detiene el desarrollo de nuevas pantallas principales en este punto hasta aprobación visual y funcional explícita del responsable.

## Evidencia definitiva de revisión

**Fuente de producto verificada:** `f7b93f9e7f29aef41cdc9be89d3838cab513777b`. [Workflow Windows 39668852346](https://github.com/robdor80/NAP/actions/runs/39668852346): ambos builds Release correctos, validación oficial conservada y cleanup satisfactorio; **25/25 pruebas dirigidas Passed, cero omisiones**. Los diez estados se comprueban mediante registros de metadatos, nunca generando ZIP. Un caso abre un host sintético vacío y el canal autenticado, sin habilitar políticas ni ejecutar assets. Se comprueban hashes de cola sin cambios y ausencia de órdenes durables para `Overview`.

Seis procesos WPF renderizaron la vista de producto con datos sintéticos y salieron con código 0; cada captura falla ante cualquier error de binding. Se inspeccionaron las seis imágenes, incluidas revisión compacta, pausa, desconexión y arranque local. Se corrigió el tamaño del renderizado para independizar el área cliente lógica del escritorio del runner. Se conservan colores/componentes RobStyle; no se añadieron tarjetas para rellenar espacio ni controles de incidencias vacías.

[Galería de seis renderizados WPF y reproducción en Windows](phase6/README.md). [TRX original](phase6/phase6-focused.trx). [Manifiesto y resultados individuales](phase6/evidence.json). El SHA256 del ZIP descargado coincide con el digest declarado por GitHub: `2063cac7738c3ce1ea7c3c8380c531f3c98be8f734eee75d28ac999158cc34b2`. También se contrastaron los seis hashes PNG y el SHA de fuente del artefacto.

Los cinco advisories NU1902/NU1903 siguen presentes (diez líneas de warning por repetición entre restore/build en cada invocación), sin supresiones ni atribución de resolución a esta fase. No se publicaron licencias, claves ni launch configs. Las capturas no contienen raíces reales ni datos privados.

Se conserva la cronología: [39667028224](https://github.com/robdor80/NAP/actions/runs/39667028224) falló en compilación del preview; [39667342046](https://github.com/robdor80/NAP/actions/runs/39667342046) falló por ambigüedad de constructores al compilar pruebas. Ambos quedan Failed y no ejecutaron pruebas. [39667646160](https://github.com/robdor80/NAP/actions/runs/39667646160) y [39668333961](https://github.com/robdor80/NAP/actions/runs/39668333961) pasaron 24 pruebas sobre sus propios SHAs y produjeron imágenes preliminares. No se atribuyen esos resultados a la fuente definitiva ni se modifican evidencias anteriores.

Los commits documentales posteriores conservan el producto del SHA verificado; no reciben una nueva ejecución ni heredan checks como si se hubieran ejecutado sobre ellos. El cierre documental solo publica esta evidencia en la rama de trabajo. No se crea PR, fusiona ni cambia main.

**PUNTO DE REVISIÓN VISUAL 1: DETENIDO.** Pendiente de aprobación visual y funcional explícita del responsable en su MSI para continuar con P6.3. Esta entrega no declara aprobada la interfaz ni terminada la Fase 6.
