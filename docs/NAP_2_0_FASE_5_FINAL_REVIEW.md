# Fase 5 — corrección final y alcance aprobado (2026-10-20)

La orden del responsable retira el gate de 1000 ZIP. El uso previsto es 1–20 ZIP y el resultado histórico integral de 100/100 es suficiente para volumen. **No están autorizadas nuevas pruebas de carga de 20, 50, 100, 1000 o más ZIP, ni repetir baterías de volumen menores ya superadas.** El fallo histórico 280/1000 sigue siendo Failed; retirar un requisito no cambia un resultado.

Antes de ejecutar pruebas nuevas o publicar, se revisaron los dos workflows. La ejecución previa `39468522189`, iniciada con el HEAD anterior, se canceló de forma controlada; GitHub confirma `completed/cancelled`. No se borraron resultados ni artefactos. Coordinación Windows/Linux y WPF habían terminado satisfactoriamente; las regresiones interrumpidas no certifican aceptación. Se conservó el artefacto parcial Windows para trazabilidad, sin reiniciar sus baterías.

`tests/functional.runsettings` excluye `AutomationVolume`, `AdmissionVolume` y sus nombres completos. Es la configuración por defecto del proyecto de tests y explícita de las dos regresiones generales CI. Además, ambas teorías históricas llevan `Skip` explícito por la prohibición del responsable, incluso si un filtro CLI reemplaza el filtro de configuración. Sus assertions y evidencias se conservan. El otro caso con 64 ZIP era una carrera funcional entre páginas: ahora prepara tres ZIP y usa páginas de dos; conserva barrera, offset no cero y prioridad de ejecución antes de nueva preparación/auditoría. No hay cambio de límites del producto.

El workflow CUTOVER sigue siendo exclusivamente manual y requiere autorización expresa para cuentas/ACL; no se ejecutó. Las selecciones automáticas restantes son funcionales, seguridad, recuperación, coordinación y WPF.

Los cinco casos Windows de symlinks conservan creación real de enlaces, rechazo y preservación de destinos. Este MSI carece del privilegio de creación; no se han cambiado cuentas, privilegios, permisos ni configuración. CI Windows exigirá los cinco resultados Passed presentes exactamente una vez; una omisión o falta de privilegio no se contabiliza como éxito. Resultado del nuevo ensayo: pendiente.

La revisión de ImageSharp y los resultados finales se incorporarán antes de cerrar esta entrega. No se ha modificado producción, Nimroel real, main ni ajustes de protección de GitHub. PR existente: [#48, Draft](https://github.com/robdor80/NAP/pull/48).
