# Validación PNG — Fase 2 · Capítulo 2.4

`PngMasterValidator` inspecciona el contenedor de un PNG maestro estático.
Determina el formato por contenido, sin confiar en la extensión ni deducir
identidad o categoría del nombre. Un texto/JPEG llamado `.png` se rechaza;
un PNG con otra extensión puede pasar esta inspección. La coherencia de los
nombres del paquete se comprobará en validación posterior.

## API y resultados

```csharp
var result = new PngMasterValidator().Validate(path);
if (result.IsValid)
{
    var info = result.ImageInfo!;
    bool matchesFourToFive = info.HasAspectRatio(4, 5);
}
```

También existe `Validate(Stream)`: empieza en la posición actual, consume
los bytes hasta finalizar o detectar un fallo y deja abierto el stream del
caller. Funciona con streams sin seek y con lecturas cortas. No restaura la
posición, no escribe y no crea temporales. El overload de ruta abre únicamente
el archivo indicado, en solo lectura.

`PngValidationResult` devuelve `Status`, `ImageInfo`, `Reason` y `IsValid`.
Estados locales, sin taxonomía global del pipeline:

- `Valid`: estructura comprobada; `ImageInfo` disponible, sin `Reason`.
- `Invalid`: firma, estructura, truncamiento o CRC incorrectos.
- `UnsupportedFeature`: APNG o chunk crítico desconocido con CRC válido.

Los resultados rechazados no incluyen `ImageInfo`. Los argumentos incorrectos
y errores de acceso/I/O propagan su excepción; no se convierten en éxitos ni
se integran todavía con errores controlados de 2.5.

`PngImageInfo` contiene `Width` y `Height` como `int` positivos, y `BitDepth`,
`ColorType` e `InterlaceMethod` como bytes. No declara que se hayan decodificado
los píxeles.

## Comprobaciones

Se comprueba la firma exacta `89 50 4E 47 0D 0A 1A 0A`. Los chunks se leen
secuencialmente: longitud big-endian, cuatro letras ASCII, payload y CRC.
Se rechaza el bit reservado del tercer carácter. Las longitudes no pueden
superar `2^31-1`; si hay seek se comparan con los bytes disponibles antes
de leer, y sin seek una lectura incompleta rechaza el archivo.

`IHDR` es primero, único y de longitud 13. Sus dimensiones deben estar en
`1..Int32.MaxValue`, sin imponer mínimo de resolución. Compression y filter
method deben ser 0; interlace puede ser 0 o 1. Las combinaciones aceptadas son:

| Color type | Bit depths |
| --- | --- |
| 0 — Grayscale | 1, 2, 4, 8, 16 |
| 2 — Truecolor | 8, 16 |
| 3 — Indexed | 1, 2, 4, 8 |
| 4 — Grayscale + alpha | 8, 16 |
| 6 — Truecolor + alpha | 8, 16 |

Se exige IDAT y al menos un byte de payload entre sus chunks. Pueden dividirse
los datos en varios IDAT consecutivos, incluidos chunks individuales vacíos;
no puede retomarse IDAT después de otro chunk. `IEND` debe seguir a los datos,
tener longitud cero y terminar el stream. Cualquier byte posterior, incluido
otro IEND o un PNG concatenado, rechaza el archivo.

PLTE, si aparece, es único, anterior a IDAT y contiene 1–256 tripletas RGB.
Se prohíbe para tipos 0 y 4, es opcional para 2 y 6, y obligatorio para 3.
En tipo 3 sus entradas no pueden superar `2^BitDepth`. No se comprueban los
índices usados por los píxeles, porque IDAT no se decodifica.

Los chunks críticos desconocidos se rechazan. Los auxiliares normales o
privados pueden pasar cuando su tipo, longitud disponible y CRC son correctos;
no se implementa aquí toda la gramática interna ni las reglas específicas
de orden/unicidad de cada chunk auxiliar. La referencia técnica es la
[especificación PNG de W3C](https://www.w3.org/TR/png-3/).

## CRC y memoria

Todos los chunks de un PNG aceptado tienen CRC-32 comprobado sobre el tipo
y payload; se compara con el CRC big-endian almacenado. `PngChunkCrc32` es
interno y aislado: no se modifica la integridad ZIP ni su extractor.

El parser utiliza un buffer de payload fijo de 8 KiB, buffers pequeños para
cabeceras/IHDR/CRC y una tabla CRC fija. No asigna memoria según la longitud
de un chunk ni acumula IDAT. Cada iteración consume bytes y reduce el payload
pendiente; no realiza seeks. El coste de lectura/CRC es lineal en los bytes
inspeccionados. No hay límite adicional configurable de tamaño/tiempo en 2.4.

CRC detecta corrupción del contenedor; no autentica el origen del archivo.

## Maestros estáticos y alcance

La presencia de `acTL`, `fcTL` o `fdAT` se rechaza explícitamente como APNG;
no se selecciona el primer frame. Si el chunk está corrupto se devuelve
`Invalid` antes de clasificar su característica como no soportada.

**No se descomprime el stream zlib de IDAT.** Un payload no decodificable
con CRC correcto puede pasar esta validación estructural. Tampoco se verifican
Adler-32, cantidad de scanlines, filtros por fila, índices de paleta o píxeles.
5.1 — PortraitPngToWebpConverter confirma el decode PNG real con ImageSharp 3.1.12,
después de aplicar MaxInputPixels y proporción exacta. Reutiliza este validador
y la misma FileStream, reseteando Position = 0; un IDAT no decodificable puede
producir portrait_decode_failed. Véase [PORTRAIT_CONVERSION.md](PORTRAIT_CONVERSION.md). Aceptar
dimensiones grandes aquí no autoriza asignar un raster de ese tamaño.

No se garantiza conformidad completa de todos los metadatos auxiliares, ni
se transforma, redimensiona, recorta, renderiza o genera WebP.

## Proporción y Production Profiles

`PngImageInfo.HasAspectRatio(widthUnits, heightUnits)` exige valores positivos
y compara exactamente `Width * heightUnits == Height * widthUnits`, elevando
los operandos a `long`. Con operandos `int` positivos los productos caben
en `long`. No usa floating point ni tolerancias; proporciones no positivas
devuelven `false`.

4×5, 1024×1280, 1536×1920 y 768×960 cumplen 4:5; 1024×1536 no.
El validador no conoce `portrait_npc`, manifest, clasificación ni registros.
La decisión existente de ese perfil sigue siendo fuente PNG 4:5 y producción
WebP 768×960, Q90, sin crop silencioso. 768×960 es resolución de producción,
no requisito mínimo del maestro; aquí no se resuelve ningún perfil.

Scene continúa sin proporción, resolución o perfil canónicos definitivos.
[5.3 — Scene](SCENE_CONVERSION.md) reutiliza este validador antes de pixel safety,
ratio source/output solicitado exacto y decode PNG real, con settings explícitos.
No introduce reglas canónicas de Scene ni modifica este contrato.

## Tests y continuidad

Las pruebas generan PNG pequeños reales mediante scanlines filtradas y
`ZLibStream`, incluidas paletas y layout Adam7. El CRC del generador usa una
implementación bitwise independiente del helper de producción. Se prueban
metadatos, todos los pares color/depth válidos, errores estructurales,
CRC de críticos/auxiliares, APNG, PLTE, IDAT, truncamientos, datos posteriores,
lecturas cortas/sin seek y memoria de lectura acotada. Un test explicita que
la validación estructural no equivale a decodificación zlib.

El placeholder textual de Fase 1 **no se modifica**: su integración sigue
probando transporte opaco. 2.4 no añadió binarios grandes ni dependencias.
Fase 4 HECHA dentro del alcance v1; Fase 5 — Conversión HECHA.
5.1 — Portrait HECHO (primitive independiente); 5.2 — Validar salida HECHO.
[5.2](PORTRAIT_OUTPUT_VALIDATION.md) comprueba el WebP en memoria, sin volver
al PNG ni alterar este validador. 5.3 — Scene HECHO;
5.4 — Perfiles genéricos HECHO; 5.5 — No recorte silencioso HECHO.

Desde [5.5 — No recorte silencioso](NO_SILENT_CROP.md), HasAspectRatio es la
primitiva única de geometría de Portrait/Scene y del validator genérico:
productos long exactos, sin floating point/tolerancia. PngMasterValidator
conserva su frontera estructural; ImageConversionGeometryValidator consume
PngImageInfo + ResolvedImageConversion después y devuelve clean o STOP genérico.
No abre PNG ni valida píxeles, budget o quality. Fase 6 — Integridad HECHA;
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); siguiente: Fase 9 — Producción repo Nimroel.
