# Auditoría previa P6.7

Fecha de ejecución UTC: 2026-10-22/23. Base e0ef903851b0679acf5c0263264f87471ff8df46; código previo 55538c7.

Se examinaron NIMROEL_HISTORICAL_STRUCTURE_AUDIT, FASE_0 (UX, fases y adenda 11.8), UNIVERSE_PROFILE_V4, ROUTING_CONTRACT_V1, DESTINATION_RESOLVER, PACKAGE_SEMANTIC_VALIDATION, IMAGE_NORMALIZATION, fuentes de Manifest v2/PackageSemanticValidator/UniverseAssetRule/ProductionDestinationResolver y Diagnóstico/Producción/Historial, y las cuatro revisiones anteriores de Fase 6.

Hechos documentados del corte 2026-10-03: ocho grupos, 39 carpetas/registros, cuatro archivos históricos por carpeta, casing Norgard/Treskal. Children: dos boys farmer, un boy y tres girls village_child; elderly: village_elder. No se certifica el estado actual de originales. La auditoría no contiene el inventario nominal completo; P6.7 no lo reconstruye ni consulta otro repositorio.

El perfil v4 vigente exige culture/location/role/sex. ValidateClassification valida presencia/permisión de dimensiones, no un vocabulario de valores. Manifest v2 valida Naming v1, sin inferencia. El resolver productivo exige package y repositorio validados. P6.7 compartirá únicamente su cálculo interno de segmentos relativos; no fabricará capacidades ValidatedAssetPackage ni ValidatedProductionRepository ni calculará destinos absolutos.

Decisión: referencia cerrada embebida de 39 metadatos sintéticos, identificadores e identidades explícitamente ficticios. El perfil embebido procede del config v4 versionado. Reutilizar loader de Manifest, clasificación y cálculo de segmentos; las comprobaciones semánticas históricas se limitan a advertencias documentales, no a otro motor de validación. Ninguna lectura de archivos de assets, ZIP, raíces, host o registros reales. Propuestas como texto, sin comandos ejecutables.

Interfaz secundaria en Ajustes, cinco secciones principales intactas. Cambio de contexto limpia selección/filtros; el título identifica siempre referencia fija Treskal, independiente del universo operativo. Exportación mediante DiagnosticReportWriter de P6.5: .txt nuevo, límite 256 KiB, rechazo de enlaces/overwrite y raíces configuradas/administrativa. Exportar solo proyección cerrada de fixtures, sin texto privado.

Disparadores revisados: ci.yml solo main/PR a main; cutover-laboratory.yml dispatch explícito; phase6-review.yml push de rama y paths focalizados. Se adaptará únicamente el workflow focalizado a pruebas P6.7 y renders sintéticos. #47/#49 abiertas; main 5f81950275098881fd0befac0a13cde08c9d751f. No se alteran incidencias, producción, permisos, licencia, installer/updater/releases ni P6.8.
