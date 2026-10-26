# NAP 2.0 — P6.3/P6.4, pulido final

22-10-2026. Rama existente `codex/nap-2-fase-6-ux`. Base documental `d939683`, código anterior `22134f4`. El responsable aprobó arquitectura, navegación y dirección RobStyle y autorizó únicamente los ajustes A/B/C. La solicitud adicional autoriza copiar esta entrega a Google Drive en `NAP 2.0/Fase 6/P6.3-P6.4 Pulido final`.

## A — Comparación ampliada

Problema real de presentación: las imágenes del detalle tenían 120 puntos de altura. Se mantiene el detalle aprobado y se añade «Ampliar comparación», que abre una ventana RobStyle de solo lectura. Las dos imágenes visualizadas conservan sus proporciones mediante `Stretch=Uniform`; el zoom, entre 50% y 300%, y Encajar actúan exclusivamente en el árbol visual WPF. A mayor ampliación hay desplazamiento horizontal y vertical. Original/lienzo/salida permanecen en la cabecera y los márgenes/sin recorte, al pie.

«Marcar bordes añadidos» superpone guías opcionales calculadas a partir de los márgenes que devuelve el host. Un trazo mínimo permite localizarlos aunque su tamaño real sea subpíxel en pantalla. Las guías no representan nuevos píxeles ni forman parte del candidato. Se pueden desmarcar para examinar la imagen sin anotaciones. La ventana no tiene acciones de aprobación y se cierra si el detalle pierde la vista previa; Escape permite cerrarla.

No se leen originales de disco, solicitan nuevas imágenes al host ni modifican los buffers o la normalización. Se amplían las miniaturas verificadas existentes, limitadas a 256 píxeles por eje; ampliar no recupera detalle perdido en esa reducción. Los datos geométricos proceden de la evidencia original. Un visor de originales a resolución completa sería otro alcance y no se ha implementado.

## B — Coherencia de estados

La contradicción era de los adaptadores sintéticos: las filas de Producción contenían cuatro assets en estados mixtos, pero Overview recibía otros contadores (14 completados y ninguno activo). El adaptador de capturas comparte ahora sus filas con la proyección sintética de Overview, incluidos nombres, identidad, fechas y revisión. Producción muestra un pendiente, uno procesando, uno completado y uno que necesita revisión; el pie indica «Procesando imágenes».

No se ha modificado la proyección real `AutomationControlViewModel.Describe`: ya da prioridad al procesamiento, comprobación de archivos, pendientes e incidencias antes de «Todo al día». Se añaden comprobaciones de combinaciones y una aserción del renderizado contra las filas. No se introducen contadores productivos, sincronizadores ni estado de ejecución local.

## C — Páginas independientes y estables

Problema real: `WorkflowDesktopReader` aplicaba `EventOffset` también a las decisiones y combinaba ambos indicadores de más registros en `MoreEvents`. Se separan `DecisionOffset` y `MoreDecisions`; WPF mantiene posiciones, etiquetas y controles independientes para cada lista, conservando asset y universo.

Se añaden límites superiores de lectura `EventThrough` y `DecisionThrough`. Se obtienen al abrir el detalle, bajo una transacción SQLite de lectura, y se conservan al paginar. Las nuevas entradas no desplazan las páginas abiertas ni provocan repeticiones. Actualizar/cambiar asset, universo o completar una mutación reinicia la lectura para ver entradas nuevas. Estos límites son posiciones de consulta, no estados ni autorizaciones.

Cada consulta retorna como máximo 20 eventos y 20 decisiones, usando un registro adicional por lista para detectar continuidad. Se conservan el orden descendente de secuencia de eventos y el de fecha/versión de decisiones, con desempate estable por orden de inserción. Las posiciones negativas o límites fuera del asset se rechazan. Los fallos de lectura y respuestas de otro contexto conservan sus barreras existentes.

Además, la lectura de la vista previa selecciona mediante SQL una única decisión: la última propuesta y su versión vigente. Evita materializar todo el historial para mostrar la imagen. Una revocación posterior de una propuesta antigua no sustituye la propuesta más reciente. Las validaciones integrales de integridad de la cola permanecen intactas.

## Archivos y contratos

| Área | Archivos |
| --- | --- |
| Comparación visual | `ComparisonWindow.xaml/.cs`, `Controls/ComparisonBorderGuides.cs`, `Views/WorkflowView.xaml/.cs` |
| Paginación WPF | `NAP.Presentation/WorkflowViewModel.cs` |
| Lecturas Core | `AutomationQueueBrowse.cs`, `WorkflowDesktopReader.cs`, `DurablePreparationReview.cs` |
| Contrato | `WorkflowDetailRequest`: desplazamiento de decisiones y límites superiores opcionales; `WorkflowDetail`: continuidad de decisiones y límites devueltos |
| Datos y render | `Phase6Preview.cs`, `Phase6WorkflowPreview.cs` |
| Verificación | `Phase6PolishTests.cs`, workflow `phase6-review.yml` |

No se añaden operaciones al protocolo ni se modifican propuestas/aprobaciones/continuación, políticas, permisos, ownership, auditorías o evidencia de transformación. Se utiliza el endpoint autenticado `WorkflowDetail` existente. Para usar los campos nuevos, App y host deben proceder de la misma versión verificada.

## Verificación y entrega

Los disparadores se revisaron antes del push: CI general solo se activa mediante main/PR hacia main; CUTOVER requiere dispatch explícito. Solo se usa el workflow focalizado de esta rama, con builds Release mediante el helper oficial existente. El filtro excluye expresamente el caso funcional ZIP anterior; las pruebas de este pulido utilizan metadatos o respuestas en memoria. No hay paquetes reales, volúmenes ZIP, proveedores, regresión general, estrés ni benchmark.

**Fuente final verificada: `91f94b972ec5a1daa7efc718509ffb3ee97e7410`.** [Workflow Windows 39839852706](https://github.com/robdor80/NAP/actions/runs/39839852706), job `120037674100`: ambos builds Release correctos; **43/43 pruebas Passed, cero fallos y cero omitidas**. Los cinco advisories ImageSharp siguen visibles. El caso funcional ZIP anterior está excluido expresamente; se comprobó su ausencia en el TRX.

Las once pruebas nuevas cubren combinaciones de estados, eventos/decisiones con cantidades asimétricas (0, 3 y 24 decisiones), navegación independiente, límites de 20 registros, altas posteriores a la apertura sin desplazamiento de páginas, revocación de propuestas antiguas, límites inválidos, lectura sin cambiar bytes de cola, cambio de universo con respuestas pendientes y transporte autenticado de los campos nuevos. Las otras 32 comprueban proyecciones P6.1/P6.2 y navegación/detalle/controles P6.3/P6.4 dentro de ese alcance; no sustituyen una regresión integral ni una prueba operativa del MSI.

**25 capturas nativas WPF**, a 1440×900 y 1200×700. Diez procesos con datos en memoria y adaptadores que rechazan escrituras. Comparación completa y zoom al 200%, controles de desplazamiento, guías de los dos bordes, conservación de buffers mediante hash y ausencia de errores de binding comprobados. La ventana ampliada usa su contenido WPF de producto sin mostrarla durante el renderizado fuera de pantalla: evita los límites físicos del escritorio del runner y conserva el contexto lógico/namescope de Window. Los renderizados de la ventana principal mantienen su método anterior. No son capturas del MSI ni imágenes retocadas. Las miniaturas ilustrativas se generan con las dimensiones y extensión de borde declaradas antes de reducirlas al límite existente de 256 por eje.

Se inspeccionaron las comparaciones normal y compacta, zoom, actividad e incidencias simultáneas, y páginas con continuidad diferente entre eventos y decisiones. Los 25 PNG coinciden con sus hashes, la fuente del artefacto coincide con el commit verificado y los diez stderr finales están vacíos. El ZIP descargado coincide con el digest declarado por Actions: `dd24dcf4ec906aa43ee1c7821ce684cc40d1e3535729e621bb60586d4ab6c1bf`.

[Galería y reproducción](phase6-polish/README.md), [informe PDF](phase6-polish/Informe-revision.pdf), [TRX final original](phase6-polish/phase6-polish.trx), [manifiesto](phase6-polish/evidence.json). Todo queda en una carpeta nueva, sin alterar `docs/phase6-review2`, `docs/phase6` ni las evidencias de fases anteriores. Los cinco artefactos preliminares y sus TRX se conservan íntegros con su resultado y motivo; no se convierten fallos en éxitos.

Destino adicional autorizado: `NAP 2.0/Fase 6/P6.3-P6.4 Pulido final` en Google Drive, con Capturas WPF, Informe y Pruebas. Los enlaces privados se comunican al responsable; no se publican identificadores privados de Drive en el repositorio.

## Estado y límites de entrega

Main sigue protegida en `5f81950275098881fd0befac0a13cde08c9d751f`. No se ha abierto PR, fusionado, activado producción/CUTOVER ni tocado assets reales, servicios, identidades o carpetas protegidas del MSI. Los commits de documentación posteriores no alteran el producto verificado ni se atribuyen una ejecución nueva de pruebas.

Este pulido termina aquí. La ampliación conserva el límite de detalle de las miniaturas; no es un visor de originales a resolución completa. La composición operativa sigue limitada al laboratorio y necesita App y host de la misma versión. La aceptación funcional del MSI es independiente.

P6.5–P6.8 fuera de alcance. La aceptación operativa en el MSI y CUTOVER siguen pendientes de #47. #49 y los avisos ImageSharp permanecen bajo seguimiento; no se ocultan ni se declaran resueltos.
