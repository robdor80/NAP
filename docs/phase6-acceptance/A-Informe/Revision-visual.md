# Revisión visual transversal RobStyle

Revisión de XAML y tokens; los renders WPF de la ejecución final complementan estas comprobaciones. No acredita accesibilidad completa ni interacción humana.

| Pantalla | Comprobación / resultado |
|---|---|
| Inicio | Conteos legibles, estado prioritario, incidencias ampliables; modo sin host sin números inventados. |
| Catálogo | Estado vacío y filtros conservados. No se cargan assets reales para fabricar evidencia. |
| Producción/Historial | Listas y detalle con scroll separado; opciones incompatibles ausentes; comandos manuales accesibles. |
| Comparación | Zoom 1×/2×, dos guías y desplazamiento en ambos ejes; buffers preservados. |
| Ajustes General | Sin histórico duplicado; texto ordinario simplificado; campos y herramientas previas conservados. |
| Diagnóstico | Fechas completas, filtros legibles y vista de informe visible en exportación. Datos corruptos/desconexión con mensajes comprensibles. |
| Licencias | Vencimiento administrativo y avisos sin modal repetitivo; fecha de renovación y registro local. |
| Histórico | Filtros fijos, lista con altura restante y detalle lateral; rutas y propuesta tienen capturas con comprobación de visibilidad. Procedencia se amplía desde el aviso de simulación. |
| Compacto / herramientas | Navegación de cinco secciones; última herramienta alcanzable; capturas específicas. |

Contraste calculado de tokens sRGB opacos sobre los cuatro fondos RobStyle (no se modifica el tema):

| Texto | Fondo | Ratio |
|---|---|---|
| Ice | Petroleum | 17.08:1 |
| Ice | PetroleumDark | 17.56:1 |
| Ice | PetroleumDeep | 17.99:1 |
| Ice | PetroleumLight | 15.16:1 |
| IceMuted | Petroleum | 12.27:1 |
| IceMuted | PetroleumDark | 12.62:1 |
| IceMuted | PetroleumDeep | 12.93:1 |
| IceMuted | PetroleumLight | 10.89:1 |
| Success | Petroleum | 11.81:1 |
| Success | PetroleumDark | 12.13:1 |
| Success | PetroleumDeep | 12.44:1 |
| Success | PetroleumLight | 10.48:1 |
| Warning | Petroleum | 12.59:1 |
| Warning | PetroleumDark | 12.94:1 |
| Warning | PetroleumDeep | 13.26:1 |
| Warning | PetroleumLight | 11.17:1 |
| Error | Petroleum | 11.10:1 |
| Error | PetroleumDark | 11.41:1 |
| Error | PetroleumDeep | 11.69:1 |
| Error | PetroleumLight | 9.85:1 |

Estos pares de texto activo superan 4,5:1. No evalúan imágenes, antialiasing, transparencias/estados deshabilitados ni todos los estados de foco. El tema conserva los indicadores de teclado existentes; recorrido Tab/Mayús+Tab, escalado y percepción de contraste en el Raider siguen pendientes de aceptación humana. No se presenta como auditoría WCAG completa.
