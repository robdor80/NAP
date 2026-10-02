# NAP

NAP es un sistema genérico de producción, conservación, catalogación, auditoría y planificación de assets visuales organizado por universos/perfiles. Nimroel es el primer Universe Profile real previsto.

El proyecto se encuentra en fase de fundación. Esta solución inicial establece una base .NET 8 limpia y extensible, con el núcleo de negocio independiente de cualquier interfaz gráfica futura.

La arquitectura es **NAP Core + Universe Profile**, equivalente conceptualmente a **CoreRPG + Universe Pack**. El núcleo aporta capacidades genéricas; cada perfil aportará las reglas y configuración de su universo.

**2.6.1 — Core Universe Scope** añade identidad fuerte, registry, raíces de almacenamiento y contexto explícito por universo. Manifest v1 permanece intacto; Manifest v2 y la configuración del perfil Nimroel serán el siguiente subcapítulo, 2.6.2. Véase [MULTI_UNIVERSE_ARCHITECTURE.md](docs/MULTI_UNIVERSE_ARCHITECTURE.md).

El producto sigue llamándose NAP; no se decide todavía un nuevo significado para sus siglas.
