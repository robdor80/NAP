# NAP 2.0 — Auditor de Diversidad Visual v1

**Fecha:** 13/11/2026. **Universos:** aislados por configuración. **Estado:** implementación propuesta en rama, compilación Windows pendiente.

## Objetivo

Explorar la variedad de la biblioteca de assets y responder preguntas como «¿Cuántos campesinos, pescadores y carpinteros tenemos?» o «¿Faltan retratos de mayores, mujeres o profesiones concretas?». No busca determinar las necesidades demográficas reales del mundo ni declarar que «sobran» NPC.

NAP calcula las cifras; Gemini interpreta únicamente un JSON agregado con identificadores de evidencia. La IA no recibe SQL ni datos de escritura, imágenes, documentos completos, prompts, claves o rutas físicas.

## Alcance de SQLite

- AssetCatalog.db v1: publicaciones verificadas de **todos los asset_type y production_profile indexados**, con clasificaciones y rasgos escalares; lectura validada de solo lectura y aislada por universo.
- LegacyPortraits.db: índice histórico de Nimroel, si está presente. Contiene cultura, localidad, profesión, sexo y rasgos artísticos. Se contabiliza como procedencia histórica diferenciada.
- Si existe el mismo asset_id en ambas fuentes, prevalece el nativo. Duplicados dentro de una fuente son error.
- Los ZIP sin procesar del Inbox, archivos sueltos, bases de otros universos, originales no indexados y estado TeraBox no entran en los recuentos. «Todos los tipos» significa **todos los tipos de assets catalogados**, no todos los archivos físicos de Windows.
- No se cambia el esquema, no se generan migraciones ni se escriben datos al consultar.

## Dimensiones

- Tipo, perfil, cultura, reino, región, localidad.
- Profesión: clasificación role, o rasgos explícitos profession/occupation/role.
- Sexo: clasificación sex, o rasgos explícitos sex/gender.
- Edad: life_stage, age_group o equivalentes explícitos. No se deduce la edad a partir del nombre o de la imagen.
- Cruces: profesión × sexo, profesión × edad, sexo × edad, cultura × profesión, localidad × profesión, cultura × sexo, localidad × sexo, tipo × perfil.
- Rasgos opcionales aprobados: etapas de edad, edad aparente, ojos, pelo, complexión, barba, ciertos detalles de vestuario y ambiente. El resto del JSON libre no se envía a Gemini.

Los datos ausentes se marcan como «(sin dato)». Los ceros por profesión y sexo o profesión y edad solo se muestran si ambas categorías existen efectivamente en la biblioteca. Los grupos que nunca se han registrado no se inventan.

## Prioridad de variedad

Una heurística local señala categorías observadas que tengan, como máximo, la cuarta parte de elementos que la categoría más frecuente de la misma dimensión, siempre que la referencia tenga al menos 6 assets. Por ejemplo: 56 herreros, 45 pescadores, 54 campesinos, 2 carpinteros → recomendar mayor variedad de carpinteros. No representa cuotas obligatorias ni un número ideal.

## Frontera de Gemini

Se reutilizan NAP_GEMINI_API_KEY y NAP_GEMINI_MODEL en la sesión normal. El cliente usa Interactions v1 HTTPS con store=false, stream=false y background=false, sin herramientas externas. Solo envía la pregunta humana y el JSON de recuentos.

La respuesta está restringida a un resumen y observaciones. Cada observación debe citar identificadores de evidencia existentes; la aplicación valida esos IDs y añade los recuentos exactos, nunca los números generados por Gemini. Se rechazan IDs desconocidos, texto con dígitos o estructuras inesperadas. Esta validación reduce alucinaciones cuantitativas, pero no certifica la interpretación artística: continúa siendo una recomendación.

Se rechazan consultas por encima de límites explícitos: 50.000 assets, 3.000 filas de evidencia o 350.000 caracteres de JSON. No hay omisiones silenciosas. Los rasgos libres no aprobados o con valores sospechosos se excluyen de la exportación.

## Interfaz

**Herramientas → Auditor IA** incluye inventario y procedencia, avisos de variedad calculados sin IA, campo para preguntas, respuestas con hechos, limitaciones y «Copiar JSON». El usuario pulsa voluntariamente el botón para enviar la consulta a Gemini. Cambiar el universo invalida el informe anterior. El modo HOST remoto y el modo de aceptación no pueden usar esta conexión local.

No se altera el PASS/FAIL de auditoría de producción, el almacenamiento, la recuperación ni la publicación.

## Pruebas y certificación pendiente

La nueva suite CatalogDiversityTests cubre el ejemplo de profesiones, intersecciones, edades, sexos, metadatos ausentes, coexistencia con históricos, tipos distintos, SQLite real sin cambios de bytes, credenciales Gemini, respuesta sintética válida y negativa. Una prueba de menú anterior se actualiza para contemplar la nueva herramienta.

**No se ha ejecutado aún .NET Build ni xUnit en el MSI.** La PR debe permanecer Draft hasta obtener esa evidencia. La CI general NAP CI permanece expresamente desactivada y no se debe invocar. No se usa Codex.

## Contratos conservados

AssetCatalog schema v1, Manifest v1/v2, Universe Profile v4, cartografía, escenas ambientales, conversión, publicación, AI audit de ZIP, Git Outbox y los otros universos siguen sin cambios. No se modifica el repositorio Videojuego_Nimroel.
