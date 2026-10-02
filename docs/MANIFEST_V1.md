# NAP Manifest v1 — Fase 2 · Capítulo 2.1

Manifest v1 es el contrato formal de identidad administrativa del asset,
su clasificación estructural y el perfil de producción que NAP deberá resolver.
El contrato se define mediante [el JSON Schema](../schemas/nap-manifest-v1.schema.json)
y este documento. En 2.1 no se implementan modelo C#, parser, serializer,
validación semántica ni registros de categorías, perfiles o dimensiones.
El siguiente capítulo es **2.2 — Modelo C#**.

## Contrato raíz

El manifest es un objeto JSON con exactamente estas cinco propiedades
obligatorias; no se permiten propiedades raíz adicionales ni valores `null`.

| Campo | Tipo | Semántica |
| --- | --- | --- |
| `schema_version` | Entero, valor `1` | Versión incompatible del contrato. No identifica una revisión del asset ni utiliza SemVer. |
| `asset_id` | String con al menos un carácter no whitespace | Identifica de forma única el asset. La gramática y reglas de naming se definirán en 2.3. |
| `asset_type` | String con al menos un carácter no whitespace | Identificador estructural de categoría. Sistema extensible, sin enum cerrado. |
| `production_profile` | String con al menos un carácter no whitespace | Identifica el perfil de producción que NAP deberá resolver. |
| `classification` | Objeto | Mapa de dimensiones estructurales normalizadas a strings con al menos un carácter no whitespace. |

`asset_type` puede describir categorías como `portrait`, `scene`, `heraldry`,
`object` y `environment`; estos ejemplos no son una lista exhaustiva.
Una categoría desconocida pasará la comprobación estructural si es una string
con al menos un carácter no whitespace, pero la futura capa semántica deberá considerarla no soportada y
detener el procesamiento (STOP). Lo mismo aplica a perfiles no soportados.

La unicidad de `asset_id`, el soporte de una combinación categoría/perfil
y la gramática de identificadores no se comprueban con este schema.
En 2.1 la definición compartida usa `type: string`, `minLength: 1` y
`pattern: "\\S"`: exige al menos un carácter no whitespace. Rechaza `""`,
`" "`, `"   "` y cadenas formadas únicamente por tabulaciones o saltos de línea.
No aplica trim automático ni impone lowercase, caracteres permitidos o
gramática de naming; esas reglas siguen pendientes de 2.3.

## Classification

Las dimensiones inicialmente conocidas son `culture`, `realm`, `region`,
`location`, `role` y `sex`. No son obligatorias para todas las categorías.
Para la combinación exacta `asset_type = portrait` y
`production_profile = portrait_npc`, se requieren `culture`, `location`,
`role` y `sex`; `realm` y `region` son opcionales. Para otras combinaciones,
este capítulo no inventa requisitos de clasificación; el schema admite
un objeto vacío y la futura capa semántica resolverá sus requisitos.

Los valores son identificadores normalizados de máquina, como `norgard`,
`treskal`, `farmer` y `male`. No deben utilizarse nombres de presentación
como `Norgard`, `Treskal`, `Campesino` o `Hombre`. Este criterio es contractual;
el schema todavía no impone una gramática de normalización.

El mapa es extensible: una futura dimensión estructural como `house` puede
añadirse sin cerrar el schema a sus seis propiedades conocidas. Todas las
dimensiones adicionales deben tener valores string con al menos un carácter no whitespace. Su admisión
estructural no implica aprobación semántica: cuando exista el registro de
NAP, una dimensión no registrada deberá rechazarse. No se implementa ese
registro en 2.1. Classification no admite rasgos visuales ni tags arbitrarios,
aunque el schema por sí solo no identifica esa diferencia semántica.

## Separación de Visual Identity

El manifest describe **qué es el asset**; `<asset_id>_visual_identity.json`
describe **qué se ve realmente en la imagen final**.

`apparent_age` queda excluido de Manifest v1, al igual que `life_stage`,
`age_band`, `body_build`, `eye_color`, `hair_color`, `beard`, `baldness`,
`skin_tone`, `face_shape` y cualquier otro rasgo visual. Tampoco deben
introducirse dentro de classification. La edad o el aspecto solicitado
pueden conservarse en el prompt; la apariencia observada pertenece a
Visual Identity. No se define aquí el contrato de Visual Identity.

## Relación con production_profile

El JSON contiene únicamente la referencia `production_profile`, por ejemplo
`portrait_npc`. NAP deberá resolver esa referencia en su futura configuración
o registro de perfiles. No se duplican parámetros de producción en el manifest.

La decisión existente para `portrait_npc` se documenta como referencia:

| Parámetro | Decisión acordada |
| --- | --- |
| Fuente | PNG maestro |
| Proporción esperada | 4:5 |
| Formato de producción | WebP |
| Resolución de producción | 768 × 960 |
| Calidad WebP | Q90 |
| Crop | No permitido; sin recorte silencioso |

Estos datos no son campos JSON ni implican que haya conversión o registro
de perfiles implementados. La política exacta de resize se resolverá con
la configuración del perfil cuando corresponda.

**Scenes todavía no tienen un perfil definitivo, resolución ni proporción
acordadas.** La categoría `scene` no permite inferir esos parámetros.

## Datos excluidos

No pertenecen al manifest los parámetros de conversión (`width`, `height`,
aspect ratio, calidad WebP, políticas de resize/crop), rutas TeraBox o del
repositorio, rutas de producción, destinos calculados, SHA-256 u otros hashes,
Job ID, estados del pipeline, fechas de procesamiento, resultados de Gemini,
auditorías, conversiones ejecutadas, thumbnails, rasgos visuales, tags libres
ni listas redundantes de nombres de archivos. El naming de archivos
corresponde a 2.3. No hay campos para datos derivados del procesamiento.

## JSON Schema y evolución

El schema utiliza [JSON Schema Draft 2020-12](https://json-schema.org/draft/2020-12/json-schema-validation),
con el identificador estable `urn:nap:manifest:v1`; este identificador no
es una dirección de descarga. `additionalProperties: false` cierra el objeto
raíz. Classification documenta sus propiedades conocidas y permite otras
strings con al menos un carácter no whitespace. Un `if`/`then` exige las cuatro dimensiones de
`portrait`/`portrait_npc`, únicamente cuando se dan ambas condiciones.

El schema valida estructura, no soporte operativo ni normalización semántica.
Un tipo, perfil o dimensión desconocidos pueden pasar el schema y requerir
STOP posterior. No se debe normalizar, renombrar o aceptar silenciosamente
un valor desconocido al implementar esa capa.

Un cambio incompatible en el contrato requiere un nuevo entero de
`schema_version` y su schema/documentación propios. No se reinterpreta
silenciosamente una versión anterior. Agregar una propiedad raíz cambia
el contrato cerrado y requiere una nueva versión. Añadir categorías,
perfiles o dimensiones registrados que respeten esta estructura extensible
no requiere por sí solo incrementar la versión. Cambiar los datos de un
asset no cambia `schema_version`. Las correcciones editoriales que no
alteren el contrato tampoco la incrementan.

La futura implementación deberá detectar y detenerse ante versiones no
soportadas; este comportamiento aún no está implementado. El ejemplo antiguo
de NAP 1 queda como contexto histórico en `NAP_CONTINUIDAD_NAP1.md`; este
documento y el schema definen Manifest v1 vigente.

## Ejemplo completo válido

```json
{
  "schema_version": 1,
  "asset_id": "portrait_treskal_farmer_male_001",
  "asset_type": "portrait",
  "production_profile": "portrait_npc",
  "classification": {
    "culture": "norgard",
    "location": "treskal",
    "role": "farmer",
    "sex": "male"
  }
}
```

Este ejemplo se usa en el fixture de Fase 1. Su test de integración sigue
tratando el manifest como bytes opacos: no incorpora validación JSON ni
semántica. El `.png` del fixture sigue siendo un placeholder de texto.
