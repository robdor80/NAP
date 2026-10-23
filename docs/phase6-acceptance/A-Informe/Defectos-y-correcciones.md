# Defectos encontrados y correcciones

| ID | Hallazgo | Corrección / estado |
|---|---|---|
| P68-A | Inventario histórico fijo a 180 px dentro de otro scroll; consulta compacta incómoda | Layout limitado al histórico: filtros arriba, lista con altura disponible y detalle lateral. Sin scroll vertical envolvente de inventario y detalle. Procedencia ampliable. |
| P68-B | Varias evidencias P6.7 tienen bytes/píxeles iguales con nombres distintos | Evidencias antiguas intactas; catálogo de duplicados en C. El capturador omite vistas sin cambio, registra página/selección/estado/hash y exige que una propuesta nombrada aparezca en pantalla. Workflow rechaza PNG idénticos entre escenarios. |
| P68-C | xUnit2030 en Phase6HistoricalTests, línea de existencia | Assert.Contains con predicado. Sin NoWarn ni cambio de analizadores. La compilación final y el TRX determinan el resultado. |
| P68-D | Segunda instancia del histórico dentro de General, además de la sección secundaria real | Eliminada la instancia redundante oculta. No cambia secciones ni capacidades. |
| P68-E | Texto habitual en Ajustes mencionaba detalles de fases futuras y configuración técnica | Texto breve: publicar requiere auditor real y revisión PASS. No se habilita publicar. |
| P68-P1 | Primer intento de P6.8 introdujo un StaticResource Visible sin declaración local | Detectado por render, corregido con conversor WPF local. Build/tests del intento se conservan y no acreditan su render fallido. |
| P68-P2 | Estilos locales no heredaban RobStyle y la lista no tenía viewport correcto; el vacío quedaba a 2 px | BasedOn en el detalle; inventario directamente limitado por Grid. Reorganización compacta para conservar altura utilizable. |
| P68-P3 | El capturador buscaba el mensaje de exportación antes de crear su árbol visual | Espera explícita al layout y margen compacto para alojar el resultado sin ocultar la lista. Export ya había creado y cotejado el .txt; no era fallo del exportador Core. |

Los defectos anteriores no resuelven los riesgos históricos de #49. Los refinamientos adicionales detectados durante render se detallan en el manifiesto y Resultados.md; no se borran intentos fallidos.
