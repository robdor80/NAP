# NAP 2.0 — F7.2 · Implementación para auditoría

Fecha: 2026-10-26. Rama exclusiva: `chatgpt/nap-2-f7-git-contracts`.
Preparación verificada: `2394303b1e93c6bf990fd6192be04a064f48fc53`.
Código sometido a las pruebas finales: `b2636106488d1de252b59ba9303137e0425428d2`.
Estado: implementación entregada para auditoría de ChatGPT; **no certificación de producción ni cierre de F7.2**.

## Componentes y frontera

| Componente | Responsabilidad |
| --- | --- |
| `GitPublicationModels.cs` | Política v1, modos cerrados, intents, lotes, historial y estados Outbox separados del receipt Git |
| `GitPublicationPolicyStore.cs` | Propuesta read-only sin permisos implícitos; guardado explícito con CAS/revisión, autorización, revocación y fingerprints |
| `GitPublicationFiles.cs` | Directorios privados locales, ACL Windows / propietario y modos Linux, JSON canónico cerrado, HMAC, CreateNew + Flush(true) + Move |
| `GitOutboxStore.cs` | Ledger independiente en `StateRoot/git-outbox-v1/outbox.json`; integridad, scope, enlaces de lote y operación y límites |
| `AutomationQueueGitCompletions.cs` | Lectura paginada por rowid global de eventos Completed; ningún cambio al schema SQLite v1 |
| `GitBatchPublicationService.cs` | Reconciliación incremental, verificación nativa, lote determinista, reserva durable, commit y recuperación; **ninguna llamada Push** |
| `GitPublicationManualService.cs` | Guardas de política para las acciones manuales existentes; plan ligado a hash de autorización y push con permiso independiente |
| `DurableExecutionService.VerifyCompletedReadOnly` | Recuperación nativa desde Queue/Attempt/Job, preparación, ledger de ejecución y catálogo PASS sin escribir esos estados |
| `AssetExecutionCoordinator.VerifyCompletedReadOnly` / `ProductionAssetPlanner.PlanCompletedReadOnly` | Reconstrucción nativa comprobada contra snapshot original sin codificar WebP ni repetir producción |
| `GitProductionService` | Seam interna `CommitBound` / `RetryCommitBound`, ID reservado y callback de autorización; mismo motor, comandos y controles originales |
| `GitProductionModels.cs` | Hash interno de autorización del plan manual; no cambia el formato receipt v1 |
| `AutomationHost.cs` | Turno Git limitado en el hilo propietario, al quedar libre el recorrido de workflows, cada cinco segundos como máximo |
| `ProfessionalUiServices.cs` | Enruta operaciones Git manuales por la nueva guarda; mantiene diálogos de confirmación y páginas existentes |
| `GitOutboxF72Tests.cs` | Casos sintéticos seleccionados; sin pruebas de volumen ni push automático |

La API Git nativa continúa siendo el motor explícito de bajo nivel. La política se aplica en los puntos de composición de NAP (host y adaptador manual); no se sustituye su runner ni se relajan sus validaciones. No se añadieron paquetes, tablas de cola, endpoints IPC, scheduler productivo ni UI WPF de configuración.

## Política y autorización

La ubicación predeterminada es `%LOCALAPPDATA%/NAP/git-publication/<universe>-<repository_binding>/`, fuera de checkout y de las raíces del universo. Contiene `policy.json` e `integrity.key` privada generada localmente; **no credenciales Git**. Los tests proporcionan explícitamente una ubicación temporal ficticia. No se modifica `settings.json` v1.

Ausencia total equivale a Disabled, sin inspección remota automática ni creación de Outbox. Presencia parcial, corrupción, versión/campos desconocidos, duplicados, firma incorrecta, temp huérfana, permisos inseguros o ancla discordante producen STOP. No se regeneran claves ni se sobrescribe configuración ilegible.

La política vincula UniverseId, binding nativo ProductionRoot+StateRoot, fingerprint de todas las raíces, rutas canónicas locales, modo, revisión, autorización/actor/UTC, identidad efectiva author/committer sin fechas, rama/upstream/remoto y sus fingerprints, prefijos y máximo de Jobs. Un prefijo es una restricción adicional; nunca acredita ownership.

`Propose(...)` inspecciona a petición explícita y devuelve `AllowCommit=false`, `AllowPush=false`. No guarda ni concede permisos. La integración administrativa debe presentar los hechos al responsable y llamar a `Save(policy, expectedRevision)` solamente después de autorización Git específica. No existe activación por consentimiento ZIP, disponibilidad de GCM, memoria de sesión ni migración.

- Disabled: sin commit/push.
- Manual: acciones existentes expresas, con autorización local y confirmaciones conservadas.
- AutoCommitBatch: solo commit local.
- AutoCommitAndPushBatch: puede representar una autorización futura de push, pero **F7.2 jamás realiza push automático**, incluso con AllowPush=true.
- Push manual: requiere AllowPush independiente, modo compatible, autorización vigente y todos los controles nativos. La PR no incorpora nuevas funciones de push.

Cambiar política exige revisión consecutiva y nuevo AuthorizationId; el historial de hashes/actor/fecha/revocación se conserva en Outbox. Las intenciones/lotes Pending de la revisión anterior pasan a Blocked. Committed se conserva. El plan manual pierde validez al cambiar el hash de política.

El escritor actualiza primero el ancla Outbox y después la política, cada final atómico. Una interrupción entre ambos deja discordancia y **bloquea**; no permite volver a una autorización antigua. No se afirma una transacción atómica entre dos archivos ni recuperación automática de esa configuración parcial. La recuperación administrativa de esos finales requiere revisión, sin borrar evidencia. Las escrituras y operaciones se serializan con el mismo mutex de política; una revocación aceptada es efectiva para la siguiente operación. No hay grandfathering.

## Evidencia nativa sin producción repetida

La proyección exige Queue Completed, su intento activo exacto, Job Completed, `ExecutionRecord.Completed`, binding íntegro con preparación/snapshot/audit/grant/roots y efectos durables. Reutiliza `DurablePreparationService.VerifyExecution`: sigue siendo necesaria la evidencia retenida de fuentes y la política de procesamiento actual. No ejecuta auditor, preparación, conversión, archive executor, producción, registro de catálogo ni transiciones de Job/Queue.

La ruta original `VerifyCompleted` reconstruía el plan con `ProductionImageEngine.Generate` en memoria. La proyección nueva selecciona `PlanCompletedReadOnly`: recupera expectativas WebP del catálogo PASS, exige tamaño/hash de los bytes físicos, deriva compañeros exclusivamente de planes nativos de archivo y comprueba el **snapshot SHA original de `ProductionExecutionEvidence` y su lista completa de publicaciones del mismo Job**. Esa comprobación une hashes de cada archivo, tipos/roles, fuentes, ruta, Job, universo, master y parámetros de conversión originales. Ni el catálogo ni los bytes/carpeta por sí solos son ownership.

Solo después se usan `ProductionAssetPlanner.Revalidate`, `ProductionAssetExecutor.VerifyCoordinated` (incluye decode WebP, fuentes, archivo y hashes) y comparación completa del catálogo con `CatalogAssetReader.FromVerified`. El `ProductionAssetResult` procede del verificador nativo, no de un JSON/listado/UI fabricado. No se codifica WebP en esta ruta. El snapshot Outbox incluye snapshot de ejecución, audit/grant, hash del receipt de producción y archivos comprobados.

Si falta evidencia, queda una correlación **Blocked** con diagnóstico `native_evidence_unavailable`; su marcador `unverified` jamás se utiliza como ownership ni puede autorizar un commit. No se modifica Completed. La verificación física inicial que ya tenía el host se conserva independientemente: puede detectar una inconsistencia física por su contrato previo; un fallo Git no se envía a ese mecanismo.

## Estados, reconciliación y lotes

| Estado | Significado / recuperación |
| --- | --- |
| Pending | Evidencia nativa comprobada, sin commit confirmado; puede tener reserva durable de lote y operación |
| Committed | Receipt nativo verificado, SHA local confirmado; nunca vuelve a Commit |
| Blocked | Evidencia, política, ownership, Git, índice o recuperación rechazados; código saneado y evidencia conservada |
| Pushed | Reservado para F7.3; F7.2 no produce este estado automáticamente |

ID de intent = SHA-256 de universo, binding, Job, snapshot verificado, revisión y AuthorizationId. Se conservan QueueItemId, WorkflowAttemptId y SourceBatchId para correlación. ID de lote = SHA-256 de los IDs de intent ordenados ordinalmente. Solo incluye una revisión/autorización del mismo repositorio. Selección ordenada y máximo 1–20 Jobs configurable.

La reconciliación lee como máximo 20 eventos de finalización por llamada mediante cursor durable del rowid **global** de eventos (no la secuencia local por item). Así recoge también Jobs antiguos que terminan más tarde. La transacción/I/O SQLite termina antes de verificar fuentes o llamar Git. Cursor e intenciones se publican en el mismo final Outbox; caída antes de ese final vuelve a leer la página, idempotentemente. Una página llena devuelve `git-reconciling` y aplaza el lote hasta alcanzar el final del recorrido, para no excluir evidencia de páginas siguientes.

No se mantienen DB/queue locks durante Git. Orden de coordinación: AutomationOwner en el hilo del universo → mutex de política → leases nativos Job/Production/Archive/Catalog según las llamadas existentes; los leases internos se liberan antes del siguiente subsistema. El callback Git se ejecuta bajo Production y la política ya retenida, sin reacquirir AutomationOwner ni invertir locks. Las llamadas manuales concurrentes deben usar el dispatcher del propietario o reciben Busy.

Todo dirty path debe estar explicado por el lote. Se mantienen staging exacto, revalidaciones de hashes/atributos/blobs/identidad/configuración, hooks desactivados por invocación y los rechazos nativos. No se utiliza add general, índice alternativo, reset, clean, force, pull, rebase ni amend. `RequireNoPending` conserva su comportamiento: un segundo lote con receipt previo Committed/Prepared queda Blocked; no se publican varios commits offline pendientes.

## Enlace y ausencia de duplicados en cada ventana de caída

Antes de crear un receipt, un único final Outbox guarda miembros exactos, BatchId, GitOperationId reservado y SHA-256 del plan nativo completo: universo/binding, HEAD/rama/upstream/remoto/tip/configuración/porcelain, mensaje, paths y hashes/tamaños de **todos** los archivos propios. `CommitBound` es interno; crea el receipt con ese ID mediante el servicio existente y conserva CreateNew y RequireNoPending. No escribe receipts desde Outbox.

| Ventana | Acción al recuperar |
| --- | --- |
| Completed antes de Outbox | Cursor paginado recrea solo la intención faltante; cero producción repetida |
| Intent registrado antes de reserva | Forma lote determinista desde la evidencia actual verificada |
| Reserva antes de Prepared | Reutiliza **el mismo ID reservado**; preparación debe coincidir con el hash congelado; de otro modo Blocked |
| Prepared antes de retorno del ID | El ID ya estaba durable en Outbox; lectura exacta, comparación del plan y Recover nativo |
| Prepared con HEAD base | Recover valida; RetryCommitBound solo bajo autorización vigente y fotografía original |
| Commit esperado congelado, HEAD aún base | RetryCommit nativo conserva author/committer/fechas y expected_commit_sha exactos |
| HEAD movido, receipt aún Prepared | Recover exige el SHA esperado exacto y verifica objeto/árbol/parent/mensaje/paths/blobs; nunca adopta un commit parecido |
| Receipt Committed antes de Outbox Committed | Recupera el receipt del ID exacto y registra Committed sin llamar Commit otra vez |
| Final Committed publicado | Reinicios no repiten el lote |

La recuperación inventaría todos los receipts mediante el parser nativo; una temp/corrupción/operación ajena no se adopta por fecha, mensaje o SHA semejante. Si otro proceso modificó HEAD/índice/ficheros, el motor bloquea. Un fallo controlado deja Blocked; `RetryBlocked(owner, intentId)` es explícito y conserva la misma operación reservada y plan. Un proceso interrumpido deja Pending, recuperable en el siguiente turno autorizado.

## Límites y revisión pendiente

- Validación ejecutada en Linux x64 / .NET SDK 8.0.427, Debug. No se ha verificado Windows 11, ACL NTFS, WPF visual, MSI ni Release; la rama Windows de seguridad requiere auditoría física posterior.
- Las interrupciones de tests son excepciones inyectadas en checkpoints y recreación del servicio. Acreditan los enlaces y recuperación del estado persistido, **no** corte de energía, fsync de directorios, fallos del disco ni kill de proceso completo.
- Ledger v1 acotado a 2 MiB por final y 4096 intents/lotes (el límite de bytes puede alcanzarse antes). Al agotarse, STOP; no compacta, borra ni reinicia evidencia. No se acredita rendimiento/volumen.
- Cursor supone el ledger append-only nativo sin VACUUM/reescritura/restauración externa que renumere rowids. Esas operaciones y rollback conjunto de archivos privados requieren revisión coordinada; no se acredita continuidad automática tras ellas.
- HMAC y ACL protegen integridad/localidad dentro del modelo de confianza del host. No ofrecen defensa frente al propietario/administrador que controle clave, binario y todos los finales, ni ancla anti-rollback fuera de la máquina.
- Fuentes retenidas, autorización de procesamiento actual o evidencia nativa faltantes dejan Blocked. Las correlaciones inicialmente `unverified` no se liberan con RetryBlocked; hace falta revisión explícita, sin inferir ownership de carpeta.
- No hay migración automática de intents antiguos a una nueva autorización. Si se cambia política con operaciones reservadas, quedan Blocked y requieren revisión y recuperación manual nativa autorizada; no se inventa un enlace nuevo ni se borra el anterior para continuar.
- La escritura de política tiene dos finales fail-closed. Una caída entre ancla y configuración necesita revisión administrativa; esta fase no implementa reparación automática de configuración parcial.
- El host sigue siendo la composición de laboratorio existente, bajo su grant y controles previos. No se ha activado producción ni CUTOVER ni añadido UI/IPC administrativo de F7.4.
- Los avisos de ImageSharp y ausencia de licencia para Release ya existentes permanecen. No se cambiaron dependencias, credenciales, licencias ni permisos del MSI. #47 y #49 siguen abiertas.

Las pruebas y sus comandos/resultados se documentan en `NAP_2_0_F7_2_PRUEBAS.md`. ChatGPT debe auditar especialmente la recuperación sin recodificación, la reserva previa a Prepared, los límites y la frontera de política antes de aceptar F7.2.
