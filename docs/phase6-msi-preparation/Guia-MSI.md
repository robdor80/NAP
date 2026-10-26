# Guía del responsable — NAP 2.0 en MSI

24/10/2026 · Preparación de Fase 6 · Prueba humana aún no iniciada

## Antes de empezar

**Estado actual: BLOQUEADO. No descargues ni abras un ejecutable de NAP para esta prueba.** La carpeta Programa contiene el estado y un manifiesto documental, no el programa. Puedes leer esta guía y las capturas sin encender el MSI. No hacen falta Visual Studio, VS Code ni herramientas de programación.

Faltan un canal privado de transferencia desde el compilador, un modo de aceptación interactivo que proteja tus datos y el cierre de avisos/dependencias del paquete. La compilación correcta en nube no resuelve esos puntos. Esta guía describe la sesión futura y marca dónde se detiene hoy.

No cambies ni borres carpetas de NAP, no copies licencias, no cambies cuentas, Registro ni permisos y no instales servicios. No uses administrador. No desactives Defender, SmartScreen ni otras protecciones. Si Windows bloquea un archivo, detén la prueba y comunica el mensaje; no pulses opciones para eludirlo.

## Descarga y procedencia

1. Abre la carpeta privada de esta entrega en Drive con la cuenta propietaria. Ahora descarga únicamente el informe o la guía PDF. **Aquí se detiene hoy la preparación del responsable.**
2. Cuando el estado se sustituya por «Listo para aceptación privada», comprueba con ChatGPT que el enlace, nombre del paquete, SHA de código compilado y manifiesto corresponden a la misma entrega. No uses adjuntos reenviados ni otro ejecutable encontrado en GitHub. La comprobación de integridad deberá contar con una herramienta segura preparada en esa entrega; hoy no existe tal herramienta para el responsable.
3. Solo con ese paquete validado, elige «Extraer todo» en Windows y una carpeta nueva para la prueba. Conserva juntos todos los archivos extraídos. No los mezcles con otra instalación y no abras solo un EXE sacado del conjunto.

## Sesión futura, después de cerrar todos los bloqueos

4. Abre únicamente el acceso de aceptación aislada que indique la futura entrega. Hoy no se ha creado ese acceso: abrir directamente el NAP.App.exe ordinario puede escribir en tus datos administrativos. Si se solicitan raíces, licencia privada, administrador o activar un host, detente.
5. Comprueba que el título identifica inequívocamente la prueba sintética. Recorre Inicio, Catálogo, Producción, Historial y Ajustes. La ausencia de assets reales es esperada. No configures carpetas ni actives operaciones manuales de producción, backups o Git.
6. Pulsa «Minimizar a bandeja» y comprueba que NAP desaparece de la barra de tareas. Recupera la ventana desde su icono junto al reloj; puede estar bajo la flecha de iconos ocultos. Anota si no vuelve.
7. En Ajustes → Diagnóstico, comprueba que se puede consultar información sin host. Un resultado vacío con «No hay registros con estos filtros» puede ser correcto. No conectes ni arranques AutomationHost para cambiarlo.
8. En Ajustes → Licencias, comprueba los avisos y la indicación «Fecha administrativa · no verificada por el proveedor». No abras, copies o reemplaces sixlabors.lic. No cambies la fecha de Windows para provocar avisos. La futura edición deberá indicar si sus cambios son solo en memoria; hoy no se garantiza persistencia aislada.
9. En Ajustes → Histórico sintético, comprueba que se identifica como simulación y muestra 39 referencias en ocho grupos. Puedes revisar filtros y detalles. No aporta una homologación real ni permite usar assets productivos.
10. Cierra mediante la X, comprueba que desaparece también el icono de bandeja y vuelve a abrir el mismo acceso de aceptación. Anota si se pierde la navegación o una preferencia que la futura guía diga que debe persistir. Las muestras en memoria podrán reiniciarse; no interpretar eso como pérdida de datos reales.
11. Si aparece un problema, detén la operación, conserva el mensaje y usa el procedimiento de la página siguiente. No repitas cambios de datos ni intentes reparar la configuración por tu cuenta.

Las capturas de esta guía son renders WPF de un runner Windows Server 2022, tamaño lógico 1200×700. No proceden del MSI ni acreditan su aceptación. Muestran controles orientativos; las fechas y datos son ficticios.

## Comunicar un problema

En esta etapa basta indicar a ChatGPT qué documento o enlace falla. Para la sesión futura, usa esta ficha:

- Versión/SHA que muestra la entrega y sección abierta.
- Qué pulsaste, qué esperabas y qué apareció.
- Texto o código del mensaje; si hubo bloqueo de Windows, su mensaje exacto.
- Si la ventana se cerró, quedó en bandeja o no respondió; si sucedió al volver a abrir.
- Captura de la ventana de NAP, revisada para no incluir rutas, nombres o documentos personales.

Comparte la ficha aquí o en la carpeta privada autorizada; no la publiques en el repositorio público con información personal. No adjuntes licencias, tokens, carpetas de usuario, bases de datos ni assets. Cuando exista modo aislado validado, el botón de informe deberá guardar solo texto anonimizado dentro de su carpeta de prueba; ese recorrido aún no está preparado. No uses una exportación arbitraria para comprobar el aislamiento actual.

## Lista sencilla de comprobaciones

Cada casilla está pendiente de una entrega apta; ninguna se marca por las pruebas de nube.

- [ ] Estado «Listo», descarga privada y procedencia/integridad verificadas.
- [ ] Inicio inequívoco en modo sintético sin leer datos habituales.
- [ ] Cinco secciones visibles y navegación clara.
- [ ] Minimizar y recuperar desde bandeja.
- [ ] Diagnóstico consultable sin host.
- [ ] Licencias claras, sin archivos privados ni cambios de reloj.
- [ ] Histórico sintético identificado y filtros comprensibles.
- [ ] Cierre y reapertura normales.
- [ ] Informe seguro disponible si falla algo.
- [ ] Datos ordinarios preservados según mecanismo previamente validado.

No se declara superada la aceptación humana. El siguiente paso técnico es cerrar los bloqueos; después podrá celebrarse una sesión del responsable en Windows 11 guiada por ChatGPT.
