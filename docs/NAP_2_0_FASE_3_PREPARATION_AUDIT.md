# NAP 2.0 — Fase 3: preparación reutilizable y auditoría durable

Fecha inicial: 2026-10-10. Actualización PreparationOnly: 2026-10-11. Base verificada: `f8e58dab0eae3c3e0827b8116b7da856da0aedbd` (`main`, merge #44, con #43). Rama: `codex/nap-2-fase-3-preparation-audit`. Informe de validación local previo a publicación.

## Estado de entrega y resolución de PreparationOnly

**El bloqueo de autorización queda resuelto mediante la decisión expresa del responsable del 2026-10-11.** Se incorpora `PreparationOnly` como capacidad persistente y versionada, limitada a operaciones preproductivas. La preparación, preservación, normalización autorizada y auditoría durable se han probado con fixtures sintéticos y proveedor simulado. La entrega queda preparada para auditoría y aceptación humana; este informe recoge la validación anterior a su publicación. La Fase 4 no se ha iniciado.

`Disabled` permanece como default. `Enabled` continúa rechazado tanto al escribir como al leer el ledger; `IsExecutionAuthorized` sigue devolviendo false. Los ocho grupos que estaban omitidos por el bloqueo ya se ejecutan con PreparationOnly. Registrar una autorización o unos presupuestos no activa ninguna política por sí solo. No se activa ningún universo real ni se simula autorización humana o PASS.

Este documento pertenece al plan NAP 2.0 de siete fases. No altera la historia de las Fases 0/0B/1/2 ni implementa ejecución productiva de Fase 4.

## Componentes y límites

| Componente | Responsabilidad implementada |
| --- | --- |
| `AutomationPolicyContracts` / `AutomationQueueLedger` / `AutomationQueueEvidence` | PreparationOnly, CAS, autorización/versiones/raíces y validación fail-closed de fila e historial; Enabled continúa prohibido. |
| `PackagePreparationService` | Semántica, roles, PNG/geometría, resolución de conversión, validación y planificación de producción/archivo **en lectura**. Reutiliza motores existentes. |
| `ProfessionalUiService` | Delega preparación determinista en Core; mantiene staging, extracción y journal manuales. Incluye advertencias de archivo en el reporte para IA. |
| `DurablePreparationService` | API explícita sin scheduler/UI mutable; recibe UniverseContext y AutomationQueueStore del mismo ámbito. Conserva item, intento y Job preasignados. |
| `UniverseProfileSnapshotRenderer` | Congela perfil declarativo completo y lo relee mediante UniverseProfileLoader; clasificación, archivos/validadores, routing y conversión. |
| `PreparationStateStore` | Ledger SQLite independiente, CAS, intenciones/checkpoints, historial encadenado, términos operativos y previews. |
| `PreparationFiles` | Inventarios, publicaciones sin sobrescritura, verificación de bundles y correspondencia ZIP/extracción. |
| `DurablePreparationNormalization` | Conservación, preview, excepción individual, derivación con motor existente, procedencia y revalidación completa. AuthorizedPolicy exige PreparationOnly vigente y parámetros operativamente validados. |
| `DurablePreparationAudit` | Solicitud/receipts vinculados, presupuesto, timeout/retry/circuit breaker y recuperación de respuesta ligada. Ejecución explícita por PreparationOnly; decisiones, errores y cortes probados con IA simulada. |
| `WorkflowAiAuditRequestBuilder` | Envelope v2 independiente; conserva API pública y construcción del builder/request manual v1. |
| `InboxAdmissionService.VerifyAdmitted` | Handoff verificable, en lectura, de un receipt final Admitted del universo esperado. |
| `AutomationQueueStore.RecordPreparationProgress` | Evento y revisión transaccionales sin reemplazar la evidencia de admisión del intento. |
| `GeminiOperationAuditClient` | Identificación mediante interfaz explícita, errores tipados, Retry-After y límite de respuesta de 1 MiB; conserva API pública manual y parser v1. |

No se componen AssetExecutionCoordinator, ejecutores de archivo/producción, conversión WebP, catálogo, host continuo ni Git productivo. No se añaden dependencias ni perfiles. No se modifica `profile.json`, journal v1 ni schema del catálogo/AutomationQueue.db.

## Autorización PreparationOnly y compatibilidad

Se añade el valor `PreparationOnly` al final de AutomationPolicyState, conservando los valores existentes y su serialización. No se cambia el DDL de AutomationQueue.db, su versión ni el journal v1. Los lectores anteriores que no conocen el nuevo enum fallan de forma cerrada; no se promete que interpreten una política nueva como Disabled.

La secuencia explícita es SavePolicy → RecordAuthorization → SetPolicyState(PreparationOnly). La activación usa CAS y confirma estado e historial en una transacción. Requiere la **versión/hash actuales** de la política, autorización exacta del mismo universo y las raíces incluidas en la definición inmutable. `Admit` delimita los paquetes admitidos cuya preparación se permite; `Normalize` y `Audit` se comprueban por separado. La definición debe contener Admit y únicamente Admit/Normalize/Audit. **Cualquier bit Archive, Produce o Register invalida PreparationOnly**, aun con autorización registrada. No se añaden permisos de host continuo ni Git, ni se invoca un ejecutor productivo.

Cambiar raíces, operaciones, límites o presupuestos modifica el hash; se requiere una versión nueva y autorización nueva. Un snapshot de versión antigua no puede activarse si dejó de ser current. `IsPreparationAuthorized` es una propiedad calculada, no un campo persistido ni una autorización transferible fuera de su queue/contexto; los consumidores también comprueban current policy, item/intento, raíces y autoridad. El lector verifica esos invariantes tanto en la fila actual como en cada entrada del historial, conservando la prohibición de Enabled.

Paused y Revoked invalidan normalización/auditoría, bloquean creación/reanudación de previews incluso con decisión humana y detienen preparación antes de preservar o avanzar el journal. Revoked es terminal. La pausa puede levantarse mediante otra transición explícita a PreparationOnly para la misma política vigente y autorizada. Un item aparcado conserva evidencias y requiere reanudación individual mediante los contratos existentes: el cambio de política no consume ni reanuda la cola automáticamente.

Por compatibilidad, la API explícita de preparación determinista existente sigue siendo invocable bajo Disabled: conserva/valida/planifica un paquete ya admitido sin IA ni normalización por política, y no constituye un worker automático. La excepción HumanDecision conserva su autorización individual distinta. La nueva capacidad PreparationOnly habilita las operaciones de normalización por política y auditoría durable que estaban bloqueadas; no se cambia el pipeline manual de la UI ni se introduce un scheduler.

## Preparación y snapshots

`Prepare(owner, itemId)` solo procesa un item Queued con intento activo y receipt Admitted ligado en Fase 2. Revalida scope, receipt, SHA/tamaño del snapshot, inventario extraído y central directory/CRC/bytes de cada entrada del ZIP. No usa el archivo cambiante de Inbox como fuente de preparación. Receipt individual alterado aparca el item; corrupción del ledger compartido bloquea el ámbito.

Se conserva la terna QueueItemId/WorkflowAttemptId/JobId. El perfil base queda vinculado al intento sin reemplazar sus rutas ni ExpectedEvidence de Fase 2. Los originales se preservan antes de derivar. La validación normal se repite con el contexto efectivo y el candidato, incluida decodificación PNG estático, límites de píxeles y presupuesto de memoria.

Un ratio correcto no exige resolución nativa igual a WebP. El resolver conserva las dimensiones finales declaradas; no se genera ningún WebP. La planificación verifica destinos y archivo existentes, sin adoptarlos por coincidencia. Solo se acepta un plan de archivo nuevo completo; destinos ocupados o evidencia incompleta quedan en revisión.

Antes de transiciones del journal se persisten ValidationIntent y PlanningIntent. Se avanza el mismo Job STAGED → VALIDATED → PLANNED. Un corte tras la transición y antes de confirmar el checkpoint permite comprobar el estado esperado y continuar. Un journal adelantado sin intención propia no se adopta. No se marca AUDITED, COMPLETED ni publicación física.

El snapshot final incluye universo/raíces, política, receipt de admisión, ZIP original, todos los archivos originales/efectivos, perfil base/efectivo completo, manifest, roles/hashes/tamaños, routing/destinos relativos, conversión, incidencias del plan/archivo, autoridad, geometría, excepción/preview y versión de motor. La identidad de motor incluye SHA de Core e ImageSharp, runtime y plataforma: cambios relevantes invalidan la coincidencia. Este criterio conservador puede requerir revisión tras actualizar binarios.

## Persistencia y orden de locks

Base independiente: `StateRoot/automation/preparation/PreparationLedger.db`, schema `user_version=1`.

| Tabla | Binding / garantías |
| --- | --- |
| metadata | Una fila con snapshot exacto de raíces y universo. |
| workflows | QueueItem PK; intento y Job únicos; revisión, payload canónico y SHA. |
| history | FK a workflows; PK item/revisión; payload/SHA y hash previo; mismo scope e identidad. |
| grants | Política hash PK; términos explícitos, inmutables, con hash propio. |
| previews | DecisionId PK; regla, parámetros, fuente/candidato, geometría y bundle ligados por hash. |

Microsoft.Data.Sqlite existente, transacciones, foreign_keys ON, synchronous FULL, journal_mode DELETE, pooling deshabilitado, inicialización por archivo privado/flush/rename. Open verifica schema exacto, metadata, quick_check, FK, filas e historial. SQL indexado y contenido deben coincidir. No se reconstruye un DB corrupto. Sidecar SQLite inesperado, incluido hot journal después de una caída, exige revisión segura; no se promete reparación automática ni recuperación tras pérdida eléctrica.

CAS impide actualizar una revisión obsoleta. Identidad, intenciones ya ligadas y snapshot final son inmutables; checkpoints no retroceden. Límites de formato: 2 MiB por payload, 1.024 revisiones por item, 100 llamadas IA por workflow, 100.000 workflows por inspección. Un límite impide continuar, no descarta evidencia.

El propietario de cola conserva afinidad de hilo. Las APIs son síncronas para no llevar ese propietario a continuaciones de otro hilo. Orden: owner de universo → mutex corto de DB (`AutomationQueueIO`/`PreparationStateIO`) o Job. No se mantienen mutex de DB durante HTTP ni se solapan las transacciones de ambos DB. Primero se confirma evidencia en el ledger preparatorio; después se registra progreso en la cola. No es una transacción distribuida: los checkpoints ligados son autoridad de recuperación, los eventos de cola son proyección de progreso.

El item preparado permanece Queued con reserva AssetKey, sin claim de ejecución productiva. Reintentos IA viven en PreparationRecord.RetryAtUtc; no se fuerza QueueState.RetryScheduled, que hoy pertenece a Running/claim. Fase 4/5 deberá componer ese handoff conservando reservas y evidencias.

## Identidades y deduplicación

Se distinguen SHA del ZIP de transporte, hash semántico, AssetKey y hash del maestro. El hash semántico incorpora identidad del asset, metadata/clasificación normalizada y hashes/tamaños/nombres de todos los companions; excluye timestamps/compression del ZIP y formato textual equivalente del manifest. No iguala dos PNG por apariencia visual.

Reempaquetado idéntico con el mismo AssetKey vincula DuplicateOf al item primario que tiene reserva y originales verificables. No adquiere una segunda reserva ni lleva su Job de admisión STAGED a PLANNED. Fase 2 ya asignó un Job distinto al ZIP con otros bytes; se conserva ese registro, sin convertirlo en segundo trabajo productivo. La comprobación se repite al recuperar, incluso si ya se guardó SemanticHash antes del corte.

Mismo AssetKey con contenido distinto → AssetCollision. Distinto AssetKey con el mismo maestro → MasterDuplicate por defecto. La excepción de duplicado exige PreparationOnly efectivo y términos operativos que lo permitan; por defecto permanece en revisión. No se adoptan assets existentes. Cada universo utiliza su DB, reservas, almacenamiento, contexto y raíces; identidad igual en otro universo es independiente.

## Originales, autoridad y normalización

Namespace provisional: `StateRoot/automation/originals/<QueueItemId>/`, con `received.zip`, `package/<AssetId>/...` y `control.json`. El control contiene operación, item, intento, Job, universo, binding de admisión e inventario. **El hash esperado del control se liga en SQLite antes de crear/copiar archivos.** Publicación desde temporal propio, CreateNew/flush, hashes/tamaños verificados y rename sin sobrescritura. Un final solo se reutiliza si coincide con la intención durable y todo su inventario; el nombre/hash suelto no basta. Temporales y archivos desconocidos se conservan para revisión.

Los candidatos se publican en `StateRoot/automation/preparation/packages/<QueueItemId>/<PreparationOperation>/`, independientes del original; candidate.zip y archivos extraídos quedan verificados mutuamente. NormalizationLineage de Fase 1 liga ZIP recibido → maestro preservado → autoridad/parámetros → PNG/ZIP derivados → intento/Job/evidencia. Se registran Proposed antes de publicar y DerivativeDeclared después de verificar. Ningún hash sustituye la conservación física.

Se reutiliza ImageNormalizationEngine: mínimo lienzo entero, extensión nearest-edge, sin crop/resize/distorsión del original. Se comprueban área, eje, píxeles de entrada/canvas, bytes por archivo/paquete, entradas y memoria por separado. El candidato se revalida y se recomputa desde el original para verificar bytes/píxeles y geometría. Los presupuestos son conservadores; no equivalen a una cota certificada de working set del proceso.

**AuthorizedPolicy** requiere política PreparationOnly vigente/exacta, Normalize permitido, autorización registrada y parámetros OperationallyValidated por perfil. No se activan 3 %/1 % como defaults: aparecen únicamente en fixtures sintéticos explícitos. Sin esa autoridad se aparca RatioUnsupported/GeometryPolicyRequired. Canon productivo y perfiles existentes se conservan; narrativa/sellos/Star Trek no se inventan.

**HumanDecision** requiere propuesta/preview durable y decisión Approved SingleAsset con actor/fecha exactos. ProposeException permite dimensiones/ratio de una regla completa existente de la misma familia; no crea reglas ni cambia routing/roles/conversión de perfiles globales. El maestro debe ser el mismo original preservado. ResumeException revalida preview, actualiza solo regla efectiva del item y reanuda el mismo intento/Job. RevokeException aparca primero y persiste revocación: un corte intermedio queda cerrado. El historial de procedencia sigue legible después de revocar; no concede permiso actual.

Nunca se crea ApprovedByUser falso ni se cambia la lectura de receipts humanos v1. No hay UI de preview/editor. Perfil ausente cuya admisión de Fase 2 quedó NeedsReview **no se convierte en Admitted** por elegir ratio: esa resolución de admisión necesita un contrato compatible explícito; Phase 3 consume únicamente Admitted. Esta frontera queda documentada para revisión, sin saltarla.

## Auditoría durable: ejecución explícita y receipts verificados

Audit exige PreparationOnly vigente y primero reconstruye preparación/plan, por lo que STOP precede cada posible llamada. Exige Audit permitido, autorización efectiva y PreparationOperationalGrant explícito para la misma política/actor, provider/model, coste máximo por llamada, secuencia de delays y circuit breaker. Registrar esos términos con Disabled no habilita llamadas. Cambiar términos requiere nueva versión de política; no existen presupuestos globales inventados.

La solicitud v2 contiene hechos completos del workflow y hash del snapshot local completo. Las raíces absolutas y el actor permanecen localmente; se envían sus bindings hash, metadata, perfiles, incidencias, roles, hashes y tamaños, no píxeles ni bytes de ZIP. La ruta productiva sigue relativa. La solicitud manual v1 conserva su formato.

RequestIntent y RequestId se confirman antes de HTTP y consumen presupuesto aun si el resultado queda desconocido. Timeout acotado, errores tipados Transport/429/5xx/Timeout transitorios, 401/403/configuración permanentes, InvalidResponse sin PASS. Retry-After y delays se persisten; maxRequests/maxAttempts/maxCost son límites conjuntos del universo/política. El coste es una reserva superior configurada, no una medición de factura. Circuit breaker durable por grant; una credencial rechazada bloquea esa dependencia. No se reintenta WARNING/FAIL para buscar resultado favorable.

Al recibir un reporte validado se liga **el receipt completo y el hash de su publicación antes de escribirlo**. ResponseReady puede reconstruir/publicar exclusivamente esa respuesta ligada después de reinicio, sin otra llamada. Un corte antes de ligar la respuesta deja resultado desconocido, cargo reservado y retry limitado; no se garantiza exactly-once remoto. Un cliente que ignore cancelación puede seguir remotamente después del timeout; la llamada local no concede PASS.

La reutilización de PASS exige igualdad de scope, Job/intento, política/autorización, provider/model/grant, request y snapshot, verificación del receipt y validaciones frescas. Manifest/archivo/perfil/política/motor/destino/evidencia alterados lo invalidan. WARNING/FAIL aparcan el item; nunca sustituyen PASS.

La batería anteriormente bloqueada verifica las decisiones y la máquina durable con proveedor simulado. Un item en NeedsReview no puede devolver Planned ni un PASS anterior como resultado operativo, aunque su registro preparatorio conserve esos checkpoints. Revocación o pausa durante la respuesta impiden publicar/reutilizar el PASS; se conserva ResponseReady ligado para diagnóstico, sin permiso vigente ni transición productiva.

## Recuperación y evidencia

| Ventana | Resultado esperado / comprobado según pruebas |
| --- | --- |
| Intención original ligada, sin temporales/final | Recopiar snapshot/inventario verificado al mismo namespace. |
| Temporal incompleto o final no ligado | NeedsReview; sin borrar ni adoptar residuos. |
| Original publicado y ligado, checkpoint pendiente | Verificar control/inventario y continuar. |
| CandidateIntent ligado sin artifacts | Recomputar candidato determinista y comprobar inventario previsto. |
| Derivación final ligada, lineage/checkpoint pendiente | Verificar y completar evidencia del mismo intento/Job. |
| Journal VALIDATED/PLANNED con intención previa | Continuar el checkpoint; no crear otro Job. |
| Hash, scope, autoridad o perfil alterado | Incidencia individual; reservas/evidencias conservadas. |
| SQLite compartido corrupto / owner de otro hilo | Error tipado bloquea ámbito; no tratarlo como ZIP fallido. |
| RequestIntent sin respuesta ligada | Retry limitado con cargo reservado, comprobado sin repetir llamada al reabrir antes del plazo. |
| ResponseReady ligado | Publicar/verificar únicamente la respuesta ligada, comprobado sin otra llamada IA. |

## Verificación y resultados

Todas las nuevas pruebas usan ZIP/PNG/manifest sintéticos, directorios temporales y clientes/handlers IA simulados. Sentinels de ArchiveRoot/ProductionRoot se comparan por ruta/hash; se exige ausencia de WebP y no creación de catálogo. Otro caso inicializa un catálogo **sintético válido antes del workflow** y comprueba bytes e integridad intactos después de preparar, aparcar otro paquete y reabrir. Fixtures no usan universos/assets reales. Hay pruebas de rollback SQLite, rechazo de primera intención sin revisión CAS ficticia, rechazo de términos operativos de universo/raíces diferentes, corrupción de índices/historial/enum/sidecars, affinity, checkpoints de original/candidato/journal, revocación humana, geometría/límites independientes y conservación píxel a píxel.

Entorno: Linux, SDK 8.0.425, temporales sintéticos en `/dev/shm/nap-tests`, sin servicios IA de pago. Compilación completa final (19,62 s) `dotnet build NAP.sln -c Release --no-restore -m:4`: **0 errores, 5 advertencias NuGet preexistentes** de ImageSharp 3.1.12 (2 NU1902 y 3 NU1903), sin nuevas dependencias. `git diff --check` y whitespace de los archivos nuevos: sin incidencias.

| Ejecución | Aprobadas | Fallidas | Omitidas | Total |
| --- | --- | --- | --- | --- |
| Focalizadas finales PreparationOnly y contratos de cola (`phase3-preparationonly-focused-final.trx`) | 162 | 0 | 0 | 162 |
| Regresión final disponible sin volumen (`phase3-preparationonly-regression-final.trx`) | 3.603 | 0 | 4 | 3.607 |

Las focalizadas finales tardaron 34 s y la regresión final 1 min 25 s. Los resultados se solapan y **no se suman**. Las focalizadas incluyen DurablePreparationTests, PreparationIntegrityTests, DurableAuditTests, PreparationPolicyTests y AutomationQueueTests. Los ocho grupos desbloqueados se expanden en **24 casos**: PASS/WARNING/FAIL y reapertura; normalización autorizada/operativamente inválida y frontera de área; cambios de perfil/manifest/policy/receipt; retries 429/503/timeout/transporte; 401/403/respuesta inválida permanentes; cinco cortes de request/respuesta/persistencia; timeout/presupuesto cero; circuit breaker compartido. Ninguno conserva Skip por autorización.

También se prueban permisos productivos rechazados aun con autorización, falta de Admit/autorización, operaciones Normalize/Audit ausentes, rollback de activación e historial, CAS, pausa/reanudación/revocación y nuevas versiones sin heredar autoridad. Un estado activo falsificado en SQLite se rechaza. La excepción de maestro duplicado requiere términos ligados al mismo actor/autorización/política; términos atribuidos a otro actor no pueden eludir la revisión por defecto. Perfiles desconocidos siguen NeedsReview sin asignación ni preparación. Revocar/pausar antes de preservar deja el journal STAGED; hacerlo antes de publicar una respuesta impide el PASS. Aparcar externamente un item Planned/Pass impide llamadas o reutilización posterior. La derivación automática conserva cada píxel original y los bytes del ZIP/PNG, y cinco checkpoints distinguen recuperación acreditada de temporales ambiguos.

Comandos finales:

```sh
dotnet build NAP.sln -c Release --no-restore -m:4
dotnet test tests/NAP.Tests/NAP.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~DurableAuditTests|FullyQualifiedName~DurablePreparationTests|FullyQualifiedName~PreparationIntegrityTests|FullyQualifiedName~PreparationPolicyTests|FullyQualifiedName~AutomationQueueTests' --logger 'trx;LogFileName=phase3-preparationonly-focused-final.trx'
dotnet test NAP.sln -c Release --no-build --no-restore --filter 'Category!=AdmissionVolume' --logger 'trx;LogFileName=phase3-preparationonly-regression-final.trx'
git diff --check
```

La regresión actual excluye los cuatro casos AdmissionVolume de Fase 2 para evitar repetir el lote de 1.000 sin cambios en su monitor/staging. **Evidencia histórica del 2026-10-10, anterior a PreparationOnly:** la suite con volumen (`phase3-full.trx`) pasó 3.544 casos, falló 0 y omitió 12 (ocho grupos entonces bloqueados y cuatro Windows). Pasaron 1/10/100/1.000 ZIP; total 20 min 45 s y caso 1.000 en 19 min 51,546 s. No se presenta ese resultado como una nueva ejecución ni como rendimiento de preparación/IA. No se certifica rendimiento sostenido bajo PreparationOnly.

Solo quedan omitidos en la regresión actual cuatro smoke tests Windows/WPF existentes; no se certifican Windows, junctions/sharing/mutex nativos ni pérdida eléctrica por compilar Windows targeting desde Linux. Todos los proveedores y presupuestos usados son sintéticos; no se llama a Gemini real. Los resultados registrados son locales y no acreditan GitHub Actions. La publicación autorizada y el estado remoto se comunicarán por separado.

Riesgos técnicos residuales: advertencias conocidas de la dependencia ImageSharp sin actualización fuera de alcance; coste de revalidar/materializar filas e historial del ledger a gran escala, sin benchmark de preparación; memoria del proceso y presión de otros procesos no certificadas por el presupuesto conservador del decoder/normalizador. La recuperación opera sobre un filesystem controlado; no se afirma protección kernel frente a un administrador que reescriba simultáneamente todos los archivos/DB. No se garantiza exactly-once ni facturación real del proveedor a partir de un timeout local.

## Decisiones y dependencias para Fase 4

1. **Decisión aprobada y aplicada:** PreparationOnly explícito, versionado y exclusivamente preproductivo, autorizado por el responsable el 2026-10-11. Los ocho grupos antes omitidos se ejecutan con fixtures sintéticos. La automatización real continúa Disabled; no queda bloqueo de habilitación pendiente para auditar esta Fase 3.
2. **Conservación pendiente:** ratificar ubicación definitiva de originals, cobertura/estrategia de backup independiente, retención, cuotas y responsabilidad operativa. La copia StateRoot no es backup externo certificado. No se toca estructura canónica de ArchiveRoot para resolverlo.
3. **Perfiles ausentes:** definir el handoff de resolución excepcional de admisión sin violar el requisito Admitted; elegir ratio no reemplaza un contrato productivo completo.
4. **Validación operativa:** aprobar parámetros reales por perfil y presupuestos/provider/model; 3 %/1 % siguen siendo propuestas sin autorización operativa implícita.
5. **Fase 4:** consumir reservas/snapshot/receipt PASS actual; resolver claim y journal posterior conforme a contratos, volver a verificar almacenamiento y ownership, incorporar procedencia a evidencias productivas sin reinterpretar journal v1. Requiere aceptación, autorización productiva separada e integración previa de esta Fase 3 en main; no reutilizar PreparationOnly ni PASS como permiso productivo.

No hay publicación, migración, cleanup, activación automática ni backup externo nuevo. No se han tocado assets reales. La revisión y la integración en main quedan reservadas al responsable.

## Diff y revisión local

La entrega completa de Fase 3 conserva 10 archivos tracked modificados y 16 archivos nuevos (Core, AI, adaptador manual, pruebas y este informe). El diff íntegro, incluyendo archivos nuevos, queda exportado en `/workspace/tools/nap-phase3-review.patch` para auditoría local; `git diff --stat` por sí solo no muestra los nuevos. Base del diff validado: `f8e58dab0eae3c3e0827b8116b7da856da0aedbd`. El responsable ha autorizado la auditoría final, commit de estos 26 archivos, push exclusivo de la rama y PR Draft; no ha autorizado merge ni Fase 4. No se modifican documentos históricos, profile.json, proyectos/dependencias, journal v1, schema de cola/catalog, ArchiveRoot/ProductionRoot reales ni Git productivo.

Auditoría final previa a publicación: se revisan autoridad PreparationOnly, aislamiento, contratos históricos y checkpoints; no se detectan bloqueos nuevos en ese alcance. El inventario contiene únicamente 26 archivos de código/documentación, sin binarios, temporales, bases de datos ni assets. La inspección de literales y patrones de credenciales no detecta secretos. Los defaults y prohibiciones productivas se conservan. Permanecen los límites Windows/WPF, las advertencias conocidas de ImageSharp y las decisiones de backup/retención indicadas arriba.

## CI remoto Windows/WPF — PR #45 (2026-10-11)

Se conserva sin cambios el job Linux de `.github/workflows/ci.yml`. Un segundo job independiente, `windows-wpf`, usa el runner Windows alojado `windows-2022` y .NET 8 para restaurar y compilar la solución Release, incluida NAP.App. Comprueba la presencia del runtime Windows Desktop y ejecuta exclusivamente las cuatro pruebas existentes de `VisualExplorerWindowsSmokeTests`. No modifica tests, fixtures ni lógica productiva. Los mismos eventos push a main y pull_request hacia main ejecutan ambos jobs, también en PR Draft; publicar un commit en la rama de la PR genera el evento synchronize.

Las pruebas abren ventanas mediante el subproceso WPF existente y comprueban renderizado, bindings, tray, virtualización, importación y preview de normalización con fixtures sintéticos. El job registra sesión y UserInteractive como diagnóstico, no como prueba de que existe un escritorio utilizable. No introduce skips, modo headless alternativo ni relajación de assertions. El timeout del job es de 20 minutos; permanece el timeout de 45 segundos por subproceso ya definido por las pruebas.

`.github/scripts/Assert-WpfTestResults.ps1` verifica el TRX incluso si el test falla: exige exactamente las cuatro identidades y outcome Passed para cada una. Una prueba omitida, ausente, duplicada, fallida o sin TRX hace fallar el job y queda descrita en el resumen de Actions. Se conserva el TRX como artifact `windows-wpf-results` durante 14 días, también ante fallos cuando se haya generado; los logs detallados quedan en el job. Restore/build fallidos no se presentan como certificación WPF.

Validación local de esta configuración: YAML parseado y comparación exacta que acredita que el job Linux, triggers y permisos no cambian; sintaxis PowerShell válida en PowerShell 7.4.13. Ocho TRX sintéticos verifican aprobación de los cuatro Passed y rechazo de Failed, NotExecuted, test ausente, duplicado, cero resultados, archivo ausente y XML inválido. El TRX de regresión Linux existente se rechaza correctamente y conserva sus cuatro NotExecuted. `git diff --check` pasa. Estas comprobaciones prueban el gate de CI, no la ejecución WPF desde Linux ni la disponibilidad del escritorio Windows remoto; no se repite la suite de aplicación porque no cambia código productivo ni tests.

La configuración permite ensayar el runner remoto sin depender de un PC del responsable, pero no prueba por sí misma que su escritorio permita estas ventanas o tray. La API GitHub ha devuelto 403 al consultar la PR; los resultados reales de Actions quedan pendientes de lectura remota y no se declaran aprobados aquí. Si el runner carece del escritorio necesario, el fallo debe analizarse en TRX/logs y comunicarse explícitamente; no se sustituye por compilación Windows targeting ni se da por superado. Las cuatro omisiones Linux históricas de la tabla anterior siguen siendo resultados locales reales, no resultados del nuevo job Windows. No se inicia Fase 4 ni se habilita producción.
