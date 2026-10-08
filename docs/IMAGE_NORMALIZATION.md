# Normalización técnica segura de imágenes — v1

La normalización es una preparación explícita de **derivaciones**, anterior a
validación, auditoría y publicación. No modifica el conversor `png_to_webp`, el
archivo de maestros, la identidad de assets ni las reglas de publicación.

## Frontera y arquitectura auditada

Universe Profile v4 declara `ImageConversionRule`; su resolución y
`ImageConversionGeometryValidator` siguen exigiendo proporción matemática exacta.
`PngMasterValidator` comprueba estructura/CRC antes del decode. Inbox, staging y
`StagedPackageExtractor` conservan sus contratos y límites de extracción segura.
`PackageSemanticValidator` valida el candidato completo según el perfil elegido.

`ImageNormalizationEngine` prepara buffers de vista previa inmutables, con acceso
público por copia. `ImageNormalizationService` conserva evidencia ligada al
universo, raíz, SHA-256 del origen, perfil, política, geometría y bytes propuestos.
Solo una decisión explícita sobre esa evidencia produce una derivación definitiva.

La presentación detecta mismatch geométrico durante la preparación normal,
antes de PLANNED/auditoría; ofrece «Preparar normalización». El Core sigue
rechazando originales incompatibles durante conversión y publicación.

## Geometría y píxeles

Sean `W:H` las dimensiones de salida declaradas; se reduce la proporción mediante
`g = gcd(W,H)`, `u = W/g`, `v = H/g`. Para una fuente `w×h`:

```text
k = max(ceil(w/u), ceil(h/v))
lienzo = (k*u) × (k*v)
izquierda = floor((k*u-w)/2); derecha = (k*u-w)-izquierda
arriba = floor((k*v-h)/2); abajo = (k*v-h)-arriba
```

Es el menor lienzo entero que contiene el original y tiene la proporción exacta.
Toda la decisión usa enteros `long` y límites previos a la asignación de memoria.
Los restos impares van a derecha/abajo. Para 1586×992 y 16:10: **1592×995**,
márgenes **3/3/1/2**, 10 728 píxeles añadidos (aproximadamente **0,682 %**).

`nearest-edge-canvas-v1` copia el píxel fuente más cercano a cada posición nueva;
las esquinas repiten el píxel de esquina. Conserva para cada `(x,y)` original:
`candidato[x+izquierda,y+arriba] == original[x,y]`. No hay remuestreo,
reordenación, crop, stretch, bandas de color inventado ni generación con IA.
La traslación es únicamente el margen aprobado; las coordenadas relativas dentro
del área original y el orden de píxeles se conservan.

Se admite PNG estático RGB/RGBA de 8 o 16 bits. Se mantiene profundidad, color,
alpha (incluido RGB oculto bajo alpha cero) y metadata soportada de gamma,
resolución, ICC, EXIF, XMP y textos PNG. El candidato es una nueva codificación PNG;
no se promete igualdad binaria con el archivo original salvo cuando no cambia
la geometría. El original siempre queda intacto byte-for-byte.
Se comprueba CRC/decode y **cada píxel**, incluidos los bordes, tras reabrir la
codificación candidata. Palette/grayscale, APNG, formatos inválidos o decode
incompatible producen STOP; no se convierten implícitamente a otro formato.

Una fuente ya exacta conserva sus bytes. La decisión queda `Unchanged`, sin crear
un nuevo maestro. Si solo cambia el perfil con consentimiento separado, puede
generarse un ZIP nuevo manteniendo el PNG original idéntico.

## Política conservadora

Defaults de `ImageNormalizationPolicy`:

| Límite | Valor |
|---|---|
| Área añadida | 100 basis points = 1 % |
| Crecimiento por eje | 100 basis points = 1 % |
| Píxeles de entrada / lienzo | 16 000 000 / 17 000 000 |
| Bytes por PNG/companion | 64 MiB |
| ZIP y contenido total extraído | 128 MiB |
| Entradas ZIP | 256 |

El área usa `(canvasPixels-sourcePixels)*10000 <= sourcePixels*areaLimit`;
cada eje usa la comparación análoga, sin epsilon ni redondeo. El porcentaje decimal
solo se muestra al usuario. WPF permite ajustar área/eje entre 0 y **5 %**, con
hasta dos decimales; el Core impone ese máximo incluso por API. Los otros budgets
son configurables mediante la política, con caps absolutos de recursos.
Una fuente 16:9 para destino 16:10 supera estos límites y produce STOP con petición
de intervención artística. No se amplían grandes áreas para forzar proporciones.

## ZIP, manifest y autorización

La UI usa el ZIP seleccionado del Inbox activo. El Core también ofrece preparación
de un PNG individual con identidad y perfil explícitos. La normalización es
genérica: no contiene excepciones por universo, localidad, familia o dimensiones.
El origen de conversión debe ser un maestro PNG requerido del perfil.

La extracción de inspección es temporal y privada, desde una copia del mismo buffer
ZIP cuyo SHA-256 queda ligado a la propuesta. No reabre el ZIP del Inbox para extraer,
por lo que cambiar y restaurar el origen durante la inspección no mezcla versiones.
Se validan el manifest original,
universo, identidad, nombres, estructura y clasificación. Si su perfil no existe,
STOP: **no se infiere ni sustituye**. El operador puede seleccionar un perfil
habilitado de la misma familia para proponer una corrección. Una confirmación
independiente autoriza únicamente `production_profile` del manifest del candidato.
No se corrigen asset_type, asset_id, clasificación, textos ni canon. Si el nuevo
contrato exige cambios adicionales, STOP. La reserialización del manifest corregido
puede cambiar su whitespace; todos los demás valores JSON permanecen iguales.

La propuesta muestra original y vista previa, resoluciones, crecimiento, márgenes,
método, comprobaciones y hashes. Cambiar origen, perfil o límites invalida la
aprobación visible. La corrección de perfil y la normalización requieren decisiones
distintas. Rechazo/cancelación nunca aprueban un candidato ni autorizan producción.
La comparación permite zoom y desplazamiento independiente; los paneles del Inbox
y de decisión tienen scroll para conservar acceso a los controles en ventanas pequeñas.
El caller de la API debe aportar `ImageNormalizationAuthorization` que atestigua
la decisión humana sobre ID y digest exactos de la propuesta; no hay aprobación
predeterminada ni autenticación de personas a través de esa API local.

## Almacenamiento, trazabilidad y fallos

```text
WorkspaceRoot/normalization/
  .nap-<guid>.inspecting/          # inspección temporal, sin candidato definitivo
  .nap-<guid>.pending/             # escritura privada, nunca usada para producción
  <operation_id>/
    candidate/<asset_id>.zip      # solo Approved; PNG dentro conforme al perfil
    control/receipt.json          # registro fuera del ZIP
  failures/<operation_id>-<attempt_id>/control/receipt.json  # sin candidato
```

Para un PNG individual, `candidate/` contiene el nombre del maestro que declare el
perfil en lugar del ZIP. Un rechazo o `Unchanged` tiene solo `control/receipt.json`.
El ZIP contiene **exactamente** los archivos permitidos por su perfil; no incorpora
recibos, previews, README ni otros archivos. Conserva los companions sin cambios.
La estructura no presupone cinco archivos ni que el source role se llame `master`.

El recibo JSON cerrado, snake_case y versionado (`schema_version=1`) conserva:
universo/asset/familia, ID de operación, ruta/SHA-256 del origen (ZIP o PNG), nombre
y SHA-256 de PNG original/candidato, perfiles, conversión, geometría, política,
algoritmo, digest de evidencia, consentimiento separado, decisión, fecha UTC y
hashes de outputs. No almacena secretos, prompts duplicados ni una identidad personal del aprobador.
Se rechazan campos desconocidos/duplicados, registros incoherentes y hashes inválidos.

Los hashes del origen se revalidan antes de escribir y antes del commit de carpeta.
La operación usa mutex local sin lock files, CreateNew/flush y **un rename de
directorio sin overwrite** que publica candidato y control juntos. Antes de ese
punto, errores y cancelación limpian únicamente el temporal propio. Después del
rename, un cierre abrupto o pérdida de respuesta puede recuperarse desde el recibo;
un reintento de la misma propuesta verifica outputs y no los reescribe. Una decisión
distinta, colisión, hash stale o candidato modificado producen STOP.

Los fallos/cancelaciones posteriores a una decisión válida registran un intento
independiente, sin candidato, con `approved_by_user`, resultado `Failed`/`Cancelled`
y `incident_codes`. No bloquean un reintento autorizado de la misma propuesta ni
convierten una publicación ya comprometida en fallo. Si el almacenamiento no admite
el registro (permisos, disco o raíz insegura), se conserva el STOP original y se
marca `normalization_control_record_unavailable`; no se inventa evidencia durable.
Una inspección inválida/cancelada antes de tener propuesta no crea un recibo de
asset; comunica el diagnóstico y limpia la inspección privada.

Se rechazan links/reparse points en origen, destinos y ancestros, raíces solapadas
y WorkspaceRoot dentro de un checkout Git. NAP no sigue links durante recuperación
o limpieza. Como las otras fronteras NAP, WorkspaceRoot debe ser local y controlada;
no se soporta sustitución concurrente hostil del árbol por un proceso externo.
El decode/encode síncrono tiene budgets; cancelación se comprueba antes/después y
por filas/copias, sin publicar una respuesta que haya sido cancelada antes del rename.

`Scan` es read-only: valida recibos/candidatos y señala residuos incompletos.
No promueve, repara, borra ni reanuda nada. La UI muestra los recibos verificados
para selección explícita. El historial carga como máximo 500 operaciones; superar
el límite requiere revisión explícita. Los journals de Job y su recuperación no
cambian ni se usan como registros de normalización.

## Continuación por el circuito normal

«Preparar candidato aprobado» verifica otra vez el recibo y ZIP y crea un staging
independiente por operación/intento. No reemplaza el ZIP de igual nombre en Inbox.
Después: extracción segura → validación → ProcessingPlan → auditoría real PASS →
confirmación de publicación → Archive/Production/SQLite/COMPLETED. La aprobación
geométrica no sirve como PASS, evidencia de producción, permiso Git ni publicación.

Se preserva asset_id. Si ya está archivado/publicado con otros bytes, las reglas
existentes de colisión y no overwrite producen STOP. La normalización no concede
una nueva identidad, sustituye un master registrado ni decide versionado/canon.

## Treskal y validación

Los dos ZIP reales citados por el usuario declaran `scene_narrative` y tienen
maestros 1586×992. El módulo puede proponer 1592×995 y, con selección/consentimiento
separados, `scene_cartography`. **No se han abierto, normalizado ni publicado esos
mapas en desarrollo.** Canon, legibilidad y posibles artefactos siguen sujetos a
revisión humana; el perfil narrativo permanece deshabilitado.

Las pruebas usan píxeles sintéticos y roots temporales: geometría/overflow,
RGBA/RGB 8/16-bit, determinismo, original intacto, ZIP hostil, límites, reparse,
fallos de escritura/permisos, cancelación, recuperación, corrupción de control y
reintentos. Dos perfiles completan el pipeline normal con PASS de fixture offline;
eso no aprueba mapas reales. Las pruebas WPF comparan ambos buffers en 1200×700 y
2560×1600 sin autorizar candidatos definitivos ni producir assets.
