# ImageSharp — revisión de seguridad de Fase 5

Estado verificado al **20-10-2026 UTC**, fuente `fd7c4ff`: **ImageSharp 3.2.0** directa en NAP.Core y transitiva en los otros siete proyectos. Licencia privada, target oficial activo y **220/220** pruebas locales dirigidas de imágenes superadas, sin omisiones. La DLL ensayada coincide con el paquete 3.2.0 y su revisión `baa37cd`.

La [ejecución original 39488901573](https://github.com/robdor80/NAP/actions/runs/39488901573) acredita Release x64 correcta en Windows y Linux, `SixLabors_ValidateLicense` satisfactorio para NAP.Core, **cero errores**, limpieza comprobada de licencia temporal y **cero pruebas ejecutadas**. La provisión privada ya está demostrada; el bloqueo anterior de licencia está superado. El workflow y observador son exclusivos del laboratorio y no se incorporan a esta PR. Detalles, alcance y metadatos en [la auditoría final](NAP_2_0_FASE_5_FINAL_REVIEW.md) y su [evidencia](NAP_2_0_FASE_5_CLOSURE_AUDIT_EVIDENCE.json).

Los **cinco NU1902/NU1903 permanecen visibles**. Se verificó directamente la [PR oficial 3208](https://github.com/SixLabors/ImageSharp/pull/3208), fusionada en `baa37cd19077b2a2a0c5667535111cf31d48c019`, que enumera los cinco IDs. El [anuncio del proveedor](https://sixlabors.com/posts/security-support-and-license-enforcement/) confirma backports y revisión de metadatos pendiente. No se añaden supresiones ni se declara scan limpio; la decisión residual de seguridad corresponde al responsable. Seguimiento de soporte v3 antes de 2027-05-12.

La revisión de las rutas PNG/WebP y los TRX dirigidos respalda los contratos de geometría, píxeles/alpha/16 bits, metadatos, límites, rechazo de entradas y conservación de fuentes/caché en su cobertura. La normalización conserva ICC; no se elimina toda exposición por no llamar directamente a ICC/TIFF/histogramas. No se identifica una rotura funcional demostrada; tampoco se afirma cobertura universal o una regresión general nueva con 3.2.0. Ninguna prueba nueva se ejecuta para esta auditoría.

**Conclusión vigente:** actualización y compilación/licencia verificadas local y remotamente; compatibilidad dirigida satisfactoria; avisos/metadatos y riesgo residual requieren decisión expresa y seguimiento. IPC y StorageUnavailable son incidencias separadas con causa no demostrada. No se acepta ningún riesgo por el responsable.

## Revisión histórica anterior a la aportación de licencia

Instantánea previa del 2026-10-20, conservada por trazabilidad. Todo este apartado describe 3.1.12 y el bloqueo previo a la licencia; sus frases en presente no describen el estado vigente, actualizado arriba.

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
