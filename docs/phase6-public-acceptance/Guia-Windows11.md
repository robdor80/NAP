# Abrir NAP de prueba en Windows 11

Esta guía es para la aceptación visual con datos ficticios. No utiliza tus datos habituales. No necesitas que otra persona acceda al MSI.

## 1. Entra en GitHub

Abre el navegador y entra en GitHub con tu cuenta normal. No necesitas un token ni instalar programas. GitHub pide iniciar sesión para descargar este tipo de archivo, aunque el repositorio sea público.

## 2. Descarga el programa

[Pulsa aquí para abrir la descarga](https://github.com/robdor80/NAP/actions/runs/40150398814/artifacts/12582294018). Si GitHub muestra la ejecución en lugar de descargar directamente, baja a **Artifacts** y pulsa **NAP-2.0-Fase6-Aceptacion-Windows-x64**. [Esta es la ejecución correcta](https://github.com/robdor80/NAP/actions/runs/40150398814). Debe figurar como correcta, con marca verde.

Se descargará **NAP-2.0-Fase6-Aceptacion-Windows-x64.zip**, de unos 75 MB. Usa este artefacto; el que contiene la palabra evidencias es para los informes técnicos.

## 3. Descomprime todo

En Descargas, pulsa con el botón derecho sobre el ZIP y elige **Extraer todo…**. Elige una carpeta nueva de tu usuario y pulsa **Extraer**. Espera a que termine. Abre la carpeta extraída; no inicies NAP desde la vista del ZIP.

## 4. Identifica el iniciador seguro

Busca **Iniciar-prueba-NAP.cmd** y haz doble clic. Si Windows oculta las extensiones, puede aparecer como **Iniciar-prueba-NAP** y su tipo será archivo de comandos de Windows. No hace falta escribir nada en una consola.

No elijas Ejecutar como administrador. No instales Visual Studio, VS Code ni .NET: el paquete lleva lo necesario. Si Windows muestra un bloqueo o advertencia de seguridad, conserva el mensaje y una captura y consulta con ChatGPT. No desactives Defender ni SmartScreen ni fuerces el bloqueo.

## 5. Comprueba la modalidad

La ventana debe indicar **ACEPTACIÓN AISLADA** en el título y **ACEPTACIÓN · DATOS FICTICIOS · SOLO MEMORIA** en la cabecera. Si no aparece ese aviso, cierra la ventana y comunícalo a ChatGPT. La consola del iniciador puede permanecer abierta mientras usas NAP; es normal.

## 6. Recorre las pantallas

Pulsa **Inicio**, **Catálogo**, **Producción**, **Historial** y **Ajustes**. Catálogo está vacío a propósito. En Producción e Historial hay cuatro filas ficticias: selecciona una, abre sus detalles y prueba el buscador y los filtros. Las acciones productivas estarán desactivadas. La indicación Procesando es una muestra; no se procesa ningún archivo real.

En Ajustes consulta **Diagnóstico**, **Licencias** e **Histórico sintético**. Puedes filtrar registros, probar una renovación ficticia y buscar/abrir referencias históricas. La renovación se pierde al cerrar. Los informes se consultan en pantalla; no se exportan a archivos en esta modalidad.

En Herramientas puedes ver Objetivos, Estadísticas, Backups, Git y RoDo. Algunas muestran información vacía o reservada. No crean backups ni ejecutan Git. Inicio muestra automatización sin configurar: es intencionado, no se conecta ningún host.

## 7. Minimiza y recupera

Pulsa **Minimizar a bandeja**. Busca el icono NAP junto al reloj de Windows; si no se ve, pulsa la flecha de iconos ocultos. Haz doble clic sobre NAP para recuperar la ventana, o pulsa con el botón derecho y elige **Abrir NAP**. También puedes probar el botón normal de minimizar: esta sesión está configurada para ir a bandeja.

## 8. Cierra correctamente

Pulsa la **X** de la ventana o elige **Salir** en el menú del icono de bandeja. La ventana y su icono deben desaparecer. No hay cambios de sesión que guardar.

## 9. Cuenta el resultado a ChatGPT

Indica qué pantallas has visto, si pudiste minimizar/recuperar/cerrar y qué problemas aparecieron. Si hubo un error, añade los pasos, el mensaje exacto y una captura. Usa Ficha-incidencia.txt si te ayuda. No hacen falta contraseñas ni tokens.

## Referencia de esta prueba

Commit d35a8c60ba2318f4950edfed339692f259aed8ed. Ejecución 40150398814. Descarga disponible hasta 2027-01-23T01:22:51Z. No es aceptación de producción ni cierre definitivo de Fase 6. Si vas a repetir la prueba después del 10/11/2026, consulta con ChatGPT para revisar el runtime incluido antes de reutilizarla.
