# Biblia de Protocolo NAP — Nimroel

**Versión:** 1.1 — protocolo cartografía Nimroel; preparación técnica mediante normalización explícita
**Ámbito:** Universo **Nimroel**
**Estado:** Documento de referencia operativo para preparar paquetes ZIP que NAP pueda procesar correctamente.
**Objetivo:** Definir cómo preparar **retratos** y **cartografía** para el perfil de Nimroel. Las escenas narrativas se conservan únicamente como propuesta futura no habilitada.

---

## 1. Principio general

Cuando un asset se prepara **para NAP**, debe entregarse como un **ZIP plano y autocontenido**.

Eso significa que:

- el **ZIP se llama exactamente igual que el `asset_id`**;
- dentro del ZIP van los archivos del asset **en la raíz**;
- **no** hay subcarpetas;
- **no** hay archivos extra;
- el ZIP contiene el **PNG maestro** y su documentación asociada;
- **NAP genera después** el WebP de producción.

---

## 2. Regla universal de empaquetado

### 2.1. Nombre del ZIP

Debe ser exactamente:

```text
<asset_id>.zip
```

Ejemplos:

```text
portrait_treskal_farmer_male_040.zip
scene_treskal_market_morning_001.zip
```

### 2.2. Estructura interna del ZIP

El ZIP debe ser **flat**. Los archivos van directamente en la raíz.

### Correcto

```text
portrait_treskal_farmer_male_040.zip
├── portrait_treskal_farmer_male_040.png
├── portrait_treskal_farmer_male_040_prompt.md
├── portrait_treskal_farmer_male_040_info.md
├── portrait_treskal_farmer_male_040_visual_identity.json
└── portrait_treskal_farmer_male_040_manifest.json
```

### Incorrecto

```text
portrait_treskal_farmer_male_040.zip
└── portrait_treskal_farmer_male_040/
    ├── portrait_treskal_farmer_male_040.png
    └── ...
```

NAP espera un package **plano**, no una carpeta dentro del ZIP.

---

## 3. Archivos permitidos dentro del ZIP

Para los assets de Nimroel que generemos mediante este protocolo, el ZIP contendrá **exactamente estos cinco archivos**:

```text
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_visual_identity.json
<asset_id>_manifest.json
```

### No se deben incluir

No deben ir dentro del ZIP:

- archivos WebP;
- miniaturas;
- README extra;
- `.DS_Store`;
- `Thumbs.db`;
- otros JSON no previstos;
- carpetas;
- varios assets mezclados;
- cualquier archivo ajeno al asset.

**Regla:** un ZIP = un asset.

---

## 4. Naming general

### 4.1. Reglas de naming

Todo `asset_id` debe:

- ir en minúsculas;
- usar `snake_case` ASCII;
- no tener espacios;
- no tener tildes;
- no tener caracteres raros;
- terminar en una secuencia `001`–`999`.

### 4.2. Formato recomendado de ID

#### Retratos

```text
portrait_<location>_<role>_<sex>_<NNN>
```

Ejemplo:

```text
portrait_treskal_farmer_male_040
```

#### Escenas

```text
scene_<location>_<descriptor>_<NNN>
```

Ejemplo:

```text
scene_treskal_market_morning_001
scene_hallheim_throne_room_002
scene_nebdek_ruins_night_003
```

> `descriptor` debe ser corto, claro y en `snake_case`.

---

# 5. Protocolo NAP para RETRATOS de Nimroel

## 5.1. Estado

Este apartado refleja el contrato operativo **ya alineado** con el perfil Nimroel actual de NAP para retratos NPC.

## 5.2. Identidad del asset

Para retratos Nimroel:

```text
universe_id        = nimroel
asset_type         = portrait
production_profile = portrait_npc
```

## 5.3. Clasificación

Para `portrait_npc`, las dimensiones mínimas obligatorias son:

```text
culture
location
role
sex
```

Dimensiones opcionales admitidas por el perfil de Nimroel:

```text
realm
region
```

### Ejemplo real de clasificación válida

```json
{
  "culture": "norgard",
  "location": "treskal",
  "role": "farmer",
  "sex": "male"
}
```

---

## 5.4. Archivos del retrato

Ejemplo completo para el asset:

```text
portrait_treskal_farmer_male_040
```

El ZIP contendrá:

```text
portrait_treskal_farmer_male_040.png
portrait_treskal_farmer_male_040_prompt.md
portrait_treskal_farmer_male_040_info.md
portrait_treskal_farmer_male_040_visual_identity.json
portrait_treskal_farmer_male_040_manifest.json
```

---

## 5.5. PNG maestro del retrato

Nombre:

```text
portrait_treskal_farmer_male_040.png
```

### Requisitos

- debe ser **PNG estático** real;
- no debe ser APNG animado;
- debe conservar el cuadro completo;
- **NAP no recorta**;
- **NAP no añade bandas**;
- **NAP no deforma** la imagen.

### Proporción obligatoria

Para retratos Nimroel, el PNG maestro debe tener proporción exacta:

```text
4:5
```

### Ejemplos válidos

```text
768 × 960
1024 × 1280
1536 × 1920
2048 × 2560
```

### Recomendación de trabajo

Para creación de maestros:

```text
1536 × 1920
```

es una muy buena resolución base.

### Límite de entrada

El PNG maestro no debe exceder el presupuesto de píxeles configurado en NAP. Como norma práctica, debemos mantenernos en tamaños razonables y seguros.

---

## 5.6. Prompt del retrato

Nombre:

```text
portrait_treskal_farmer_male_040_prompt.md
```

Contenido recomendado:

```markdown
# Prompt

[Prompt exacto utilizado para generar el retrato]
```

### Regla de oro

Debe guardarse el **prompt exacto o prácticamente exacto** utilizado para crear la imagen.

---

## 5.7. Información del retrato

Nombre:

```text
portrait_treskal_farmer_male_040_info.md
```

Contenido recomendado:

```markdown
# portrait_treskal_farmer_male_040

Retrato de NPC de Nimroel.

- Universo: Nimroel
- Tipo: portrait
- Perfil: portrait_npc
- Cultura: norgard
- Localización: treskal
- Rol: farmer
- Sexo: male

## Descripción

[Descripción breve del personaje y del aspecto representado.]

## Notas

[Observaciones relevantes de continuidad visual, contexto o producción.]
```

---

## 5.8. Visual Identity del retrato

Nombre:

```text
portrait_treskal_farmer_male_040_visual_identity.json
```

Debe contener la identidad visual estable del asset. Por ejemplo:

- edad aparente;
- sexo;
- color de pelo;
- color de ojos;
- tono de piel;
- barba;
- peinado;
- vestuario;
- rasgos faciales;
- accesorios;
- estado de suciedad/desgaste;
- cualquier rasgo persistente útil.

### Regla

Debe ser **JSON válido**.

### Importante

NAP exige su presencia y nombre correcto. Además, para nuestro flujo de trabajo, este archivo debe ser coherente con la biblia visual y con el asset final.

---

## 5.9. Manifest del retrato

Nombre:

```text
portrait_treskal_farmer_male_040_manifest.json
```

### Ejemplo canónico

```json
{
  "schema_version": 2,
  "universe_id": "nimroel",
  "asset_id": "portrait_treskal_farmer_male_040",
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

### Reglas

- `schema_version` = `2`
- `universe_id` = `nimroel`
- `asset_type` = `portrait`
- `production_profile` = `portrait_npc`
- `classification` debe contener al menos:
  - `culture`
  - `location`
  - `role`
  - `sex`
- no deben añadirse propiedades arbitrarias “porque sí”.

---

## 5.10. Qué hace NAP después con el retrato

NAP:

1. recibe el ZIP;
2. lo detecta y lo valida;
3. extrae el package;
4. valida el manifest;
5. valida el PNG maestro;
6. archiva el maestro PNG;
7. genera el WebP de producción;
8. copia prompt/info/visual_identity/manifest a producción;
9. registra el resultado.

### Producción esperada del retrato

Para Nimroel, el retrato se convierte a WebP de producción según el perfil actual.

Actualmente, para retratos NPC:

```text
768 × 960
Quality 90
```

Ejemplo de salida:

```text
ProductionRoot/portraits/norgard/treskal/farmer/male/
└── portrait_treskal_farmer_male_040/
    ├── portrait_treskal_farmer_male_040.webp
    ├── portrait_treskal_farmer_male_040_prompt.md
    ├── portrait_treskal_farmer_male_040_info.md
    ├── portrait_treskal_farmer_male_040_visual_identity.json
    └── portrait_treskal_farmer_male_040_manifest.json
```

El **PNG maestro no se copia a Producción**: queda archivado en Archive.

---

# 6. Propuesta futura para ESCENAS NARRATIVAS de Nimroel

## 6.1. Estado

NAP tiene soporte genérico de conversión para escenas. El perfil de Nimroel declara `scene_cartography` (sección 6B); **no declara `scene_narrative`**. Esta sección conserva una propuesta futura para escenas narrativas, sin habilitarla ni aplicarla a mapas. Los paquetes narrativos no se admiten mientras no exista una regla aprobada en el perfil activo.

Por tanto, esta sección actúa como:

1. **referencia de empaquetado propuesta** para futuras escenas narrativas de Nimroel;
2. guía de trabajo de ChatGPT;
3. referencia para alinear el `UniverseProfile` de Nimroel con este estándar.

## 6.2. Identidad propuesta del asset de escena

### Estándar recomendado

```text
universe_id        = nimroel
asset_type         = scene
production_profile = scene_narrative
```

> Propuesta pendiente para escenas narrativas. No es un perfil habilitado. Si se aprueba una regla futura, el ZIP deberá adaptarse a su contrato exacto. Para mapas, usar exclusivamente `scene_cartography`.

---

## 6.3. Naming de escenas

Formato recomendado:

```text
scene_<location>_<descriptor>_<NNN>
```

Ejemplos:

```text
scene_treskal_market_morning_001
scene_hallheim_throne_room_002
scene_nebdek_ruins_night_003
scene_border_forest_ambush_004
```

### Reglas para `descriptor`

Debe:

- ser breve;
- ser claro;
- describir el motivo principal de la escena;
- ir en `snake_case`;
- no ser excesivamente largo.

---

## 6.4. Clasificación recomendada para escenas

Como estándar mínimo recomendado para Nimroel:

```text
culture
location
```

Opcionales recomendadas, si aportan valor:

```text
realm
region
```

### Ejemplo simple

```json
{
  "culture": "norgard",
  "location": "treskal"
}
```

### Ejemplo más completo

```json
{
  "culture": "norgard",
  "realm": "norgard",
  "region": "western_hold",
  "location": "treskal"
}
```

> La clasificación final deberá coincidir con lo que permita el perfil Nimroel de escenas.

---

## 6.5. Archivos de la escena

Ejemplo completo para el asset:

```text
scene_treskal_market_morning_001
```

El ZIP contendrá:

```text
scene_treskal_market_morning_001.png
scene_treskal_market_morning_001_prompt.md
scene_treskal_market_morning_001_info.md
scene_treskal_market_morning_001_visual_identity.json
scene_treskal_market_morning_001_manifest.json
```

La lógica es exactamente la misma que en retratos: **cinco archivos, raíz del ZIP, nada más**.

---

## 6.6. PNG maestro de la escena

Nombre:

```text
scene_treskal_market_morning_001.png
```

### Requisitos

- PNG estático real;
- no APNG;
- cuadro completo;
- sin crop posterior por parte de NAP;
- la imagen debe venir ya con la proporción correcta del perfil final.

### Proporción

La proporción exacta de las escenas debe coincidir con la que declare el perfil de conversión de escenas.

### Propuesta narrativa futura

Los ejemplos narrativos siguientes proponen esta proporción, sin declarar una regla activa:

```text
16:9
```

con maestros como:

```text
1920 × 1080
2560 × 1440
```

### Regla crítica

Si el perfil de escena que terminemos aprobando no usa `16:9`, el PNG maestro deberá respetar **la proporción exacta que se configure**. NAP no corregirá esa discrepancia mediante recorte silencioso.

---

## 6.7. Prompt de la escena

Nombre:

```text
scene_treskal_market_morning_001_prompt.md
```

Contenido recomendado:

```markdown
# Prompt

[Prompt exacto utilizado para generar la escena]
```

Debe incluir el prompt completo de la escena, igual que en retratos.

---

## 6.8. Información de la escena

Nombre:

```text
scene_treskal_market_morning_001_info.md
```

Contenido recomendado:

```markdown
# scene_treskal_market_morning_001

Escena de Nimroel.

- Universo: Nimroel
- Tipo: scene
- Perfil: scene_narrative
- Cultura: norgard
- Localización: treskal

## Descripción

[Descripción breve de la escena: qué representa, momento del día, entorno, atmósfera, etc.]

## Notas

[Observaciones de continuidad visual, lore, personajes visibles o detalles importantes.]
```

---

## 6.9. Visual Identity de la escena

Nombre:

```text
scene_treskal_market_morning_001_visual_identity.json
```

Debe ser un JSON válido que describa, en la medida de lo posible, la identidad visual estable de la escena. Por ejemplo:

- localización representada;
- arquitectura dominante;
- clima;
- estación;
- hora del día;
- paleta dominante;
- presencia de personajes;
- elementos fijos del entorno;
- nivel de desgaste;
- ambiente general.

### Regla

Aunque NAP no haga aún una validación semántica profunda de este JSON, para nuestro flujo debe ser **limpio, válido y útil**.

---

## 6.10. Manifest de la escena

Nombre:

```text
scene_treskal_market_morning_001_manifest.json
```

### Ejemplo recomendado

```json
{
  "schema_version": 2,
  "universe_id": "nimroel",
  "asset_id": "scene_treskal_market_morning_001",
  "asset_type": "scene",
  "production_profile": "scene_narrative",
  "classification": {
    "culture": "norgard",
    "location": "treskal"
  }
}
```

### Nota importante

**Este ejemplo es narrativo, NO cartográfico. Para mapas usar siempre la sección 6B con `scene_cartography`.**

Este manifest refleja el **estándar recomendado** para escenas Nimroel. El valor exacto de `production_profile` deberá coincidir con la regla declarada finalmente en el perfil de universo activo.

---

---

# 6B. MAPAS Y CARTOGRAFÍA — `scene_cartography` (NUEVO EN V1.1)

> **Regla de precedencia:** para **mapas y planos** de Nimroel, esta sección tiene prioridad sobre el perfil genérico `scene_narrative` de la sección 6. Ese perfil narrativo todavía no está configurado en NAP.

## 6B.1. Identidad exacta

```text
universe_id        = nimroel
asset_type         = scene
production_profile = scene_cartography
```

La cartografía diegética es un tipo de **escena gráfica**, pero se trata con un perfil propio para no mezclarla con las escenas narrativas.

## 6B.2. ID y ZIP

Nombre recomendado: `scene_<location>_<descriptor>_<NNN>`. Ejemplo:

```text
scene_treskal_official_city_plan_001.zip
├── scene_treskal_official_city_plan_001.png
├── scene_treskal_official_city_plan_001_prompt.md
├── scene_treskal_official_city_plan_001_info.md
├── scene_treskal_official_city_plan_001_visual_identity.json
└── scene_treskal_official_city_plan_001_manifest.json
```

Los **cinco archivos van directamente dentro del ZIP**, sin carpetas y sin archivos adicionales.

## 6B.3. Manifest exacto

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

Obligatorias: `culture` y `location`. Opcionales cuando el canon lo confirme: `realm`, `region`.

## 6B.4. Resolución y conversión

- **Maestro:** PNG estático con proporción **exacta 16:10**.
- Ejemplos correctos: **1600×1000**, **1920×1200**, **2560×1600**.
- **Producción:** WebP **1600×1000 Q90** según `scene_cartography` en el perfil de Nimroel.
- **Prohibido:** recortes, bandas, distorsión, cambios silenciosos de relación de aspecto.
- **Ruta de producción:** `scenes/cartography/<culture>/<location>/<asset_id>/`.
- El PNG original pasa al almacenamiento de Archivo, nunca se pone como WebP dentro del ZIP.

## 6B.5. Prompt, info y Visual Identity

El `_prompt.md` conserva el prompt real; `_info.md` explica uso, procedencia, versión y relación con los mapas canónicos; `_visual_identity.json` conserva las características cartográficas (estilo, idioma de rótulos, emblemas, elementos geográficos, carácter diegético), sin inventar geografía canónica.

## 6B.6. Los dos mapas actuales de Treskal

Según la auditoría aportada, `scene_treskal_official_city_plan_001.zip` presenta dos diferencias con esta nueva regla:

1. Su manifest lleva `scene_narrative`, no `scene_cartography`.
2. Su PNG **1586×992** es **aproximadamente 16:10**, no exactamente 16:10: NAP bloquearía la conversión a 1600×1000.

**No editar ni destruir el ZIP original**. La adaptación exige un paquete corregido aparte y un maestro con proporción realmente 16:10, previa revisión visual; no vale forzar o estirar la imagen. El segundo ZIP debe auditarse por separado. Hasta fusionar el nuevo perfil en NAP y validar esos ZIP, permanecen en Inbox **sin publicar**.

## 6B.7. Estado de implantación

NAP incorpora además [normalización técnica segura](IMAGE_NORMALIZATION.md) como
preparación explícita: puede proponer 1586×992 → 1592×995 conservando los píxeles
originales y extendiendo solo bordes bajo límites. La comparación y aprobación
humana son obligatorias; `scene_narrative` → `scene_cartography` exige un segundo
consentimiento. El original sigue intacto, el candidato se revalida y después
necesita auditoría PASS/publicación confirmada. No hay adaptación dentro del
conversor ni aceptación/publicación automática de mapas. Los dos originales reales
no se han usado durante el desarrollo y permanecen pendientes de revisión separada.

`scene_cartography` está integrado en `main` mediante PR #40, declarado en `config/universes/nimroel/profile.json`, con pruebas de clasificación, conversión y pipeline completo sobre paquetes sintéticos en raíces temporales. La normalización explícita se entrega en rama/PR independientes para revisión. Estas pruebas no validan el canon ni aprueban los dos mapas reales de Treskal; no se han modificado ni publicado.

La identidad del perfil se valida antes de planificar. La geometría exacta se comprueba antes de convertir y publicar. El pipeline puede archivar primero el paquete original íntegro; un STOP de conversión no autoriza modificar ese archivo ni producir WebP. El [contrato cartográfico](NIMROEL_CARTOGRAPHY_PACKAGE_V1.md) detalla la corrección controlada y la frontera de validación.


# 7. Qué NO debe hacer ChatGPT al preparar ZIPs para NAP

No debe:

- meter varios assets en un mismo ZIP;
- poner subcarpetas dentro del ZIP;
- inventar nombres fuera del naming acordado;
- incluir WebP dentro del ZIP;
- poner previews o thumbnails extras;
- usar `.jpg` en lugar de `.png` para el maestro;
- cambiar el `asset_id` entre archivos;
- crear manifest con campos arbitrarios;
- crear assets con proporción incorrecta esperando que NAP los “arregle”.

---

# 8. Protocolo operativo para ChatGPT

Cuando el usuario pida:

> “Prepáralo para NAP”

ChatGPT debe entender que debe entregar un paquete con este estándar.

## 8.1. Si es un retrato

Debe crear:

```text
<asset_id>.zip
```

con:

```text
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_visual_identity.json
<asset_id>_manifest.json
```

usando:

```text
universe_id        = nimroel
asset_type         = portrait
production_profile = portrait_npc
```

## 8.2. Si es una escena narrativa futura

Debe crear:

```text
<asset_id>.zip
```

con:

```text
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_visual_identity.json
<asset_id>_manifest.json
```

usando, como estándar recomendado:

```text
universe_id        = nimroel
asset_type         = scene
production_profile = scene_narrative
```

Solo tras aprobar y declarar ese perfil en el `UniverseProfile` activo. Actualmente `scene_narrative` no está habilitado.

## 8.3. Si es un mapa o plano cartográfico

Crear el mismo ZIP plano de cinco archivos de la sección 6B, con `asset_type = scene`, `production_profile = scene_cartography`, clasificación obligatoria `culture`/`location` y PNG estático de proporción exacta 16:10. `realm`/`region` son opcionales cuando estén confirmados. NAP conserva el maestro intacto y genera WebP 1600×1000 Q90; no corrige geometría ni canon automáticamente.

---

# 9. Flujo previsto con RobGit + móvil + MSI

Flujo esperado:

```text
ChatGPT genera asset preparado para NAP
→ se guarda el ZIP en la carpeta sincronizada del repo
→ push desde móvil / RobGit
→ pull en el MSI / GitHub Desktop
→ el ZIP aparece en la Inbox observada por NAP
→ NAP lo detecta
→ el usuario revisa / autoriza
→ NAP lo procesa
```

### Importante

NAP no debe reinterpretar el ZIP como “cualquier cosa”: debe recibir ya el asset **correctamente normalizado**.

---

# 10. Importante: assets legacy pre-NAP

Los **39 retratos ya existentes** en el repositorio **no deben tratarse como assets nuevos normales**.

Esos retratos ya están en producción y pertenecen a la etapa pre-NAP.

Por tanto:

- **no deben borrarse**;
- **no deben republicarse ciegamente**;
- **no deben colisionar con nuevos paquetes**.

Deben pasar por un proceso específico de:

```text
adopción / migración legacy → NAP
```

para que NAP los reconozca, complete lo necesario y los registre sin romper la producción existente.

Este documento se aplica sobre todo a:

1. **assets nuevos**, y
2. assets legacy que se reprocesen expresamente mediante un flujo de migración controlado.

---

# 11. Checklist rápido

## Checklist universal

Antes de dar un ZIP por bueno, comprobar:

- [ ] El ZIP se llama exactamente `<asset_id>.zip`
- [ ] Dentro del ZIP no hay carpetas
- [ ] Dentro del ZIP solo hay los 5 archivos esperados
- [ ] Todos los archivos usan exactamente el mismo `asset_id`
- [ ] El manifest es válido
- [ ] El PNG maestro es correcto
- [ ] El prompt está guardado
- [ ] El info está guardado
- [ ] El `visual_identity.json` es válido
- [ ] No hay WebP dentro del ZIP
- [ ] No hay archivos extra

## Checklist de retrato

- [ ] `asset_type = portrait`
- [ ] `production_profile = portrait_npc`
- [ ] ratio del PNG = `4:5`
- [ ] clasificación mínima: `culture`, `location`, `role`, `sex`

## Checklist de escena narrativa futura (perfil no habilitado)

- [ ] `asset_type = scene`
- [ ] `production_profile` coincide con la regla aprobada
- [ ] ratio del PNG coincide exactamente con el perfil de escena
- [ ] clasificación mínima recomendada: `culture`, `location`

## Checklist de cartografía

- [ ] `asset_type = scene`
- [ ] `production_profile = scene_cartography`
- [ ] PNG estático de ratio exacto `16:10`
- [ ] clasificación obligatoria: `culture`, `location`
- [ ] solo `realm`, `region` como clasificación opcional
- [ ] procedencia y canon revisados; ninguna corrección silenciosa
- [ ] originales de Treskal conservados; candidato corregido revisado por separado

---

# 12. Fórmulas maestras

## Retrato Nimroel — fórmula fija

```text
ZIP:         <asset_id>.zip
ID:          portrait_<location>_<role>_<sex>_<NNN>
Tipo:        portrait
Perfil:      portrait_npc
Maestro:     PNG 4:5
Companions:  prompt + info + visual_identity + manifest
Producción:  WebP generado por NAP
```

## Escena narrativa Nimroel — propuesta futura no habilitada

```text
ZIP:         <asset_id>.zip
ID:          scene_<location>_<descriptor>_<NNN>
Tipo:        scene
Perfil:      scene_narrative   (o el perfil exacto aprobado)
Maestro:     PNG con ratio exacto del perfil de escena
Companions:  prompt + info + visual_identity + manifest
Producción:  WebP generado por NAP
```

---

## Cartografía Nimroel — fórmula declarada

```text
ZIP:         <asset_id>.zip (cinco archivos planos)
ID:          scene_<location>_<descriptor>_<NNN>
Tipo:        scene
Perfil:      scene_cartography
Clasificación: culture + location; realm/region opcionales
Maestro:     PNG estático 16:10, archivado íntegro
Companions:  prompt + info + visual_identity + manifest
Producción:  WebP 1600×1000 Q90
Ruta:        scenes/cartography/<culture>/<location>/<asset_id>/
```

# 13. Conclusión operativa

A partir de ahora, para Nimroel, el estándar de preparación NAP queda resumido así:

## Retratos

```text
<asset_id>.zip
├── <asset_id>.png
├── <asset_id>_prompt.md
├── <asset_id>_info.md
├── <asset_id>_visual_identity.json
└── <asset_id>_manifest.json
```

con:

```text
asset_type         = portrait
production_profile = portrait_npc
PNG maestro        = 4:5
```

## Mapas y planos cartográficos

```text
<asset_id>.zip
├── <asset_id>.png
├── <asset_id>_prompt.md
├── <asset_id>_info.md
├── <asset_id>_visual_identity.json
└── <asset_id>_manifest.json
```

con:

```text
asset_type         = scene
production_profile = scene_cartography
PNG maestro        = ratio exacto 16:10
WebP producción    = 1600×1000 Q90
```

---

## Biblia de protocolo NAP adoptada

Este documento define el protocolo v1.1 de preparación de ZIPs de retratos y cartografía. El perfil cartográfico ya está integrado; la nueva preparación por normalización se entrega para revisión independiente. La sección narrativa sigue siendo una propuesta futura no habilitada. Ningún ejemplo autoriza cambios en cartografía canónica ni publicación automática de los originales.
