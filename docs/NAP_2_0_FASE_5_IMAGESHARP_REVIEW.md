# ImageSharp — revisión de seguridad de Fase 5

Fecha de consulta: 2026-10-20. **Los cinco avisos siguen pendientes; no se ha cambiado la versión de NAP ni suprimido NU1902/NU1903.**

`dotnet list NAP.sln package --include-transitive --vulnerable --format json --no-restore` confirma `SixLabors.ImageSharp` **3.1.12**, directa en NAP.Core y transitiva en CLI, AI, Presentation, App, AutomationHost, CoordinationProbe y tests. La enumeración de paquetes completa corrobora la resolución. La biblioteca net6.0 no añade dependencias NuGet propias. Una compilación con caché sin avisos no sustituye esta auditoría.

| Advisory | Gravedad publicada | Ruta afectada | Evaluación del uso actual |
|---|---|---|---|
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44) | Moderate | Parser ICC y dimensiones CLUT | Normalización conserva metadatos ICC. No se usa `IccProfile.Entries` directamente; no se considera eliminada la exposición de la dependencia. |
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4) | High | Índice de histograma calculado desde luminancia no validada | NAP no invoca HistogramEqualization; revisión estática, sin prueba de explotación. |
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q) | High | Escritura fuera del buffer del encoder TIFF T4 | NAP genera PNG/WebP y no invoca este encoder. La dependencia sigue afectada según el registro. |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g) | Moderate | Bucle sin progreso al decodificar BigTIFF | Conversiones usan decoders PNG/WebP explícitos; normalización valida primero el formato PNG antes de `Image.Load`. No se autoriza TIFF. |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w) | High | Buffer del encoder TIFF T6 | No hay encoder TIFF en las rutas de NAP revisadas; no equivale a corregir el paquete. |

Los registros consultados señalan 4.1.2 como primera corrección y rangos amplios que aún incluyen 3.2.0. La [release oficial 3.2.0](https://github.com/SixLabors/ImageSharp/releases/tag/v3.2.0), publicada el 9 de octubre, incorpora [backports de seguridad y enforcement de licencia](https://github.com/SixLabors/ImageSharp/pull/3208), incluyendo los cinco identificadores. Esta discrepancia entre backport y metadatos se conserva expresamente: no se desactivan avisos ni se declara un scan limpio por interpretación del changelog.

3.2.0 conserva net6.0 y carece de nuevas dependencias NuGet de runtime. La revisión de cambios no identifica una migración necesaria en las APIs PNG/WebP usadas por NAP; endurece validación de entradas, por lo que imágenes malformadas pueden rechazarse antes. No se ha certificado compatibilidad de ejecución, normalización, metadatos ni igualdad de píxeles con 3.2.0.

**Bloqueo demostrado de actualización:** el paquete añade el target `SixLabors_ValidateLicense` antes de CoreCompile. Una compilación Release aislada, sin tocar el PackageReference del producto, terminó con exit 1 y `No Six Labors license found`. No se ha facilitado/configurado una licencia válida. No se omite el target, falsifica una clave, cambia Release a Debug ni retira assets de build para eludirlo. NUPKG 3.2.0 inspeccionado: SHA256 `31e4f2c76128326cad1395d497e08e97624159016fc95064b46f6283d85bb44d`.

No existe una 3.1.13 publicada en el índice NuGet consultado. La rama 4.x también implica cambio mayor; no se realiza esa migración ni un rediseño. La revisión no resuelve requisitos de licencia para el responsable. Tras disponer de una licencia válida por canal seguro, puede evaluarse 3.2.0 y ejecutar únicamente pruebas pequeñas de decode PNG/WebP, geometría, píxeles/alpha/16 bits, metadatos, normalización, validación adversarial y preservación de originales. No hay autorización para nuevas cargas.

La validación funcional actual sobre 3.1.12 no prueba que estos advisories estén corregidos. Queda un riesgo de dependencia vulnerable, particularmente al tratar metadatos/entradas hostiles. El cierre de seguridad requiere actualización certificada o una decisión expresa del responsable sobre el riesgo; no se infiere esa aceptación.

Evidencias conservadas en la entrega final: enumeraciones JSON de paquetes, resumen de advisories y log de la compilación aislada. No se ejecutaron PoC de consumo elevado ni pruebas de carga. No se adquirió licencia, envió información al proveedor ni añadieron secretos a GitHub.
