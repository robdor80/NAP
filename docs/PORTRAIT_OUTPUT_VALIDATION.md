# Portrait WebP Output Validation — Fase 5 · Capítulo 5.2

**Fase 4 HECHA dentro del alcance v1. Fase 5 — Conversión HECHA.
5.1 — Portrait, 5.2 — Validar salida Portrait y 5.3 — Scene HECHOS.
5.4 — Perfiles genéricos HECHO; 5.5 — No recorte silencioso HECHO.**

Frontera productiva posterior a 5.1: valida el WebP en memoria antes de
cualquier futura escritura a producción. Reutiliza ImageSharp 3.1.12 sin
nuevas dependencias ni cambios a los contratos de conversión.

## API

```csharp
public NapIssueReport Validate(
    PortraitWebpImage image,
    PortraitConversionSettings settings)
```

PortraitWebpOutputValidator es una clase pública sealed sin estado,
con constructor público por defecto y sin overloads.
Null produce ArgumentNullException con ParamName `image` o `settings`.
Consume exclusivamente PortraitWebpImage + PortraitConversionSettings y
devuelve NapIssueReport. No recibe source PNG, paths, ProcessingPlan, package,
repository, routing, UniverseContext o UniverseProfile. Sin dependencia de
Nimroel/portrait_npc, CLI o UI.

## Orden y alcance

1. **Metadata vs settings:** Width/Height/WebpQuality deben coincidir con
   OutputWidth/OutputHeight/WebpQuality. Un mismatch se detiene antes de acceder
   a bytes o intentar decode. SourceWidth/SourceHeight son historia del source
   y no se validan aquí.
2. **WebP real:** obtiene una copia mediante ToArray, sin reflection productiva
   ni mutación. Comprueba firma RIFF/WEBP, longitud RIFF exacta y límites de
   chunks/padding para rechazar contenedores dañados/truncados: el decoder de
   ImageSharp es permisivo en estos casos. Esa comprobación no sustituye el
   decode: una MemoryStream read-only alimenta explícitamente
   WebpDecoder.Instance.Decode<Rgba32> con DecoderOptions.SkipMetadata = true.
   No autodetección ni Identify superficial; decodifica los píxeles.
3. **Dimensiones reales:** decoded.Width/Height deben coincidir exactamente con
   los settings. Como metadata ya coincidió, también coinciden con image.Width/Height.
4. **Report limpio:** devuelve new NapIssueReport([]).

Devuelve como máximo un issue: el primer fallo de ese orden. PNG/JPEG válidos,
bytes vacíos/random, WebP corrupto o truncado se rechazan. Solo se captura
ImageFormatException de ImageSharp, incluidos sus subtipos de contenido
inválido. No catch(Exception), OutOfMemoryException, exception logging,
Exception.Message o stack traces en issues. Fallos de recursos no se ocultan.

## Issues

Todos son Error + Stop, SubjectPath null; Details numéricos invariant.

| Code | Message | Detail |
|---|---|---|
| portrait_output_metadata_mismatch | The portrait WebP metadata does not match the configured output. | `image=<width>x<height> q=<quality>; expected=<width>x<height> q=<quality>` |
| portrait_output_invalid_webp | The portrait output is not a valid decodable WebP image. | null |
| portrait_output_dimensions_mismatch | The decoded portrait WebP dimensions do not match the configured output. | `decoded=<width>x<height>; expected=<width>x<height>` |

Report limpio: IsClean true, ShouldStop false, CanContinue true.
Primer fallo: IsClean false, ShouldStop true, CanContinue false.

La comprobación del contenedor se comparte desde 5.3 mediante el helper
internal WebpContainerValidator.IsComplete, extracción de la lógica privada
existente sin ampliar semántica. SceneWebpOutputValidator lo reutiliza;
no se interpreta VP8/VP8L/VP8X ni se cambia API, orden, codes, messages o
comportamiento observable de Portrait. Decode real sigue siendo obligatorio.

## Quality e inmutabilidad

Quality se verifica **solo como metadata contractual**:
image.WebpQuality == settings.WebpQuality. No se infiere/reconstruye quality
real del bitstream: no es una propiedad portable/reversible de WebP.
No hay heurísticas de calidad ni restricción adicional lossy/lossless en 5.2.
5.1 configura el encoder lossy; 5.2 comprueba los hechos de su contrato actual.

No altera bytes, image metadata ni settings. No re-encodea, resizea, recorta,
rellena, normaliza o repara un output defectuoso; devuelve STOP. Cero filesystem
I/O: no File, Directory, FileStream, temporales, archivos o directorios creados.
Todo salvo el fixture de integración de tests funciona en memoria. Sin hashes,
checksums productivos, deduplicación o persistencia.

## Integración 5.1 → 5.2

```csharp
var settings = new PortraitConversionSettings(768, 960, 90, 4_000_000);
var conversion = new PortraitPngToWebpConverter().Convert(sourcePath, settings);
if (conversion.IsConverted)
{
    var report = new PortraitWebpOutputValidator().Validate(conversion.Image!, settings);
}
```

Test real: PNG 1024×1280 → converter → WebP 768×960 Q90 → validator → report
limpio. Este test usa parámetros explícitos sin cargar profile.json ni hardcode
Core. Desde 5.4, Nimroel también declara 768×960 Q90 en
[Profile v4](UNIVERSE_PROFILE_V4.md); su resolver no invoca este validator.
Se verifican source y bytes intactos
y ausencia de archivos/directorios de salida; el temporal se elimina en finally.
Las demás pruebas generan imágenes pequeñas en memoria, sin binarios añadidos.

Una imagen WebP real 640×800 con metadata/settings 768×960 pasa metadata pero
produce dimensions_mismatch. Metadata incorrecta sobre bytes inválidos produce
metadata_mismatch, demostrando prioridad. Tests cubren corrupción de píxeles,
contenedor/truncamiento, cultura, repetición e inmutabilidad; validar solo la
firma o los campos del objeto no supera esta frontera.

## Limitaciones y continuidad

Report limpio no autoriza escritura y no significa que el asset esté COMPLETED.
Solo confirma que el output en memoria superó 5.2. No sustituye verificaciones
futuras después de persistir bytes; seguirán siendo necesarias. No comprueba
fidelidad visual frente al PNG ni quality efectiva del encoder.

Sin ProductionRoot/ArchiveRoot/TeraBox, Jobs, estados, recovery, IA, SQLite,
CLI/UI, orquestación o Fase 6. Fase 5 HECHA; 5.1–5.5 HECHOS.
[Scene](SCENE_CONVERSION.md) conserva tipos propios y no define canon de Nimroel.
5.4 — Perfiles genéricos y [5.5 — No recorte silencioso](NO_SILENT_CROP.md) HECHOS.
5.5 valida geometría source/output antes de futura ejecución genérica; esta
frontera de output conserva su API y comportamiento. Fase 6 — Integridad
EN CURSO; 6.1 — SHA-256 HECHO; 6.2 — Duplicados SIGUIENTE.
