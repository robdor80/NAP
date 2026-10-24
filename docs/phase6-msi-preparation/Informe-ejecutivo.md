# NAP 2.0 — Preparación de aceptación privada en MSI

24 de octubre de 2026 · Cierre de Fase 6 · Aceptación humana pendiente

## Resultado

**Preparación técnica verificada en nube; entrega del programa bloqueada. No ejecutar todavía NAP en el MSI.** Se ha generado una publicación Release Windows x64 con .NET 8 autónomo, pero no un paquete de aceptación entregable y aprobado. No se ha publicado ningún ejecutable, artefacto binario ni Release. El candidato efímero se eliminó del runner al finalizar.

La carpeta de Drive está privada y su acceso de lectura/descarga se comprobó con un archivo documental inocuo. Falta establecer una transferencia de binarios desde Actions sin exposición al público. También falta un modo interactivo cuyo aislamiento cubra datos administrativos, coordinación común, raíces y exportaciones.

## Verificación nueva

| Comprobación | Resultado |
|---|---|
| Actions Windows Server 2022 x64 | Correcto, ejecución 40123851505 |
| Publicación autónoma | .NETCore y WindowsDesktop 8.0.32 incluidos; 478 archivos, 174.800.044 bytes antes de manifiesto |
| Pruebas focalizadas sin host/escritores persistentes | 49/49 superadas, 0 omitidas |
| Vistas WPF reales y RobStyle | 14 escenarios, 15 PNG, 0 errores de binding; cinco secciones y bandeja comprobadas por preview |
| ImageSharp sin material privado de licencia en ejecución | PNG y WebP de 2×2 píxeles en memoria: correcto |
| SQLite nativo | DLL coincide por hash con publicación; consulta en memoria correcta |
| Integridad | Manifiesto con SHA-256 de cada archivo y procedencia conservado |
| Datos ordinarios NAP en runner | Sin cambios en las ubicaciones NAP comparadas; no es prueba global de contención |
| Ejecutable en Drive o GitHub | Ninguno; 0 artefactos de la ejecución |

Código compilado: **b2e9413f0d3b9d3eb4df79f07fce74685d861d7c**. El código de aplicación, pruebas y perfiles permanece idéntico al previamente verificado ed556bbbdf8a6971cd22d38f96ff88885a366225; se añadieron un workflow, un script y una sonda de verificación. Los commits posteriores de evidencia/documentación no son el SHA compilado.

## Bloqueos antes del MSI

1. **Transferencia privada:** Drive está preparado, pero la identidad del conector no se transmite al runner. Un artefacto ordinario de este repositorio público no es una entrega privada.
2. **Aislamiento interactivo:** `--settings` no redirige recordatorios ni perfiles. LicenseRegister escribe al iniciar y usa coordinación en ProgramData capaz de crear ACL. No se ha cambiado esa autoridad ni añadido una segunda.
3. **Cierre del paquete:** avisos de terceros todavía incompletos, distribución del runtime por revisar y dependencia externa de Git para herramientas no incluidas. El candidato sirve para verificación sintética, no garantiza toda la aplicación sin prerrequisitos.

La solución de menor alcance propuesta es una edición de aceptación exclusivamente sintética, reutilizando las vistas y las reglas Core existentes, sin escritores, importación de raíces ni arranque ordinario; requiere decidir si esa sesión de solo lectura satisface la aceptación. El transporte requerirá una identidad de transferencia autorizada o una solución cifrada estándar validada, sin copiar secretos al programa. No se ha aplicado ninguna de estas ampliaciones.

## Seguridad y punto de parada

El escaneo no encontró sixlabors.lic, configuraciones personales, PDB/obj ni patrones de secretos en el candidato. Se mantuvo la validación oficial de Six Labors. No se inspeccionó el valor del secreto ni la licencia del MSI. La ejecución sin licencia privada en los casos sintéticos no sustituye los derechos de uso/redistribución ni acredita todos los caminos funcionales.

Los cinco avisos NU1902/NU1903 siguen visibles y bajo seguimiento en #49. #47 permanece abierto y no se activa CUTOVER. No se han cambiado main, cuentas, permisos de Windows o de Drive, servicios, web, instalador ni updater. No se han procesado assets reales ni ZIP de assets, ni realizado volumen/estrés.

El responsable puede descargar y leer estos informes y la guía. No debe buscar otro ejecutable, copiar licencias, alterar protecciones ni iniciar una prueba humana hasta cerrar los bloqueos. Esta preparación no declara superada la aceptación ni inicia Fase 7.

Evidencia: https://github.com/robdor80/NAP/actions/runs/40123851505

Documentación oficial sobre artefactos: https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts
