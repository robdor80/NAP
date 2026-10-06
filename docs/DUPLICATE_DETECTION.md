# Duplicate Detection — Fase 6 · Capítulo 6.2

**Fase 5 — Conversión HECHA. Fase 6 — Integridad HECHA.
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO.
6.5 — Recuperación tras fallo HECHO. Siguiente: Fase 7 — Auditor IA.**

6.2 compara dos fingerprints ya conocidos, `candidate` frente a `existing`,
para distinguir identidad administrativa de identidad binaria. Es una capa pura,
determinista y pairwise. No descubre archivos ni decide cómo resolver el resultado.
Depende de los value objects de identidad multiuniverso y de
[6.1 — SHA-256](SHA256_INTEGRITY.md); los contratos existentes permanecen intactos.

## Flujo y fingerprint

```text
bytes → Sha256Hasher [6.1] → Sha256Digest
      → AssetContentFingerprint → AssetDuplicateAnalyzer [6.2]
      → AssetDuplicateAnalysis
```

```csharp
public sealed record AssetContentFingerprint
{
    public AssetContentFingerprint(UniverseAssetKey assetKey, Sha256Digest digest);
    public UniverseAssetKey AssetKey { get; }
    public Sha256Digest Digest { get; }
}
```

`UniverseAssetKey` contiene `UniverseId + AssetId`, la identidad administrativa
completa. `Sha256Digest` identifica los bytes exactos mediante SHA-256 canónico.
El fingerprint significa «este asset está asociado a este contenido».
No contiene path, filename, timestamp, tamaño, manifest, classification,
production profile, asset type, destino, ubicación ni estado de Job.

El constructor valida primero `assetKey` y luego `digest`: null produce
ArgumentNullException con esos ParamName exactos. Retiene las referencias
originales porque ambos inputs son inmutables. Es sealed record, con igualdad
por valor, operadores `==`/`!=` y GetHashCode coherente; las dos propiedades
son get-only, sin setters o propiedades adicionales.

## Analyzer y frontera de universo

```csharp
public sealed class AssetDuplicateAnalyzer
{
    public AssetDuplicateAnalysis Analyze(
        AssetContentFingerprint candidate,
        AssetContentFingerprint existing);
}
```

Clase stateless con constructor público sin parámetros y una sola API pública.
Orden: candidate null, existing null, igualdad de UniverseId, igualdad de
AssetKey, igualdad de Digest, construcción del resultado. Null produce
ArgumentNullException con ParamName `candidate` o `existing`.

**La comparación solo es válida dentro del mismo universo.** Si difieren los
UniverseId por valor, lanza ArgumentException con ParamName `existing` y texto:

```text
The candidate and existing fingerprints must belong to the same universe.
```

Es el texto suministrado a ArgumentException; .NET añade su sufijo de parámetro.
La frontera se aplica a las cuatro combinaciones de AssetId/digest, también
si coinciden ambos. No se clasifica ninguna relación entre universos distintos.
No existe un universo actual global ni relaciones implícitas entre universos.

## Matriz exacta

`AssetContentRelation` es un enum público con exactamente estos cuatro valores,
en orden: Distinct (0), SameAssetSameContent (1), SameAssetDifferentContent (2),
DifferentAssetSameContent (3).

Dentro del mismo universo se usa igualdad por valor de AssetKey y Digest:

| AssetKey | Digest | Relation | Report | IsIdempotent | IsCollision | IsPossibleDuplicate |
| --- | --- | --- | --- | --- | --- | --- |
| Distinto | Distinto | Distinct | Vacío, clean, Continue | false | false | false |
| Igual | Igual | SameAssetSameContent | Vacío, clean, Continue | true | false | false |
| Igual | Distinto | SameAssetDifferentContent | Un Error + Stop | false | true | false |
| Distinto | Igual | DifferentAssetSameContent | Un Warning + Continue | false | false | true |

Los reports vacíos tienen IsClean true, HasErrors false, ShouldStop false y
CanContinue true. Filenames, paths, casing del filesystem, roles, classification,
AssetType y ProductionProfile no intervienen.

## Diagnósticos direccionales

Ambos issues tienen **SubjectPath null**: no existe un archivo sujeto de este
análisis. Detail usa exclusivamente UniverseId.Value, AssetId y Sha256Digest.Hex,
sin UniverseAssetKey.ToString() como contrato serializado. Es diagnóstico estable,
no formato de persistencia. No hay ordenación o formato dependiente de cultura.

### Colisión de identidad

SameAssetDifferentContent produce exactamente un issue:

- Code: `asset_content_collision` (`NapIssueCodes.AssetContentCollision`).
- Severity: Error. Disposition: Stop.
- Message: `The asset key is already associated with different content.`
- Detail exacto:

```text
universe=<universe>; asset_id=<assetId>; existing_sha256=<existing>; candidate_sha256=<candidate>
```

IsClean false, HasErrors true, ShouldStop true y CanContinue false.
`existing_sha256` identifica el contenido conocido; `candidate_sha256` el candidato.
Invertir los argumentos invierte ambos hashes en Detail. No se elige el antiguo,
el nuevo, el más reciente, el más grande ni una ubicación preferente: solo STOP.
No existe resolución automática.

### Posible duplicado

DifferentAssetSameContent produce exactamente un issue:

- Code: `asset_content_possible_duplicate` (`NapIssueCodes.AssetContentPossibleDuplicate`).
- Severity: Warning. Disposition: Continue.
- Message: `The same content is already associated with a different asset key.`
- Detail exacto:

```text
universe=<universe>; candidate_asset_id=<candidate>; existing_asset_id=<existing>; sha256=<digest>
```

IsClean false, HasErrors false, ShouldStop false y CanContinue true.
Invertir argumentos invierte candidate_asset_id/existing_asset_id. Puede ser
un error humano, un duplicado accidental o reutilización intencionada. La capa
solo advierte para revisión futura; no borra, fusiona, reutiliza, renombra,
rechaza ni sobrescribe automáticamente.

## Resultado e invariantes

`AssetDuplicateAnalysis` es public sealed class, con constructor internal.
Propiedades públicas get-only exactas: Relation (AssetContentRelation), Candidate
y Existing (AssetContentFingerprint), Issues (NapIssueReport), IsIdempotent,
IsCollision e IsPossibleDuplicate (bool derivados únicamente de Relation).
Retiene los inputs originales y el report inmutable existente.

El constructor exige candidate, existing e issues no null, relación definida,
mismo universo y coherencia exacta de Relation con las igualdades de key/digest.
Distinct/SameAssetSameContent requieren report vacío. La colisión requiere
exactamente un AssetContentCollision/Error/Stop; el posible duplicado exactamente
un AssetContentPossibleDuplicate/Warning/Continue. Rechaza estados imposibles
mediante ArgumentException o ArgumentOutOfRangeException; null mediante
ArgumentNullException. Los tests verifican estas defensas con reflection sin
abrir el constructor productivo.

## Idempotencia semántica limitada

SameAssetSameContent significa únicamente: «comparado contra este fingerprint
conocido, el mismo asset tiene exactamente el mismo contenido». Permite una
futura decisión idempotente, pero no demuestra un Job previo o COMPLETED,
una copia en TeraBox, un WebP en producción ni una actualización de SQLite.
No hay AlreadyProcessed, Completed, SafeToSkip, CanWrite, ShouldExecute,
JobId, State o Timestamp. Un análisis limpio no autoriza ejecución o escritura.

## Alcance y pruebas

Cero filesystem I/O, hashing interno, búsquedas, scanning, catálogo, colección
global, índices, registros, SQLite o persistencia. No recibe paths ni crea
Sha256Hasher; consume digests ya calculados. No guarda fingerprints en schemas,
manifest/profile, JSON, sidecars, logs, archivos o TeraBox. No cambia modelos
package/plan, conversiones, CRC ZIP, CLI, dependencias o configuración Nimroel.
Jobs/estados/recovery y Fase 7 siguen fuera del alcance de 6.2.
[6.3 — Job ID](JOB_ID.md) añade exclusivamente la identidad global de proceso,
independiente de los fingerprints y sin modificar el analyzer o su resultado.
6.3 no crea modelo/lifecycle de Jobs o persistencia. Desde
[6.4 — Estados persistentes](JOB_STATES.md) existe un journal mínimo separado
bajo StateRoot; los fingerprints/analyzer siguen puros y sin persistencia.
[6.5 recovery](JOB_RECOVERY.md) HECHO: discovery de checkpoints durables en solo
lectura, separado del analyzer. Fase 6 HECHA; siguiente: Fase 7 — Auditor IA.

Una futura capa de catálogo podrá obtener múltiples fingerprints y agregar
llamadas a esta primitiva. No existe overload Analyze(candidate, IEnumerable).
`hash origen == hash destino` tras copiar/escribir sigue siendo una verificación
futura; 6.2 no implementa ni autoriza copias.

Las pruebas cubren contratos exactos, igualdad, matriz completa, dirección,
aislamiento en cuatro combinaciones, invariantes del constructor/reporte,
inmutabilidad, repetición y culturas tr-TR/ar-SA. SHA-256 real sobre fuentes
independientes cubre las cuatro relaciones, incluido cambio de un byte.
La integración Nimroel recorre PackageSemanticValidator → ValidatedAssetPackage
→ master path → Sha256Hasher → fingerprint; un segundo paquete controlado del
mismo universo demuestra independencia de path, role, classification y perfil,
con contenido y filesystem intactos.
