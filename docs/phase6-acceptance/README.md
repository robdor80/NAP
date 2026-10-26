# P6.8 — Auditoría y preparación de aceptación final

23/10/2026 · NAP 2.0 · RobStyle · rama `codex/nap-2-fase-6-ux`.

**Preparada técnicamente para revisión humana, sin certificación final del MSI ni autorización productiva.** Código efectivamente verificado: `ed556bbbdf8a6971cd22d38f96ff88885a366225`. [Actions 39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656): dos builds Release Windows, 157/157 casos focalizados y 113 PNG WPF distintos. Los commits posteriores de archivo y documentación no se presentan como nuevas pruebas de código.

- **A. Informe:** [PDF ejecutivo](A-Informe/Informe-ejecutivo.pdf), [Markdown técnico](A-Informe/Informe-tecnico.md), [matriz](A-Informe/Matriz-P6.1-P6.7.md), [defectos](A-Informe/Defectos-y-correcciones.md), [seguridad](A-Informe/Informe-seguridad.md), [revisión visual](A-Informe/Revision-visual.md), [riesgos](A-Informe/Riesgos-y-limitaciones.md) y [decisiones](A-Informe/Decisiones-pendientes.md).
- **B. Capturas:** [galería PDF](B-Capturas-WPF/Galeria-WPF.pdf), [galería Markdown](B-Capturas-WPF/Galeria-WPF.md), [PNG originales en Drive](B-Capturas-WPF/Galeria-Drive.md). Los 113 originales y sidecars están en [runs/39921263656](runs/39921263656).
- **C. Evidencia:** [resultados exactos](C-Pruebas-evidencias/Resultados.md), [manifiesto](C-Pruebas-evidencias/evidence.json), [TRX final original](runs/39921263656/phase6-acceptance.trx), [logs finales](runs/39921263656/build-and-review.log), [ZIP originales de las seis ejecuciones](archives/), [metadatos Actions](C-Pruebas-evidencias/actions-runs.json), [duplicados anteriores](C-Pruebas-evidencias/Duplicados-P6.7.md), [transferencia y recuperación](C-Pruebas-evidencias/Transferencia.md), [fuentes](C-Pruebas-evidencias/verified-source-files.json), [lista de cambios](C-Pruebas-evidencias/changed-files.txt) y [hashes](SHA256SUMS.txt).
- **D. Raider GE78 HX:** [guía PDF](D-Aceptacion-MSI/Guia-MSI.pdf), [guía Markdown](D-Aceptacion-MSI/Guia-MSI.md) y [acta pendiente](D-Aceptacion-MSI/Lista-comprobacion.md). No hay paquete privado ejecutable preparado; requisitos y bloqueo explicados en la guía.
- **E. Cierre:** [preparación de integración/CI](E-Cierre-distribucion/Preparacion-cierre-Fase6.md) y [distribución futura](E-Cierre-distribucion/Distribucion-futura.md).

Entrega en [Drive: NAP 2.0/Fase 6/P6.8 Auditoría y cierre](https://drive.google.com/drive/folders/1kvNzb-xWJ549TcxjYSEBrBMUtg_3Mold), manteniendo los permisos. Los enlaces individuales observados figuran en [drive-files.json](C-Pruebas-evidencias/drive-files.json).

Verificación desde la raíz del repo: `python docs/phase6-acceptance/Verify-evidence.py` (Python 3/Pillow). Contrasta TRX, dimensiones, estados/píxeles, bytes de los seis ZIP originales con sus digest Actions, fuentes de producto/tests y hashes de entrega. La versión exacta del workflow que ejecutó las pruebas está en [verified-build-workflow.yml](C-Pruebas-evidencias/verified-build-workflow.yml); la opción posterior de recuperación no ejecuta tests/build/render.

Los cinco intentos preliminares permanecen en `runs/<id>` y `archives/actions-<id>.zip`. No sumar sus 157 casos ni presentar sus renders repetidos como pruebas distintas. P6.1–P6.7 permanecen sin sobrescribir.

Reservas: #49 (IPC, StorageUnavailable, ImageSharp); políticas sin caducidad temporal; Windows 11/teclado/DPI/bandeja/persistencia todavía requieren juicio humano; paquete privado pendiente de canal validado y revisión de dependencias/licencia. #47/CUTOVER sigue bloqueando cualquier uso real.

Sin PR, fusión, cambios de main/#47/#49/assets reales, instalación, cuentas/ACL/servicios, Release pública, web, updater ni Fase 7. Detener en P6.8.
