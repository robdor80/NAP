# NAP

NAP es un sistema genérico de producción, conservación, catalogación, auditoría y planificación de assets visuales organizado por universos/perfiles. Nimroel es el primer Universe Profile real, definido declarativamente en [profile.json](config/universes/nimroel/profile.json).

El proyecto se encuentra en fase de fundación. Esta solución inicial establece una base .NET 8 limpia y extensible, con el núcleo de negocio independiente de cualquier interfaz gráfica futura.

La arquitectura es **NAP Core + Universe Profile**, equivalente conceptualmente a **CoreRPG + Universe Pack**. El núcleo aporta capacidades genéricas; cada perfil aportará las reglas y configuración de su universo.

**2.6 — Multi-Universe Foundation** está completo: 2.6.1 añade identidad fuerte, registry, raíces y contexto por universo; 2.6.2 añade [Manifest v2](docs/MANIFEST_V2.md), [configuración genérica de perfiles](docs/UNIVERSE_PROFILE_V1.md), loader, reglas de clasificación y aislamiento léxico entre raíces. Manifest v1 permanece intacto como contrato histórico. Véase [MULTI_UNIVERSE_ARCHITECTURE.md](docs/MULTI_UNIVERSE_ARCHITECTURE.md).

**2.7 — ZIP deliberadamente incorrectos para tests** está hecho: [auditoría adversarial](docs/ADVERSARIAL_ZIP_TESTS.md), fixtures reproducibles y corrección de apertura de cabeceras truncadas.

**Fase 2 HECHA (2.1–2.8).** **2.8.1 — [Package Contract v1](docs/PACKAGE_CONTRACT_V1.md) + [Universe Profile v2](docs/UNIVERSE_PROFILE_V2.md) HECHO:** archivos declarativos y compatibilidad histórica. **2.8.2 — [Package Semantic Validator](docs/PACKAGE_SEMANTIC_VALIDATION.md) HECHO:** Manifest v2 estricto, universo/rule/classification, envelope flat, required/extras y contenido soportado, en solo lectura. Produce un ValidatedAssetPackage inmutable. **Routing solo podrá consumir ValidatedAssetPackage.** Siguiente: Fase 3 — Routing y repo, todavía no iniciada. No hay routing, SQLite, TeraBox, UI ni migración de manifests.

El producto sigue llamándose NAP; no se decide todavía un nuevo significado para sus siglas.
