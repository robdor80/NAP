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
