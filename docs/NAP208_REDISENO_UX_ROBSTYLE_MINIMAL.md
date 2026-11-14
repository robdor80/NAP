# NAP 2.0.8 · RobStyle Minimal

Estado: implementación en rama de revisión; validación y entrega en curso. No fusionar en main.

## Procedencia comprobada

- Base: `1e634b9292842ccb1a1da63042753376a2023fe8`, HEAD remoto de #76 al comenzar.
- Cadena conservada por ascendencia: #73 (`eef8738`), #74 (`c1cedf5`), #75 (`1f5c096`) y #76 (`1e634b9`). Todas seguían Draft y sin fusionar al iniciar.
- Main permanecía en `3edf82cd5a8035389111108a022c164eafec68d1`.
- Rama: `codex/nap-2.0.8-ux-minimal`. Ninguna PR anterior modificada.
- El dato de aceptación funcional en el ordenador del propietario procede del encargo. Esta intervención no accede a ese equipo ni certifica su instalación física.

## Presentación implementada

| Pantalla | Vista habitual | Acceso secundario o técnico conservado |
|---|---|---|
| Inicio | Universo, estado breve, acción permitida, Entrada, cinco actividades e incidencias | Recibos, resultado íntegro de Gemini, rutas, diagnóstico, búsqueda explícita de ZIP y detención |
| Producción | Activos: en curso, revisión y pendientes; búsqueda y paginación de 20 | Filtros por estado/lote, Archivados, restauración y eliminación confirmada, trabajo manual |
| Historial | Completados por defecto, fecha y resultado único; ficha contextual | Todos los estados por filtro, actividad y decisiones paginadas, operaciones manuales |
| Catálogo | Galería amplia, nombres legibles, imágenes sin recorte, búsqueda visible | Filtros plegables, perfil e ID exactos avanzados, ficha tras selección, comparación, documentos y eliminación con inventario |
| Objetivos | Lista compacta y progreso, estado vacío | Formulario al abrir Nuevo objetivo y campañas plegadas; filtros reales conservados |
| Estadísticas | Total, resultados filtrados, retratos, escenas y profesiones | Distribuciones completas y progreso de objetivos plegados |
| Auditor IA | Pregunta, Consultar IA, respuesta y recuentos | Ver datos utilizados, categorías menos representadas y Copiar JSON |
| Copias de seguridad | Crear copia, lista y restauración para selección compatible | Tipo, retención, inventario e información Git |
| Git | Estado humano y Configurar Git | Editor íntegro de autorización, permisos, actor, vínculos, outbox, recibos y recuperación |
| Ajustes | General: universos, Gemini, preferencia de bandeja, ubicaciones plegables y actualizaciones | Diagnóstico, HOST, SQLite, almacenamiento, histórico sintético, licencias, Git y mantenimiento |
| Navegación | Inicio, Catálogo, Producción, Historial y Ajustes; herramientas secundarias | RoDo retirado del menú; recursos, ViewModel, vistas y preparación conservados |

Se mantienen el fondo petróleo RobStyle y la identidad oscura; los contornos pasan a un tono discreto y el acento cian distingue las acciones principales. El pie deja de repetir controles y estados de automatización.

## Límites respetados

Los cambios de producto están en WPF y NAP.Presentation, más la versión compartida. No se cambia Core, AI, AutomationHost, esquemas SQLite, perfiles, rutas, credenciales, permisos Git, políticas de retención ni motor de producción. Inno Setup conserva AppId, ruta per-user y Restart Manager. No se ejecuta NAP CI.

Los adaptadores no conceden permisos: Inicio invoca Activate/Pause/Resume existentes; Producción compone tres lecturas disjuntas ya autorizadas, con un máximo de veinte filas por página y sondeos de recuento. Historial mantiene el acceso al resto de estados por filtros.

## Escalabilidad y búsqueda

- Se conserva VirtualizingTilePanel, reciclado, páginas de sesenta y LRU de cuatro páginas (máximo 240 modelos de mosaico en caché).
- Las imágenes se solicitan al hacerse visibles; los bitmaps se liberan al reciclar y continúan limitados a dos decodificaciones simultáneas.
- La búsqueda por texto usa páginas de hasta 240 resúmenes. Conserva posiciones de coincidencias y agregados por tipo/perfil, sin leer todas las fichas, documentos, maestros ni miniaturas.
- La búsqueda respeta los filtros estructurales y acepta nombres e identificadores, sin distinguir acentos o mayúsculas. No llama a Gemini.
- Las distribuciones de clasificación se exploran con filtros del catálogo. Tras una búsqueda por texto se indica que se debe restablecer esa búsqueda para explorarlas; no se inventan clasificaciones a partir del nombre.
- Los nombres descriptivos se derivan de identificadores disponibles mediante vocabulario de presentación. Se conserva el ID original en los detalles técnicos.

## Recuentos y observación 39/41

Catálogo y Estadísticas distinguen Total del catálogo de Resultados filtrados, con lecturas nativas e históricas del adaptador existente. Si listado y estadísticas filtradas discrepan en una lectura, aparece un aviso explícito. No se corrige ninguna cifra mediante constantes.

La prueba temporal comprueba total 32, búsqueda 16, escenas 8 y restablecimiento 32 sin alterar los bytes de SQLite. La prueba WPF utiliza 5.000 registros temporales. No se dispone aquí de las bases del propietario; por tanto no puede afirmarse el origen de su observación 39/41. No se han reconstruido, migrado ni corregido sus bases.

## Validación y evidencias

Primera ejecución de implementación: Windows Release y 25 regresiones personales PASS; publicación bloqueada por ausencia inicial de la prueba visual.

En la siguiente ejecución, `43616793978`, compilaron App, HOST y pruebas; pasaron las 25 regresiones personales y las cuatro comprobaciones nuevas de NAP208, incluida la aplicación WPF real con 5.000 registros temporales y cero errores de enlaces. Se produjeron capturas reales de los controles de la aplicación, identificadas en pantalla como datos sintéticos temporales, no mockups ni imágenes generadas de una interfaz.

La ampliación detectó tres fallos: un test antiguo esperaba roles originales cuando la presentación anterior devolvía etiquetas traducidas; se conserva ahora Role original y una propiedad Label separada. Dos comprobaciones Git se detuvieron antes de operar por propietario de archivo temporal diferente en el token elevado del runner Windows. Core mantiene ese rechazo. Las comprobaciones de commit/push se ejecutarán en el runner Linux del mismo workflow focalizado, bajo su usuario ordinario, sin nuevos usuarios Windows ni modificación de ACL productivas.

La matriz visual mide 1200×700, 1536×960 y 2560×1600, esta última con render WPF a 125 %. Es renderización real en Windows Server 2022 de Actions; no equivale a una comprobación física en Windows 11. Se mantienen las capturas y mediciones exactas en el artefacto de evidencias.

Pendientes de registrar en el cierre: ejecución final, métricas exactas, revisión visual de capturas, actualización aislada 2.0.7→2.0.8, instalador y SHA-256. No se publica instalador si falla una comprobación crítica.
