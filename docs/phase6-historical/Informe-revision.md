# NAP 2.0 — P6.7: homologación histórica sintética de Treskal

Fecha solicitada: 23/10/2026. Ejecución iniciada el 22/10/2026 UTC. Rama existente `codex/nap-2-fase-6-ux`; base documental `e0ef903`, código previo `55538c7`. Entrega para revisión humana. P6.8 no iniciada.

## Resultado y uso

Ajustes → Histórico sintético incorpora una herramienta RobStyle independiente del host y de las raíces de assets. Conserva los cinco accesos principales de NAP. Permite consultar 39 referencias en ocho grupos, filtrar por grupo y estado, buscar referencia o identidad ficticia, abrir el detalle y examinar cada incidencia. Restablecer filtros y cambiar el universo operativo limpia filtros y selección; la herramienta identifica siempre su referencia fija de Treskal. Escape o Cerrar detalle cierra la revisión.

El detalle separa identidad/clasificaciones sintéticas, ruta histórica modelada y ruta canónica propuesta; conserva el identificador ficticio entre ambas. Las incidencias presentan evidencia y propuesta documental, sin botones de aplicación, aprobación, reanudación, publicación ni migración. Se cubren ausencia de metadata, incertidumbre de identidad, rol desconocido, alternativas ambiguas, documentación incompleta, semántica incompatible y posible duplicación sintética.

Exportar simulación genera un informe .txt completo de las 39 referencias, aunque haya filtros activos. Siempre exporta la referencia cerrada, nunca datos del universo operativo. Mantiene las garantías de P6.5: archivo nuevo sin overwrite, máximo 256 KiB, fuera de las raíces configuradas Workspace/Production/Archive y del directorio administrativo, sin atravesar enlaces. No modifica sus fuentes. Los identificadores exportados pertenecen al fixture sintético explícito; no son identificadores privados extraídos de logs.

## Hechos históricos y datos simulados

Fuente documental: `docs/NIMROEL_HISTORICAL_STRUCTURE_AUDIT.md`, corte del **03/10/2026**. Documenta 39 registros/carpetas y cuatro archivos por asset: WebP, prompt.md, info.md y visual_identity.json. Distribución: farmer 10; carpenter 5; blacksmith 5; healer 3; children 6; elder 6; fisher 2; tavernkeeper 2. Estos hechos no certifican el estado actual de Nimroel ni convierten la estructura histórica en un ZIP nuevo.

La auditoría describe children/boy|girl: dos boys farmer, un boy y tres girls village_child, sex male/female. Elder corresponde a village_elder. La ruta canónica permanece `portraits/{culture}/{location}/{role}/{sex}/{asset_id}` con `norgard/treskal`; el árbol histórico usaba `Norgard/Treskal`. No se deduce semántica desde identificadores, carpetas ni apariencia.

Las **39 filas individuales son ficticias**. No se reconstruye el inventario nominal de originales. Identificadores `portrait_p67_reference_NNN`, etiquetas de identidad, clasificaciones individuales, rutas modeladas, datos ausentes e incidencias son sintéticos. La pareja de posible duplicación comparte una clave artificial de identidad; no se han comparado imágenes ni hashes reales. Las anomalías inyectadas no se atribuyen al corte histórico, que documentaba los cuatro archivos presentes.

Estados del fixture: **28 Conforme en simulación**, **10 Necesita revisión documental**, **1 No verificable sin originales**. Conforme significa únicamente validación de metadata sintética en ese alcance. Todos los registros mantienen «No verificable sin originales · sin homologación para producción» para su condición real.

## Arquitectura y seguridad

Core carga una referencia JSON cerrada y el perfil Nimroel v4 embebido desde el config versionado. No recibe rutas de entrada, imágenes, paquetes ni metadata libre. Reutiliza `AssetManifestV2Loader.Load(Stream)`, `UniverseAssetRule.ValidateClassification` y el cálculo de segmentos extraído de `ProductionDestinationResolver` a un método interno común. No se construye un segundo motor de validación ni se crean capacidades `ValidatedAssetPackage` o `ValidatedProductionRepository`.

El resolver productivo conserva su API pública y sus comprobaciones de universo, Naming, segmentos reservados y contención. La simulación devuelve solo texto relativo, sin ProductionRoot ni rutas absolutas de destino. Las comprobaciones históricas son advertencias documentales específicas: el perfil actual verifica dimensiones y Naming, pero no certifica un catálogo universal de valores de role/sex. No se presenta un valor desconocido como profesión real.

No se llama al validador de paquetes sobre carpetas históricas ni al procesamiento/normalización de imágenes. No se crean maestros, manifests históricos, ZIP de assets, carpetas canónicas, registros, aprobaciones ni operaciones de producción. No se escanea Videojuego_Nimroel ni se accede a su raíz real. El código de WPF solo añade consulta de fixtures y exportación segura de texto.

## Verificación y procedencia

**Código fuente efectivamente verificado: `f3130ce7144644fb4179920c0332371ed0c5991c`.** [Workflow Windows 39870877507](https://github.com/robdor80/NAP/actions/runs/39870877507). Ambos builds Release aprobados; **108/108 pruebas Passed, cero fallos y cero omisiones**: 14 casos P6.7, 93 casos pequeños del resolver y un caso de navegación sin configuración. **34 PNG originales**, 17 en cada resolución, 18 procesos con stderr vacío, sin errores de binding. Los cinco advisories NU1902/NU1903 existentes permanecen visibles.

Evidencias preliminares preservadas: c49c9e4 (run 39870109208), f0f4ca3 (39870425854) y 329b3ae (39870560823), cada una con 108 pruebas Passed y 28 PNG. No hubo fallos de compilación o tests en estas ejecuciones. Los refinamientos posteriores documentan agrupación, etiquetas y capturas inferiores de incidencias, sin atribuir los resultados anteriores al SHA final. Los commits documentales de entrega posteriores conservan exactamente el producto verificado y no se presentan como nuevas ejecuciones.

La verificación se limita a pruebas pequeñas con metadata en memoria y fixtures temporales. Las pruebas existentes del resolver usan datos artificiales y un caso de validación pública de un PNG mínimo sintético, sin ZIP ni assets reales. No se ejecutan suites generales, miles de pruebas, pruebas de volumen ZIP, estrés, benchmarks, proveedores de IA ni CUTOVER. El entorno local Linux no dispone de dotnet; los builds y renders se ejecutan en el runner Windows, con el helper oficial de licencia existente. No se modifica el archivo privado local ni se suprimen advisories ImageSharp.

Los PNG son renderizados del árbol WPF de producto, fuera de pantalla, a 1440×900 y 1200×700, con adaptadores sintéticos que rechazan mutaciones. Cubren inventario, children, elder, ambigüedad, posible duplicado, no verificable, filtros vacíos, cambio de contexto y exportación. Incluyen detalle desplazado y verificaciones de agrupación/navegación y ausencia de errores de binding. No son capturas del MSI ni certificación operativa o de accesibilidad completa.

Los disparadores de Actions se revisaron antes de cada push de producto. CI general sigue limitado a main/PR hacia main, CUTOVER al dispatch con autorización explícita y la revisión focalizada a esta rama y sus paths. No se abre PR ni se fusiona. Se conserva la evidencia preliminar con su SHA y resultado, sin atribuirle la verificación del código posterior. El intento de transferencia CLI de artefactos/logs fue rechazado por la política de red; se utilizó el conector GitHub autorizado, sin alterar la política.

## Entrega, límites y revisión humana

La entrega incluye Markdown/PDF de revisión, galería PDF y Markdown, PNG originales en ambas resoluciones, inventario documentado y fixture JSON, informe de simulación/anomalías, TRX, logs, hashes, procedencia y procedimiento de homologación futura. Se conservan en GitHub y en Drive privado bajo `NAP 2.0/Fase 6/P6.7 Homologación histórica sintética`, sin cambiar permisos. Los enlaces privados de Drive se facilitan al responsable y no se incorporan al repositorio.

[Homologación real futura](Homologacion-real-futura.md) documenta, sin ejecutar, certificación del entorno, backup verificado, inventario actualizado, identidad y metadata, rutas, duplicados/colisiones, propuesta de correcciones, aprobación humana explícita, un solo retrato piloto, verificación posterior y recuperación. La adopción real necesita otra autorización y un proceso compatible con Core; los cuatro archivos históricos no satisfacen por sí mismos el contrato de nuevos paquetes.

Queda pendiente la revisión humana de diseño, uso por teclado, desplazamiento y exportación en el MSI. No hay originales que permitan certificar identidad, integridad, duplicación o compatibilidad productiva. El prototipo no verifica el entorno productivo ni resuelve las condiciones operativas de #47 o los riesgos bajo seguimiento de #49. Los avisos de seguridad ImageSharp permanecen visibles.

**Parada obligatoria al concluir P6.7.** Main, CUTOVER y #47/#49 permanecen intactos; sin PR, fusión, producción, installer, updater, Releases ni P6.8. Ningún asset real ha sido utilizado.
