# Plan Logs — Fase 4 · Capítulo 4.4

**Fase 3 HECHA. Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1.
4.1 — ProcessingPlan, 4.2 — Dry Run, 4.3 — Plan Validation y 4.4 — Logs HECHOS.
Fase 5 — Conversión HECHA. 5.1 — Portrait, 5.2 — Validar salida Portrait, 5.3 — Scene, 5.4 — Perfiles genéricos y 5.5 — No recorte silencioso HECHOS. Fase 6 — Integridad HECHA; 6.1 — SHA-256 HECHO; 6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO; 6.5 — Recuperación tras fallo HECHO; Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite.**

Logs útiles pero no invasivos: un resumen humano breve del resultado de
planning/validation, separado del preview detallado de Dry Run.

## Contrato puro

Clase sellada, sin estado y con constructor público por defecto:

```csharp
public string Render(ProcessingPlan plan, NapIssueReport validationReport)
```

PlanLogTextRenderer devuelve exclusivamente un string. Un input null produce
ArgumentNullException con ParamName igual a `plan` o `validationReport`.
El caller entrega los hechos; el renderer no llama validator, builder, resolver
ni DryRunTextRenderer y no recibe snapshot, package, repository, context o profile.

## Privacidad por omisión

Solo incluye AssetKey.UniverseId.Value, AssetKey.AssetId, AssetType,
ProductionProfile, ProductionDestination.RelativeDirectory, los flags
IsClean, ShouldStop, CanContinue, Issues.Count y, por issue, Code,
Severity, Disposition. Usa identificadores machine-safe y enums controlados.

Omite deliberadamente classification, PackageRoot, ManifestPath, FilesByRole,
ProductionDestination.RootPath y FullDirectoryPath, NapIssue.Message,
SubjectPath y Detail. No introduce rutas absolutas ni rutas de archivos fuente,
exception messages, stack traces, tokens, API keys o credentials.
No intenta adivinar secretos mediante heurísticas de redacción: los campos
potencialmente sensibles ni siquiera se leen.

RelativeDirectory se muestra tal como se recibe, sin normalizar, recalcular
ni cambiar separators. Los flags proceden del report, sin inventar Status/State,
PASS/FAIL o permiso de ejecución. Warning + Continue significa
`is_clean: false`, `should_stop: false`, `can_continue: true`.

## Formato exacto v1

Ejemplo Nimroel limpio, con ancestros canónicos y directorio final ausente:

```text
NAP PLAN LOG
Planning summary v1

ASSET
  universe_id: nimroel
  asset_id: portrait_treskal_farmer_male_040
  asset_type: portrait
  production_profile: portrait_npc

DESTINATION
  relative_directory: portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040

VALIDATION
  is_clean: true
  should_stop: false
  can_continue: true
  issue_count: 0

ISSUES
  (none)

SAFETY
  Absolute paths and source file paths are intentionally omitted.
  This log does not authorize execution.
```

Ejemplo STOP del validator en Windows ante el ancestro histórico
`portraits/Norgard`. No se adapta/migra casing; el FullPath observado y el
Detail expected/observed no entran en el log:

```text
NAP PLAN LOG
Planning summary v1

ASSET
  universe_id: nimroel
  asset_id: portrait_treskal_farmer_male_040
  asset_type: portrait
  production_profile: portrait_npc

DESTINATION
  relative_directory: portraits/norgard/treskal/farmer/male/portrait_treskal_farmer_male_040

VALIDATION
  is_clean: false
  should_stop: true
  can_continue: false
  issue_count: 1

ISSUES
  [0]
    code: plan_destination_casing_conflict
    severity: Error
    disposition: Stop

SAFETY
  Absolute paths and source file paths are intentionally omitted.
  This log does not authorize execution.
```

La misma estructura representa `plan_destination_exists` u otros códigos
del report. Cero issues usa `(none)`; uno o más issues conserva exactamente
el orden entregado, incluidos duplicados, con índices desde cero. No ordena,
deduplica ni filtra. Solo muestra code/severity/disposition, nunca
StopsProcessing por separado. Severity y Disposition son independientes.

Booleanos ASCII lowercase `true`/`false`; count e índices invariant.
LF canónico (`"\n"`), sin CR, BOM, newline final ni trailing whitespace.
Mismos inputs producen exactamente el mismo texto, independiente de cultura,
plataforma y llamadas previas. No añade líneas al formato mostrado.

## Alcance y limitaciones deliberadas

Cero filesystem I/O: funciona con paths inexistentes y no crea archivos ni
directorios. No Console, Debug, Trace, StreamWriter, logging framework ni
paquetes de logging. Sin persistencia, timestamps, JobId, IDs de sesión,
estados, exceptions, modelo de log adicional o formato JSON/XML/YAML.
No duplica CLASSIFICATION, PACKAGE, INPUT FILES, OPERATIONS ni el SAFETY
completo del Dry Run. No revalida sources, routing, existencia o colisiones.

Un report limpio es point-in-time y no autoriza ejecución ni escritura.
El renderer representa el report recibido, no demuestra su correspondencia
con el plan ni añade una frontera pre-execution.

Fase 4 está completada dentro del alcance v1 actual: 4.1 congela hechos,
4.2 representa el Dry Run, 4.3 valida destination contra snapshot y 4.4 produce
este resumen seguro. ProcessingPlan crecerá cuando existan contratos de
archive, conversión, outputs WebP, hashes y operaciones reales; ninguno se
implementa aquí. Persistencia/historial, timestamps y diagnósticos futuros
requieren sus contratos y una política explícita de privacidad; Jobs/estados
pertenecen a Fase 6. Fase 5 — Conversión HECHA; 5.1 — Portrait HECHO
como primitive independiente ([PORTRAIT_CONVERSION.md](PORTRAIT_CONVERSION.md)),
sin integrar conversión/exceptions en este log. 5.2 — Validar salida Portrait y 5.3 — Scene HECHOS;
5.4 — Perfiles genéricos HECHO; 5.5 — No recorte silencioso HECHO.
Portrait ya prohíbe crop.

Desde [6.1 — SHA-256](SHA256_INTEGRITY.md) existe una primitiva independiente
de hashing. ProcessingPlan continúa sin guardar hashes, Plan Validation sin
usarlos y Plan logs sin mostrarlos o persistirlos. No se reabre Fase 4.
6.2 — Duplicados HECHO; 6.3 — Job ID HECHO; 6.4 — Estados HECHO;
6.5 — Recuperación tras fallo HECHO. Fase 7 — Auditor IA HECHA (7.1–7.5); Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6); Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4); siguiente: Fase 10 — SQLite. El journal de estados es infraestructura
separada; el renderer conserva su contrato sin persistencia o JobId/JobState.

## Validación

Tests de API/null, outputs completos limpio/STOP, flags independientes,
múltiples issues en orden con duplicados, privacidad con sentinels e inyección
de newline, LF, determinismo bajo tr-TR/ar-SA/sv-SE, ausencia de creación
física y pipeline real Nimroel resolver → builder → validator → renderer.
El caso histórico Windows comprueba code/Error/Stop sin FullPath/Detail;
en otras plataformas el ancestro con casing diferente queda ausente respecto
al destino canónico y el report es limpio.
