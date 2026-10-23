# Guía de aceptación — MSI Raider GE78 HX · Windows 11

Código de referencia verificado: `ed556bbbdf8a6971cd22d38f96ff88885a366225` · [Actions 39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656).

**Estado: procedimiento preparado; aceptación pendiente. No hay paquete privado listo en P6.8.** No se ha instalado ni ejecutado nada en el MSI. “MSI” aquí es el ordenador Raider, no un instalador .msi.

## 1. Antes de empezar

El responsable debe disponer de autorización expresa para la aceptación y una sesión de Windows de prueba ya preparada, sin configuración ni raíces productivas de NAP. No cree cuentas, cambie permisos/servicios, active características Windows, mueva originales ni modifique el reloj siguiendo esta guía. Si no existe ese aislamiento, pare y registre “bloqueado por preparación del entorno”.

La revisión puede comenzar consultando informe y galería en Drive sin ejecutar software. La aceptación funcional completa necesita un paquete privado Windows x64 y su manifiesto de origen. No abra copias antiguas de Debug/Release ni descargue un supuesto instalador de otra procedencia.

## 2. Obtener e identificar la compilación

En P6.8 el workflow autorizado compila y renderiza, pero no publica binarios. El repositorio es público: sus artefactos no constituyen un canal privado. Por eso esta entrega no incluye enlace ficticio a ejecutables ni instrucciones para extraerlos de las capturas.

Antes de la sesión, el mantenedor deberá proporcionar por canal privado aprobado una carpeta completa Windows x64, preferentemente autónoma (sin instalar runtime), derivada del **SHA verificado del manifiesto C**, con NAP.App.exe, todas sus DLL nativas/administradas, runtimes, archivos .deps.json/.runtimeconfig.json, perfiles incluidos y avisos de licencias de terceros. Si es dependiente del framework, debe indicar el runtime **.NET 8 Windows Desktop x64 exacto** ya disponible; no instalarlo sin autorización adicional. Debe validar la licencia mediante el helper oficial en build, limpiar el temporal, comprobar que no se incluyeron sixlabors.lic, secretos, configuraciones privadas o datos, y adjuntar inventario SHA-256. No omitir el validador ni copiar la licencia del MSI para “hacerlo funcionar”.

La generación privada del paquete y la verificación de todas sus dependencias quedan bloqueadas hasta disponer de ese canal y revisión de licencia; esta guía no autoriza una Release ni un instalador definitivo. El nombre de versión visible de la app no identifica por sí solo el commit. El manifiesto debe contener SHA completo, fecha UTC, workflow/run, arquitectura, modo autónomo o runtime y hashes de todos los archivos. El responsable cotejará ese SHA con C/Resultados.md. Si no coincide, rechazar antes de abrir.

El mantenedor debe entregar también un acceso directo de prueba que identifique claramente el aislamiento. El arranque ordinario lee perfiles/configuración locales y escribe el registro administrativo en `%LOCALAPPDATA%\NAP\license-reminders.json`; un `--settings` alternativo por sí solo **no aísla todo**. Nunca abrir la app ordinaria en el perfil productivo para esta prueba. No hay opción interactiva aislada nueva implementada en P6.8.

## 3. Arranque y cierre

1. Anote fecha, nombre del equipo, versión de Windows, resolución y escala (sin cambiar reloj).
2. Desde la sesión de prueba autorizada, abra el acceso directo entregado. No use “Ejecutar como administrador”. Si aparece petición de elevación, pare.
3. Compruebe que ve NAP y las cinco secciones. Sin raíces configuradas puede abrirse Ajustes: es correcto. No copie rutas reales ni configure automatización.
4. Cierre con la X. Abra de nuevo. No debe haber bloqueo ni pérdida de los datos de prueba. Si hay error, siga el apartado 10.

## 4. Navegación e interfaz

1. Recorra Inicio, Catálogo, Producción, Historial y Ajustes; compruebe que el nombre y selección coinciden.
2. Abra Herramientas: Objetivos, Estadísticas, Backups, Git y RoDo. Llegue hasta la última opción con ratón y teclado; RoDo puede indicar función reservada, sin simular conversaciones.
3. Use Tab, Mayús+Tab, Enter y Escape. Debe verse el foco y poder salir de campos y detalles. No active comandos de escritura.
4. Revise 1440×900 y 1200×700, en ventana cuando el escritorio lo permita. Si hay escala alta, registre la escala real; no la confunda con los PNG de 96 DPI. Compruebe filtros, fechas completas, textos, contraste, botones y desplazamiento. Anote cualquier campo oculto.

## 5. Bandeja

Pulse “Minimizar a bandeja”, compruebe el icono de NAP en la zona de notificación (incluidos iconos ocultos), y restaure con doble clic. Pruebe Abrir NAP y navegación del menú. En Ajustes → General active minimizar durante esta sesión y use el botón de minimizar de Windows. Salga desde el menú y confirme que NAP cierra. No cierre ni reinicie ningún host productivo.

## 6. Diagnóstico local

En Ajustes → Diagnóstico, actualice sin host. Un estado vacío comprensible es correcto; no deben inventarse trabajos. Pruebe filtros de fecha, gravedad y categoría, incluyendo un filtro sin resultados; no espere páginas si no hay suficientes registros. Use “Consultar información local” si aparece una desconexión. Copie o exporte el informe a un archivo .txt **nuevo en la carpeta de prueba entregada**, fuera del almacenamiento NAP. Abra el texto y verifique que no incluye rutas, IDs privados, claves ni nombres reales. No exporte bases, perfiles privados o logs completos del equipo.

## 7. Licencias y persistencia

En Ajustes → Licencias, compruebe ImageSharp y fecha administrativa **18/01/2028**. Los avisos previstos son 20/10/2027, 19/12/2027 y 11/01/2028, más vencimiento. No representan certificación del proveedor. En octubre de 2026 no se espera aviso de renovación. Cierre/reabra NAP y confirme que la información sigue presente. No abra sixlabors.lic.

En el registro **exclusivamente de prueba**, si el responsable lo ha permitido, registre renovación administrativa hasta 18/01/2029, cierre y reabra y confirme persistencia y recálculo. No haga esto en el perfil ordinario. No puede comprobar atendido sin un aviso pendiente: marque “no aplicable en fecha real”, consulte los escenarios sintéticos de la galería y no cambie el reloj. Umbrales, atención y saltos de días están cubiertos por tests con fecha inyectada. Dejar abierta la app al cambio natural de día es opcional y aún pendiente; no bloquear el equipo toda la noche para simularlo.

## 8. Histórico sintético

1. Abra Ajustes → Histórico sintético: deben aparecer 39 referencias, ocho grupos y aviso de simulación sin homologación.
2. Lea más filas con la lista ampliada; los filtros deben permanecer accesibles. Seleccione una fila: aparece su detalle lateral. Escape o Cerrar detalle vuelve al inventario completo.
3. Filtre children y elder; compruebe que son agrupaciones históricas, con roles explícitos. Revise rutas histórica/propuesta e incidencias, desplazando **el detalle** para leer la propuesta completa.
4. Busque una referencia, pruebe una combinación sin resultados y restablezca. Cambiar universo operativo no debe conservar selección antigua; no configure raíces reales para hacerlo.
5. Exporte simulación a un .txt nuevo fuera de NAP. Debe decir sintético, no crear carpetas canónicas ni ofrecer adoptar/publicar/corregir originales. Si propone operación real, rechace.

## 9. Producción, Historial y funciones manuales

Sin host, compruebe que la desconexión se explica y no hay contadores ficticios ni acciones automáticas habilitadas. Abra Operaciones manuales en Producción y las herramientas antiguas para confirmar acceso; no prepare ZIP, publique, restaure, haga commit/push, borre copias ni active universos. Las capturas de incidencias/comparación son laboratorio sintético; no use assets reales para reproducirlas. Aprobación/continuación reales y equivalencia operativa manual requieren otro ensayo sintético autorizado y no quedan aceptadas por mirar botones.

## 10. Ante un fallo

Pare la acción; no repita una escritura con resultado desconocido. Anote paso, hora, SHA del manifiesto, mensaje visible y si había host. Si NAP responde, exporte solo el informe anonimizado a un archivo nuevo. Adjunte una captura sin datos privados, resolución/escala y el formulario de comprobación. Si no arranca, recoja el mensaje sin rutas privadas ni claves; no busque ni envíe sixlabors.lic, dumps de memoria o bases completas. Entregue por la carpeta privada acordada, no por un issue público con información privada. No borre diarios, leases o evidencias ni intente reparar permisos.

## 11. Decisión

Aceptar la revisión de interfaz solo si todos los puntos aplicables pasan, SHA/hashes coinciden, no hay errores de navegación/persistencia ni operación real aparente y el responsable firma. Rechazar por bloqueo, cierre inesperado, texto oculto que impida operar, pérdida de registro de prueba, datos reales, permiso/elevación inesperada, licencia/secretos incluidos o capacidad de saltar validaciones. Un requisito previo ausente es **bloqueado**, no aprobado.

La firma certifica únicamente el alcance observado. No instala NAP, no autoriza producción/CUTOVER ni cierra #47/#49. La Fase 6 no está definitivamente certificada hasta esta aceptación y la decisión humana correspondiente.
