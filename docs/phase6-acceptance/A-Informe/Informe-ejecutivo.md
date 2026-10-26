# NAP 2.0 — P6.8 · Auditoría y preparación de cierre

23 de octubre de 2026. Rama exclusiva `codex/nap-2-fase-6-ux`.

**Preparada técnicamente para revisión humana de Fase 6; aceptación funcional Windows 11 y certificación productiva pendientes.** No se ha instalado ni ejecutado nada en el MSI Raider GE78 HX.

## Qué se ha corregido

El inventario histórico usa la altura disponible: aproximadamente 401 px a 1440×900 y 201 px a 1200×700, frente al límite anterior de 180 px y su scroll exterior. Los filtros permanecen visibles, el detalle se consulta lateralmente y las rutas/propuestas tienen evidencias específicas. Se conservan las cinco secciones y RobStyle.

Se corrige xUnit2030 con Assert.Contains, se elimina una instancia oculta redundante del histórico y se simplifica texto técnico de Ajustes. Las capturas registran el estado real, omiten vistas sin cambio y comprueban que la zona nombrada esté en pantalla. P6.7 conserva sus 34 PNG intactos: se documentan seis pares idénticos y 28 imágenes distintas.

## Comprobación realizada

**Código verificado: `ed556bbbdf8a6971cd22d38f96ff88885a366225`.** [Actions 39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656).

- Dos builds Release Windows correctos, con validación oficial de licencia.
- **157/157 pruebas focalizadas**, cero fallos/omisiones; no se suman ejecuciones históricas.
- **113 PNG WPF distintos**, 38 escenarios en dos tamaños y 76 procesos. Cero errores de binding; prueba nativa de ocultar/restaurar bandeja.
- Metadatos por captura, TRX, logs, hashes y comparación de fuentes; cinco intentos preliminares conservados con sus fallos de render y estado real.
- Sin tests de ZIP, regresión general, volumen, benchmark, estrés ni assets reales. Los ejemplos de comparación son píxeles sintéticos en memoria.

Windows Server 2022 del runner y render a 96 DPI no sustituyen la experiencia del Raider Windows 11 ni una aceptación de teclado, DPI real, persistencia y bandeja por el responsable.

## Reservas que siguen abiertas

#49: timeout IPC sin causa demostrada, StorageUnavailable histórico sin explicación y cinco advisories NU1902/NU1903 de ImageSharp 3.2.0, visibles y sin supresión. Pasar pruebas sintéticas no resuelve estos riesgos.

Las políticas se invalidan por estado/referencia/actor, pero el contrato actual no tiene caducidad por fecha. Se documenta como limitación pendiente de decisión; no se amplían contratos ni se relajan autorizaciones.

#47/CUTOVER continúa bloqueando cualquier uso real. Las operaciones manuales mantienen sus adaptadores Core; esta revisión no demuestra el aislamiento efectivo de antiguos escritores en el MSI.

## Aceptación del Raider y paquete privado

La guía y el acta están en D. Incluyen requisitos, identificación inequívoca del SHA, arranque/cierre, navegación, bandeja, diagnóstico local, licencias, histórico, acceso manual, fallos y criterios de aceptación/rechazo.

**No hay paquete ejecutable privado preparado.** El workflow disponible compila/renderiza sobre un repositorio público y no dispone de un canal binario privado validado. Un artefacto ordinario de Actions no debe presentarse como distribución privada. La guía exige un paquete Windows x64 completo, manifiesto/hashes, revisión de dependencias/licencia y sesión Windows aislada ya autorizada; un --settings alternativo no aísla por sí solo el registro administrativo. No se copia sixlabors.lic ni se improvisa su tratamiento. Sin esos requisitos, la aceptación es “bloqueada”, no “aprobada”.

## Cierre e integración futura

La entrega A–E contiene matriz transversal, defectos/riesgos/seguridad, galería y originales, pruebas/ejecuciones/hashes, guía del Raider, plan de cierre y especificación futura de instalador .exe x64, Releases, enlace web y evaluación de Velopack. Todo está preparado documentalmente, sin implementar distribución.

Antes de una futura PR: aceptación humana del alcance y reservas, autorización nueva de CI general, revisión de disparadores, revisión del SHA integrado y checks acordados satisfactorios. No abrir la PR para provocar pruebas que hoy están prohibidas. La fusión necesita decisión humana independiente y no autoriza CUTOVER.

`main` permanece en `5f81950275098881fd0befac0a13cde08c9d751f`. Sin PR, fusión, cambios en #47/#49, assets originales, cuentas/ACL/servicios, instalación, Release pública, updater, web ni Fase 7. El commit final documental se identifica en el cierre de entrega; no se atribuye una nueva ejecución de código.
