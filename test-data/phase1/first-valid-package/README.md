# Primer paquete válido de prueba — Fase 1 · Capítulo 1.6

Este fixture demuestra la cadena real:

`Inbox → Detection → Readiness → Staging → Safe Extraction`

`payload/` contiene cinco archivos pequeños con el prefijo provisional
`portrait_treskal_farmer_male_001`: `.png`, `_prompt.md`, `_info.md`,
`_manifest.json` y `_visual_identity.json`. El `.png` es texto de prueba,
no una imagen real. Los JSON son payload opaco y no constituyen esquemas.
No se comprueban PNG, JSON, asset_id, naming semántico ni coherencia del asset.
«Válido» significa únicamente que el ZIP tiene una estructura segura y puede
atravesar los componentes de Fase 1.

Se versionan los archivos fuente, no un ZIP binario. El proyecto de tests
copia únicamente `payload/` al directorio de salida. Así el test encuentra
el fixture desde `AppContext.BaseDirectory`, sin depender del directorio
de trabajo. El README no se incluye en el paquete.

`PhaseOneIntegrationTests.LegitimatePackage_CompletesPhaseOneWithoutChangingOriginals`
genera el ZIP en un entorno temporal aislado y lo copia a Inbox. Invoca
secuencialmente `InboxPreparer`, `InboxPackageDetector`,
`InboxPackageReadinessChecker`, `InboxPackageStager` y
`StagedPackageExtractor`, sin mocks ni orquestador de producción.
Readiness usa dos muestras con intervalo cero, apropiado para un fixture
cerrado y estable; el stager vuelve a comprobar readiness con el mismo checker.

El test comprueba un único candidato con nombre/ruta esperados, los estados
`Ready`, `Staged` y `Extracted`, los cinco archivos finales exactos y su
igualdad byte a byte con el fixture. También verifica el contenido del ZIP
antes y después de staging, ambos ZIP intactos después de extracción,
la ausencia de `.partial` y `.nap-*.extracting`, y archivos ajenos colocados
en las tres áreas. Limpia exclusivamente el entorno temporal propio.

Desde la raíz del repositorio:

```text
dotnet build
dotnet test
```

Para ejecutar solo esta integración:

```text
dotnet test --filter FullyQualifiedName~PhaseOneIntegrationTests
```

Este capítulo prueba el camino feliz completo de Fase 1. La validación
semántica y la futura orquestación de producción quedan para fases posteriores.
