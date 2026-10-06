# Portrait PNG → WebP — Fase 5 · Capítulo 5.1

**Fase 4 HECHA dentro del alcance v1. Fase 5 — Conversión HECHA.
5.1 — Portrait, 5.2 — Validar salida Portrait y 5.3 — Scene HECHOS.
5.4 — Perfiles genéricos HECHO; 5.5 — No recorte silencioso HECHO.**

Primera conversión gráfica real del Core: PNG maestro estático → validación
estructural → safety pixels → proporción exacta → decode real → resize sin crop
→ WebP lossy en memoria. No integra ni autoriza escrituras de producción.

## Dependencia

Única dependencia añadida: [SixLabors.ImageSharp 3.1.12](https://www.nuget.org/packages/SixLabors.ImageSharp/3.1.12),
versión estable exacta fijada en NAP.Core.csproj. Managed, cross-platform,
compatible con net8.0, sin dependencias transitivas ni runtimes nativos.
Se usa la rama estable 3.1 para mantener la compilación sin los warnings de
clave de licencia introducidos en 4.1.2; no se suprimen warnings.
ImageSharp conserva su [Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/v3.1.12/LICENSE).
La CI existente usa ubuntu-latest y .NET 8; no requiere cambiar su workflow.
La validación local se ejecuta en Windows, no constituye una ejecución de CI Linux.

## Contratos

Todos los tipos son sealed.

```csharp
public PortraitConversionSettings(
    int outputWidth, int outputHeight, int webpQuality, long maxInputPixels)

// PortraitWebpImage
public byte[] ToArray()

public PortraitConversionResult(NapIssueReport issues, PortraitWebpImage? image)

// PortraitPngToWebpConverter
public PortraitConversionResult Convert(
    string sourcePath, PortraitConversionSettings settings)
```

PortraitConversionSettings expone OutputWidth/OutputHeight/WebpQuality/MaxInputPixels
get-only. Dimensiones 1..16383 (límite WebP), quality 0..100, maxInputPixels > 0.
Un valor inválido produce ArgumentOutOfRangeException con su nombre de parámetro.
No hay AllowCrop, CropMode, ResizeMode ni FitMode configurables.

PortraitWebpImage tiene constructor internal y congela SourceWidth/SourceHeight,
Width/Height, WebpQuality y bytes. Copia el array recibido; ToArray es la única
forma pública de obtener bytes y devuelve una copia nueva cada vez.
No expone un stream mutable ni escribe archivos.

PortraitConversionResult exige `issues.IsClean == (image is not null)`;
si se viola lanza ArgumentException sobre `image`. Issues null lanza
ArgumentNullException sobre `issues`. IsConverted equivale a Image no null.
Incluso un report Warning + Continue impide adjuntar una imagen.

PortraitPngToWebpConverter es stateless y tiene únicamente la API pública
Convert mostrada, sin overloads. sourcePath null/empty/whitespace usa
ThrowIfNullOrWhiteSpace (ArgumentException, ParamName sourcePath; null deriva
en ArgumentNullException). settings null produce ArgumentNullException,
ParamName settings.

## Lectura, seguridad y decode

Abre exactamente sourcePath usando FileMode.Open, FileAccess.Read, FileShare.Read.
Una misma FileStream se usa para PngMasterValidator.Validate(Stream) y decode,
reduciendo la ventana entre ambas lecturas; no garantiza inmutabilidad del
archivo frente a modificaciones externas en todas las plataformas.

Una estructura inválida o característica no soportada se devuelve mediante
NapIssueMapper, conservando png_invalid/png_unsupported_feature y sourcePath
como SubjectPath. No duplica la validación ni intenta decode tras un rechazo.

Antes de reservar el raster, usa las dimensiones de PngImageInfo y productos
long exactos:

```csharp
(long)sourceWidth * sourceHeight <= settings.MaxInputPixels
(long)sourceWidth * settings.OutputHeight ==
    (long)sourceHeight * settings.OutputWidth
```

Primero se aplica el límite de píxeles; después la proporción, sin floating point,
tolerancias, crop, pad, letterbox, fondos o distorsión geométrica.
El límite es obligatorio y explícito, sin presupuesto universal hardcoded.

Tras pasar ambos checks, Position = 0. PngDecoder.Instance.Decode<Rgba32>
fuerza PNG, decodifica IDAT y usa SkipMetadata; no AutoOrient ni detección
automática de JPEG. Dimensiones decoded deben coincidir con IHDR; de lo
contrario se devuelve portrait_decode_failed.

Solo se captura ImageFormatException de ImageSharp en torno al decode,
incluyendo su subtipo InvalidImageContentException: cubre errores del header
zlib y del contenido gráfico. No catch(Exception), catch(IOException) ni
exception logging. FileNotFoundException, DirectoryNotFoundException,
UnauthorizedAccessException e IOException de acceso real se propagan.
Resize/encode permanecen fuera del catch. No se devuelve Exception.Message,
stack trace o InnerException en un issue.

## Issues nuevos

Todos son Error + Stop, SubjectPath = sourcePath, números invariant.

| Code | Message | Detail |
|---|---|---|
| portrait_input_too_large | The portrait PNG exceeds the configured pixel safety limit. | `<width>x<height>; max_pixels=<MaxInputPixels>` |
| portrait_aspect_ratio_mismatch | The portrait PNG aspect ratio does not match the configured output ratio. | `source=<width>x<height>; output=<outputWidth>x<outputHeight>` |
| portrait_decode_failed | The portrait PNG could not be decoded. | null |

Un rechazo devuelve report no limpio e Image null, sin resize/encode.

## Resize y salida

Si las dimensiones ya coinciden, no se resamplea. En otro caso se resizea
exactamente al tamaño configurado con KnownResamplers.Lanczos3 explícito.
Desde 5.5 la primitiva única de ratio es PngImageInfo.HasAspectRatio, que compara
productos long exactos, sin floating point o tolerancia. El refactor conserva
portrait_aspect_ratio_mismatch, message/detail/SubjectPath y orden anteriores.
ResizeMode.Stretch solo se aplica tras demostrar igualdad exacta de proporción;
conserva el cuadro completo sin distorsión, crop, pad o letterbox.

WebpEncoder usa FileFormat = Lossy y Quality = settings.WebpQuality.
Encode a MemoryStream interno; el resultado contiene bytes WebP y metadata
original/final, con report vacío e IsConverted true. No hay archivo de salida.

Ejemplo de llamada explícita al converter. Desde 5.4 el perfil real Nimroel
declara 768×960 Q90 en [Profile v4](UNIVERSE_PROFILE_V4.md), con source role
master. ImageConversionResolver resuelve esos parámetros desde un package
validado y recibe MaxInputPixels runtime; no ejecuta esta llamada:

```csharp
var settings = new PortraitConversionSettings(768, 960, 90, 4_000_000);
var result = new PortraitPngToWebpConverter().Convert(sourcePath, settings);
```

4:5, 768×960 Q90 sin crop: 1024×1280 y 1536×1920 se reducen; 384×480 permite
upscale; 768×960 se encodea directamente. 768×960 es resolución de producción,
no mínimo del maestro. Ninguno de estos valores ni Nimroel/portrait_npc está
hardcoded en el converter; otros tamaños/proporciones/quality son explícitos.

## Pruebas y límites

Pruebas sintéticas de contratos, límites, CRC/estructura/APNG/JPEG, pixel budget
antes de decode (headers enormes con IDAT inválido), ratio exacto, IDAT con CRC
correcto pero no decodificable, copia defensiva, source intacto y archivos sin
cambios. Los cuatro tamaños representativos generan WebP lossy real, decodificable
con dimensiones exactas; cuadrantes/edges conservados comprueban ausencia de
crop/pad/letterbox. EXIF no cambia orientación. Repeticiones se comparan por
resultado funcional; byte-identidad entre versiones de ImageSharp no es contrato.
Los temporales de tests se eliminan mediante IDisposable.

La única I/O productiva es lectura del PNG. Sin ProductionRoot, ArchiveRoot,
TeraBox, directory creation, ProcessingPlan, repository, routing, destination,
CLI, UI, hashes, Jobs, estados, recovery, auditor IA, SQLite u orquestación.
No cambia el maestro ni persiste el resultado. El caller recibe bytes, sin
permiso de ejecución/escritura de assets. El converter no valida su salida:
[5.2 — PortraitWebpOutputValidator](PORTRAIT_OUTPUT_VALIDATION.md) es la frontera
productiva separada, posterior a 5.1, para metadata/settings, decode WebP real
y dimensiones; no se integra automáticamente ni autoriza escritura.

Fase 5 HECHA. 5.1–5.5 HECHOS. [Scene](SCENE_CONVERSION.md)
tiene tipos propios/settings explícitos sin perfil canónico; no modifica 5.1.
5.4 — Perfiles genéricos HECHO: Universe Profile v4 declara y resuelve reglas,
sin modificar los contratos Portrait ni añadir orquestación.
[5.5 — No recorte silencioso](NO_SILENT_CROP.md) HECHO formaliza la invariante
full-frame y añade el validator geométrico genérico, sin conectarlo a Portrait.
Ratio exacto + Stretch + ausencia de crop/pad preservan el frame; Stretch aislado
no prueba esa garantía. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Siguiente: Fase 7 — Auditor IA.
