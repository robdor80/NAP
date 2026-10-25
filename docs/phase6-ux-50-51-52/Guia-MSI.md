# Segunda comprobación en el MSI

1. Descargar el nuevo artefacto de GitHub Actions, extraerlo completo en una carpeta nueva y ejecutar Iniciar-prueba-NAP.cmd. No requiere SDK ni herramientas de desarrollo. Confirmar el rótulo ACEPTACIÓN / DATOS FICTICIOS / SOLO MEMORIA.
2. En Ajustes > Licencias, desplegar Nueva fecha de vencimiento. Comprobar legibilidad de días, cabecera, navegación, meses y años; elegir una fecha conocida y verificar que el editor y la renovación ficticia muestran esa misma fecha. Repetir el calendario Desde/Hasta de Diagnóstico y confirmar que DIAGNÓSTICO, Información local y Exportar informe… aparecen correctamente acentuados. Revisar hover, selección, hoy y foco de teclado.
3. En Ajustes > Histórico sintético, comprobar las etiquetas y separaciones. Desplegar grupos y estados: Todos los grupos · 39 debe caber en una línea. Buscar 001 (1 resultado), pulsar Restablecer (39), elegir farmer (10), volver a Restablecer.
4. Repetir con ventana grande y compacta, aproximadamente 1440x900 y 1200x700, usando el escalado habitual del MSI. Confirmar ausencia de controles superpuestos o texto partido innecesariamente.
5. En Herramientas, seleccionar Objetivos, Estadísticas, Backups, Git y RoDo. Verificar bordes completos y un solo contorno de selección. Recorrer el submenú con Tab para comprobar que el foco de teclado se distingue de la selección.
6. Cambiar universo, minimizar a bandeja y restaurar, cerrar y abrir de nuevo. Confirmar que las acciones productivas siguen bloqueadas y que la sesión sintética no persiste.
7. Comunicar el resultado visual de #50, #51 y #52. Permanecen abiertas hasta esta confirmación; no declarar cerrada la Fase 6.


## Entrega verificada

Commit compilado: `0a27b9c50e528db3e77f87f85e73eb266379cd96`.

Ejecución: https://github.com/robdor80/NAP/actions/runs/40243110761

Descarga: https://github.com/robdor80/NAP/actions/runs/40243110761/artifacts/12618090727

SHA-256 del ZIP descargado de Actions: `7afdaab8b054b491ebe89ebce2608d680c6fa27acc882b6e733689b9f7723671`. El ZIP interno del ensayo usa otra compresión; este es el hash de la descarga del usuario.

Caducidad indicada por GitHub: 2027-01-23T23:04:47Z. Requiere sesión en GitHub. No es una Release.

49 pruebas focalizadas superadas; cero errores de binding. Búsqueda 001: 1; Restablecer: 39; farmer: 10. Diez comprobaciones geométricas del submenú superadas; las diez fallaban en la referencia anterior. Cuadrículas de meses y años comprobadas con doce botones visibles y días ocultos. Selección de fecha y navegación verificadas.

ETW: cero accesos a ubicaciones habituales de NAP, cero escrituras fuera del laboratorio, cero procesos hijos y cero eventos perdidos. 491 archivos verificados por manifiesto tras descargar el artefacto. Arranque directo y CMD, argumentos y junctions inseguros, renovación en memoria, cambio de universo y bandeja superados. Sin licencias privadas ni patrones de secretos en el paquete. Avisos legales conservados.
