# NAP 2.0 — P6.3 / P6.4, revisión visual 2

Inicio de trabajo: 22-10-2026. Rama existente: `codex/nap-2-fase-6-ux`, base `cf1011a`. Main verificada en `5f81950275098881fd0befac0a13cde08c9d751f`, sin cambios.

El responsable aprobó expresamente los seis modelos sintéticos de P6.1/P6.2 y autorizó P6.3/P6.4 en su instrucción del 22-10-2026. Es aprobación de diseño y navegación, no certificación operativa del MSI ni CUTOVER. La evidencia anterior se conserva intacta.

## Alcance

Producción e Historial proyectan cola y eventos durables mediante páginas y filtros; el detalle se lee bajo demanda. Las operaciones manuales originales siguen en secciones secundarias. No hay una segunda autoridad de escritura.

Ajustes aprobados: «Minimizar a bandeja» conserva el mismo handler; los controles de pausa/reanudación nombran el universo seleccionado; Herramientas mantiene la navegación principal fija y dispone de desplazamiento independiente y foco de teclado en tamaño compacto.

## Contratos e integración

Se añaden operaciones tipadas de lectura `WorkflowPage`, `WorkflowDetail`, `WorkflowBatches` y mutaciones `ReviewPropose`, `ReviewApprove`, `ReviewContinue` al final del enum de protocolo. Las lecturas no se registran como órdenes ni reparan trabajos. Páginas de hasta 40 registros (UI: 20), búsqueda por nombre de paquete de hasta 80 caracteres, filtros por estado/lote, eventos y versiones de decisiones de 20 en 20. Se ofrecen los 40 lotes más recientes de la cola, sin inicializar el registro del runtime. Un asset reobservado pertenece a cada lote que lo recibió mediante sus observaciones, sin duplicar el trabajo. La búsqueda excluye la ruta de Inbox.

`AutomationHost.RunReviewAsync` usa el presupuesto pesado y el hilo propietario del universo ya existentes. Revalida grant, sesión, inhibiciones, autorización y política exacta del item. No exige pausar todo el universo para revisar un asset aparcado ni cambia controles de otros assets/universos. La continuación sí requiere ese universo reanudado y el asset sin inhibición. No se admiten delegados ni rutas suministradas por WPF.

Las opciones excepcionales se construyen en Core desde el perfil congelado: misma familia, rol de maestro, archivos y clasificaciones compatibles. Solo se ofrecen las dimensiones del perfil existente y las originales cuando son válidas; no hay entrada libre ni instalación de nuevas reglas. Se preservan límites de área/ejes/píxeles/bytes/memoria y reglas completas; el motor vuelve a comprobar la propuesta.

Se reutilizan `ProposeException`, previews durables, `RecordDecision` y `ResumeException`. Cada elección nueva revoca decisiones anteriores del mismo asset. La aprobación vincula item, revisión, intento, decisión, versión y evidencia exactos; el actor procede del canal autenticado, no de un campo editable de WPF. La continuación es una acción distinta que revalida evidencia/política y conserva el Job. No confiere PASS, publicación ni autoridad global. Los comandos conservan el replay durable y la barrera de resultado incierto del bridge existente; una desconexión no provoca reintento automático de mutaciones.

La lectura de detalles reutiliza `QueryHeavyAsync` y el mismo presupuesto pesado del host. Las vistas previas son miniaturas en memoria (máximo 256 por eje y 300 KB por imagen), generadas únicamente desde bundles preservados/verificados, con presupuesto previo de decode y sin aceptar rutas arbitrarias. La consulta no crea propuestas ni modifica decisiones. La ausencia o alteración de la imagen deja visible la incidencia con un aviso breve, sin habilitar una solución insegura. El perfil original y el propuesto, las dimensiones originales/lienzo/salida y los bordes se conservan en la presentación y en la confirmación individual.

## Límites reales

Los perfiles desconocidos o incompatibles rechazados durante admisión carecen del paquete preservado/intentado que exige `ProposeException`. Se explican y permanecen bloqueados, con corrección externa; no se usan los comandos manuales para inyectarlos en una cola existente. Colisiones, duplicados dudosos, originales ausentes/alterados, almacenamiento, autorización y auditorías WARNING/FAIL conservan sus barreras. No existe aprobación superficial de IA ni reanudación universal.

Los journals manuales y recibos Git anteriores no contienen una cronología ni un nombre de asset recuperable en todos los casos: se presentan como operación manual y «Sin fecha registrada», sin fabricar fechas o relaciones. Los recibos originales y backups siguen accesibles mediante sus herramientas. Los detalles de automatización usan las fechas/eventos durables reales.

La conexión real sigue limitada al arranque explícito de laboratorio. La composición ordinaria y el despliegue productivo permanecen pendientes de #47. #49 sigue abierta: IPC intermitente, StorageUnavailable histórico y cinco advisories ImageSharp, sin declarar resueltos.

## Verificación focalizada y límites

El workflow de rama fue revisado antes de modificarlo. CI general sigue restringido a push/PR de main y CUTOVER a dispatch explícito; no se activan. Solo el workflow focalizado de esta rama ejecuta las pruebas de Fase 6 y el caso de navegación sin configuración.

Se añade una prueba funcional de un único asset geométrico sintético: admisión, propuesta, evidencia, aprobación y continuación preparatoria por el host. No es una prueba de volumen ZIP; no habilita política productiva, proveedor real, publicación, archivo productivo ni catálogo real. Los demás casos usan metadatos o respuestas en memoria. Se mantienen las exclusiones de volumen; no hay regresiones generales, estrés ni benchmarks.

Los renderizados usan adaptadores en memoria que rechazan toda escritura. Se comprueban Producción, Historial, selección compatible, propuesta, decisión aprobada, auditoría bloqueada, vacío, error de lectura y acceso manual en 1440×900 y 1200×700, además de los ajustes de Inicio. Las capturas adicionales de la zona inferior conservan la comparación, las acciones y la actividad tras desplazarse. Inicio compacto añade evidencia de la última opción de Herramientas dentro de un viewport acotado. No son capturas del MSI ni equivalen a aceptación humana.

## Uso y continuidad

Producción e Historial muestran nombre, universo, estado, resultado y fecha. La selección abre el detalle sin bloquear WPF. Filtros aplicables con Enter o Filtrar, lotes recientes, páginas anterior/siguiente y actividad durable bajo demanda. Inicio enlaza la incidencia exacta incluso fuera de la página visible. Esc o Cerrar detalle vuelve a la lista. El refresco de cinco segundos actualiza la lista abierta sin cambiar su página y se detiene sobre un detalle seleccionado para conservar la elección humana. No cambia la ejecución del host al navegar.

Al cambiar de universo se cancelan las lecturas y se invalidan sus respuestas y confirmaciones anteriores. Las lecturas tienen un máximo de 20 segundos, incluida la navegación directa desde Inicio. Una desconexión borra acciones obsoletas; una mutación con resultado incierto exige consultar de nuevo y no se reintenta automáticamente.

La corrección sigue: seleccionar asset → elegir una opción compatible → preparar y revisar vista previa → aprobar explícitamente solo ese asset → continuar. Aprobar no continúa ni publica por sí solo. No hay propuesta de regla reutilizable ni modificación de políticas globales: el contrato disponible en este puente se limita a `SingleAsset`.

Preparación, adaptación manual, auditoría y publicación anteriores siguen en Trabajo manual. Consulta de recibos en Git; backups en Herramientas. Las operaciones manuales conservan sus adaptadores, confirmaciones, coordinación y comprobaciones originales; no se utilizan para resolver trabajos de la cola. En arranque ordinario se muestra la desconexión de automatización y se mantiene el acceso manual. No se crea un fallback local para una orden remota fallida.

En 1200×700, lista y detalle se desplazan de forma independiente; algunos controles y la comparación requieren desplazamiento. La plantilla local de Herramientas usa filas acotadas para que el ScrollViewer reciba una altura real, conservando la plantilla RobStyle de las demás vistas. El foco y los controles de teclado existentes se conservan; su aceptación ergonómica en el MSI queda pendiente.

## Riesgos y pendientes de aceptación

La prueba preparatoria de un único asset verifica el protocolo autenticado y los registros reales con datos sintéticos; no certifica el proveedor de IA, producción, publicación, recuperación integral ni el MSI. Las pantallas sintetizadas usan imágenes ilustrativas y respuestas en memoria; no se presentan como el resultado operativo de ese asset de prueba.

Sin paquete preservado o evidencia válida no se ofrece corrección. No hay selector libre ni tratamiento automático de perfiles desconocidos, auditorías bloqueadas, duplicados, almacenamiento o autorización. Estos casos requieren la corrección permitida fuera de este flujo y/o autorización válida, sin afirmar que se han resuelto.

#47 y #49 permanecen abiertas. P6.5–P6.8 no se han desarrollado. La validación funcional y visual del responsable en el MSI es independiente de esta evidencia sintética.

## Entrega verificable

**Fuente de producto verificada: `22134f4059cadfd7d31e2a222dbd985086b0854b`.** [Ejecución final Windows 39701832746](https://github.com/robdor80/NAP/actions/runs/39701832746), job `119572634265`: builds Release de App WPF y ensamblado de pruebas correctos; **33 ejecutadas, 33 Passed, 0 fallos y 0 omitidas**. Validación oficial y cleanup existentes conservados. No se ejecutó la suite general ni un ensayo de volumen.

Cobertura focalizada: consultas autenticadas y acotadas por universo, filtros por estado/nombre/lote, reobservación del mismo asset en dos lotes sin duplicar trabajo, lectura sin modificar bytes de cola ni inicializar runtime, detalle/eventos, decisiones durables y revocación de propuestas anteriores, replay sin duplicación, vinculación exacta de evidencia/versión/revisión/confirmación, bloqueo de autorización revocada, original alterado y WARNING/FAIL. El único asset funcional sintético continúa preparatoriamente con su Job original, sin PASS ni publicación y sin cambiar política/perfil global/control del universo. ViewModels verifican contexto, respuestas tardías, confirmación al cambiar universo, conexión fallida, resultado incierto y conservación de elecciones frente al sondeo.

Veinte procesos WPF generaron **29 capturas**: diez estados en dos tamaños, ocho vistas inferiores adicionales y una de Herramientas compactas desplazadas hasta RoDo. Cero errores de binding; comprobaciones de previsualización, ausencia de aprobación sobre FAIL, viewport acotado y desplazamiento efectivo. Se inspeccionaron visualmente las vistas representativas habituales y compactas, incluyendo comparación/aprobación, continuación, actividad, vacío, error, herramientas y acceso manual. En compacto se necesita desplazamiento para llegar a algunas imágenes y acciones; no se presenta como aceptación ergonómica del MSI.

[Galería y reproducción](phase6-review2/README.md). [Manifiesto con resultados individuales y SHA256](phase6-review2/evidence.json). [TRX final original](phase6-review2/phase6-review2.trx). Los 29 hashes de PNG y el SHA de fuente se contrastaron contra el artefacto original. El ZIP descargado coincide con el digest declarado: `e0cb1da8c995c7c4c5a637b4e7a56c0b3f4183fb554ba56777700dc794d554ce`. Los bytes TRX y metadatos originales se preservan mediante `.gitattributes` en la nueva carpeta de evidencia.

Se conservan los cuatro TRX preliminares y sus SHAs/resultados: la primera ejecución falló por medir el hash antes de que arrancase el propietario; la tercera esperaba una clase de excepción distinta de la parada remota que efectivamente bloqueó el original sintético alterado. No se convierten en éxitos ni se sobrescriben. La galería detalla la cronología completa. La evidencia de P6.1/P6.2 y Fase 5 permanece intacta.

Los cinco advisories ImageSharp NU1902/NU1903 siguen visibles, sin supresión. No se declara resuelto #49. No se ampliaron timeouts del protocolo, no se relajó ownership, política, autorización ni validación de evidencias. La suite manual completa y la certificación operativa del MSI no se han ejecutado.

| Commit técnico | Contenido |
| --- | --- |
| `b7c87da` | Producción, Historial, proyecciones y resolución individual por el host; ajustes de P6.2. |
| `04540dc` | Identidad de navegación, conservación de elecciones e historial durable de decisiones. |
| `57ce62b` | Lecturas acotadas, integridad de imágenes y evidencia de las zonas inferiores. |
| `2b53623` | Lotes existentes de solo lectura, reobservaciones y búsqueda solo por nombre. |
| `22134f4` | Viewport compacto de Herramientas y verificación de acceso a RoDo. |

Los commits documentales posteriores solo incorporan evidencia e instrucciones; no modifican el producto ni se atribuyen una ejecución propia de pruebas.

Main permanece en `5f81950275098881fd0befac0a13cde08c9d751f`, protegida. No se ha creado PR ni fusionado; no se ha activado CUTOVER, producción o ningún servicio. No se han tocado otros repositorios, activos reales, identidades, ACL, cuentas ni carpetas protegidas del MSI. #47 y #49 siguen abiertas.

**PUNTO DE REVISIÓN VISUAL 2: DETENIDO.** P6.3/P6.4 quedan implementados para revisión humana. No se declara aprobada esta entrega ni terminada la Fase 6. No se continuará con P6.5 sin aprobación expresa del responsable; la validación funcional y visual posterior en el MSI sigue siendo independiente.
