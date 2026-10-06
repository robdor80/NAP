# Job ID — Fase 6 · Capítulo 6.3

**Fase 5 — Conversión HECHA. Fase 6 — Integridad HECHA.
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO.
6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.**

El requisito histórico de NAP es: «Cada proceso debe tener un Job ID».
6.3 formaliza exclusivamente esa identidad fuerte, estable y persistible como
string. No significa que NAP ya tenga un lifecycle persistente de Jobs;
existe el identificador que dicho lifecycle podrá usar.

## Contrato público

```csharp
public sealed record JobId
{
    public JobId(string value);
    public string Value { get; }
    public override string ToString();
    public static JobId Create();
}
```

Value es get-only y ToString devuelve exactamente Value. No hay setters,
propiedades adicionales, Parse, TryParse, FromGuid o propiedad Guid.
El constructor es la frontera para rehidratar un valor persistido en el futuro.
Retiene el string exacto, sin trim, cambios de casing ni normalización.

## Formato y validación

Formato exacto: `job_<32 lowercase ASCII hexadecimal characters>`.
Longitud total 36: prefijo literal `job_` y 32 caracteres de `0-9` o `a-f`.

Ejemplo válido:

```text
job_00112233445566778899aabbccddeeff
```

También son válidos `job_ffffffffffffffffffffffffffffffff` y
`job_00000000000000000000000000000001`. El valor
`job_00000000000000000000000000000000` es inválido: evita un sentinel/default
ambiguo como identidad real. Los demás IDs con ceros no están prohibidos.

Orden del constructor: null, longitud, prefijo exacto, charset ASCII lowercase,
all-zero. Null lanza ArgumentNullException con ParamName `value`.
Cualquier string no nulo inválido lanza ArgumentException, ParamName `value`,
con message base exacto:

```text
A Job ID must use the canonical form 'job_' followed by 32 lowercase ASCII hexadecimal characters and must not be all zero.
```

.NET añade el sufijo habitual de parámetro a ArgumentException.Message.
Se rechazan uppercase, prefijos alternativos, guiones, braces, GUID con guiones,
0x, sufijos, espacios, tabs, saltos de línea y caracteres/dígitos Unicode.
El contrato pertenece a JobId; AssetNamingRules no es su autoridad.

## Generación e igualdad

Create usa `Guid.NewGuid().ToString("N")`, prefijado por `job_`, y construye un
JobId mediante el mismo constructor. El formato N aporta 32 hex lowercase sin
guiones. No existe una segunda lógica de validación o dependencia externa.
No usa DateTime/DateTimeOffset, Random, contadores, PID, ThreadId, hostname,
username, UniverseId o AssetId para generar o codificar contexto.

JobId es un value object sealed record: mismo Value válido implica Equals y
`==` true, `!=` false y GetHashCode coherente. Valores diferentes son distintos.
La igualdad efectiva es exacta, independiente de cultura. Validación, ToString
y Create también son independientes de cultura y del filesystem.

El ID es opaco y estable: ni su prefijo ni el GUID codifican metadata de proceso.
GetHashCode es un contrato de igualdad en memoria, no el identificador persistible;
la representación que podrá persistirse es Value.

## Identidad global y multiuniverso

JobId identifica globalmente un proceso lógico dentro de NAP; no está scoped por
UniverseId. Dos universos no deben reutilizar intencionadamente el mismo JobId.
Un Job futuro podrá pertenecer a un universo, pero esa pertenencia no forma parte
de JobId. UniverseAssetKey conserva su significado: UniverseId + AssetId.

JobId no sustituye UniverseAssetKey, AssetId, Sha256Digest, path o timestamp.
No elimina UniverseContext explícito ni crea un «current universe».
No contiene UniverseId, AssetId, AssetKey, State, Status, Timestamp, CreatedAt,
paths o metadata. Es independiente del asset, universo y estado.

## Alcance y continuidad

Poseer un JobId no demuestra que el Job exista en SQLite, haya comenzado o
terminado, esté COMPLETED, haya ejecutado escrituras, pueda recuperarse o
pertenezca a un asset concreto.

6.3 no crea Job/ProcessingJob/JobRecord u otros modelos, estado/enum/transiciones,
lifecycle, timestamps, repositorio/store/registry, journal, checkpoint o resume.
Cero filesystem I/O, persistencia, SQLite, schemas nuevos o cambios de schemas.
No guarda IDs en JSON, manifests, profiles, sidecars, logs, archivos, TeraBox,
producción o archive. No añade NapIssueCodes o dependencias.

ProcessingPlan, ProcessingPlanBuilder y ProcessingPlanValidator permanecen
intactos y sin JobId. El plan es un artefacto que un Job futuro podrá contener
o referenciar; no se anticipa esa relación. DryRunTextRenderer y
PlanLogTextRenderer conservan sus contratos, sin líneas JOB_ID artificiales.
El lifecycle futuro decidirá la proyección en historial/logs.

```text
JobId [6.3 HECHO]
  → JobState + machine/record/store [6.4 HECHO]
  → recuperación tras fallo [6.5 HECHO]
```

[6.4 — Estados persistentes](JOB_STATES.md) ya añade el vocabulario, reglas puras,
JobStateRecord y journal mínimo bajo StateRoot. JobId permanece intacto y sin
I/O o estado propio; no hay ProcessingJob/orquestador. [6.5 recovery](JOB_RECOVERY.md)
descubre checkpoints durables en solo lectura, sin avanzar estados o ejecutar
assets. La Fase 6 está HECHA. Fase 7 — Auditor IA HECHA (7.1–7.5); siguiente: Fase 8 — TeraBox / ArchiveRoot.
Persistir FAILED terminal no ejecuta recuperación. No hay ejecución de assets,
CLI o UI. [Fase 7 — Auditor IA](AI_AUDIT.md) audita hechos del plan sin incluir
JobId ni integrar transiciones de estado.

## Pruebas

JobIdTests comprueba tipo/API exactos mediante reflection sin debilitar
encapsulación, valores válidos retenidos, null y message/ParamName de inválidos,
longitud/prefijo/charset y cada posición del sufijo. Verifica all-zero y un único
nibble no cero en cada posición, igualdad/operadores/hash, Create y reconstrucción,
culturas tr-TR/ar-SA e independencia de contexto/estado/paths.
Se comprueban múltiples resultados válidos de Create sin convertir una muestra
aleatoria sin colisiones en garantía contractual de unicidad.
