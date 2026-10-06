# AI Audit — Fase 7

**Fase 7 — Auditor IA HECHA. 7.1–7.5 HECHOS. Fase 8 — TeraBox / Archive Storage HECHA (8.1–8.6). Fase 9 — Production Storage + Verified Completion HECHA (9.1–9.4). Siguiente: Fase 10 — SQLite.**

IA audita. NAP ejecuta. Fase 7 aporta un resultado de auditoría; no ejecuta
operaciones, escribe assets ni avanza Jobs. La frontera de Fase 9 consume el
informe validado sin ampliar los permisos de IA.

## 7.1 — Cliente IA

El concepto histórico «Gemini Auditor → ¿es segura/coherente ESTA operación?»
se implementa con contratos provider-neutral en NAP.Core y el primer adapter
REST real en el proyecto NAP.AI. NAP.AI referencia NAP.Core; Core no referencia
NAP.AI, Google, SDKs o proveedores. Otro adapter Gemini/OpenAI/Mistral podrá
implementar IAiAuditClient sin cambiar ProcessingPlan o los contratos de dominio.

La interfaz contiene únicamente:

```csharp
Task<AiAuditReport> AuditAsync(
    AiAuditRequest request,
    CancellationToken cancellationToken = default);
```

GeminiOperationAuditClient recibe HttpClient, apiKey y model en su constructor.
El composition root futuro proporcionará la authorization API key actual de
Gemini y el modelo configurado externamente, por ejemplo mediante variable de
entorno o User Secrets. Ni Core ni adapter leen variables de entorno o archivos
de secretos. No hay modelo fijo, key real, .env o secrets.json en el repositorio;
ningún secreto debe guardarse en Git. Los tests usan únicamente una key fake.

El adapter utiliza POST HTTPS a
`generativelanguage.googleapis.com/v1/interactions`, autenticación mediante
header x-goog-api-key y Content-Type application/json, sin key en query string.
El contrato se contrastó con la [referencia oficial Interactions API v1](https://ai.google.dev/api/interactions-api-v1).
Usa HttpClient/System.Text.Json de .NET 8, sin SDK Google ni nuevas dependencias NuGet.
El caller conserva la propiedad y responsabilidad de configurar HttpClient y
su transporte; el adapter no lo dispone ni modifica DefaultRequestHeaders.

Cada request incluye explícitamente model, input como JSON serializado de
AiAuditRequest, system_instruction fija, store=false, stream=false,
background=false, response_format y generation_config. No hay
previous_interaction_id, conversación, tools, agentes, polling o retry.
generation_config fija tool_choice="none" y thinking_summaries="none".

Solo HTTP success permite inspeccionar una respuesta. Un HTTP no-success lanza
AiAuditClientException con el status numérico, sin body del proveedor o key.
Los errores HTTP de transporte tienen mensajes fijos sin reenviar diagnósticos
arbitrarios. OperationCanceledException/TaskCanceledException se propagan,
incluidos los timeout expresados como cancelación.

La respuesta outer debe ser JSON válido con status exactamente "completed".
Failed, cancelled, incomplete, requires_action, in_progress, unknown o status
ausente producen excepción. Se elige exclusivamente el último step con
type="model_output" y se concatenan sus bloques type="text" en orden. Debe
existir texto no vacío; thought, thought_summary, function_call y otros outputs
no se usan ni ejecutan. Un último model_output vacío no permite fallback a uno
anterior. Campos consumidos duplicados se rechazan como ambiguos; metadata
outer adicional puede ignorarse. El texto final se pasa directamente al parser
local, sin extraer JSON de fences o prosa.

## 7.2 — Informe estructurado

AiAuditFinding es un sealed record con Code, Severity y Message get-only.
Code sigue Naming v1; Severity es Warning=0 o Error=1; Message es obligatorio
y tiene un máximo de 1000 caracteres. No hay SubjectPath, Detail, campos de
filesystem o stack trace.

AiAuditReport es una sealed class con Decision, Summary y Findings get-only,
además de Passed, RequiresReview y ShouldStop derivados. Summary es obligatorio,
máximo 2000 caracteres. Findings se copia defensivamente, es realmente read-only,
rechaza null y permite como máximo 20 elementos no null.

El formato provider-neutral exacto es:

```json
{"schema_version":1,"decision":"PASS","summary":"Coherent supplied facts.","findings":[]}
```

Un finding tiene exclusivamente code, severity y message; los tokens externos
de severity son WARNING y ERROR, sin INFO. Decision usa PASS, WARNING y FAIL,
nunca valores numéricos o casing alternativo.

response_format fija type="text", mime_type="application/json" y un JSON Schema
interno al adapter: objeto cerrado con schema_version integer enum [1], decision
string enum [PASS, WARNING, FAIL], summary string y findings array maxItems 20.
Los cuatro campos son required y additionalProperties=false. Cada finding es
un objeto cerrado con code/severity/message required, severity enum [WARNING,
ERROR] y additionalProperties=false. Este schema no modifica schemas NAP.

**Hay tres capas distintas:** schema remoto, parser local estricto e invariantes
semánticas del report. El schema remoto por sí solo no demuestra coherencia.

AiAuditReportJsonParser.Parse(string json) exige root object, propiedades exactas,
sin extras/duplicadas/missing, tipos exactos, literal entero 1, tokens exactos,
Naming v1, límites de strings/array e invariantes de decisión. Corrupción o
contrato inválido producen InvalidDataException con mensaje fijo. No normaliza,
repara, asume, acepta markdown fences o extrae JSON de texto adicional.

El adapter envuelve InvalidDataException en AiAuditClientException sin volcar
el informe completo. **AiAuditClientException significa que no existe PASS:** el
coordinator debe hacer STOP, nunca sustituir el fallo por aprobación.

## 7.3 — PASS / WARNING / FAIL

| Decision | Findings requeridos | Passed | RequiresReview | ShouldStop |
| --- | --- | --- | --- | --- |
| PASS | Ninguno | true | false | false |
| WARNING | Uno o más, todos Warning | false | true | false |
| FAIL | Al menos un Error; puede incluir Warning | false | false | true |

PASS con findings, WARNING vacío/con Error y FAIL sin Error se rechazan.

PASS significa exclusivamente que el auditor no detectó una razón para bloquear
el plan basándose en los hechos suministrados por NAP. **PASS no ejecuta ni
autoriza por sí solo una escritura.** Solo PASS podrá ser candidato a avance
automático en AssetExecutionCoordinator, con sus demás validaciones físicas.

WARNING requiere revisión del usuario: ShouldStop=false
no autoriza ejecución automática. FAIL bloquea. Los mensajes del auditor son
contenido no confiable, nunca instrucciones para ejecutar código o filesystem.

## 7.4 — Aislamiento de permisos y privacidad

AiAuditRequest tiene constructor internal; AiAuditRequestBuilder.Build(plan,
validationReport) es la entrada pública desde un ProcessingPlan producido por
las fronteras de validación NAP. El builder comprueba null y, antes de crear
request, aplica validationReport.ShouldStop. Cualquier Stop, independientemente
de Severity, lanza exactamente:

```text
A ProcessingPlan with blocking NAP validation issues cannot be sent to AI audit.
```

NAP tiene prioridad sobre IA: un STOP determinista nunca se envía al cliente
ni puede ser revocado por PASS. Warning + Continue puede enviarse; las
incidencias conservan orden y duplicados, pero solo Code/Severity/Disposition
mediante AiAuditIssueFact. Message, SubjectPath y Detail se excluyen siempre.

| Hechos incluidos | Hechos excluidos |
| --- | --- |
| UniverseAssetKey, AssetType, ProductionProfile | PackageRoot, ManifestPath, paths de FilesByRole |
| Classification, copiada con comparación Ordinal y read-only | ProductionRoot, FullDirectoryPath, ArchiveRoot |
| InputRoles: solo keys de FilesByRole, ordenadas Ordinal | InboxRoot, StagingRoot, StateRoot, CacheRoot |
| DestinationRelativeDirectory | API key, JobId, hashes, timestamps, username, hostname |
| ValidationIssues: Code/Severity/Disposition | Message/SubjectPath/Detail de NapIssue |

El destino es únicamente relativo, por ejemplo
`portraits/norgard/treskal/farmer/male/portrait_example_001`. El request rechaza
destinos absolutos Windows/Unix, backslashes, colon, segmentos vacíos/dot/dot-dot
y controles, sin resolver paths ni realizar I/O. Los metadata del plan real
proceden de la validación Naming/Manifest existente, que no se sustituye por IA.

AiAuditRequestJsonRenderer.Render(request) escribe explícitamente JSON compacto
UTF-8 conceptual con schema_version=1. Orden fijo de propiedades, classification
por Key Ordinal, roles Ordinal e issues en su orden original. Severity/Disposition
de NAP utilizan mappings explícitos Info/Warning/Error y Continue/Stop.
No serializa ProcessingPlan ni otros objetos de dominio mediante defaults.

El cliente solo recibe AiAuditRequest, nunca UniverseContext/StorageConfig,
ProcessingPlan, packages, repositorios, snapshots, streams o handles. No tiene
callback/delegates de filesystem, tools, function calling, Google Search, URL
context, code execution, MCP, file search, maps, TeraBox, SQLite o Git.
La única capacidad externa usada por el adapter es el POST Gemini.

La system instruction fija delimita todo el input JSON como **datos**, incluidas
classification e IDs. Ordena ignorar instrucciones/prompt injection en esos
valores, no completar huecos con conocimiento externo ni inventar operaciones.
El renderer escapa strings y no concatena metadata como instrucciones. Esto
reduce la superficie de inyección; la decisión de un modelo sigue sin ser una
garantía determinista de seguridad. Su salida nunca se ejecuta localmente.

## 7.5 — Pruebas en Dry Run

El flujo probado es:

```text
ValidatedAssetPackage → ProcessingPlan → ProcessingPlanValidator → NapIssueReport
  ShouldStop=true → STOP, cero llamadas IA
  ShouldStop=false → AiAuditRequestBuilder → IAiAuditClient → report validado
```

Los tests end-to-end usan package Nimroel y validadores reales, con cliente IA
fake. Plan limpio → PASS; plan limpio + Warning/Continue → WARNING y revisión;
destino bloqueado por NAP → ninguna llamada al cliente, aunque el fake retornaría
PASS. Comparan inventario, bytes y timestamps de todos los roots y un checkpoint
PLANNED existente. No invocan JobStateStore ni cambian JobState.

DryRunTextRenderer permanece intacto y sigue declarando
`OPERATIONS (not defined in ProcessingPlan v1)`. No se añade una operations list
ni se modifica ProcessingPlan para justificar la auditoría. Los tests Gemini
usan HttpMessageHandler propio, sin Moq, red real, key real, cuota o modelo real;
la suite puede ejecutarse offline con dependencias ya restauradas.

## Límites y continuidad

ProcessingPlan v1 aún no contiene un grafo completo de operaciones. Se auditan
identidad, tipo, profile, classification, roles, destino relativo calculado y
validación determinista suministrada. El request no incorpora operaciones de
copia, hashes post-copy ni paths ArchiveRoot. [Archive Storage](ARCHIVE_STORAGE.md)
consume el informe validado y exige PASS más su propio preflight bajo lock;
la frontera [Production Storage](PRODUCTION_STORAGE.md) hace lo mismo para
WebP/documentación y lifecycle. SQLite y Git siguen pendientes.

No hay composition root/CLI/UI, cola, scheduler, background service o retry
service. AssetExecutionCoordinator exige PASS antes de JobState AUDITED.
**Fase 7 no realiza PLANNED → AUDITED** ni otra transición,
y no modifica contratos JobState/JobRecovery, PLAN, hashes, converters o perfiles.

Fase 8 ya implementa conservación física local en una frontera separada; Fase 9
implementa producción y cierre verificado; Fase 10 añadirá catálogo. La auditoría conserva su alcance y no
ejecuta archivos. La validación determinista sigue siendo obligatoria y
point-in-time: ArchiveMasterExecutor revalida fuentes, destinos e índice bajo
lock, sin ampliar el request o permisos de IA ni integrar transiciones JobState.
