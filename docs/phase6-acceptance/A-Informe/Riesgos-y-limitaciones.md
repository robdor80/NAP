# Riesgos y limitaciones

| Riesgo / límite | Estado | Condición antes de considerarlo resuelto |
|---|---|---|
| #49: timeout IPC después de cancelación/reinicio | Preexistente abierto. Contratos dirigidos pueden pasar sin reproducirlo. | Encontrar causa, implementar corrección y demostrar recuperación con evidencia específica autorizada. |
| #49: StorageUnavailable histórico | Preexistente abierto. Vista de diagnóstico sintética representa el estado; no explica su causa. | Nueva reproducción trazable y causa demostrada. Conservar NeedsReview/sidecars/originales. |
| #49: advisories NU1902/NU1903 ImageSharp 3.2.0 | Avisos visibles, no suprimidos. Los backports del proveedor no eliminan por sí solos los metadatos de advisories. | Evaluación de seguridad y metadatos actuales antes de distribuir/desplegar. |
| Políticas sin vencimiento por fecha | Limitación confirmada de AutomationAuthorization/StoredAutomationPolicy; invalidación por estado/referencia/actor. | Si se exige TTL, contrato versionado y revisión de seguridad en alcance independiente. No se añade un TTL arbitrario en P6.8. |
| Autoridad escritora productiva en MSI | #47 abierta. IPC CurrentUserOnly y host sintético no demuestran aislamiento entre identidades reales. | Certificar identidad única, binarios/configuración/raíces/padres y antiguos escritores según CUTOVER autorizado. |
| Adaptadores manuales locales | Compatibilidad conservada: arranque ordinario usa Core local; con host se usa RemoteDesktopService, sin fallback. | No confundir “Core exclusivo” con “host obligatorio en todo arranque”. Convivencia/denegación efectiva en producción pendiente de #47. |
| Laboratorio WPF | Runner Windows Server 2022, render lógico a 96 DPI. | Aceptación Windows 11 física, DPI, teclado, bandeja y reinicio de app en MSI. |
| Histórico | 39 identidades ficticias, metadata cerrada, sin lectura de originales. | No certifica retratos históricos, integridad, adopción ni routing de assets reales. |
| Diagnóstico | Solo página actual o muestra local acotada; no todos los logs del sistema. | Si falta evidencia, pedir recogida controlada sin exportar secretos ni bases completas automáticamente. |
| Pruebas limitadas | Sin suite general, volumen ZIP, carga, benchmark, producción, cuentas/ACL/servicios ni reloj del MSI. | Alcance futuro separado; no inferir resultados no ejecutados. |
| Paquete privado MSI | No preparado: workflow disponible de build/render, repositorio público y sin canal privado binario validado. | Compilación Windows x64 por canal privado, dependencias y licencia revisadas, manifiesto y hashes. No usar Actions público como entrega privada. |

No se han modificado #47 ni #49 ni se propone cerrarlas por esta auditoría.
