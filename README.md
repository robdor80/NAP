# NAP — Nexus Asset Platform

NAP es un sistema genérico de producción, conservación, catalogación, auditoría y planificación de assets visuales organizado por universos/perfiles. Nimroel es el primer Universe Profile real, definido declarativamente en [profile.json](config/universes/nimroel/profile.json).

El proyecto se encuentra en fase de fundación. Esta solución inicial establece una base .NET 8 limpia y extensible, con el núcleo de negocio independiente de cualquier interfaz gráfica futura.

La arquitectura es **NAP Core + Universe Profile**, equivalente conceptualmente a **CoreRPG + Universe Pack**. El núcleo aporta capacidades genéricas; cada perfil aportará las reglas y configuración de su universo.

**2.6 — Multi-Universe Foundation** está completo: 2.6.1 añade identidad fuerte, registry, raíces y contexto por universo; 2.6.2 añade [Manifest v2](docs/MANIFEST_V2.md), [configuración genérica de perfiles](docs/UNIVERSE_PROFILE_V1.md), loader, reglas de clasificación y aislamiento léxico entre raíces. Manifest v1 permanece intacto como contrato histórico. Véase [MULTI_UNIVERSE_ARCHITECTURE.md](docs/MULTI_UNIVERSE_ARCHITECTURE.md).

**2.7 — ZIP deliberadamente incorrectos para tests** está hecho: [auditoría adversarial](docs/ADVERSARIAL_ZIP_TESTS.md), fixtures reproducibles y corrección de apertura de cabeceras truncadas.

**Fase 2 HECHA (2.1–2.8).** **2.8.1 — [Package Contract v1](docs/PACKAGE_CONTRACT_V1.md) + [Universe Profile v2](docs/UNIVERSE_PROFILE_V2.md) HECHO:** archivos declarativos y compatibilidad histórica. **2.8.2 — [Package Semantic Validator](docs/PACKAGE_SEMANTIC_VALIDATION.md) HECHO:** Manifest v2 estricto, universo/rule/classification, envelope flat, required/extras y contenido soportado, en solo lectura. Produce un ValidatedAssetPackage inmutable. **Routing solo podrá consumir ValidatedAssetPackage.**

**Fase 3 — Routing y repo HECHA. 3.1 — [Production Repository Boundary](docs/PRODUCTION_REPOSITORY_BOUNDARY.md) HECHO:** valida únicamente los atributos de `UniverseContext.Storage.ProductionRoot` y produce un `ValidatedProductionRepository` inmutable. Es read-only, sin enumerar entries, crear carpetas, resolver links ni exigir `.git`.

**3.2 — [Repository Scanner](docs/REPOSITORY_SCANNER.md) HECHO:** consume exclusivamente una raíz validada, revalida el root y fotografía archivos/directorios con nombres exactos, RelativePath con `/` y orden Ordinal. Es read-only, no lee contenidos ni interpreta `.git`; cualquier entry ReparsePoint causa STOP sin traversal ni snapshot parcial.

**3.3 — [Routing Contract v1](docs/ROUTING_CONTRACT_V1.md) HECHO:** segmentos estructurados Literal/Classification/AssetId y [Universe Profile v3](docs/UNIVERSE_PROFILE_V3.md), sin templates libres, I/O ni resolución de destinos. Las dimensiones usadas por routing deben ser RequiredClassification.

**3.4 — [Structural Change Detection](docs/STRUCTURAL_CHANGE_DETECTION.md) HECHO:** diff puro entre snapshots del mismo universo/root, con Added/Removed/KindChanged y RelativePath Ordinal. Sin I/O, inferencias move/rename, contenido, routing semantics ni ignores.

**3.5 — [Nimroel Historical Structure Audit + Canonical Routing Policy](docs/NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md) HECHO:** el corte histórico 39/39 separa el layout legacy de la semántica y fija para `portrait_npc` la ruta `portraits/{culture}/{location}/{role}/{sex}/{asset_id}`. Nimroel usa Profile v3; Routing Contract v1 resulta suficiente. No se han migrado assets ni escrito en producción.

**3.6 — [Destination Resolver](docs/DESTINATION_RESOLVER.md) HECHO:** calcula un `ProductionAssetDestination` desde `ValidatedAssetPackage` + `ValidatedProductionRepository`, usando exclusivamente `package.AssetRule.Routing`. Aísla universos, valida segmentos y nombres reservados Windows, y comprueba contención léxica bajo ProductionRoot. Sin I/O, snapshot, comprobación de existencia/colisiones ni escrituras. Resolver un destino no autoriza escribirlo.

**Fase 4 — PLAN / Dry Run HECHA dentro del alcance v1. 4.1 — [ProcessingPlan](docs/PROCESSING_PLAN.md) HECHO:** snapshot inmutable de identidad, metadata completa, inputs validados y destino de producción recibido. ProcessingPlanBuilder comprueba coherencia de universo/AssetKey/root, sin re-routing ni I/O. No añade operaciones, outputs, timestamps, JobId o escrituras; es la base del plan y todavía no un grafo de ejecución completo.

**4.2 — [Dry Run](docs/DRY_RUN.md) HECHO:** DryRunTextRenderer devuelve texto humano determinista desde ProcessingPlan, con orden Ordinal, LF fijo y paths escapados. Muestra los hechos congelados y declara que las operaciones aún no están definidas; no revalida, inspecciona filesystem, inventa outputs ni ejecuta o autoriza nada. Sin CLI.

**4.3 — [Plan Validation](docs/PLAN_VALIDATION.md) HECHO:** ProcessingPlanValidator interpreta los prefijos del destino contra un ProductionRepositorySnapshot materializado, sin filesystem I/O. Permite directorios ausentes y ancestors canónicos; el primer casing conflict Windows, File blocker o destino Directory existente produce Error + Stop. No revalida sources ni usa hashes/idempotencia. Un resultado limpio es point-in-time y no autoriza escritura. **4.4 — [Logs](docs/PLAN_LOGS.md) HECHO:** PlanLogTextRenderer devuelve un resumen determinista de identidad, destino relativo y NapIssueReport, omitiendo rutas absolutas/fuente y Message/SubjectPath/Detail. Sin persistencia, timestamps ni JobId. **Fase 4 HECHA dentro del alcance v1. Fase 5 — Conversión EN CURSO. 5.1 — Portrait, 5.2 — Validar salida Portrait y 5.3 — Scene HECHOS; siguiente: 5.4 — Perfiles genéricos.**

**Fase 5 — Conversión EN CURSO. 5.1 — [Portrait PNG → WebP](docs/PORTRAIT_CONVERSION.md) HECHO:** primitive independiente con settings explícitos, lectura PNG en un mismo stream, validación estructural y límites/proporción antes de decode real. ImageSharp 3.1.12, Lanczos3 sin crop/pad/letterbox y WebP lossy en memoria; source intacto, sin archivos de salida ni conexión a ProcessingPlan. Nimroel 768×960 Q90 es ejemplo probado, no hardcode ni configuración cargada del perfil. **5.2 — Validar salida Portrait y 5.3 — Scene HECHOS; 5.4 — Perfiles genéricos SIGUIENTE; 5.5 — No recorte silencioso pendiente.** Portrait ya prohíbe crop.

**5.2 — [Portrait WebP Output Validation](docs/PORTRAIT_OUTPUT_VALIDATION.md) HECHO:** PortraitWebpOutputValidator contrasta metadata/settings, comprueba contenedor y fuerza decode WebP real, después verifica dimensiones decodificadas. Primer fallo Error + Stop, SubjectPath null; quality solo como metadata contractual. Cero I/O, re-encode, repair, hashes o autorización de escritura.

**5.3 — [Scene](docs/SCENE_CONVERSION.md) HECHO:** tipos Scene propios con settings explícitos, PNG validado/decode real, pixel safety y ratio exacto, resize Lanczos3 sin crop y WebP lossy en memoria, más output validation. Comparte con Portrait únicamente el helper internal RIFF/WEBP, extraído sin cambios de comportamiento. **No existe todavía perfil canónico Nimroel Scene**: sin resolución/ratio/quality fijos ni regla scene en config; ejemplos 16:9/3:2 solo técnicos. Sin filesystem output, ProcessingPlan, hashes o autorización de escritura.

NAP nació originalmente como Nimroel Asset Pipeline. Tras evolucionar a una arquitectura multiuniverso, el nombre oficial pasa a ser Nexus Asset Platform. Nimroel permanece como el primer Universe Profile soportado.
