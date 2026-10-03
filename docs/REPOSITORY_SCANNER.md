# Repository Scanner — Fase 3 · Capítulo 3.2

**Fase 3 EN CURSO. 3.1–3.4 HECHOS. Siguiente: 3.5 — Historical Structure Audit / clasificación.
3.6 — Destination Resolver pendiente.**

```text
ValidatedProductionRepository
        ↓
ProductionRepositoryScanner
        ↓
ProductionRepositorySnapshot
```

3.1 autoriza/valida exclusivamente el root del contexto. 3.2 fotografía la
estructura filesystem real de esa raíz. 3.3 define routing declarativo y 3.4
compara snapshots sin semántica; 3.5 será el primer capítulo que interpreta
la estructura. El scanner todavía no sabe qué es un asset ni
qué carpetas son correctas, sobrantes, antiguas o específicas de un perfil.

## Entrada y revalidación

La única API pública de ejecución es:

```csharp
ProductionRepositoryScanResult Scan(ValidatedProductionRepository repository)
```

Repository nulo lanza `ArgumentNullException`. No hay overload para string,
UniverseContext ni rutas arbitrarias. UniverseId y RootPath del snapshot se
conservan exactamente desde el objeto validado de 3.1.

Como 3.1 no bloquea el filesystem, antes de enumerar se leen de nuevo los
atributos actuales de RootPath. Solo FileNotFoundException y
DirectoryNotFoundException se traducen a `production_root_missing`. Si el root
ahora es archivo se reutiliza `production_root_invalid`; si es directorio
ReparsePoint, `production_root_reparse`. Todos son Error + Stop, SubjectPath
igual a RootPath y Snapshot null. Otros errores operativos se propagan.
No se inspeccionan ancestros ni se resuelven targets físicos.

## Fotografía raw e inmutable

`ProductionRepositoryEntryKind` contiene exactamente `Directory` y `File`.
Cada `ProductionRepositoryEntry` es sellado e inmutable, con constructor
internal sin I/O, y expone RelativePath, FullPath y Kind.

RelativePath se forma concatenando los nombres realmente enumerados con `/`
entre niveles. No empieza ni termina en slash. Conserva exactamente casing,
espacios, acentos y Unicode. No aplica lowercase, normalización Unicode,
Naming v1 ni vocabularios de perfil. Una barra inversa que forme parte de un
nombre válido en Unix se conserva como carácter del nombre, no se sustituye.

FullPath es la ruta absoluta nativa de la entry enumerada bajo RootPath. La raíz
misma no es una entry. Se incluyen archivos y todos los directorios, también
vacíos. No se aceptan rutas de manifest, classification u otro input externo.

`ProductionRepositorySnapshot` es sellado e inmutable, con constructor internal
sin I/O. Expone UniverseId, RootPath e IReadOnlyList de Entries. Toma una copia
defensiva, la ordena por RelativePath con StringComparer.Ordinal y la expone
mediante una colección read-only. Después de su construcción no consulta el
filesystem; conserva su observación aunque cambie el repositorio.

`ProductionRepositoryScanResult` expone Issues, Snapshot e IsValid mediante
propiedades de solo lectura. Su constructor rechaza report nulo y exige
**report limpio si y solo si Snapshot no null**. Cualquier issue, incluso
Continue, impide adjuntar un snapshot; cualquier STOP deja Snapshot null.

## Recorrido y reparse

El recorrido usa un Stack explícito de directorios en memoria, sin recursión de
llamadas. Enumera un nivel cada vez y consulta atributos de cada descendiente.
Antes de visitar un directorio pendiente vuelve a comprobar si es ReparsePoint.
El orden de enumeración del filesystem no es contrato: el snapshot final se
ordena por RelativePath Ordinal.

Cualquier descendiente marcado ReparsePoint produce
`repository_entry_reparse`, Error + Stop, SubjectPath absoluto de esa entry y
Detail con RelativePath. No se incluye como entry válida, no se abre, no se
resuelve su target y nunca se añade para traversal. Se pueden recorrer las
demás ramas normales para localizar otros links; los issues se ordenan por
RelativePath Ordinal. Si hay algún reparse, no se devuelve snapshot parcial.

Los links circulares y los que apuntan fuera de ProductionRoot causan el mismo
STOP. No hay realpath ni resolución física. La contención procede del recorrido
de descendants enumerados desde la raíz validada y de no atravesar reparse
detectados, sin aceptar paths externos.

## Read-only, contenidos e infraestructura

El scanner no crea, borra, mueve, copia, renombra, reescribe ni cambia permisos,
atributos o timestamps deliberadamente. Solo enumera estructura y consulta
metadata necesaria; no abre ni lee contenido de archivos.

JSON roto, PNG corrupto, manifests inválidos y archivos bloqueados para lectura
de contenido pueden figurar en el snapshot si el sistema permite enumerarlos
y consultar sus atributos. No se parsea JSON, cargan manifests, validan PNG,
calculan hashes, consultan servicios de red ni interpretan assets completos o
incompletos.

No se ejecuta Git, no se exige ni interpreta `.git` y no hay lista de ignores.
`.git`, `.github`, `.gitignore` y README se fotografían como cualquier otra
entry normal. También una entry de infraestructura ReparsePoint causa STOP.

## Límites y siguiente trabajo

El snapshot representa una observación puntual, no una transacción consistente
del filesystem. No se garantiza estabilidad frente a cambios concurrentes,
incluidos cambios entre consultar atributos y enumerar. Desapariciones de
entries/directorios, cambios de acceso y otros errores de I/O durante traversal
se propagan; solo la comprobación inicial del root controla ausencia mediante
los códigos de 3.1. No hay retries, watchers ni locks persistentes.

El snapshot y la lista de trabajo se materializan en memoria. Por ahora no hay
cuota explícita de entries ni profundidad. El recorrido iterativo evita depender
de la profundidad del stack de llamadas, pero no impone límites de recursos.

**3.3 define Routing Contract v1 y 3.4 — [Structural Change Detection](STRUCTURAL_CHANGE_DETECTION.md)
compara snapshots con cero I/O. Siguiente: 3.5 — Historical Structure Audit / clasificación.**
No hay resolución de routing,
destination resolver, comparación con estructura esperada, detección semántica
de new folders, migración, ProcessingPlan, Dry Run, SQLite, TeraBox, conversión,
automatización Git ni UI. No se ha decidido el árbol canónico real de Nimroel:
esa decisión requiere observar y auditar después su estructura histórica.
3.3 dispone del contrato Profile v3, pero Nimroel sigue en v2 sin routing rules.
