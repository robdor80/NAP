# P6.5–P6.6 — Evidencias de revisión

Código verificado: `55538c7669cce459a1ea039437abb51fd7193e31`. [Ejecución Windows Release](https://github.com/robdor80/NAP/actions/runs/39853649755). **61/61 pruebas focalizadas, 22 PNG WPF, cero errores de binding**. No equivalen a aceptación funcional o visual en el MSI.

[Informe de revisión](Informe-revision.md) · [Informe PDF](Informe-revision.pdf) · [Galería PDF](Galeria-WPF.pdf) · [Contrato y funcionamiento](../NAP_2_0_FASE_6_P6_5_P6_6.md) · [Evidencia JSON](evidence.json) · [TRX original](final/phase6-settings.trx) · [SHA-256](SHA256SUMS.txt) · [Archivos fuente cambiados](changed-source-files.txt).

Los PNG son capturas nativas sin edición posterior, del árbol WPF de producto y datos sintéticos en memoria. Los avisos usan fechas simuladas; no se cambia el reloj. Los botones de licencias usan un registro en memoria para las capturas; las pruebas focalizadas comprueban la persistencia real en directorios temporales. El render de exportación pulsa el botón real de copia, comprueba el portapapeles y crea/lee un informe nuevo con el escritor de producto. El diálogo nativo de destino se reserva a la comprobación humana.

Sin host, el ejemplo de captura carece intencionadamente de journals y actividad local, por lo que presenta una lista vacía. La lectura de journals válidos/dañados y los fallos locales se verifican además en las pruebas focalizadas.

## Galería final

| Estado | 1440×900 | 1200×700 |
| --- | --- | --- |
| Diagnóstico: consulta de eventos | [Abrir](final/ajustes-diagnostics-1440x900.png) | [Abrir](final/ajustes-diagnostics-1200x700.png) |
| Filtros de fecha, gravedad y categoría | [Abrir](final/ajustes-diagnostics-filtered-1440x900.png) | [Abrir](final/ajustes-diagnostics-filtered-1200x700.png) |
| Informe anonimizado: copia y exportación verificadas | [Abrir](final/ajustes-diagnostics-export-1440x900.png) | [Abrir](final/ajustes-diagnostics-export-1200x700.png) |
| Sin host: información local disponible, sin datos inventados | [Abrir](final/ajustes-diagnostics-offline-1440x900.png) | [Abrir](final/ajustes-diagnostics-offline-1200x700.png) |
| Error de lectura: consulta fallida sin reparar trabajos | [Abrir](final/ajustes-diagnostics-error-1440x900.png) | [Abrir](final/ajustes-diagnostics-error-1200x700.png) |
| Licencias: antes del umbral de 90 días | [Abrir](final/ajustes-licenses-1440x900.png) | [Abrir](final/ajustes-licenses-1200x700.png) |
| Licencia: aviso de 90 días · 20/10/2027 | [Abrir](final/ajustes-licenses-90-1440x900.png) | [Abrir](final/ajustes-licenses-90-1200x700.png) |
| Licencia: aviso de 30 días · 19/12/2027 | [Abrir](final/ajustes-licenses-30-1440x900.png) | [Abrir](final/ajustes-licenses-30-1200x700.png) |
| Licencia: aviso de 7 días · 11/01/2028 | [Abrir](final/ajustes-licenses-7-1440x900.png) | [Abrir](final/ajustes-licenses-7-1200x700.png) |
| Licencia: vencimiento administrativo superado · 20/01/2028 | [Abrir](final/ajustes-licenses-expired-1440x900.png) | [Abrir](final/ajustes-licenses-expired-1200x700.png) |
| Licencia: renovación registrada hasta 18/01/2029 | [Abrir](final/ajustes-licenses-renewed-1440x900.png) | [Abrir](final/ajustes-licenses-renewed-1200x700.png) |

## Procedencia y verificación

El original descargado de Actions se conserva en [nap-settings-39853649755.zip](nap-settings-39853649755.zip), con SHA-256 `71ccb2373c5eb9e478ad51650edb44589602c48c711bd264aa4137313dcbcbfe`. `final/` contiene sus bytes extraídos sin normalizar TRX ni PNG. Se verificaron los 22 hashes de `captures.sha256`, las resoluciones, los 22 stderr vacíos, el origen y los contadores TRX. Los dos informes de ejemplo son idénticos y no incluyen rutas, nombres privados, IDs ni contenido de licencias.

El mecanismo oficial de validación y sus cinco advisories de ImageSharp siguen intactos. #49 permanece abierta. No se usan ZIP de assets, producción, CUTOVER ni regresiones generales.

## Ejecuciones previas conservadas

No forman parte de la evidencia final. Se preservan los ZIP originales y sus TRX, sin corregir resultados históricos.

| Run | Código | Resultado | Motivo de continuación |
| --- | --- | --- | --- |
| 39851793980 | 7b22800 | 58/60 | Excepción por journal dañado y preparación incompleta de fixture; ambos corregidos después. |
| 39852213416 | 3640e05 | 60/60, 22 PNG | Revisión de estilo de fechas y pie sintético; se añade copia nativa y test de registro ampliable. |
| 39852801254 | 6f0b0e1 | 61/61, 22 PNG | Se ajusta la presentación de fechas y la independencia de Ajustes. |
| 39853145437 | 6438917 | 61/61, 22 PNG | Separación del temporizador local de licencias del sondeo del host. |
| 39853332726 | 70e2407 | 61/61, 22 PNG | La inspección visual detecta año recortado; se amplía el campo y se añade guardia de anchura. |

## Punto de parada

Se solicita revisión humana de P6.5/P6.6. `main`, CUTOVER y assets reales permanecen intactos; sin PR ni merge. La validación funcional del MSI continúa pendiente. No se inicia P6.7/P6.8.
