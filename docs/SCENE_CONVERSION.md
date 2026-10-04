# Scene Conversion — Fase 5 · Capítulo 5.3

**Fase 4 HECHA dentro del alcance v1. Fase 5 — Conversión EN CURSO.
5.1 — Portrait, 5.2 — Validar salida Portrait y 5.3 — Scene HECHOS.
5.4 — Perfiles genéricos HECHO. 5.5 — No recorte silencioso SIGUIENTE.**

Scene conversion capability exists, but no canonical Nimroel Scene production profile has been defined yet.

5.3 añade conversión gráfica real y validación de output en memoria. No decide
resolución, proporción, quality, production_profile, routing o clasificación
canónicos de Scene. No modifica profile.json, añade regla scene ni crea Profile v4.
Los parámetros son explícitos y los límites de settings son técnicos.

## Contratos Scene propios

Todos los tipos públicos son sealed; no dependen de tipos Portrait.

```csharp
public SceneConversionSettings(
    int outputWidth, int outputHeight, int webpQuality, long maxInputPixels)

// SceneWebpImage, constructor internal
public byte[] ToArray()

public SceneConversionResult(NapIssueReport issues, SceneWebpImage? image)

// ScenePngToWebpConverter, stateless, única API pública
public SceneConversionResult Convert(string sourcePath, SceneConversionSettings settings)

// SceneWebpOutputValidator, stateless, única API pública
public NapIssueReport Validate(SceneWebpImage image, SceneConversionSettings settings)
```

Settings expone OutputWidth/OutputHeight/WebpQuality/MaxInputPixels get-only:
dimensiones 1..16383 (WebP), quality 0..100 y maxInputPixels > 0.
Valores inválidos producen ArgumentOutOfRangeException con ParamName exacto.
No añade modos configurables de crop/fit/resize ni mínimos de resolución.

SceneWebpImage congela SourceWidth/SourceHeight, Width/Height y WebpQuality;
copia los bytes recibidos y devuelve una copia nueva por ToArray, incluso vacía.
No array interno mutable ni stream público. SceneConversionResult exige
`issues.IsClean == (image is not null)`, o ArgumentException sobre image.
Issues null produce ArgumentNullException sobre issues; IsConverted equivale
a Image no null, incluso reports Warning + Continue requieren Image null.

El converter usa ThrowIfNullOrWhiteSpace para sourcePath (ParamName sourcePath);
settings null produce ArgumentNullException sobre settings. El output validator
rechaza image/settings null con ArgumentNullException y ParamName exacto.
Sin overloads ni constructores especiales.

## Conversión PNG → WebP

Abre exactamente el PNG source mediante FileMode.Open, FileAccess.Read,
FileShare.Read. Una misma FileStream sirve para PngMasterValidator.Validate(Stream)
y decode real; source no se modifica ni se crea copia temporal. Fallos estructurales
usan NapIssueMapper con png_invalid/png_unsupported_feature existentes y
SubjectPath sourcePath. No intenta decode tras rejection.

Tras estructura válida, usa PngImageInfo. Antes de reservar raster:

```csharp
(long)sourceWidth * sourceHeight <= settings.MaxInputPixels
(long)sourceWidth * settings.OutputHeight ==
    (long)sourceHeight * settings.OutputWidth
```

Primero pixel safety, después proporción solicitada exacta, sin floating point
ni tolerancia. No es una proporción canónica de Scene: el output del caller
debe conservar el encuadre completo del source. Fallos devuelven Error + Stop
sin decode, resize o encode.

Position = 0 antes de PngDecoder.Instance.Decode<Rgba32> con SkipMetadata = true.
Decode PNG forzado, no autodetection ni AutoOrient. Dimensiones decoded deben
coincidir con IHDR; si no coinciden se devuelve scene_decode_failed. Solo se
captura ImageFormatException de ImageSharp y sus subtipos en torno al decode;
sin catch(Exception), exception logging o detalles de excepciones en issues.
FileNotFoundException, DirectoryNotFoundException, UnauthorizedAccessException
e IOException de acceso real se propagan.

Si cambia tamaño, resize exacto con KnownResamplers.Lanczos3 explícito.
ResizeMode.Stretch solo después de demostrar igualdad de proporción. Sin Crop,
Pad, BoxPad, Max/Min, letterbox, fondos o distorsión geométrica. Si coincide
tamaño no resamplea. Upscale permitido, sin inventar mínimo de Scene.

WebpEncoder configura FileFormat = Lossy y Quality = settings.WebpQuality.
Encode a MemoryStream interno; resultado limpio con SceneWebpImage y metadata
source/output exacta. No guarda WebP ni crea directorios.

## Output validation

SceneWebpOutputValidator devuelve como máximo el primer fallo:

1. Metadata Width/Height/WebpQuality vs settings; no SourceWidth/SourceHeight.
2. Copia mediante ToArray y WebpContainerValidator.IsComplete.
3. WebpDecoder.Instance.Decode<Rgba32>, SkipMetadata = true, MemoryStream read-only.
4. Dimensiones decoded vs settings exactas.
5. NapIssueReport vacío: IsClean true, ShouldStop false, CanContinue true.

El helper compartido es internal static y extrae exactamente la lógica privada
de 5.2: RIFF/WEBP, longitud total y límites/padding de chunks. No amplía semántica
ni interpreta VP8, VP8L, VP8X, ALPH, ANIM, EXIF, XMP o ICC. No es parser completo;
decode de píxeles sigue siendo obligatorio. Portrait usa el mismo helper sin
cambiar API, orden, codes, messages o comportamiento observable.

PNG/JPEG, random/empty, WebP corrupto o truncado se rechazan. Solo captura
ImageFormatException; no OutOfMemoryException ni catch(Exception). Metadata
incorrecta tiene prioridad sobre bytes inválidos; metadata engañosa no evita
comprobar las dimensiones reales.

Quality solo se verifica como metadata contractual, no se infiere del bitstream.
Un WebP lossless decodificable puede superar output validation; la conversión
genera lossy, pero el validator no inventa una restricción de modo de encoder.
No re-encode, resize, repair, normalización, quality heuristics ni hashes.
Image/bytes/settings intactos. Output validation es completamente en memoria.

## Seis códigos Scene

Todos Error + Stop. Details numéricos invariant, como máximo un issue.

| Code | Message | SubjectPath | Detail |
|---|---|---|---|
| scene_input_too_large | The scene PNG exceeds the configured pixel safety limit. | sourcePath | `<width>x<height>; max_pixels=<MaxInputPixels>` |
| scene_aspect_ratio_mismatch | The scene PNG aspect ratio does not match the configured output ratio. | sourcePath | `source=<width>x<height>; output=<width>x<height>` |
| scene_decode_failed | The scene PNG could not be decoded. | sourcePath | null |
| scene_output_metadata_mismatch | The scene WebP metadata does not match the configured output. | null | `image=<width>x<height> q=<quality>; expected=<width>x<height> q=<quality>` |
| scene_output_invalid_webp | The scene output is not a valid decodable WebP image. | null | null |
| scene_output_dimensions_mismatch | The decoded scene WebP dimensions do not match the configured output. | null | `decoded=<width>x<height>; expected=<width>x<height>` |

## Ejemplos exclusivamente técnicos

| Source | Output | Quality |
|---|---|---|
| 1920×1080 | 1280×720 | 88 |
| 1500×1000 | 900×600 | 93 |
| 640×360 | 1280×720 | 88 |

Fixtures/tests 16:9 y 3:2, no canon de Nimroel ni valores fijos del Core.
MaxInputPixels se establece explícitamente a 4_000_000 en estos tests.
Los tres prueban conversión → output validator → report limpio; también se
prueba tamaño idéntico. Bordes/cuadrantes conservados comprueban no crop/pad,
los bytes WebP decodifican y el source permanece idéntico, sin archivos de salida.

Tests adicionales: APIs, límites/overflow long, inmutabilidad, estructura PNG/APNG,
pixel budget antes de decode, IDAT indecodificable con CRC correcto, proporción
exacta, metadata/output engañosos, corrupción/truncamiento, quality contractual,
lossless, cultura, repetición y orientación. No se añaden binarios grandes;
temporales PNG de tests tienen cleanup. Las pruebas Portrait existentes verifican
regresión de outputs válidos, truncados y corruptos tras extraer el helper.

## Alcance y continuidad

Única dependencia reutilizada: ImageSharp 3.1.12; sin cambios al csproj.
La única I/O productiva es lectura del source PNG. Sin filesystem output,
ProductionRoot, ArchiveRoot, TeraBox, ProcessingPlan, repository/routing,
Nimroel hardcode, hashes/checksums productivos, duplicates, Jobs/estados,
recovery, auditor IA, SQLite, CLI/UI, execution orchestrator o Fase 6.

Report limpio no autoriza ejecución/escritura ni significa COMPLETED. La futura
verificación después de persistir bytes sigue siendo necesaria; no se prueba
fidelidad visual o quality efectiva desde el bitstream. Los contratos públicos
Scene/Portrait conservan sus contratos públicos separados. Desde
[5.4 — Profile v4](UNIVERSE_PROFILE_V4.md), la declaración y resolución son
genéricas por asset rule, sin converter genérico público ni ejecución automática.
Nimroel sigue sin regla Scene ni decisiones canónicas de producción.
Fase 5 EN CURSO; 5.4 — Perfiles genéricos HECHO,
5.5 — No recorte silencioso SIGUIENTE.
