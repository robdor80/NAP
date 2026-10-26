# NAP 2.0 — Informe técnico P6.8

23 de octubre de 2026 · rama `codex/nap-2-fase-6-ux` · base de entrega P6.7 `35625ee`. El SHA efectivamente probado, las ejecuciones y los resultados exactos figuran en [Resultados](../C-Pruebas-evidencias/Resultados.md) y [manifiesto](../C-Pruebas-evidencias/evidence.json). Los commits documentales posteriores no se atribuyen pruebas nuevas.

## Alcance y resultado

Estabilización de Fase 6, conservando RobStyle, cinco secciones y operaciones anteriores. Se corrigen inventario histórico, evidencia repetida y xUnit2030, y se elimina una instancia oculta redundante del histórico. No cambia Core, contratos de autorización ni almacenamiento productivo. La nueva presentación es un ajuste limitado de la herramienta secundaria histórica: filtros permanecen arriba, lista con altura disponible y detalle lateral, cada uno con desplazamiento propio. Procedencia ampliable desde el aviso de simulación. No rediseña otras pantallas.

## Evidencia trazable

La fuente es el SHA del manifiesto, anterior a commits de evidencia y documentación. El workflow construye App y assembly de tests en Release Windows con licencia oficial; ejecuta exclusivamente el filtro de 157 casos y renderiza 38 escenarios a dos tamaños. El caso antiguo que crea un ZIP sintético se excluye expresamente; no se ejecutan pruebas de ZIP ni assets en P6.8. Los escenarios de comparación generan píxeles ilustrativos en memoria; el histórico es metadata cerrada. Los exports verifican texto sintético nuevo en el runner.

Cada PNG se conserva sin modificación. Los sidecars identifican página, sección, selección, capacidades, tamaño, hash y errores de binding; las comparaciones añaden zoom, guías y hashes de buffers. Las aserciones de captura exigen inventario visible y rutas/propuesta completas en la vista nombrada. Las vistas que no cambian píxeles se omiten dentro del mismo escenario. El workflow rechaza PNG idénticos entre evidencias. La verificación offline compara también píxeles decodificados, dimensiones, TRX, fuentes y hashes de entrega.

Los 34 PNG de P6.7 permanecen intactos. Seis pares idénticos se catalogan en [Duplicados-P6.7](../C-Pruebas-evidencias/Duplicados-P6.7.md): 28 imágenes distintas, sin atribuirles 34 validaciones funcionales. Los fallos preliminares P6.8 se conservan por ejecución; no se agregan los tests aprobados de cada intento como si fueran casos nuevos.

## Auditoría transversal

- [Matriz P6.1–P6.7](Matriz-P6.1-P6.7.md): criterios, fuentes, pruebas realizadas y comprobaciones pendientes.
- [Defectos y correcciones](Defectos-y-correcciones.md): alcance de cada cambio y fallos preliminares.
- [Seguridad](Informe-seguridad.md): autoridad, universos, autorización, IPC, durabilidad, export y compatibilidad.
- [Revisión visual](Revision-visual.md): pantallas y contraste de tokens, sin certificar accesibilidad humana.
- [Riesgos y limitaciones](Riesgos-y-limitaciones.md) y [decisiones pendientes](Decisiones-pendientes.md).

## Preparación de aceptación y cierre

[Guía del Raider](../D-Aceptacion-MSI/Guia-MSI.md), [acta sin cumplimentar](../D-Aceptacion-MSI/Lista-comprobacion.md), [preparación del cierre](../E-Cierre-distribucion/Preparacion-cierre-Fase6.md) y [distribución futura](../E-Cierre-distribucion/Distribucion-futura.md).

El paquete privado no se genera por falta de un canal binario privado validado en el workflow del repositorio público y revisión de sus dependencias/licencia. La guía indica cómo identificar y obtener posteriormente una compilación de prueba completa sin improvisar ni copiar sixlabors.lic. La aceptación queda bloqueada hasta esos requisitos y la sesión aislada autorizada.

`main`, CUTOVER, #47/#49 y assets reales se mantienen intactos. Sin PR, fusión, instalación, cuentas/ACL/servicios, Release pública, updater, web ni Fase 7. Preparación técnica no equivale a certificación productiva.
