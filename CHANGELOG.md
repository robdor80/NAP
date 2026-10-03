# Changelog

Todos los cambios relevantes de este proyecto se documentarán en este archivo.

El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el proyecto sigue [Semantic Versioning](https://semver.org/lang/es/).

## [Unreleased]

### Added

- Base inicial de la solución .NET 8.
- Capítulo 1.5: extractor ZIP seguro en NAP.Core, publicación mediante temporal propio, límites configurables, comprobación CRC-32 y tests de seguridad.
- Capítulo 1.6: primer paquete representativo en test-data y prueba de integración de Fase 1 completa, desde Inbox hasta extracción segura, preservando ZIP originales y contenido ajeno.
- Capítulo 2.1: contrato Manifest v1 formalizado, JSON Schema Draft 2020-12, documentación y manifest del fixture actualizado; sin modelo ni validación C#.
- Capítulo 2.2: modelo C# AssetManifestV1 extensible, mapeo System.Text.Json y tests de serialización sin normalización ni validador de producción.
- Capítulo 2.3: Naming v1 formalizado, comprobaciones puras de identificadores/prefijos, nombres canónicos de paquete y reglas de naming en el schema Manifest v1.
- Capítulo 2.4: inspección PNG estructural por stream con CRC de chunks, metadatos IHDR, reglas PLTE/IDAT, rechazo APNG/datos tras IEND y proporciones exactas; sin decodificación ni conversión.
- Capítulo 2.5: NapIssue con códigos estables, severidad y STOP independientes, mapper de resultados Readiness/Staging/ZIP/PNG y report inmutable con tests; sin cambiar los componentes ni crear PackageValidator.
- Capítulo 2.6.1 — Multi-Universe Core Foundation: UniverseId, UniverseAssetKey, UniverseProfile, UniverseRegistry, UniverseStorageConfig y UniverseContext con tests; NAP adopta arquitectura multiuniverso y Nimroel queda como primer perfil previsto, sin dependencia específica en el Core ni cambios a Manifest v1.
- Capítulo 2.6.2: Manifest v2 multiuniverso y DTO, contrato genérico Universe Profile v1, perfil Nimroel declarativo con loader y reglas de clasificación, comprobación de universo activo y aislamiento puro de storage roots con universe_storage_overlap (Error + Stop); schemas/tests sin nuevas dependencias ni cambios a Manifest v1.
- Capítulo 2.7: auditoría del boundary ZIP con inventario, 74 casos nuevos y fixtures sintéticos deterministas, invariantes de filesystem y mapeo real a NapIssue; Fase 2 completa y revisión de arquitectura previa a Fase 3 pendiente.
- Capítulo 2.8.1: Package Contract v1, AssetPackageFileRule puro e inmutable, PackageFiles con snapshots/unicidad, Universe Profile v2 y loader explícito v1/v2; Nimroel migrado a cuatro archivos requeridos, manteniendo Profile v1 histórico intacto y su fixture de compatibilidad. Sin PackageValidator ni cambios a extracción ZIP; Fase 2 reabierta para 2.8.2 antes de routing.
- Capítulo 2.8.2: loader runtime estricto de Manifest v2 y PackageSemanticValidator de solo lectura, con contexto de universo explícito, envelope flat/canónico, clasificación y archivos por perfil, dispatch png_master mediante mapper existente y 13 códigos package_*; resultado con invariantes y ValidatedAssetPackage inmutable con snapshots defensivos. Fase 2 completa; routing futuro solo podrá consumir el objeto validado, sin implementar Fase 3 ni modificar extracción ZIP o Manifest v1 histórico.
- Capítulo 3.1 — Production Repository Boundary: validador read-only de los atributos de ProductionRoot procedente exclusivamente de UniverseContext, ValidatedProductionRepository sellado/inmutable con constructor internal y resultado con invariantes de report limpio; códigos production_root_missing, production_root_invalid y production_root_reparse (Error + Stop), propagando otros errores operativos. Tests de identidad, raíces, reparse, invariantes y snapshots. No enumera entries, crea carpetas, resuelve links ni exige Git. Fase 3 EN CURSO; siguiente 3.2 — Repository Scanner, sin routing ni árbol canónico Nimroel decidido.

### Changed

- Marca oficial: NAP pasa a significar **Nexus Asset Platform**; **Nimroel Asset Pipeline** queda como origen histórico. Nimroel sigue siendo el primer Universe Profile real soportado. Esta decisión actualiza la documentación vigente, sin cambios técnicos de arquitectura ni renombrar repositorio, solución, proyectos, namespaces o identificadores. Fase 2 sigue completa y Fase 3 no iniciada.

### Fixed

- Capítulo 2.7: la apertura de una entry cuyo offset local apunta al EOF o deja una signatura truncada convierte EndOfStreamException de .NET 8 en InvalidArchive, manteniendo cleanup y propagación de otros errores de E/S.
