# NAP 2.0 — P6.3 / P6.4, revisión visual 2

Inicio de trabajo: 22-10-2026. Rama existente: `codex/nap-2-fase-6-ux`, base `cf1011a`. Main verificada en `5f81950275098881fd0befac0a13cde08c9d751f`, sin cambios.

El responsable aprobó expresamente los seis modelos sintéticos de P6.1/P6.2 y autorizó P6.3/P6.4 en su instrucción del 22-10-2026. Es aprobación de diseño y navegación, no certificación operativa del MSI ni CUTOVER. La evidencia anterior se conserva intacta.

## Alcance

Producción e Historial proyectan cola y eventos durables mediante páginas y filtros; el detalle se lee bajo demanda. Las operaciones manuales originales siguen en secciones secundarias. No hay una segunda autoridad de escritura.

Ajustes aprobados: «Minimizar a bandeja» conserva el mismo handler; los controles de pausa/reanudación nombran el universo seleccionado; Herramientas mantiene la navegación principal fija y dispone de desplazamiento independiente y foco de teclado en tamaño compacto.

## Contratos e integración

Se añaden operaciones tipadas de lectura `WorkflowPage`, `WorkflowDetail`, `WorkflowBatches` y mutaciones `ReviewPropose`, `ReviewApprove`, `ReviewContinue` al final del enum de protocolo. Las lecturas no se registran como órdenes ni reparan trabajos. Páginas de hasta 40 registros (UI: 20), búsqueda de hasta 80 caracteres, filtros por estado/lote, eventos de 20 en 20.

`AutomationHost.RunReviewAsync` usa el presupuesto pesado y el hilo propietario del universo ya existentes. Revalida grant, sesión, inhibiciones, autorización y política exacta del item. No exige pausar todo el universo para revisar un asset aparcado ni cambia controles de otros assets/universos. La continuación sí requiere ese universo reanudado y el asset sin inhibición. No se admiten delegados ni rutas suministradas por WPF.

Las opciones excepcionales se construyen en Core desde el perfil congelado: misma familia, rol de maestro, archivos y clasificaciones compatibles. Solo se ofrecen las dimensiones del perfil existente y las originales cuando son válidas; no hay entrada libre ni instalación de nuevas reglas. Se preservan límites de área/ejes/píxeles/bytes/memoria y reglas completas; el motor vuelve a comprobar la propuesta.

Se reutilizan `ProposeException`, previews durables, `RecordDecision` y `ResumeException`. Cada elección nueva revoca decisiones anteriores del mismo asset. La aprobación vincula item, revisión, intento, decisión, versión y evidencia exactos; el actor procede del canal autenticado, no de un campo editable de WPF. La continuación es una acción distinta que revalida evidencia/política y conserva el Job. No confiere PASS, publicación ni autoridad global. Los comandos conservan el replay durable y la barrera de resultado incierto del bridge existente; una desconexión no provoca reintento automático de mutaciones.

Las vistas previas son miniaturas en memoria (máximo 256 por eje y 300 KB por imagen), generadas únicamente desde bundles preservados/verificados, con presupuesto previo de decode y sin aceptar rutas arbitrarias. La consulta no crea propuestas ni modifica decisiones. La ausencia de imagen deja visible la incidencia sin habilitar una solución insegura.

## Límites reales

Los perfiles desconocidos o incompatibles rechazados durante admisión carecen del paquete preservado/intentado que exige `ProposeException`. Se explican y permanecen bloqueados, con corrección externa; no se usan los comandos manuales para inyectarlos en una cola existente. Colisiones, duplicados dudosos, originales ausentes/alterados, almacenamiento, autorización y auditorías WARNING/FAIL conservan sus barreras. No existe aprobación superficial de IA ni reanudación universal.

Los journals manuales y recibos Git anteriores no contienen una cronología ni un nombre de asset recuperable en todos los casos: se presentan como operación manual y «Sin fecha registrada», sin fabricar fechas o relaciones. Los recibos originales y backups siguen accesibles mediante sus herramientas. Los detalles de automatización usan las fechas/eventos durables reales.

La conexión real sigue limitada al arranque explícito de laboratorio. La composición ordinaria y el despliegue productivo permanecen pendientes de #47. #49 sigue abierta: IPC intermitente, StorageUnavailable histórico y cinco advisories ImageSharp, sin declarar resueltos.

## Verificación prevista y límites

El workflow de rama fue revisado antes de modificarlo. CI general sigue restringido a push/PR de main y CUTOVER a dispatch explícito; no se activan. Solo el workflow focalizado de esta rama ejecuta las pruebas de Fase 6 y el caso de navegación sin configuración.

Se añade una prueba funcional de un único asset geométrico sintético: admisión, propuesta, evidencia, aprobación y continuación preparatoria por el host. No es una prueba de volumen ZIP; no habilita política productiva, proveedor real, publicación, archivo productivo ni catálogo real. Los demás casos usan metadatos o respuestas en memoria. Se mantienen las exclusiones de volumen; no hay regresiones generales, estrés ni benchmarks.

Los renderizados usan adaptadores en memoria que rechazan toda escritura. Se comprueban Producción, Historial, propuesta, decisión aprobada, auditoría bloqueada, vacío, error de lectura y acceso manual en 1440×900 y 1200×700, además de los ajustes de Inicio. No son capturas del MSI ni equivalen a aceptación humana.

Resultados definitivos, commits y capturas se incorporarán antes de detenerse en el punto de revisión visual 2. P6.5–P6.8 fuera de alcance.
