# NAP 2.0.7 · Auditor IA con el Host personal

La integración parte de `1f5c0963924885d1c8612015c8d529df75f5364c` (PR #75). Se reutilizan el lector, el analizador y el cliente de Gemini que ya están en la cadena de PR. Se conserva la versión solicitada y el instalador con Restart Manager. El artefacto de Actions lleva el prefijo `NAP-2.0.7-Auditor-IA-Host` y el SHA del commit para distinguirlo de la primera compilación 2.0.7.

## IPC y datos

El IPC v1 añade dos operaciones autenticadas, limitadas al Host personal y al universo de la solicitud.

| Operación | Entrada | Salida |
| --- | --- | --- |
| `DiversityInventory` | Universo y cuerpo vacío | Recuentos, procedencia, distribuciones, categorías poco representadas y disponibilidad de Gemini |
| `DiversityAsk` | Universo y pregunta de hasta 1200 caracteres | Informe consultado para esa pregunta, respuesta y evidencias resueltas por el Host |

El Host abre SQLite en solo lectura con los lectores existentes. En Nimroel combina `AssetCatalog.db` y `LegacyPortraits.db` y cuenta una sola vez cada identidad. Si ambos índices contienen un mismo recurso, prevalece la ficha nativa. El cruce localidad, profesión y sexo permite responder preguntas sobre un lugar concreto. Los datos ausentes se distinguen de los ceros de categorías ya conocidas.

La consulta vuelve a leer el inventario cuando se pulsa «Consultar IA». Gemini recibe la pregunta y los agregados de ese informe. El cliente de diversidad valida las referencias antes de mostrar la respuesta. Las cifras mostradas junto a cada referencia proceden del informe SQLite. El Host conserva los límites del analizador, el plazo de 60 segundos y una consulta de diversidad simultánea. Durante la llamada de red el propietario del universo queda disponible para el trabajo de automatización.

La configuración de Gemini se carga en el Host una sola vez para los dos clientes y se recarga por el procedimiento existente. Se usa el almacén DPAPI y el mecanismo de variables de entorno ya admitido. El IPC de diversidad lleva únicamente datos agregados y no persiste preguntas o respuestas en el diario de solicitudes. Las sesiones no personales rechazan ambas operaciones.

## Comprobaciones

- Compilación Release de WPF y Host, y pruebas focalizadas por clase en el flujo personal. La CI general «NAP CI» queda desactivada.
- SQLite aislado por universo, deduplicación nativo/histórico, rechazo de inventario aportado por el cliente, verificación de evidencias, ausencia de llamadas automáticas y acceso denegado al laboratorio.
- Una prueba aislada con Gemini real validó el IPC y la habilitación de «Consultar IA» en WPF. SQLite contenía nueve recursos únicos (dos nativos y siete históricos). Gemini citó el reparto correcto de pescadores de Treskal: una pescadora y dos pescadores. Ambas bases quedaron idénticas byte por byte.
- La comprobación de actualización usa el instalador publicado de 2.0.6 y el paquete nuevo. El runner comprueba SHA-256, versiones de WPF y Host, cierre del Host anterior con la DLL en uso, registro per-user y conservación de la configuración.

## Actualización

En la PR de esta integración, descarga el artefacto cuyo nombre comienza por `NAP-2.0.7-Auditor-IA-Host-`, verifica el fichero SHA-256 y ejecuta `NAP-Setup-2.0.7.exe` desde la misma cuenta Windows. Instala sobre 2.0.6 con el asistente habitual. Si detecta NAP en ejecución, selecciona el cierre de aplicaciones. Abre **Herramientas → Auditor IA**, comprueba los recuentos y pulsa «Consultar IA» para enviar una pregunta.

La validación del nuevo instalador con el catálogo real y la configuración del propietario se realiza cuando el propietario lo instala.
