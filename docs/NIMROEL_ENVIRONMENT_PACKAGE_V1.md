# Nimroel — escenas ambientales y narrativas · contrato NAP v1

**Fecha:** 2026-11-13  
**Universo:** `nimroel`  
**Regla de producción:** `asset_type=scene` + `production_profile=scene_environment`  
**Estado:** propuesta implementada en rama de NAP, pendiente de validación ejecutable y fusión.

## 1. Alcance

El perfil `scene_environment` admite escenas ambientales y de ambientación narrativa, tanto interiores como exteriores: paisajes, aldeas y ciudades, vistas urbanas, caminos, puertos, mercados, viviendas, castillos, fortalezas, tabernas y talleres. Admite variantes de luz, meteorología y estación, siempre que sean **imágenes estáticas PNG**. No valida por sí mismo hechos del canon ni concede aprobación artística.

Es independiente de `scene_cartography`. Los mapas diegéticos se producen con `scene_cartography` **16:10 / 1600×1000 Q90** y los retratos con `portrait_npc` **4:5 / 768×960 Q90**. No se modifica ninguna regla preexistente. El identificador `scene_narrative` no es un alias y continúa sin configurarse.

## 2. Contrato de transferencia: cinco archivos planos

Para un identificador de ejemplo `scene_treskal_market_evening_001` (solo ilustrativo; comprobar previamente la disponibilidad del ID), la entrega es un ZIP llamado exactamente:

```text
scene_treskal_market_evening_001.zip
├── scene_treskal_market_evening_001.png
├── scene_treskal_market_evening_001_manifest.json
├── scene_treskal_market_evening_001_prompt.md
├── scene_treskal_market_evening_001_info.md
└── scene_treskal_market_evening_001_visual_identity.json
```

- Los **cinco archivos** están directamente en la raíz del ZIP; sin directorio envolvente, subdirectorios, `README`, `WebP`, miniaturas ni extras.
- El ZIP, el PNG y los cuatro companions mantienen exactamente el mismo `asset_id` canónico y extensiones minúsculas.
- El identificador respeta `docs/NAMING_V1.md`: ASCII lowercase snake_case, prefijo `scene_`, tres dígitos finales `001`–`999` y estabilidad/unicidad de identidad.
- El master PNG es estático, con píxeles reales decodificables y **relación exacta 16:9**; no se aceptan dimensiones simplemente aproximadas.
- `_prompt.md` debe preservar el prompt efectivamente utilizado, `_info.md` la ficha y el contexto de canon, y `_visual_identity.json` los datos visuales estructurados relevantes. Los tres archivos son obligatorios aunque NAP no valide todavía todo su contenido artístico.
- **No** incluir un WebP de producción: NAP lo genera tras las verificaciones.

### Manifest v2 exacto

```json
{
  "schema_version": 2,
  "universe_id": "nimroel",
  "asset_id": "scene_treskal_market_evening_001",
  "asset_type": "scene",
  "production_profile": "scene_environment",
  "classification": {
    "culture": "norgard",
    "location": "treskal"
  }
}
```

`classification` admite exclusivamente las claves `culture`, `realm`, `region` y `location`; **culture y location son obligatorias**. `realm` y `region` son opcionales y no cambian la ruta. Los valores siguen la gramática Naming v1. Los nombres de culturas y lugares deben proceder del canon, no inventarse para pasar una validación técnica.

Cualquier campo raíz adicional, manifiesto v1, perfil distinto, archivo ausente, archivo adicional, casing incorrecto o ruta anidada detiene el paquete. El ZIP seguro y el paquete semánticamente válido son comprobaciones separadas.

## 3. Conversión sin recorte ni deformación

`config/universes/nimroel/profile.json` declara:

| Parámetro | Valor |
| --- | --- |
| Fuente | PNG maestro estático, `png_master` |
| Conversión | `png_to_webp`, rol `master` |
| Proporción | 16:9 exacta |
| Salida | WebP 1920×1080 |
| Calidad | Q90, lossy |
| Recorte, padding, letterbox, distorsión | Prohibidos |
| Redimensionado proporcional | Permitido: reducción, identidad o ampliación |

La geometría se comprueba con productos enteros `(long)sourceWidth*1080 == (long)sourceHeight*1920` sin tolerancias. Un PNG `1920×1080`, `2560×1440`, `3840×2160` o `1280×720` es proporcional; solo será aceptable si supera además la validación PNG y **el límite de píxeles configurado en ejecución**. La preparación manual actual de NAP utiliza por defecto `64_000_000` píxeles; los tests sintéticos utilizan deliberadamente un presupuesto inferior de `4_000_000`. No existe un límite de resolución universal expresado por este perfil.

El motor existente `ProductionImageEngine` valida estructura, decode, proporción, conversión y WebP final. No se añade un segundo conversor ni se modifica `ScenePngToWebpConverter`. Cualquier mismatch produce STOP, nunca una corrección invisible. La normalización con intervención humana, cuando proceda, es un flujo explícito separado que conserva el maestro original.

## 4. Routing, originales y publicación

La ruta se obtiene de los cinco segmentos declarativos:

```text
scenes/environment/<culture>/<location>/<asset_id>/
```

En producción se guardan el WebP, el manifest v2 **sin reserializar** y los tres companions, por ejemplo:

```text
scenes/environment/norgard/treskal/scene_treskal_market_evening_001/
├── scene_treskal_market_evening_001.webp
├── scene_treskal_market_evening_001_manifest.json
├── scene_treskal_market_evening_001_prompt.md
├── scene_treskal_market_evening_001_info.md
└── scene_treskal_market_evening_001_visual_identity.json
```

El archivo PNG maestro se archiva con su documentación en el `ArchiveRoot` autorizado y se preserva íntegro. La sincronización a TeraBox es externa a NAP; **un archivo local archivado no prueba por sí solo su existencia remota**. El ZIP de entrada y el PNG maestro no se incorporan al directorio de producción Git. SQLite registra únicamente tras el cierre de producción verificado. Un `PASS` de auditoría no significa publicación: deben superarse también las comprobaciones de seguridad y las autorizaciones del flujo operativo.

La auditoría IA de NAP revisa los hechos del plan, pero **no ve la imagen ni certifica automáticamente el canon visual**. La fábrica de assets debe aplicar por separado las biblias y controles artísticos vigentes de Nimroel.

## 5. Pruebas y límites de certificación

`tests/NAP.Tests/NimroelEnvironmentProfileTests.cs` añade cobertura de:

1. Regla v4, cinco roles, conversión y ausencia de modificaciones a los perfiles existentes.
2. Manifest v2, ZIP plano, staging/extracción y cinco ficheros requeridos.
3. Rechazo de manifiestos erróneos, perfiles no admitidos, clasificación inválida, extras, archivos ausentes, nombres con casing incorrecto y directorios anidados.
4. Validación del PNG, STOP de ratios 16:10 y 4:3 y preservación de las fuentes tras rechazo.
5. Resolución independiente de `scenes/environment/norgard/treskal/<asset_id>`.
6. Conversión WebP real 1920×1080 Q90, comprobación de esquinas y bandas de color para constatar encuadre completo.
7. Recorrido de prueba ZIP → Inbox → staging → validación → plan → auditor PASS sintético → Archive → producción → SQLite → COMPLETED; idempotencia y sentinelas de retratos/cartografía intactos.

Los tests emplean únicamente datos temporales sintéticos; el `PASS` offline **no** representa una auditoría real con Gemini ni aprueba ningún asset del videojuego. Las regresiones generales de NAP se ejecutarán únicamente cuando el responsable autorice expresamente el workflow `NAP CI`; su ejecución automatizada permanece deshabilitada por decisión anterior. Esta documentación no acredita por sí sola una compilación o suite ejecutada.

## 6. Referencias

- `docs/PACKAGE_CONTRACT_V1.md`
- `docs/PACKAGE_SEMANTIC_VALIDATION.md`
- `docs/MANIFEST_V2.md`
- `docs/NAMING_V1.md`
- `docs/SAFE_ZIP_EXTRACTION.md`
- `docs/SCENE_CONVERSION.md`
- `docs/NO_SILENT_CROP.md`
- `docs/NIMROEL_CARTOGRAPHY_PACKAGE_V1.md`
- `schemas/nap-manifest-v2.schema.json`
- `config/universes/nimroel/profile.json`
