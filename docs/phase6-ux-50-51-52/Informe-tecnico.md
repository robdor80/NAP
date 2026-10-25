# NAP 2.0 - Correcciones UX #50, #51 y #52

Fecha: 25/10/2026. Rama: codex/nap-2-fase-6-ux.
Base anterior a la corrección: fe69ba3500845c3e0f63c9336f159d0a2cda3bee.
Main comprobado antes del trabajo: 5f81950275098881fd0befac0a13cde08c9d751f.

## Alcance y causas

#50: Los DatePicker locales solo tematizaban el editor. El calendario emergente conservaba plantillas del sistema con superficies claras y texto heredado del tema oscuro. Se eliminan los dos estilos locales duplicados y se incorpora RobStyle.Dates.xaml, compartido por todos los DatePicker y Calendar. Define las plantillas de días, meses/años, cabecera, navegación y editor. Diferencia selección, hoy, mes adyacente, fecha bloqueada, deshabilitado, hover y foco. Se mantienen los enlaces SelectedDate y la lógica de WPF. Contraste calculado sobre los tokens de color: texto/fondo 17,56:1, texto de selección 12,93:1 y texto atenuado/fondo 12,62:1; son valores de los colores base y no una medición física del monitor.

#51: El WrapPanel del histórico colocaba juntos dos ComboBox sin márgenes laterales, un buscador estrecho y las acciones; el selector de grupo tenía 170 DIP para una etiqueta larga. Se usan campos de 230/260/250 DIP con márgenes uniformes de 16 DIP, etiquetas alineadas y acciones en una fila separada. WrapPanel permite redistribuir campos si disminuye el ancho. No se modifica HistoricalSimulationViewModel. Diagnóstico compartía el patrón de ComboBox sin espacio lateral; se le añaden los mismos 16 DIP. Las barras de Producción/Historial ya tienen separación explícita y no se alteran.

#52: ItemsControl aplicaba Margin=-8 dentro de ToolsScroll, cuyo viewport recortaba los píxeles del borde izquierdo. Se sustituye por margen positivo de 4 DIP. ToolsNavigation hereda las reglas de selección existentes, dibuja un solo borde redondeado y usa FocusVisualStyle interior discontinuo para la navegación por teclado. Se conserva Navigation para el menú principal.

## Método de capturas y límites

GitHub Actions usa Windows Server 2022 y WPF real. La referencia anterior se compila desde su SHA inmutable; se copian únicamente Phase6Acceptance.cs y Phase6UxEvidence.cs como instrumentación idéntica en ambos ensayos. Las vistas, estilos y lógica de la referencia permanecen originales. Se usa la edición aislada de aceptación. La instrumentación renderiza primero el cliente con RenderTargetBitmap para evitar el escalado automático del Viewbox de VisualBrush en el escritorio reducido del runner.

Las capturas representan el cliente lógico WPF de 1440x900 y 1200x700 DIP a 96 DPI. RenderTargetBitmap dibuja el árbol visual real. Popup tiene una HWND independiente: se captura su árbol WPF por separado y se compone en el anclaje lógico del control para evitar las limitaciones de resolución del escritorio del runner. Los PNG con sufijo -popup conservan la captura independiente. No se presentan como capturas del MSI ni como validación de Windows 11 o de escalado DPI físico.

Las pruebas conservan las comprobaciones de edición sintética: comandos productivos rechazados, navegación, cambio de universo, bandeja nativa, renovación en memoria, manifiesto, runtime completo, arranque directo y con el CMD, rechazo de argumentos y junctions inseguros, canarios y observación ETW. Se ejecuta el conjunto focalizado existente, sin pruebas de volumen de ZIP ni regresión general.

## Aceptación pendiente

Las tres incidencias permanecen abiertas para confirmación del responsable en Windows 11. No se abre PR, no se fusiona, no se cambia main, no se activa CUTOVER #47 y no se cierra #49 ni la Fase 6. Se conservan los avisos de seguridad y demás reservas conocidas. No se accede al MSI ni a datos o assets productivos.


## Entrega verificada

Commit compilado: `0a27b9c50e528db3e77f87f85e73eb266379cd96`.

Ejecución: https://github.com/robdor80/NAP/actions/runs/40243110761

Descarga: https://github.com/robdor80/NAP/actions/runs/40243110761/artifacts/12618090727

SHA-256 del ZIP descargado de Actions: `7afdaab8b054b491ebe89ebce2608d680c6fa27acc882b6e733689b9f7723671`. El ZIP interno del ensayo usa otra compresión; este es el hash de la descarga del usuario.

Caducidad indicada por GitHub: 2027-01-23T23:04:47Z. Requiere sesión en GitHub. No es una Release.

49 pruebas focalizadas superadas; cero errores de binding. Búsqueda 001: 1; Restablecer: 39; farmer: 10. Diez comprobaciones geométricas del submenú superadas; las diez fallaban en la referencia anterior. Cuadrículas de meses y años comprobadas con doce botones visibles y días ocultos. Selección de fecha y navegación verificadas.

ETW: cero accesos a ubicaciones habituales de NAP, cero escrituras fuera del laboratorio, cero procesos hijos y cero eventos perdidos. 491 archivos verificados por manifiesto tras descargar el artefacto. Arranque directo y CMD, argumentos y junctions inseguros, renovación en memoria, cambio de universo y bandeja superados. Sin licencias privadas ni patrones de secretos en el paquete. Avisos legales conservados.

## Auditoría adicional de la entrega vigente

Al comenzar esta revisión, la rama estaba en `5644aeb18d5e82ab7b9dc2f1f2788fff7c2bca22`. Las tres correcciones ya existían. Se preservaron todos los commits posteriores al paquete original y se verificó que main seguía en `5f81950275098881fd0befac0a13cde08c9d751f`.

La comparación de código y capturas reveló una regresión de codificación introducida al espaciar Diagnóstico: cinco textos aparecían con caracteres corruptos. Se restauraron «DIAGNÓSTICO», «Categoría», «Información local», «Exportar informe…» y «Registros de diagnóstico» en DiagnosticView.xaml. No cambia ningún binding, comando, fecha ni lógica. El commit compilado de esta entrega incluye esta reparación y todas las correcciones anteriores.

Se volvió a ejecutar el workflow focalizado completo, incluidas compilación Windows x64, capturas WPF antes/después, 49 pruebas, aislamiento ETW/canarios, bloqueos productivos y verificación de la descarga. La versión anterior de la entrega y sus evidencias permanecen en el historial del repositorio y de Drive. Las capturas finales de Diagnóstico permiten comprobar la restauración del texto. La confirmación visual en Windows 11 y al DPI del MSI sigue pendiente.
