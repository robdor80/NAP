# SHA-256 Integrity — Fase 6 · Capítulo 6.1

**Fase 5 — Conversión HECHA. Fase 6 — Integridad EN CURSO.
6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO.
6.5 — Recuperación tras fallo SIGUIENTE.**

6.1 responde únicamente «¿Cuál es el SHA-256 exacto de estos bytes?». SHA-256
representa 32 bytes / 256 bits como 64 caracteres hexadecimal lowercase ASCII.
El digest depende exclusivamente del contenido binario: no del path, filename,
Asset ID, universo, metadata, timestamps, extensión, clasificación, routing o
formato gráfico. Archivos con idénticos bytes producen idéntico digest aunque
tengan nombres/carpetas distintos. Un byte distinto cambia el digest salvo
colisión criptográfica.

## Sha256Digest

```csharp
public sealed record Sha256Digest
{
    public Sha256Digest(string hex);
    public string Hex { get; }
    public override string ToString();
}
```

El constructor admite únicamente `[0-9a-f]{64}` mediante comprobación ASCII
explícita. Null produce ArgumentNullException, ParamName `hex`; toda otra
representación inválida produce ArgumentException, ParamName `hex`.
Rechaza empty/whitespace, longitud distinta de 64, uppercase o mixed case,
Unicode, caracteres fuera de 0–9/a–f, prefijo 0x, espacios, separadores, newline
y tab. No hay trim, lowercasing automático o normalización de input.

Hex es get-only y ToString devuelve exactamente Hex. Es un value object:
Equals, == y != comparan el valor; iguales tienen GetHashCode coherente.
No expone byte[] mutable ni propiedades adicionales. La representación
canonical lowercase del resultado se genera en el hasher, no corrigiendo un
digest proporcionado al constructor.

## Sha256Hasher

Clase pública sealed y stateless con exactamente dos APIs:

```csharp
public Sha256Digest Compute(string path);
public Sha256Digest Compute(Stream stream);
```

Compute(path) usa ArgumentException.ThrowIfNullOrWhiteSpace(path), ParamName
`path`. Abre FileStream con FileMode.Open, FileAccess.Read y FileShare.Read,
lee todos los bytes y dispone el stream interno al terminar, también ante fallos.
No normaliza path, usa Path.GetFullPath, resuelve links, copia archivos, modifica
atributos/timestamps deliberadamente o escribe. FileNotFoundException,
DirectoryNotFoundException, UnauthorizedAccessException, IOException y demás
errores operativos se propagan normalmente; no hay catch(Exception) ni NapIssue.

Compute(stream) exige no null (ArgumentNullException, `stream`) y CanRead true
(ArgumentException, `stream`). **Empieza en la posición actual y consume hasta
EOF; no rebobina antes ni restaura Position después.** El stream pertenece al
caller y permanece abierto tanto tras éxito como tras error. No necesita CanSeek,
Length ni Position y admite streams no seekables y lecturas cortas.

La implementación usa System.Security.Cryptography.SHA256.HashData(Stream)
de la BCL .NET 8. El procesamiento es streaming con memoria acotada, sin
File.ReadAllBytes, ReadToEnd, ToArray del input, MemoryStream intermedio o
preasignación según Length. El resultado criptográfico es el digest de 32 bytes;
Convert.ToHexString y ToLowerInvariant producen la representación canónica.
No se implementa criptografía manual ni un selector de algoritmos.

Hashing solo lee: no modifica bytes del source, llama Write/SetLength o crea
archivos adicionales. El caller debe mantener estable el input; el digest
describe los bytes leídos, sin añadir locks de workflow, retries o leases.

## Vectores y pruebas

| Bytes | SHA-256 esperado |
| --- | --- |
| Vacío | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| ASCII abc | ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad |

Los expected de tests son constantes independientes; no se calculan llamando
al mismo algoritmo dentro del test. Se cubren archivos y MemoryStream, igualdad
path/stream, nombres y carpetas distintos, cambio de un byte, todos los bytes
0x00..0xFF y texto Unicode según bytes reales de encoding.
`prefixabc` desde Position 6 da exactamente el digest de `abc`, y una segunda
lectura desde EOF da el digest vacío. Wrappers privados prohíben Length,
Position, Seek, Write y SetLength, permiten lecturas cortas y propagan IOException.
Un stream generado de 8 MiB + 37 bytes verifica consumo completo sin materializar
el input; no se añaden fixtures binarios grandes ni medidas frágiles de GC.

Las pruebas comprueban source intacto, ausencia de archivos extra, repetición,
digest canónico bajo distintas culturas y disposición del FileStream interno.
Las integraciones calculan el hash del master/source de ValidatedAssetPackage
tanto en Nimroel como en el fixture genérico v4, manteniendo package intacto.
Otra integración hashea WebP producido en memoria por Portrait mediante un
MemoryStream read-only, sin archivo de output. Su hash identifica esos bytes;
no promete reproducibilidad criptográfica entre versiones de ImageSharp.

## SHA-256 y CRC ZIP

ZipEntryIntegrity y el CRC existente permanecen intactos. CRC verifica integridad
del transporte según el formato ZIP; SHA-256 aporta identidad criptográfica
genérica del contenido. No se sustituye CRC ni se reutiliza como identidad de asset.
No se usan SHA-1, MD5, xxHash u otros algoritmos en esta primitiva.

## Alcance y continuidad

6.1 no define AssetHash, PackageHash, ManifestHash, CompositeHash o hash de JSON
canónico. No concatena AssetId/path/metadata con el input. No almacena hashes en
ValidatedAssetPackage, ProcessingPlan, ResolvedImageConversion o WebP images.
ProcessingPlan no los congela, Plan Validation no los usa y Plan logs no los
muestra. Fase 4 conserva su alcance.

6.1 no incorpora política de duplicados al hasher. Desde
[6.2 — Duplicados](DUPLICATE_DETECTION.md), AssetContentFingerprint asocia
UniverseAssetKey + Sha256Digest y el analyzer compara pairwise dentro del mismo
universo, sin I/O ni hashing interno. La matriz define idempotencia semántica
limitada, colisión Error + Stop, posible duplicado Warning + Continue y Distinct.
Cross-universe se rechaza; ninguna relación implica un Job completado.
[6.3 — Job ID](JOB_ID.md) ya formaliza la identidad global de proceso, sin
acoplarla a hashes ni modificar esta primitiva.
[6.4 — Estados persistentes](JOB_STATES.md) implementa reglas puras y journal
mínimo únicamente bajo StateRoot, sin modificar Sha256Digest/Sha256Hasher.
Orquestación, recuperación (6.5 siguiente), SQLite y TeraBox siguen sin implementar.
No hay persistencia en JSON/profile/
manifest, sidecars .sha256, SHA256SUMS, logs o bases de datos. No hay copias,
escrituras de producción/archive, output persistence, CLI/UI, auditor IA o Fase 7.
6.1 no añade issue codes; 6.2 añade solo asset_content_collision y
asset_content_possible_duplicate. No se añaden dependencias o cambios de schemas/config.

La semántica de 6.2 usa la identidad completa UniverseAssetKey y digests conocidos,
sin modificar Sha256Digest/Sha256Hasher ni guardar fingerprints en modelos.
No hay catálogo, colección global o resolución automática de colisiones/duplicados.
`hash origen == hash destino` continúa como requisito futuro de verificación
cuando existan escrituras/copias; 6.1 todavía no las implementa ni autoriza.
Fase 6 permanece EN CURSO, sin marcarla completa.
