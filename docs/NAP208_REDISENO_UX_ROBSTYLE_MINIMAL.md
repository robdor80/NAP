# NAP 2.0.8 · RobStyle Minimal

Estado: implementación y entrega completadas para revisión. PR Draft [#77](https://github.com/robdor80/NAP/pull/77), sin fusionar. Código empaquetado: `06ece907ea0a335d34b751857c986e44b95b50f3`; los commits posteriores solo documentan la entrega.

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

## Cierre y entrega verificada

[Ejecución final del único workflow focalizado](https://github.com/robdor80/NAP/actions/runs/43641707250), sobre `06ece907ea0a335d34b751857c986e44b95b50f3`: **SUCCESS**.

| Comprobación | Resultado |
|---|---|
| App WPF + HOST Release, .NET 10, Windows x64 autocontenido | PASS · 0 errores de compilación |
| Circuito personal, recepción, retirada segura, históricos, Auditor IA e IPC | 25 PASS · 0 FAIL · 0 omitidas |
| Presentación, filtros, objetivos, copias/restauraciones aisladas, WPF anterior y nuevo, 5.000 registros | 36 PASS · 0 FAIL · 0 omitidas |
| Permisos de commit/push y etiquetas Git, usuario ordinario Linux | 4 PASS · 0 FAIL · 0 omitidas |
| Total de comprobaciones focalizadas | **65 PASS · 0 FAIL · 0 omitidas** |
| Enlaces WPF en el recorrido de 5.000 registros | 0 errores |
| Inno Setup y actualización per-user 2.0.7→2.0.8 | PASS |
| Restart Manager cierra el HOST anterior con clrjit.dll en uso | PASS |
| Configuración de prueba y registro per-user conservados | PASS |
| Arranque del ejecutable ya instalado, WPF, bandeja e IPC | PASS · 0 errores de enlaces |
| Bases y archivos reales del propietario | No accedidos ni modificados |

Persisten dos avisos CA2014 de Core y el aviso CA1416 de una prueba Windows, anteriores a este cambio. No se alteró Core para silenciarlos. Las consultas Gemini reales quedaron expresamente fuera de esta repetición: se conservan sus pruebas y lógica, sin nuevo consumo real de proveedor. NAP CI no se ejecutó.

### Mediciones observadas: 5.000 registros temporales

Catálogo SQLite nativo temporal con 3.750 retratos verticales y 1.250 escenas horizontales; los datos se eliminan al terminar la prueba. El hash de SQLite antes y después fue idéntico.

- Relectura del catálogo con una nueva sesión de validación: **184,5 ms**.
- Búsqueda de «pescadora»: **579,3 ms**, con **2.500** coincidencias. Búsqueda vacía de coincidencias y restablecimiento también PASS.
- Memoria residente al comienzo: **138,2 MiB**; al final: **279,7 MiB**; pico observado: **294,2 MiB**. Se refrescaron los contadores del proceso antes de leer los valores finales.
- Recorrido completo: **61** peticiones de páginas, **3** peticiones de ficha y **225** peticiones de miniatura; no se solicitaron las 5.000 imágenes ni las 5.000 fichas.
- Máximo solicitado por página: **240** resúmenes en la búsqueda. La galería conserva páginas de 60 y LRU máximo de cuatro.

| Ventana de prueba | Render | Mosaicos realizados al desplazar al centro | Páginas en caché observadas | Desplazamiento al centro |
|---|---|---:|---:|---:|
| 1200×700 | 100 % | 6 | 2 | 37,5 ms |
| 1536×960 | 100 % | 10 | 2 | 11,9 ms |
| 2560×1600 | 125 % | 28 | 3 | 77,6 ms |

Son observaciones de una ejecución en Windows Server 2022 y .NET 10.0.15, con almacenamiento y cachés del runner. No son una garantía de tiempos, FPS o consumo en otros equipos. [Mediciones completas](nap208-evidencias/result.json).

### Capturas reales

[Descargar evidencias WPF y resultados TRX](https://github.com/robdor80/NAP/actions/runs/43641707250/artifacts/14132960216).

La carpeta `visual/` incluye **53 PNG reales**, generados desde los controles de la App compilada: diez pantallas en cada resolución, fichas de Catálogo, filtros, escenas horizontales, detalles de Producción e Historial, Ajustes avanzado, búsqueda, ausencia de resultados y errores explícitos de lectura. En Producción se comprueba que un fallo de consulta no se presenta como «Todo al día». Se verifica foco alcanzable y navegación Tab. El banner identifica los datos sintéticos temporales.

La carpeta `installed/` contiene además el arranque WPF del producto ya instalado, sus resultados y captura. Los estados de trabajo del recorrido visual usan una fuente de prueba de solo lectura; el catálogo y sus imágenes se leen del SQLite temporal mediante el adaptador real. Las pruebas funcionales del HOST y las evidencias IA se verifican por separado.

La política de red de este entorno impidió descargar los PNG para una inspección manual adicional. Quedan accesibles en el artefacto para la auditoría visual independiente. No se declara esa revisión manual efectuada ni aceptación física de Windows 11.

### Instalador final

[Descargar NAP 2.0.8 — instalador, SHA-256 e inventario (ZIP de Actions)](https://github.com/robdor80/NAP/actions/runs/43641707250/artifacts/14131774969).

Artefacto exacto: `NAP-2.0.8-UX-Minimal-06ece907ea0a335d34b751857c986e44b95b50f3`. Utilizar este artefacto final, que contiene:

- `output/NAP-Setup-2.0.8.exe`.
- `output/NAP-Setup-2.0.8.exe.sha256`.
- `inventory.json`, inventario completo de la entrega.
- `provenance.json`, versión, fuente y procedencia de compilación.
- `upgrade-result.json`, resultado de actualización aislada.

SHA-256 del EXE, recalculado por el procedimiento de prueba antes de instalar:

```text
0785e40c5a49dce32d0c3278253fc1f0b32643246863e7af4aaedb0322f0ff3b
```

Instalar sobre 2.0.7 con la misma cuenta Windows y la ubicación habitual. Si Inno Setup detecta aplicaciones en uso, aceptar su cierre mediante Restart Manager. No repetir importaciones ni regenerar launch.json. El instalador conserva el procedimiento existente y no incorpora una firma nueva.

Los artefactos de Actions caducan el 12/02/2027. [Índice de entrega](nap208-evidencias/entrega.json) y [evidencias Git](https://github.com/robdor80/NAP/actions/runs/43641707250/artifacts/14132855070).

## Pendientes para la aceptación del propietario

1. Auditoría visual independiente de los PNG y comprobación física en Windows 11 con su escalado habitual. La generación y las comprobaciones WPF automatizadas ya pasaron.
2. Contrastar la observación 39/41 con sus bases y filtros reales, sin reconstruirlas. No hay evidencia suficiente aquí para atribuirla a corrupción ni corregir cifras.
3. Autorización posterior antes de cualquier fusión. La PR sigue Draft; main y las PR #73–#76 no fueron modificadas.

