# NAP — Nexus Asset Platform

NAP es un sistema genérico de producción, conservación, catalogación, auditoría y planificación de assets visuales organizado por universos/perfiles. Nimroel es el primer Universe Profile real, definido declarativamente en [profile.json](config/universes/nimroel/profile.json).

El proyecto se encuentra en fase de fundación. Esta solución inicial establece una base .NET 8 limpia y extensible, con el núcleo de negocio independiente de cualquier interfaz gráfica futura.

La arquitectura es **NAP Core + Universe Profile**, equivalente conceptualmente a **CoreRPG + Universe Pack**. El núcleo aporta capacidades genéricas; cada perfil aportará las reglas y configuración de su universo.

**2.6 — Multi-Universe Foundation** está completo: 2.6.1 añade identidad fuerte, registry, raíces y contexto por universo; 2.6.2 añade [Manifest v2](docs/MANIFEST_V2.md), [configuración genérica de perfiles](docs/UNIVERSE_PROFILE_V1.md), loader, reglas de clasificación y aislamiento léxico entre raíces. Manifest v1 permanece intacto como contrato histórico. Véase [MULTI_UNIVERSE_ARCHITECTURE.md](docs/MULTI_UNIVERSE_ARCHITECTURE.md).

**2.7 — ZIP deliberadamente incorrectos para tests** está hecho: [auditoría adversarial](docs/ADVERSARIAL_ZIP_TESTS.md), fixtures reproducibles y corrección de apertura de cabeceras truncadas.

**Fase 2 HECHA (2.1–2.8).** **2.8.1 — [Package Contract v1](docs/PACKAGE_CONTRACT_V1.md) + [Universe Profile v2](docs/UNIVERSE_PROFILE_V2.md) HECHO:** archivos declarativos y compatibilidad histórica. **2.8.2 — [Package Semantic Validator](docs/PACKAGE_SEMANTIC_VALIDATION.md) HECHO:** Manifest v2 estricto, universo/rule/classification, envelope flat, required/extras y contenido soportado, en solo lectura. Produce un ValidatedAssetPackage inmutable. **Routing solo podrá consumir ValidatedAssetPackage.**

**Fase 3 — Routing y repo EN CURSO. 3.1 — [Production Repository Boundary](docs/PRODUCTION_REPOSITORY_BOUNDARY.md) HECHO:** valida únicamente los atributos de `UniverseContext.Storage.ProductionRoot` y produce un `ValidatedProductionRepository` inmutable. Es read-only, sin enumerar entries, crear carpetas, resolver links ni exigir `.git`.

**3.2 — [Repository Scanner](docs/REPOSITORY_SCANNER.md) HECHO:** consume exclusivamente una raíz validada, revalida el root y fotografía archivos/directorios con nombres exactos, RelativePath con `/` y orden Ordinal. Es read-only, no lee contenidos ni interpreta `.git`; cualquier entry ReparsePoint causa STOP sin traversal ni snapshot parcial. **Siguiente: 3.3 — Routing**, todavía no implementado; 3.4–3.6 pendientes. El árbol canónico Nimroel sigue sin decidir; no hay SQLite, TeraBox, UI ni migración de manifests.

NAP nació originalmente como Nimroel Asset Pipeline. Tras evolucionar a una arquitectura multiuniverso, el nombre oficial pasa a ser Nexus Asset Platform. Nimroel permanece como el primer Universe Profile soportado.
