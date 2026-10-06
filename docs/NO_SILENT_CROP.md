# No Silent Crop — Fase 5 · Capítulo 5.5

**Fase 4 HECHA dentro del alcance v1. Fase 5 — Conversión HECHA;
5.1–5.5 HECHOS. Fase 6 — Integridad HECHA;
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.**

El principio histórico de NAP exige conservar el cuadro completo del maestro.
Portrait y Scene ya lo cumplían en 5.1/5.3; 5.5 formaliza una invariante genérica
reutilizable sin cambiar el comportamiento observable de esas APIs.

## Semántica actual de png_to_webp

ImageConversionKind.PngToWebp y su JSON `png_to_webp` significan actualmente:
source PNG estático validado, output WebP con dimensiones configuradas,
igualdad exacta de aspect ratio y conservación del frame completo. Se permite
reducción, misma resolución o upscale proporcional. Crop, Pad, BoxPad,
letterbox y distorsión están prohibidos. No hay alternativa configurable.
Un ratio diferente produce Error + Stop antes de resize.

La única primitiva matemática de ratio de los converters actuales y del
validator genérico es PngImageInfo.HasAspectRatio(int widthUnits,
int heightUnits). Exige source y target positivos y compara:

```text
(long)source.Width * outputHeight == (long)source.Height * outputWidth
```

Los productos long evitan overflow de 32 bits. No hay floating point,
división, decimal ratio, redondeo, epsilon, porcentaje o tolerancia geométrica.
Dimensiones cero/negativas devuelven false y la frontera genérica falla cerrada
con el mismo issue de mismatch; no inventa excepciones para estados internos
imposibles. Los objetos productivos válidos ya garantizan dimensiones positivas.

## Geometry validator

ImageConversionGeometryValidator es public sealed class stateless con una
única API pública, sin overloads:

```csharp
public NapIssueReport Validate(
    PngImageInfo source,
    ResolvedImageConversion conversion)
```

Orden: source no null, conversion no null, ratio exacto, report limpio.
Los null producen ArgumentNullException con ParamName `source` o `conversion`,
respectivamente. Usa exclusivamente source.HasAspectRatio(conversion.OutputWidth,
conversion.OutputHeight). Si coincide devuelve report vacío: IsClean true,
ShouldStop false, CanContinue true.

Si no coincide devuelve exactamente un issue:

| Campo | Valor |
| --- | --- |
| Constante | NapIssueCodes.ImageConversionAspectRatioMismatch |
| Code | image_conversion_aspect_ratio_mismatch |
| Severity | Error |
| Disposition | Stop |
| Message | The source image aspect ratio does not match the configured output ratio; full-frame conversion is required. |
| SubjectPath | conversion.SourcePath exacto |
| Detail | source=&lt;width&gt;x&lt;height&gt;; output=&lt;width&gt;x&lt;height&gt; |

Detail usa FormattableString.Invariant; por ejemplo
`source=1920x1080; output=768x960`, independiente de cultura.

El validator realiza cero filesystem I/O. SourcePath se usa únicamente como
SubjectPath, incluso si es inexistente o contiene segmentos no normalizados.
No abre PNG, comprueba existencia, normaliza paths, decodifica píxeles o procesa
WebP. No valida estructura PNG, MaxInputPixels, quality, routing, destino o hashes.
Recibe metadata y configuración obtenidas por fronteras previas.

## Portrait y Scene

La única modificación productiva en cada converter sustituye la comparación
manual duplicada por `!info.HasAspectRatio(settings.OutputWidth,
settings.OutputHeight)`. Mantienen orden de validaciones, pixel safety,
decode, resize, encoder, APIs, excepciones, messages, details y SubjectPath.
Conservan los códigos históricos portrait_aspect_ratio_mismatch y
scene_aspect_ratio_mismatch. No se construyen snapshots de conversión dentro de
los converters ni se les conecta el nuevo validator.

Ambos mantienen ResizeMode.Stretch y KnownResamplers.Lanczos3. Stretch es
proporcional aquí porque se ejecuta **después** de demostrar ratio idéntico.
La garantía completa es precondición de ratio exacto + Stretch + ausencia de
Crop/Pad/BoxPad/letterbox/AutoOrient. Stretch aislado no prueba esa garantía.
Mismatch detiene antes de decode y resize; upscale compatible sigue permitido.

## Integración genérica y pruebas

```text
Universe Profile v4 → PackageSemanticValidator → ValidatedAssetPackage
→ ImageConversionResolver → ResolvedImageConversion
→ PngMasterValidator sobre el source → PngImageInfo
→ ImageConversionGeometryValidator → clean / STOP
```

Las integraciones usan Nimroel Portrait master → 768×960 Q90, con source 4:5
compatible y 16:9 incompatible; y el fixture test_universe image_asset /
image_profile → 1200×800 Q87, con source 3:2 compatible y 4:3 incompatible.
El package de ratio incompatible puede ser semánticamente válido: la geometría
es una frontera posterior. Los tests no llaman converters y comprueban bytes y
estructura del package intactos.

Las pruebas unitarias cubren contrato público, nulls, ratios compatibles,
upscale, dimensiones inválidas, productos extremos, diferencias mínimas sin
tolerancia, detalles invariant y path inexistente exacto. La regresión existente
de Portrait/Scene conserva errores y prioridad antes de decode. Las pruebas
funcionales full-frame refuerzan sus cuadrantes con bandas de los cuatro bordes,
cuatro esquinas distintas y centro, tanto en reducción como en ampliación.
WebP es lossy: se usan tolerancias amplias de color para estas comprobaciones
visuales, nunca para la igualdad de aspect ratio.

La revisión explícita del Core no encuentra operaciones Crop, Pad, BoxPad o
AutoOrient en conversión. Se conservan únicamente los dos ResizeMode.Stretch
condicionados al ratio exacto. No hay tests que lean archivos .cs buscando strings.

## Versionado y continuidad

Universe Profile v4 y el perfil Nimroel permanecen intactos, sin Scene canónico.
No se crea Profile v5, fixture v5 ni campos crop_mode, allow_crop, fit_mode,
resize_mode, letterbox, padding o framing_policy. Cualquier crop deliberado
futuro requerirá contrato explícito nuevo: evolución de schema o tipo/modo
inequívoco, con consentimiento/configuración explícita. Nunca podrá cambiar
silenciosamente la semántica existente de png_to_webp.

Resolver configuración y validar geometría son fronteras previas a ejecución.
Report clean no autoriza escritura. No existe ejecución/orquestación genérica,
converter genérico público o ampliación de ProcessingPlan/Dry Run. En 5.5 no se añadieron
escrituras de producción/archive, hashes, duplicates, Jobs, estados, recovery,
IA, SQLite, CLI o UI. Fase 5 queda HECHA. Desde
[6.1 — SHA-256](SHA256_INTEGRITY.md), existe una primitiva de identidad de bytes
separada, sin modificar el validator geométrico. Fase 6 HECHA;
6.1 HECHO y 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.
