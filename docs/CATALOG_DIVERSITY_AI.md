# NAP 2.0 — Inteligencia de catálogo y diversidad visual

**Fecha:** 2026-11-13  
**Estado:** implementado en rama de desarrollo, no certificado ni fusionado.  
**Alcance:** lectura de SQLite y consulta humana opcional a Gemini, separada de la auditoría obligatoria de paquetes.

## Objetivo

NAP debe responder a preguntas como:

- «¿Cuántos retratos de farmer, fisher, blacksmith y carpenter tenemos?»
- «¿Hay mucha menos variedad de carpinteros que de herreros?»
- «¿Qué distribución de sexos y grupos de edad está documentada?»
- «¿Qué combinaciones de profesión y edad están poco representadas?»
- «¿Qué perfiles y territorios tienen menos variedad visual registrada?»

El resultado se refiere a la **diversidad de assets**, no al censo ficticio del universo. Ningún número se interpreta como excedente de población ni como cuota obligatoria. Un valor no registrado es **desconocido**, nunca cero inventado.

## Arquitectura

1. `CatalogDiversityReader` consulta **únicamente SQLite ya existente** en el universo activo, con acceso de lectura, usando el `AssetCatalog.Read` y la frontera de archivos de NAP.
2. Se leen de `assets` `asset_type` y `production_profile`, de `classifications` las dimensiones canónicas, y de `visual_traits` únicamente los rasgos escalares de texto extraídos de `_visual_identity.json` por el catálogo.
3. `Aggregate` calcula recuentos por categoría, cobertura por dimensión y cruces **reales por asset**. No multiplica márgenes de categorías, no incluye otros universos, no lee PNG/WebP/documentos y nunca altera los archivos.
4. Se señalan oportunidades relativas cuando una categoría **registrada** tiene cuatro veces menos ejemplares que la categoría de mayor representación en su misma dimensión, si esta última tiene al menos ocho ejemplares. El umbral es una **heurística revisable**, no una regla demográfica ni una sentencia de la IA. Si una dimensión solo tiene una categoría no puede descubrir otras inexistentes en el índice.
5. `CatalogDiversitySnapshot.ToJson` exporta hechos agregados, cruces y limitaciones de procedencia; no contiene IDs individuales, caminos, documentos, hashes, imágenes ni SQL.
6. `GeminiCatalogInsightClient`, independiente de `GeminiOperationAuditClient`, recibe exclusivamente ese JSON y una pregunta humana. Responde en castellano mediante `Interactions v1` con `store=false`, `stream=false`, `background=false` y herramientas deshabilitadas. Sus `evidence_ids` son validados contra el snapshot local: NAP presenta los recuentos reales, aunque la explicación de Gemini sigue siendo orientativa.
7. El usuario accede por **Consultar IA**, sin alterar el flujo de «Preparar → Auditar plan → Publicar asset». Si Gemini no está configurado, el informe local sigue disponible y la pregunta queda deshabilitada.

## Datos de partida y limitaciones

- SQLite ya guarda exactamente lo que los manifests y JSON de identidad aportaron al indexar assets físicamente verificados. La IA no inspecciona imágenes visualmente en esta función.
- Para consultas sobre profesiones se necesitan valores como `classification:role` (`farmer`, `carpenter`, etc.) o rasgos textuales inequívocos en `_visual_identity.json`.
- Para edad hacen falta **grupos explícitos** (por ejemplo, `/age_group` o `/age_range`); la ausencia de ellos **no se sustituye por suposiciones visuales**. Los números de edad sueltos no se transforman automáticamente en grupos.
- Sexo registrado, grupo de edad aparente, cultura y profesión pueden cruzarse únicamente cuando están presentes **en el mismo asset**.
- Los activos externos que no están catalogados en SQLite quedan fuera. No se escanean todos los archivos del disco ni se confunde falta de registro con ausencia real de imágenes.
- El análisis es genérico por universo: no presupone que `portrait_npc` sea el único perfil; cartografía y escenas ambientales también cuentan, con sus metadatos existentes.
- No hay lectura de SQL aportado por el usuario, ni ejecución de código generado por Gemini, ni permisos de escritura.
- Límite operativo inicial de 50.000 assets verificables por informe, 12.000 categorías, 12.000 cruces y 512 KiB de JSON para la IA. **Si se exceden, se detiene el análisis:** no se presenta una selección parcial como si fuera el total.
- La respuesta de Gemini es una **interpretación no vinculante**. Para decidir la siguiente producción deben considerarse la biblia visual y la intención artística del autor.

## Ejemplo de interpretación

Si la base registra 56 `blacksmith`, 45 `fisher`, 54 `farmer` y 2 `carpenter`, el sistema puede indicar que `carpenter` está comparativamente poco representado **entre profesiones registradas**. No afirma que «sobren» 56 herreros ni que se deban crear exactamente 54 carpinteros.

Si hay retratos con `classification:sex` y `visual:/age_group`, las consultas cruzadas muestran combinaciones realmente presentes; si falta `age_group`, la herramienta advierte de que esa dimensión no está suficientemente documentada.

## Código y pruebas

- `src/NAP.Core/CatalogDiversity.cs`: lectura inmutable y hechos agregados.
- `src/NAP.AI/GeminiCatalogInsightClient.cs`: interpretación opcional sin herramientas.
- `src/NAP.Presentation/ProfessionalPages.cs` y `ShellViewModel.cs`: estado y consulta.
- `src/NAP.App/Views/CatalogIntelligenceView.xaml`: interfaz.
- `tests/NAP.Tests/CatalogDiversityTests.cs`: conteos, cruces, ausencia de campos y SQLite real.
- `tests/NAP.Tests/GeminiCatalogInsightClientTests.cs`: solicitud stateless, evidencias y errores.

**Certificación pendiente:** el repositorio de desarrollo no tiene un SDK .NET ejecutable en este entorno; antes de fusión se requiere al menos `dotnet build NAP.sln -c Release` y los filtros focalizados correspondientes en Windows. **No activar ni modificar la CI general de NAP sin autorización adicional.**
