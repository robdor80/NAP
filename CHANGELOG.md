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

### Fixed

- Capítulo 2.7: la apertura de una entry cuyo offset local apunta al EOF o deja una signatura truncada convierte EndOfStreamException de .NET 8 en InvalidArchive, manteniendo cleanup y propagación de otros errores de E/S.
