# NAP 2.0 — P6.8: auditoría y preparación de cierre

[Entrega completa A–E](phase6-acceptance/README.md). [Informe ejecutivo PDF](phase6-acceptance/A-Informe/Informe-ejecutivo.pdf), [galería WPF](phase6-acceptance/B-Capturas-WPF/Galeria-WPF.pdf) y [guía del Raider](phase6-acceptance/D-Aceptacion-MSI/Guia-MSI.pdf).

Código efectivamente verificado `ed556bbbdf8a6971cd22d38f96ff88885a366225`, [Actions 39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656): dos builds Release Windows, 157/157 pruebas focalizadas, 113 PNG originales distintos, 38 escenarios a dos tamaños, cero errores de binding. Se conservan cinco intentos preliminares y sus resultados reales. Los seis ZIP originales cotejados con digest Actions y entradas byte por byte quedan en el repositorio. La recuperación documental posterior no repite pruebas.

Correcciones: inventario histórico con viewport útil aproximado 401/201 px, filtros accesibles y detalle lateral; captura real de estados/rutas/propuestas con metadatos y omisión de repeticiones; Assert.Contains para xUnit2030; histórico oculto redundante eliminado y texto habitual de Ajustes simplificado. Ningún cambio de Core o relajación de autorizaciones. Matriz transversal y límites de evidencia documentados.

Drive: [P6.8 Auditoría y cierre](https://drive.google.com/drive/folders/1kvNzb-xWJ549TcxjYSEBrBMUtg_3Mold). Sin cambios de permisos. Las evidencias anteriores no se sobrescriben.

Aceptación Windows 11 del MSI Raider GE78 HX pendiente. No hay paquete binario privado preparado: el workflow del repositorio público carece de canal privado validado; guía de preparación/dependencias/licencia y criterios de bloqueo incluidos. Riesgos #49 y autorización temporal ausente permanecen documentados; #47/CUTOVER sigue bloqueado.

Main conserva `5f81950275098881fd0befac0a13cde08c9d751f`; #47 y #49 abiertas sin modificar. Sin PR, fusión, assets reales, CUTOVER, instalación, cuentas/ACL/servicios, distribución pública, web ni Fase 7. La Fase 6 queda preparada para aceptación humana, sin certificación definitiva. **Parada en P6.8.**
