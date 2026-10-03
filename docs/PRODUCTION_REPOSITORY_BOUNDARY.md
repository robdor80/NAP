# Production Repository Boundary — Fase 3 · Capítulo 3.1

**3.1 y 3.2 HECHOS. Fase 3 — Routing y repo EN CURSO. Siguiente: 3.3 — Routing.**

La frontera valida exclusivamente la raíz de producción autorizada del universo
activo y produce un objeto que los componentes posteriores podrán consumir:

```text
UniverseContext
    ↓
ProductionRepositoryValidator
    ↓
ValidatedProductionRepository
```

## Origen de la raíz e identidad

`ProductionRepositoryValidator.Validate(UniverseContext context)` no acepta una
ruta externa adicional. La única raíz procede de
`context.Storage.ProductionRoot`; nunca de manifest, metadata, classification
ni contenido del repositorio. `context == null` lanza `ArgumentNullException`.

`UniverseStorageConfig` conserva su responsabilidad previa: exigir rutas fully
qualified y normalizarlas léxicamente mediante `Path.GetFullPath`, sin I/O.
`UniverseContext` sigue garantizando igualdad entre Profile.Id y
Storage.UniverseId. El nuevo validador no duplica ni mueve esas validaciones.

`ValidatedProductionRepository` es una clase sellada e inmutable, con constructor
internal que recibe el contexto y no hace I/O. No tiene constructor público ni
permite que consumidores externos fabriquen uno desde un string arbitrario.
Expone únicamente `UniverseId` y `RootPath`, copiados exactamente de `context.Id`
y `context.Storage.ProductionRoot`: raíz absoluta normalizada, sin convertirla
en ruta relativa, resolver targets o añadir segmentos.

Cada resultado conserva su universo y raíz propios. No existe current universe,
global production root, singleton ni estado estático mutable.

## Validación y códigos estables

El validador obtiene la raíz del contexto y llama a `File.GetAttributes` sobre
esa raíz exclusivamente. No utiliza `Directory.Exists`, que podría ocultar
errores operativos. Aplica este orden:

| Condición | Código | Resultado |
| --- | --- | --- |
| `FileNotFoundException` o `DirectoryNotFoundException` al leer atributos | `production_root_missing` | Error + Stop, Repository null |
| Existe, pero sin atributo Directory | `production_root_invalid` | Error + Stop, Repository null |
| Es directorio y el root mismo tiene atributo ReparsePoint | `production_root_reparse` | Error + Stop, Repository null |
| Directorio normal | Sin issue | Report limpio, Repository no null |

Los tres códigos están centralizados en `NapIssueCodes`; `SubjectPath` conserva
el ProductionRoot absoluto exacto. Los mensajes humanos no son contrato estable.
Un archivo se rechaza antes de comprobar ReparsePoint, según el orden anterior.

Solo las dos excepciones de ausencia se convierten en missing. Acceso denegado,
otros `IOException` y errores operativos no equivalentes a ausencia se propagan.
No hay `catch (Exception)` ni conversión de errores inesperados a NapIssue.

## Resultado e invariantes

`ProductionRepositoryValidationResult` es sellado y expone propiedades de solo
lectura: `NapIssueReport Issues`, `ValidatedProductionRepository? Repository` y
`bool IsValid`, derivado de Repository no null.

Su constructor rechaza report nulo y estados incoherentes: **informe limpio si y
solo si Repository no null**. Cualquier issue, incluso Continue, impide adjuntar
una raíz validada; cualquier STOP deja Repository null. El constructor no repite
la validación de filesystem.

## Read-only y alcance de links

3.1 solo lee los atributos de la raíz: no crea carpetas, escribe archivos, copia,
mueve, elimina, renombra ni cambia permisos. No enumera entries, no recorre
subdirectorios, no abre archivos internos ni interpreta nombres o metadata.
Puede haber archivos arbitrarios, subdirectorios, nombres especiales y links
internos; quedan fuera de este capítulo.

Se rechaza únicamente el directorio raíz directamente marcado ReparsePoint.
No se inspeccionan ancestros, no se siguen links internos ni se resuelven targets
físicos. Una raíz normal bajo un ancestro junction/symlink no se rechaza ni se
reescribe. Los futuros componentes que recorran entries deberán aplicar sus
propias reglas de contención y links.

No se busca ni exige `.git`: ProductionRoot es un directorio autorizado,
independientemente de su asociación futura con Git. Tampoco se comprueba la
existencia de WorkspaceRoot o ArchiveRoot.

La validación describe un instante; no bloquea el filesystem ni garantiza que
la raíz permanezca igual después. Los consumidores deberán controlar cambios
posteriores y sus propios errores de acceso.

## Tests y continuación

Los tests usan raíces temporales sintéticas y snapshots antes/después de
atributos, entries y contenido, sin seguir links. Cubren identidad/ruta exactas,
normalización previa, universos independientes, ausencia sin creación, archivo,
reparse root, contenido arbitrario y bloqueo exclusivo de archivos, links
internos circulares, ancestros reparse e invariantes de construcción.
En Windows se comprueba además validación sin permiso para listar entries y
propagación de un IOException de nombre inválido. Las junctions de Windows no
requieren privilegios de symlink; en otras plataformas se usan directory symlinks.
Los casos específicos de Windows solo ejecutan su comprobación en Windows.

**3.2 — [Repository Scanner](REPOSITORY_SCANNER.md) HECHO:** consume únicamente
`ValidatedProductionRepository`, revalida el root y fotografía estructura raw,
sin leer contenidos ni atravesar reparse internos. 3.1 conserva sus responsabilidades.
Todavía no existen routing, reglas de carpetas, destination resolver, creación de
carpetas, movimiento/copia de assets, auditoría de assets existentes,
ProcessingPlan, Dry Run, Git, SQLite, TeraBox, conversión ni UI en este capítulo.

La estructura canónica real de Nimroel sigue sin decidir: requiere conocer y
auditar su repositorio existente mediante 3.2. No se impone un árbol nuevo, no se
añade routing al perfil ni se crea Universe Profile v3. Los contratos, schemas,
perfil Nimroel y fixtures históricos permanecen intactos.
