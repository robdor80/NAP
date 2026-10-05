# Nimroel Historical Structure Audit + Canonical Routing Policy — Fase 3 · Capítulo 3.5

**Corte de auditoría documentado: 2026-10-03.** Este capítulo registra una
fotografía histórica del repositorio externo `robdor80/Videojuego_Nimroel` en
ese corte. No garantiza que su contenido presente o futuro siga siendo idéntico.

## Alcance y ProductionRoot

La raíz lógica de producción visual observada es:

```text
Worldbuilding/Direccion artistica/Assets
```

NAP debe configurar `ProductionRoot` para que apunte localmente a esa carpeta.
La ruta absoluta depende de cada instalación y no se incorpora al perfil. Todo
routing se expresa relativo a `ProductionRoot`; `Worldbuilding`,
`Direccion artistica` y `Assets` no son segmentos de routing.

3.5 es una auditoría y una decisión declarativa. No mueve archivos, renombra
carpetas, escribe en el repositorio externo ni genera manifests históricos.

## Hechos observados

En la raíz de `Assets` se observaron estos archivos de infraestructura y la
rama de retratos:

```text
PORTRAIT_REGISTRY.json
PORTRAIT_REGISTRY.md
PORTRAIT_WORKFLOW_RULES.md
VISUAL_ASSET_REGISTRY.md
nimroel_asset_naming_convention.md
portraits/
└── Norgard/
    └── Treskal/
        ├── blacksmith/
        ├── carpenter/
        ├── children/
        ├── elder/
        ├── farmer/
        ├── fisher/
        ├── healer/
        └── tavernkeeper/
```

`PORTRAIT_REGISTRY.json` fue la fuente estructurada histórica para contrastar
identidad y metadata. La convención histórica de nombres queda documentada por
`nimroel_asset_naming_convention.md`; 3.5 no la reinterpreta como una regla
universal de routing.

El corte contiene 39 registros y 39 carpetas físicas de asset, con
correspondencia 39/39. No se observaron carpetas registradas ausentes, archivos
históricos obligatorios ausentes, archivos extra dentro de esas carpetas ni
diferencias entre `asset_id` y el nombre de su carpeta.

| Grupo histórico | Assets |
| --- | ---: |
| farmer | 10 |
| carpenter | 5 |
| blacksmith | 5 |
| healer | 3 |
| children | 6 |
| elder | 6 |
| fisher | 2 |
| tavernkeeper | 2 |
| **Total** | **39** |

Cada carpeta contiene exactamente cuatro archivos históricos:

```text
<asset_id>.webp
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_visual_identity.json
```

Estos son hechos del corte auditado, no requisitos nuevos de Package Contract ni
una promesa sobre futuras revisiones del repositorio externo.

## Excepciones semánticas del layout histórico

Veintisiete retratos siguen esencialmente `<role>/<sex>/<asset_id>`. Las ramas
`children` y `elder` mezclan, en cambio, agrupación histórica y semántica.

Los seis assets de `children/boy|girl` no comparten un único role. El registry
declara `profession = farmer` para dos boys y `profession = village_child` para
un boy y tres girls; `sex` es `male|female` y `age_range = child`. Por ello
`boy|girl` representa sex histórico con vocabulario de edad, y `children` no
puede usarse universalmente como role.

Los seis assets de `elder/male|female` declaran
`profession = village_elder`, `age_range = elderly` y `sex = male|female`.
`elder` es una agrupación histórica de etapa vital, no el machine identifier
semántico del role.

El árbol observado usa el casing `Norgard/Treskal`. Las classifications de NAP
usan los machine identifiers `norgard/treskal`. No se configura title casing,
display name ni mapping de casing para routing.

## Política canónica de Nimroel

Para la única regla Nimroel `portrait + portrait_npc`, el destino canónico es:

```text
portraits/{culture}/{location}/{role}/{sex}/{asset_id}
```

Routing Contract v1 lo representa exactamente como:

1. `Literal("portraits")`
2. `Classification("culture")`
3. `Classification("location")`
4. `Classification("role")`
5. `Classification("sex")`
6. `AssetId()`

`realm` y `region` continúan permitidas como metadata opcional, pero no forman
parte del routing. La ruta depende de semántica estable del manifest validado:
`culture`, `location`, `role`, `sex` y `asset_id`. No depende de life stage,
`boy|girl`, agrupaciones legacy, casing de presentación ni edad aparente.

NAP no deduce ninguna classification de los tokens de `AssetId`. El
[Destination Resolver de 3.6](DESTINATION_RESOLVER.md) recibe `ValidatedAssetPackage` y usa
`Manifest.Classification`. Routing Contract v1 ya expresa toda la política; no
se necesita Routing v2, transformaciones, condicionales o scripts.

## Ejemplos conceptuales, sin migración

```text
portraits/Norgard/Treskal/farmer/male/portrait_treskal_farmer_male_002
→ portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_002

portraits/Norgard/Treskal/children/boy/portrait_treskal_farmer_boy_002
→ portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_boy_002

portraits/Norgard/Treskal/children/boy/portrait_treskal_boy_001
→ portraits/norgard/treskal/village_child/male/portrait_treskal_boy_001

portraits/Norgard/Treskal/elder/male/portrait_treskal_elder_male_001
→ portraits/norgard/treskal/village_elder/male/portrait_treskal_elder_male_001
```

Los `AssetId` permanecen estables incluso cuando contienen tokens legacy que no
coinciden con la nueva carpeta semántica. Los ejemplos describen una migración
conceptual; 3.5 no la ejecuta ni crea scripts u operaciones planificadas.

## Configuración y siguiente frontera

En 3.5 el perfil real Nimroel migró a Universe Profile v3 y declaró esta ruta.
Desde 5.4 usa [Profile v4](UNIVERSE_PROFILE_V4.md), que añade conversión Portrait
sin alterar esta política ni el corte histórico. Conserva
identidad, dimensiones, allowed/required classification y los cuatro
`package_files` de v2. El fixture histórico v2 permite seguir comprobando el
contrato anterior sin hacer depender esos tests de la configuración vigente.

**Fase 3 HECHA (3.1–3.6). 3.5 — Nimroel Historical Structure Audit + Canonical Routing Policy y 3.6 — Destination Resolver HECHOS.
Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. Fase 5 HECHA;
5.1–5.5 HECHOS; 5.5 — No recorte silencioso HECHO.** 3.6 calcula
directorios con la regla retenida en el package validado y la raíz del repository
validado. Sustituye segmentos y usa Path.Combine/Path.GetFullPath sin I/O,
inferencia ni adaptación al layout histórico. No crea carpetas ni implementa
ProcessingPlan, Dry Run, migración o escritura de producción. El corte histórico
de esta auditoría permanece intacto.
