# Preparación de cierre de Fase 6

## Estado

P6.1–P6.7 tienen antecedentes de revisiones técnicas/visuales sintéticas favorables según autorización del responsable. P6.8 estabiliza y documenta ese conjunto. Los resultados nuevos se identifican en C por SHA y ejecución. No se declara aceptación física del MSI ni certificación productiva.

Funcionalidades conservadas: cinco secciones, herramientas secundarias, resumen con alcance de universo, control host, bandeja, trabajo/historial durables, revisión individual Core, diagnóstico seguro/local, registro administrativo de licencias y referencia histórica cerrada. Las operaciones manuales anteriores conservan sus contratos y accesos. Matriz A detalla las pruebas y sus límites.

Defectos corregidos: altura y scroll histórico, captura diferenciada y visible, xUnit2030, instancia oculta redundante y texto técnico habitual. Los fallos preliminares de la propia estabilización se conservan. Pendientes de producto/operación: caducidad temporal no presente en contrato, aceptación manual y paquete privado no disponible; riesgos IPC/StorageUnavailable/ImageSharp preexistentes de #49. No se declaran solucionados por no reproducirse.

Evidencia: PNG originales a dos tamaños con estado/hash; galería PDF/MD; TRX; stdout/stderr; logs build/render; metadatos Actions; manifiestos y verificación de fuentes; inventario de duplicados anteriores. Runs preliminares en carpetas propias, sin sobrescribir P6.1–P6.7. No sumar ejecuciones ni imágenes como tests independientes.

## Pruebas todavía pendientes

Windows 11 del Raider: teclado, DPI real, bandeja/menús, persistencia/reinicio, suspensión/cambio natural de día y juicio humano RobStyle. Equivalencia ejecutando operaciones manuales sobre un universo aislado, aceptación del binario privado completo y despliegue multiidentidad no se han ejecutado aquí. Regresión general, cargas y volumen no están autorizados en P6.8. No certifica assets históricos reales.

## Dependencias y futuras puertas

- #47 sigue bloqueando uso real: identidad/raíces/padres/antiguos escritores, backup externo verificado, leases y recuperación, despliegue protegido y autorización final independiente.
- #49 sigue abierta: causa de IPC y StorageUnavailable sin resolver, avisos ImageSharp visibles. Integración de software con reservas no equivale a solución.
- Paquete privado y aceptación humana según D; ninguna instalación automática ni preparación de cuentas/ACL.

## Futura PR y CI (no ejecutadas)

1. Obtener aceptación humana del alcance Fase 6 y decidir expresamente las reservas. Publicar acta privada anonimizada si procede.
2. Comparar la rama con main en lectura; registrar SHA base y cabeza; resolver conflictos en la **misma rama** solo con alcance aprobado y repetir pruebas afectadas por nuevos cambios.
3. Antes de abrir PR, revisar `.github/workflows/ci.yml`: una PR a main dispara Linux/Windows generales, coordinación y WPF. La autorización actual prohíbe esas suites; se requiere autorización nueva y explícita para ellas. No abrir PR “para ver qué ocurre”.
4. Recomendar PR inicialmente borrador con descripción concreta, matriz/evidencias y SHA verificado, solo después de aprobar la estrategia CI. Si se decide ejecutar suite general, delimitar runners/secretos y preservar todos los resultados. Mantener fuera ZIP volume, CUTOVER y cambios de cuentas/ACL salvo autorización independiente. No rebajar tests obligatorios para obtener verde.
5. Si main avanzó, revalidar el resultado integrado sobre su SHA exacto. Revisión de seguridad/licencia y checks acordados deben pasar; fallos sin explicar bloquean fusión.
6. Fusionar solo con aprobación humana, revisión de código, CI acordada satisfactoria y reservas explícitas. Mantener #47/#49 abiertas; no habilitar producción ni distribuir por la mera fusión.

Condición de cierre: informe completo, evidencia íntegra, rama limpia/publicada y aceptación humana pendiente identificada. **Preparada técnicamente para revisión; no certificada definitivamente.** Detener aquí; no Fase 7.
