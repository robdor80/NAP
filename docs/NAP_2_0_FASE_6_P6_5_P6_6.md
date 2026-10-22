# NAP 2.0 — P6.5 Diagnóstico y P6.6 Licencias

Entrega en `codex/nap-2-fase-6-ux`, continuación autorizada de `fbb88c2`. Se detiene para revisión humana al terminar P6.6. No incluye P6.7 ni P6.8.

[Informe de revisión, galería y evidencias](phase6-settings/README.md).

## P6.5 — Diagnóstico

Ajustes conserva General y añade Diagnóstico y Licencias. Diagnóstico consulta el universo seleccionado en la cabecera; los filtros permiten fecha inicial/final inclusivas, gravedad y categoría. Los detalles se abren bajo demanda. Los controles permiten copiar el informe al portapapeles y exportarlo a un nuevo archivo de texto.

La nueva operación de lectura autenticada `AutomationOperation.Diagnostics`, añadida al final del enum para conservar sus valores anteriores, devuelve `DiagnosticPage` mediante `AutomationHost.QueryAsync`. Usa exclusivamente los eventos durables existentes de la cola y no crea recibos de mutación. `DiagnosticQuery` limita cada página a 30 eventos, con una fila adicional para determinar si existen más, cursor exclusivo y cota superior estable. Los filtros SQL tienen parámetros; no se enumeran ni cargan historiales completos. Gravedad es una proyección explícita de resultado e incidencia existentes: rechazado → error; revisión/incidencia → aviso; resto → información. No pretende añadir una gravedad duradera al evento original.

La interfaz cancela lecturas al cambiar filtros o universo y descarta respuestas tardías o de otro universo. El límite de espera es de 20 segundos. Un fallo de conexión se muestra sin inventar registros; Información local permite consultar los datos disponibles de la sesión.

En el arranque ordinario también se consultan hasta 30 journals `job_*.json` existentes del universo, únicamente en el nivel superior de `StateRoot`, con límite de 16 KiB por journal. Se usan la validación y el estado real de `JobStateStore`; no se inicializa ni recupera el pipeline. Los registros dañados se conservan y producen un error de lectura. No se inventan fechas para journals o avisos que no las registran: aparecen como «Sin fecha registrada» y quedan fuera de un filtro de fechas. El modo conectado consulta almacenamiento del host a través de su contrato; WPF no abre su cola directamente.

El informe se construye por lista de campos permitidos: fechas reales, gravedad y categoría comprensibles, vocabulario cerrado de estados/resultados/incidencias y códigos locales reconocidos de `NapIssueCodes`. Se excluyen mensajes libres, nombres de assets y universos, usuarios, rutas, IDs de trabajos, hashes, tokens, cabeceras, credenciales y cualquier contenido de licencia. Es una exclusión estructural de datos, no una promesa de reconocer mediante expresiones regulares todos los secretos posibles en un log arbitrario. Los códigos desconocidos no se exportan.

La exportación crea un archivo `.txt` nuevo, de hasta 256 KiB, fuera de todas las raíces configuradas y del directorio administrativo de NAP. Rechaza enlaces, destinos existentes y archivos de otro tipo. Nunca ejecuta reparación, limpieza, aprobación o reanudación. Copiar/exportar no cambia trabajos ni registros fuente.

## P6.6 — Licencias

Registro administrativo por usuario en `%LocalAppData%/NAP/license-reminders.json`, independiente de los universos, del host y de CUTOVER. Formato JSON versionado, hasta 32 licencias, publicación atómica, exclusión de campos desconocidos/duplicados, tamaño máximo de 256 KiB y coordinación mediante el mecanismo local existente. Un registro ilegible se conserva; no se reemplaza silenciosamente.

Primera entrada: **Six Labors ImageSharp Community**. Vencimiento administrativo inicial: **18/01/2028**, identificado expresamente como no verificado por el proveedor. `sixlabors.lic` figura exclusivamente como nombre asociado en los metadatos. El gestor carece de operaciones para abrir ese archivo y no consulta, almacena ni exporta su contenido. La validación oficial de los builds no se modifica.

| Tramo | Fecha inicial |
| --- | --- |
| 90 días | 20/10/2027 |
| 30 días | 19/12/2027 |
| 7 días | 11/01/2028 |
| Vencimiento | 18/01/2028 |

Se comprueba al iniciar NAP, al abrir Licencias, al actualizar esa sección y al cambiar el día durante el uso. Un temporizador local comprueba el cambio de fecha cada minuto; funciona también tras restaurar el equipo o la ventana. No modifica el sondeo existente del host ni exige conectarlo. No cambia el reloj del equipo.

Cada umbral alcanzado se registra una sola vez para cada vencimiento. Si NAP estuvo cerrado, el siguiente arranque registra los tramos que correspondan y conserva el aviso más urgente pendiente. Un acceso discreto en la barra lateral conduce a Licencias. No hay ventanas emergentes. Marcar el aviso como atendido persiste la atención de los umbrales ya alcanzados para ese vencimiento; los umbrales futuros siguen generando su aviso. Reiniciar no repite los avisos atendidos.

Registrar una renovación exige una fecha posterior a hoy y al vencimiento registrado. Conserva la fecha anterior y el día de registro, recalcula los tramos y evalúa de nuevo los avisos. Se conservan hasta 50 renovaciones y 200 entradas de aviso por licencia; la vista muestra las últimas 12 entradas de aviso. El formato y la interfaz de registro permiten añadir otras licencias sin depender del host.

Un aviso administrativo no bloquea la ejecución de NAP, no sustituye una comprobación del proveedor y no silencia avisos técnicos o de seguridad de ImageSharp.

## Compatibilidad y límites

- Se conservan las cinco secciones y el estilo RobStyle. Las funciones manuales, la normalización, las decisiones excepcionales, las autorizaciones y los controles de producción no cambian.
- Los diagnósticos exportan solo la página o muestra consultada. No son un volcado completo, no incluyen texto libre de logs ni permiten correlacionar IDs de assets entre informes. Esta reducción evita exponer contenido privado. Si se precisa ampliar el diagnóstico futuro, deberá añadirse otra proyección explícita, revisada y acotada.
- Sin host se dispone de la sesión actual y de una muestra de journals locales válidos; no se afirma conocer toda la actividad automática histórica. Los journals existentes no registran fecha y no se les atribuye una.
- La renovación se registra manualmente; no hay consulta al proveedor, suscripción, descarga de licencia ni actualización de dependencias. Se mantiene el seguimiento de #49 y de las advertencias existentes de ImageSharp.
- Las capturas se producen con el árbol WPF real y adaptadores sintéticos, en Windows. No certifican funcionamiento, escalado, accesibilidad completa ni aceptación visual definitiva en el MSI.
- `main`, CUTOVER #47, cuentas, ACL, servicios y assets reales de Nimroel permanecen fuera del trabajo. No se crea PR ni se fusiona. La API de protección de `main` devuelve 403 con esta integración; se verifica por separado su referencia real y se conservan los disparadores de CI general y CUTOVER.

## Aceptación pendiente en el MSI

Verificar el acceso por teclado a las dos secciones, selectores de fecha, foco y desplazamiento a 1440×900 y 1200×700. Probar copia/exportación a un archivo nuevo fuera de los almacenes, cambio de universo durante una consulta, desconexión del host y consulta local. Comprobar registro/atención/renovación en la instalación de prueba usando metadatos administrativos controlados, sin abrir el archivo privado ni cambiar el reloj. Confirmar posteriormente la continuidad de las operaciones autorizadas del MSI.

La aprobación humana de esta entrega y la aceptación funcional en el MSI siguen pendientes. No se continúa con P6.7/P6.8.
