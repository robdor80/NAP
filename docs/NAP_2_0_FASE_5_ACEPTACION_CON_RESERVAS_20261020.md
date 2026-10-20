# NAP 2.0 — Fase 5: aceptación técnica CON RESERVAS

**Fecha:** 20 de octubre de 2026. **Repositorio:** `robdor80/NAP`. **PR:** [#48](https://github.com/robdor80/NAP/pull/48).

## Decisión del responsable

El responsable ha dado expresamente **luz verde para cerrar la Fase 5**, aceptando que las incidencias señaladas permanezcan **documentadas y sujetas a seguimiento posterior**. Esta aceptación se refiere **solo al alcance de desarrollo y validación del software de Fase 5**; no equivale a afirmar que las incidencias se hayan resuelto, ni concede autorización de despliegue/CUTOVER, producción o tratamiento de activos reales de Nimroel.

**Estado de aceptación:** Fase 5 **APROBADA TÉCNICAMENTE CON RESERVAS** (P5.1–P5.9 desarrollados; P5.10 aceptado de manera condicionada). La fusión de PR #48 es una operación posterior sujeta a las comprobaciones y precauciones de abajo.

## Reservas expresamente aceptadas para cierre de software

1. **Timeout IPC intermitente después de cancelación/reinicio:** causa sin demostrar; un caso local falló y comprobaciones dirigidas posteriores pasaron. No se afirma solución ni inexistencia de riesgo. Seguimiento en [#49](https://github.com/robdor80/NAP/issues/49).
2. **StorageUnavailable histórico:** causa sin demostrar y no reproducido recientemente; el tratamiento seguro NeedsReview y la conservación de originales/sidecars no convierten la incidencia en resuelta. Seguimiento en [#49](https://github.com/robdor80/NAP/issues/49).
3. **Cinco advisories NuGet ImageSharp 3.2.0:** Six Labors indica backports, pero persisten avisos publicados. No suprimir avisos ni declarar el análisis limpio; verificar actualización de metadatos y cualquier riesgo residual. Seguimiento en [#49](https://github.com/robdor80/NAP/issues/49).

## Evidencias que sostienen la decisión

- [CI histórico funcional](https://github.com/robdor80/NAP/actions/runs/39474539947) sobre ImageSharp 3.1.12: Windows **3784/3784**, Linux **3777 Passed / 7 NotExecuted**, WPF **4/4**. Los cinco symlinks reales y escenarios de recuperación que se requerían se verificaron en CI Windows.
- ImageSharp 3.2.0: **220/220 pruebas locales dirigidas** de imágenes; compilación Release y licencia Community validadas para NAP.Core.
- [Laboratorio remoto](https://github.com/robdor80/NAP/actions/runs/39488901573): compilación Release x64 **Windows y Linux correctas**, validación oficial `SixLabors_ValidateLicense` satisfactoria, temporal de licencia eliminado, **cero pruebas ejecutadas**. La evidencia se conserva en la rama de laboratorio sin importarla a la PR.
- El volumen histórico **100/100** cumple el requisito vigente. El fallo histórico **280/1000** sigue siendo Failed, y **1000 ZIP dejó de ser un requisito** por decisión del responsable. No se reescriben ni repiten resultados.
- La regresión funcional anterior **no** fue repetida sobre ImageSharp 3.2.0. Los resultados se atribuyen a sus versiones y SHAs originales, sin extender indebidamente su cobertura.

## Condiciones de cierre de repositorio

- Proteger `main` antes de fusionar, conforme a una política realista para un repositorio individual. No asumir que los checks históricos certifican un nuevo commit. No dejar reglas que obliguen a CI nueva si la ejecución de esas baterías está expresamente prohibida.
- Evitar que la fusión dispare la CI general no autorizada. Verificar el mecanismo seleccionado **antes** de pulsar Merge y comprobar después el listado de Actions. No ejecutar nuevas pruebas para conseguir verdes artificiales.
- Conservar el seguimiento [#49](https://github.com/robdor80/NAP/issues/49) y la restricción de [CUTOVER #47](https://github.com/robdor80/NAP/issues/47).
- No hay autorización para modificar ACL, identidades, servicios, carpetas reales, producción, instalar/desinstalar ni migrar datos. [CUTOVER #47](https://github.com/robdor80/NAP/issues/47) permanece abierto como bloqueo independiente del uso productivo.

**Pruebas expresamente no autorizadas:** nuevas pruebas de volumen ZIP de ningún tamaño, repetir la regresión completa, benchmarks, estrés o CUTOVER sin una orden nueva del responsable.

Esta aceptación **no sustituye la revisión del diff** ni supone que la PR esté fusionada hasta que GitHub lo confirme.
