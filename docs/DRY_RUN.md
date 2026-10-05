# Dry Run — Fase 4 · Capítulo 4.2

**Fase 3 HECHA. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — ProcessingPlan,
4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS.
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; siguiente: 6.2 — Duplicados.**

## Propósito y API

```text
ProcessingPlan
      ↓
DryRunTextRenderer
      ↓
texto humano de Dry Run
```

`DryRunTextRenderer` es public sealed y sin estado, con una única API pública
declarada, sin overloads ni constructor especial:

```csharp
public string Render(ProcessingPlan plan)
```

Null produce ArgumentNullException con ParamName `plan`. Solo consume el plan
inmutable de 4.1 y devuelve string; no requiere package, repository, snapshot,
contexto ni perfil. Muestra hechos disponibles, sin revalidarlos ni recalcular
destinos. El texto es humano, no un contrato de intercambio machine-readable:
no hay JSON/XML/YAML, DTO adicional de report, parser o schema.

## Formato textual v1

La estructura exacta es:

```text
NAP DRY RUN
ProcessingPlan v1 - read-only preview

ASSET
  universe_id: <universe>
  asset_id: <asset_id>
  asset_type: <asset_type>
  production_profile: <production_profile>

CLASSIFICATION
  <dimension>: <value>
  ...

PACKAGE
  package_root: "<escaped path>"
  manifest_path: "<escaped path>"

INPUT FILES
  <role>: "<escaped path>"
  ...

PRODUCTION DESTINATION
  production_root: "<escaped path>"
  relative_directory: <relative directory>
  full_directory_path: "<escaped path>"

OPERATIONS
  (not defined in ProcessingPlan v1)

SAFETY
  This renderer performs no filesystem I/O.
  No operation is executed or authorized by this dry run.
```

Los placeholders y `...` representan las entradas variables de este ejemplo;
no son líneas adicionales del renderer. Classification se ordena por dimension
y FilesByRole por role, ambos con StringComparer.Ordinal. Cada colección vacía
conserva su sección con una sola línea `  (none)`. No se filtra classification
por routing: realm y region también aparecen si están en el plan.

ASSET muestra UniverseId.Value y AssetId separados, sin UniverseAssetKey.ToString.
PACKAGE conserva ManifestPath separado de INPUT FILES. Solo se renderizan los
mappings presentes: sin reconstrucción de filenames, manifest artificial,
archivos opcionales ausentes ni outputs WebP.

PRODUCTION DESTINATION muestra exactamente RootPath, RelativeDirectory y
FullDirectoryPath del destino congelado. No normaliza paths, convierte
separadores, cambia casing ni interpreta Routing o tokens de AssetId.
RelativeDirectory se muestra directamente, sin comillas ni escaping, pues
procede de la frontera segura de 3.6.

El output usa siempre LF (`\n`), independientemente de plataforma, sin BOM,
newline final ni espacios al final de líneas. Labels y safety son literals fijos
ASCII. El orden y los escapes no dependen de CurrentCulture/CurrentUICulture;
entradas lógicamente equivalentes producen exactamente el mismo string.

## Paths seguros en una sola línea

PackageRoot, ManifestPath, cada source path de FilesByRole, RootPath y
FullDirectoryPath se muestran entre comillas dobles mediante un helper privado:

| Carácter | Representación |
| --- | --- |
| Backslash | `\\` |
| Comilla doble | `\"` |
| CR | `\r` |
| LF | `\n` |
| TAB | `\t` |
| Otros char.IsControl | `\uXXXX`, cuatro dígitos hexadecimales uppercase |

Por ejemplo, `C:\NAP\package` se muestra como `"C:\\NAP\\package"`.
Unicode normal se conserva, incluyendo acentos, caracteres no latinos y emoji.
Un source path con newline y texto `OPERATIONS` no puede crear una sección
adicional. Este quoting se aplica exclusivamente a paths; machine identifiers
y RelativeDirectory se muestran directamente.

## Ejemplo Nimroel

El pipeline existente conecta package/repository validados, resolver de 3.6,
destino calculado, builder de 4.1 y renderer de 4.2. Para
`portrait_treskal_farmer_male_002`, el report incluye:

```text
ASSET
  universe_id: nimroel
  asset_id: portrait_treskal_farmer_male_002
  asset_type: portrait
  production_profile: portrait_npc

CLASSIFICATION
  culture: norgard
  location: treskal
  role: farmer
  sex: male
```

La línea de destino relativa es exactamente:

```text
  relative_directory: portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_002
```

Si realm y region están presentes, aparecen entre location y role en orden
Ordinal. La clasificación y el destino conservan machine casing: no hay
inferencia de layout legacy `children/boy` o `elder/male`.

## Límites y siguiente capítulo

El renderer realiza **cero filesystem I/O** y tampoco escribe en consola.
No simula filesystem ni valida ejecutabilidad, identidad, roots, existencia,
colisiones o contenido del repositorio. No recibe snapshots, llama al builder
o resolver, crea carpetas o escribe archivos. Paths inexistentes son suficientes.
No se añade CLI, flags, UI, logs ni integración externa.

Desde [5.4 — Profile v4](UNIVERSE_PROFILE_V4.md), la conversión puede declararse
y resolverse desde el package validado. ProcessingPlan v1 todavía no congela
esa regla y Dry Run v1 todavía no muestra una operación de conversión.
5.4 introduce contrato/configuración; una futura evolución del plan podrá
consumirlo cuando se formalicen operaciones, sin fijar un capítulo para ello.

OPERATIONS declara exactamente que las operaciones no están definidas en
ProcessingPlan v1. No promete copy/convert/create/write/archive ni inventa
maestro, archive/TeraBox, output WebP, hashes, JobId, timestamps o estados.
SAFETY garantiza el comportamiento del renderer, no el estado global del
filesystem ante procesos concurrentes. Renderizar no ejecuta ni autoriza nada.

4.3 — [Plan Validation](PLAN_VALIDATION.md) valida estructuralmente el destino
contra un snapshot materializado, sin I/O ni revalidación de sources. Es una
frontera separada del renderer; un report limpio es point-in-time y no autoriza
escrituras. 4.4 — Logs HECHO: resumen textual privacy-safe. Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad EN CURSO; 6.1 — SHA-256 HECHO; siguiente: 6.2 — Duplicados. El objetivo futuro completo de PLAN crecerá cuando
existan los contratos correspondientes, sin presentarlos como implementados hoy.

Los tests verifican el documento completo, LF, orden Ordinal, colecciones vacías,
escapes en los cinco tipos de paths, controles/Unicode, inyección de secciones,
independencia cultural, determinismo y el pipeline real Nimroel. No crean
archivos físicos para probar el renderer.
