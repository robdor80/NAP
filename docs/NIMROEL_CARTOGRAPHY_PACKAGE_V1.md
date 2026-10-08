# Nimroel — paquete de cartografía NAP v1

**Estado:** implementado y probado en rama independiente; incorporación a `main` sujeta a revisión del PR y CI.
**Universo:** `nimroel`.
**Regla independiente:** `asset_type=scene`, `production_profile=scene_cartography`.

## 1. Alcance

Planos urbanos, mapas mercantiles y cartografía diegética de Nimroel. No es el perfil narrativo `scene_narrative` ni modifica `portrait_npc`. El mapa diegético no sustituye ni reescribe el GeoJSON espacial canónico de Treskal.

## 2. Contrato del ZIP

Nombre: `<asset_id>.zip`, con exactamente cinco ficheros planos en la raíz:

```
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_visual_identity.json
<asset_id>_manifest.json
```

Ninguna subcarpeta, preview, WebP, README ni otro archivo extra. ID en lowercase snake_case terminado en `001`–`999`, como `scene_treskal_official_city_plan_001`.

### Manifest canónico

```json
{
  "schema_version": 2,
  "universe_id": "nimroel",
  "asset_id": "scene_treskal_official_city_plan_001",
  "asset_type": "scene",
  "production_profile": "scene_cartography",
  "classification": {
    "culture": "norgard",
    "location": "treskal"
  }
}
```

Las dimensiones requeridas son `culture` y `location`; `realm` y `region` son opcionales. No se inventan clasificaciones ni se añaden otros campos al manifest.

## 3. Conversión y rutas

Perfil declarado en `config/universes/nimroel/profile.json`:

- PNG maestro estático, validador `png_master`.
- Proporción fuente **exacta 16:10**. Dimensiones fuente válidas, por ejemplo: `1600×1000`, `1920×1200`, `2560×1600`. 1920×1080 **NO es 16:10**.
- WebP de producción: **1600×1000**, **Q90**, sin crop/pad/letterbox/estiramiento asimétrico.
- PNG original archivado; companions y manifest preservados sin reescritura.
- Routing: `scenes/cartography/<culture>/<location>/<asset_id>/`.

## 4. Incidencia real del primer mapa de Treskal

Según la auditoría aportada del ZIP original `scene_treskal_official_city_plan_001.zip`:

- cinco archivos correctos y manifiesto v2 correcto en estructura;
- `production_profile` actual del manifest: **`scene_narrative`**;
- el perfil nuevo requiere **`scene_cartography`**;
- PNG fuente: **1586×992**; la proporción **NO** es exactamente 16:10.

**Resultado:** este ZIP no se puede publicar aún con el perfil nuevo. Debe prepararse una nueva versión controlada del package (original conservado) con `production_profile=scene_cartography` y un PNG maestro verdaderamente 16:10. No se debe renombrar ni reescalar/distorsionar el PNG original a escondidas. Se requiere comprobar también el segundo mapa por separado.

### Corrección controlada pendiente

1. Conservar el ZIP original y sus bytes; no lo modifica esta implementación.
2. Preparar el candidato corregido en otro directorio de revisión, sin sobrescribir
   el original ni decidir automáticamente una nueva identidad/versionado canónico.
3. Declarar `scene_cartography` en el manifest del nuevo candidato. Cambiar solo
   ese campo **no basta**: 1586×992 sigue siendo incompatible con la conversión.
4. Reexportar desde una fuente cartográfica con composición 16:10 exacta o preparar
   un nuevo maestro aprobado visualmente. La decisión de composición/canon requiere
   revisión humana; NAP no recorta, estira, rellena ni modifica el original para resolverlo.
5. Revisar los companions y validar el nuevo ZIP; obtener auditoría PASS y autorización
   explícita de publicación. Auditar el segundo mapa por separado antes de admitirlo.

La validación semántica comprueba PNG estructural y clasificación; el ratio exacto
se comprueba por el validador geométrico y por el motor antes de generar/publicar WebP.
Un archive puede conservar los bytes originales incluso si después la conversión
produce STOP; esto no autoriza publicación, corrección ni reescritura del maestro.

## 5. Seguridad

- Un ZIP en Inbox solo es un candidato; no se publica automáticamente.
- `Preparar → Auditar (PASS) → Publicar` requiere validaciones y autorizaciones explícitas.
- No tocar los mapas existentes, los 39 retratos legacy, ni el repo de worldbuilding.
- `scene_narrative` permanece sin activar en Nimroel hasta que se diseñe de forma separada.
- No se altera ni interpreta geografía canónica a partir del arte ilustrado.

## 6. Pruebas de integración reproducibles

`NimroelCartographyProfileTests` usa exclusivamente directorios temporales y mapas
sintéticos `scene_treskal_test_plan_001`, nunca los dos ZIP reales de Treskal.
Comprueba clasificación, routing, perfil narrativo rechazado y STOP read-only de
1586×992 y 1920×1080. Los maestros 1600×1000 y 1920×1200 completan
ZIP → readiness → staging → extracción → validación → conversión → Archivo →
publicación verificada → SQLite → COMPLETED, con PASS de fixture offline.
Verifica SHA-256/bytes del maestro, todos los companions, preservación del ZIP
y de sentinelas publicados; repetir el mismo Job es idempotente y no escribe.
Ese PASS sintético prueba el motor y no aprueba mapas reales ni implica llamadas a IA.
La integración existente del retrato sigue comprobando WebP 768×960 Q90 y su ruta.
