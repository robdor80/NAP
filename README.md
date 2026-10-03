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

**Fase 4 — PLAN / Dry Run EN CURSO. 4.1 — [ProcessingPlan](docs/PROCESSING_PLAN.md) HECHO:** snapshot inmutable de identidad, metadata completa, inputs validados y destino de producción recibido. ProcessingPlanBuilder comprueba coherencia de universo/AssetKey/root, sin re-routing ni I/O. No añade operaciones, outputs, timestamps, JobId o escrituras; es la base del plan y todavía no un grafo de ejecución completo.

**4.2 — [Dry Run](docs/DRY_RUN.md) HECHO:** DryRunTextRenderer devuelve texto humano determinista desde ProcessingPlan, con orden Ordinal, LF fijo y paths escapados. Muestra los hechos congelados y declara que las operaciones aún no están definidas; no revalida, inspecciona filesystem, inventa outputs ni ejecuta o autoriza nada. Sin CLI.

**4.3 — [Plan Validation](docs/PLAN_VALIDATION.md) HECHO:** ProcessingPlanValidator interpreta los prefijos del destino contra un ProductionRepositorySnapshot materializado, sin filesystem I/O. Permite directorios ausentes y ancestors canónicos; el primer casing conflict Windows, File blocker o destino Directory existente produce Error + Stop. No revalida sources ni usa hashes/idempotencia. Un resultado limpio es point-in-time y no autoriza escritura. **Siguiente: 4.4 — Logs.** Fase 4 sigue EN CURSO.

NAP nació originalmente como Nimroel Asset Pipeline. Tras evolucionar a una arquitectura multiuniverso, el nombre oficial pasa a ser Nexus Asset Platform. Nimroel permanece como el primer Universe Profile soportado.
