# Package Semantic Validation — Fase 2 · Capítulo 2.8.2

**HECHO.** La frontera semántica une las capacidades existentes después de
extracción ZIP y antes de routing. **Routing solo podrá consumir
ValidatedAssetPackage.** Fase 3 no se implementa en este capítulo.

```text
ZIP seguro → directorio extraído estable
          → PackageSemanticValidator.Validate(root, UniverseContext)
          → PackageSemanticValidationResult
              issues STOP → Package null
              report limpio → ValidatedAssetPackage → routing futuro
```

El validador solo lee: no crea, mueve, copia, borra, renombra, corrige contenidos
ni convierte archivos. No calcula destinos ni SHA-256 y no consulta production
repo, SQLite, TeraBox o red. No hay estado de universo global ni dependencias
nuevas. Seguridad ZIP y validez semántica son responsabilidades separadas: un
ZIP legible y seguro puede contener un package inválido.

## Manifest v2 estricto

`AssetManifestV2Loader.Load(Stream)` lee desde la posición actual y conserva
abierto el stream del caller; `Load(path)` abre solo lectura. Usa
System.Text.Json sin JSON Schema runtime package y no modifica el DTO histórico.

Exige exactamente schema_version, universe_id, asset_id, asset_type,
production_profile y classification. Rechaza propiedades desconocidas y
duplicadas, incluidas las claves de classification. La raíz/classification
deben ser objetos y las propiedades de identidad/valores de classification,
strings no nulos con la gramática Naming v1 correspondiente.

schema_version debe representar **exactamente el número 2**. Admite `2.0`,
`2e0`, `20e-1` o `0.2e1`, equivalentes al entero del schema, sin redondear números
próximos a 2. universe_id, asset_type, production_profile y claves/valores de
classification son machine identifiers de máximo 64; asset_id tiene máximo 96
y debe satisfacer `AssetNamingRules.IsValidAssetId` y `MatchesAssetType`.
No se aplica trim, lowercase, inferencia ni normalización de datos.

La carga estricta acepta classification vacío por forma; su suficiencia se
comprueba después con el perfil. Tipos/estructura/JSON incorrectos producen
JsonException o una subclase; versión/formas/relación de naming incorrectas
producen InvalidDataException mediante guardas explícitas. No depende de
capturar ArgumentException de modelos con datos hostiles. Los errores de E/S
se propagan; no hay catch(Exception).

Manifest v1 sigue histórico y no se migra ni se le infiere universo. Un package
con v1 devuelve package_manifest_invalid. Su schema, DTO y fixture de Fase 1
permanecen intactos.

## Estructura, discovery y envelope

`PackageSemanticValidator.Validate(string packageRoot, UniverseContext context)`
es síncrono y requiere contexto explícito no nulo. Root nulo/vacío/whitespace o
argumentos de path incorrectos son errores de programación. El path se convierte
en absoluto y se elimina el separador final, conservando el root del volumen.
Un path inexistente o que es archivo produce package_root_invalid sin crear nada.
Los demás errores operativos de filesystem no se convierten en issues de package.

Se enumeran únicamente las entradas top-level, ordenadas por filename Ordinal.
La raíz directamente reparse/link y cualquier entry top-level reparse/link o
subdirectorio producen package_structure_invalid. Se usan atributos estándar
.NET, sin resolver targets ni recorrer subdirectorios. Ante estructura inválida
no se lee ningún manifest o archivo de contenido.

Los candidatos al manifest son archivos top-level con suffix exacto
`_manifest.json`, comparación Ordinal. Cero candidatos producen
package_manifest_missing; más de uno, package_manifest_ambiguous. No se elige
arbitrariamente ni se lee un candidato ambiguo.

El candidato único se carga estrictamente. Datos inválidos producen
package_manifest_invalid; Detail no contiene JSON completo ni stack trace.
El filename debe ser exactamente `<asset_id>_manifest.json`, y el nombre del
package root normalizado, exactamente asset_id. Los mismatches producen issues
sin renombrar. Un manifest con filename no canónico también es inesperado al
comparar el conjunto permitido de archivos.

## Universo, rule y clasificación

`ManifestUniverseScope.Matches` comprueba la identidad contra `context.Id`.
Ante mismatch se para sin aplicar el perfil activo al universo ajeno. La
identidad validada será `UniverseAssetKey(context.Id, manifest.AssetId)`:
el universo no se incorpora al asset_id ni a filenames.

`context.Profile.TryGetAssetRule` busca la combinación exacta AssetType +
ProductionProfile, sin fallback. Si no existe, se para. Tras manifest/universo/
rule fiables, `ValidateClassification` comprueba required/allowed dimensions;
el issue informa de claves faltantes/no permitidas, sin valores. No se validan
vocabularios como norgard/treskal/farmer/male ni se infiere clasificación.

Profile v1 sigue cargándose globalmente. Si su runtime rule tiene PackageFiles
vacío, el conjunto permitido consta únicamente del manifest universal; puede
validarse si cumple el resto del contrato. No se inventan archivos ni se
reinterpretan reglas históricas. Nimroel actual utiliza Profile v2.

## Archivos y contenido

Los filenames adicionales proceden exclusivamente de `AssetRule.PackageFiles`
mediante `ResolveFileName(manifest.AssetId)`. El conjunto permitido es manifest
universal canónico + todos esos filenames, incluidos los optional.

Se comparan nombres **enumerados realmente**, mediante Ordinal, sin usar
File.Exists como prueba de casing. Required ausente produce
package_required_file_missing; optional ausente no produce issue ni aparece en
FilesByRole. Un nombre con casing incorrecto es unexpected y, si reemplaza un
required, también deja ese archivo canónico missing.

Todo archivo fuera del conjunto permitido produce package_unexpected_file,
incluidos thumbnails, readmes, archivos de otro asset y ProductionWebP en el
package Nimroel de entrada. No se ignora, mueve ni borra.

ContentValidator null no inspecciona contenido específico. Prompt, info y
Visual Identity de Nimroel solo se comprueban por presencia/nombre; no se parsea
Visual Identity ni se crea su schema.

Solo se soporta operativamente `png_master`. Para un archivo presente se usa
`PngMasterValidator` y su mapper existente: png_invalid o
png_unsupported_feature, Error + Stop, con el path concreto del master y Reason
en Detail. No hay códigos package_png_* ni requisito de aspect ratio 4:5.
La validación PNG conserva su alcance estructural: no decodifica zlib/píxeles
ni comprueba completamente metadatos auxiliares.

Un validator desconocido en archivo presente produce
package_content_validator_unsupported, sin ejecutar nada dinámicamente.
Si el archivo está ausente, no se emite ese issue: required missing basta, y
optional ausente es válido. No hay registry de plugins, reflection ni fallback.

## Resultado, snapshot y orden

`PackageSemanticValidationResult` es inmutable y contiene Issues, Package e
IsValid. Su constructor exige report limpio **si y solo si** Package existe.
Un report con STOP nunca lleva Package; cualquier issue impide adjuntar un
objeto validado. Las salidas inválidas del validator son Error + Stop.

`ValidatedAssetPackage` es una clase sellada con constructor internal, sin I/O
ni enumeración adicional. Solo se construye al terminar todas las comprobaciones
aplicables con report limpio. Expone AssetKey, PackageRoot, ManifestPath,
Manifest, AssetRule y FilesByRole. Los paths son absolutos y AssetRule es la
regla inmutable exacta del perfil.

FilesByRole toma snapshot defensivo, comparer Ordinal y wrapper read-only.
Solo contiene archivos declarados realmente presentes, sin manifest artificial,
extras u optional ausentes. ManifestPath está separado. Como el DTO de manifest
contiene Dictionary mutable, se toma una copia al construir y se devuelve una
copia defensiva por acceso: modificar el DTO expuesto no modifica el snapshot.

Orden determinista de comprobación/issues:

1. Root y estructura; entries estructurales inválidas por nombre Ordinal.
2. Discovery/carga de manifest (early return si no existe, es ambiguo o inválido).
3. Manifest filename y root name, en ese orden.
4. Universo y rule; early return ante mismatch o rule desconocida.
5. Classification: missing en orden de rule, not_allowed ordenado Ordinal.
6. Required missing en orden de PackageFiles del perfil.
7. Unexpected files ordenados por filename Ordinal.
8. Content validation en orden de PackageFiles, solo para presentes.

Con manifest, universo y rule válidos se recopilan errores independientes,
aunque ya exista un STOP. La base semántica inválida nunca se completa por
inferencia. NapIssueMapper sigue adaptando solo resultados locales existentes;
el validator crea sus propios issues mediante helpers internos pequeños.

## Códigos y contexto

Todos los nuevos códigos son **Error + Stop**:

| Código | SubjectPath |
| --- | --- |
| package_root_invalid | Root normalizado. |
| package_structure_invalid | Root/entry estructural concreta. |
| package_manifest_missing | Root. |
| package_manifest_ambiguous | Root. |
| package_manifest_invalid | Manifest candidato. |
| package_manifest_filename_mismatch | Manifest real. |
| package_root_name_mismatch | Root. |
| package_universe_mismatch | Manifest. |
| package_rule_not_found | Manifest. |
| package_classification_invalid | Manifest; Detail contiene solo dimensiones. |
| package_required_file_missing | Path canónico esperado; Detail contiene Role. |
| package_unexpected_file | Archivo real. |
| package_content_validator_unsupported | Archivo presente; Detail contiene validator. |

## Tests y límites de confianza

Los tests generan packages pequeños en temp roots controlados, con PNG real
mínimo y CRC independiente. Verifican carga estricta, guardas, reglas genéricas,
Nimroel, v1 histórico, casing, orden múltiple, mapper PNG, snapshots e invariantes
de resultados. Se comparan bytes y estructura antes/después de packages válidos
e inválidos; no se usan timestamps frágiles, red, sleeps ni binarios grandes.
Los tests de directory links usan junctions en Windows y symlinks en otras
plataformas; el de file symlink es no-Windows y el de sharing error, Windows-only.

El package debe estar estable y normalmente proceder del extractor controlado.
Se rechazan los links detectados en raíz y top-level; no se implementa otra
resolución de ancestros/targets ni un segundo extractor. El caller mantiene la
seguridad de la ubicación ya extraída. Mutaciones externas concurrentes quedan
fuera del contrato: desaparición o acceso denegado al leer pueden propagar E/S.
No hay retries, watchers, leases persistentes ni hashes. El objeto inmutable es
un snapshot semántico de la validación, no una garantía de que los bytes del
filesystem no puedan cambiar después.

La carga JSON materializa un JsonDocument en memoria con opciones estándar;
no se añade una cuota nueva de tamaño del manifest/configuración. Los límites
operativos adicionales se decidirán cuando corresponda.

Desde [5.4 — Universe Profile v4](UNIVERSE_PROFILE_V4.md), UniverseAssetRule
valida en configuración que Conversion.SourceRole corresponde Ordinal a un
PackageFiles required `.png` con `png_master`. Un perfil incompatible falla al
cargarse, antes de validar packages. PackageSemanticValidator conserva su
algoritmo; ValidatedAssetPackage retiene la misma AssetRule y por tanto
AssetRule.Conversion. Tests de config → package → resolver demuestran esa
retención para Nimroel y el fixture genérico, sin convertir ni escribir salidas.

**Fases 2 y 3 HECHAS. Fase 4 HECHA dentro del alcance v1. Fase 5 HECHA;
5.1–5.5 HECHOS; 5.5 — No recorte silencioso HECHO.**
Routing solo podrá consumir ValidatedAssetPackage.
