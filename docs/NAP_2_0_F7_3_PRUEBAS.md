# F7.3 — Pruebas sintéticas focalizadas para auditoría

Fecha: 2026-10-26. Rama exclusiva `chatgpt/nap-2-f7-3-auto-push`, PR #55 Draft.
Adenda posterior: [corrección focalizada F73-A1 y sus pruebas](NAP_2_0_F7_3_F73_A1.md). Los resultados siguientes corresponden a la entrega inicial y no se atribuyen al código corregido sin repetirlos.

Última adenda: [F73-A2, reproducción del corte y verificación focalizada](NAP_2_0_F7_3_F73_A2.md). Sus resultados y SHA se conservan por separado.

Código de la entrega inicial sometido a verificación: **`18fafeb810acb15d7ffdcee0fe06d1eef5f43322`**.
Los cambios posteriores de documentación/evidencia no modifican ese código.

## Entorno y aislamiento

Linux x64, .NET SDK **8.0.427** instalado temporalmente en `/tmp/nap-dotnet`, Debug, Git **2.52.0**. No Windows/WPF/MSI/Release ni certificación productiva.
Fixtures `nap-preparation-<guid>` temporales, universo ficticio `test_universe`, identidad **Synthetic F72 / f72@example.invalid**, producción/archive/catalog sintéticos, Git init y bare locales. Se comparte el fixture nativo F7.2, no se fabrican estados FilesVerified ni se simula Completed sin ejecutar su workflow sintético.

Cada caso crea **1 ZIP**, excepto el lote nominal de **5 ZIP** y el control F7.2 del segundo lote (**2 ZIP**). Los originales de entrada se cuentan una sola vez; las copias nativas de staging/received no son entradas adicionales. No escenarios 10/20, volumen, benchmarks, estrés ni repeticiones para simular carga. El filtro y `functional.runsettings` excluyen suites generales y volumen.

Los contadores `NativePushCommands` se toman de `CommandStarting` del runner nativo antes de arrancar el proceso: incluyen comandos rechazados. Excluyen el push inicial del fixture (uno por caso) y las publicaciones externas explícitas del fixture. Estos últimos también son exclusivamente locales: hay un push externo adicional en remote advanced, manual externo, AlreadyPushed preexistente y reconocimiento AutoCommitBatch. Ninguna prueba usa GitHub, credenciales reales, assets reales, production ni CUTOVER.

## Resultado final

**31/31 casos Passed, 0 Failed y 0 omitidos** sobre el SHA final: **27/27 F7.3 (2 m 59 s)** y **4/4 controles F7.2 (29 s)**, según los resúmenes de VSTest. Ambos comandos terminaron con exit code 0. Las compilaciones del código final no produjeron errores ni avisos nuevos de código; se conservan los avisos preexistentes de ImageSharp/licencia.

Verificación final: **36 ZIP de entrada en total** (31 F7.3 + 5 en controles F7.2), máximo **5 por escenario**. El JSON de los 27 casos F7.3 cuenta **16 comandos push nativos intentados**, uno rechazado por el bare ficticio y quince aceptados, más los pushes de fixture explícitamente separados. La recuperación tras los seis checkpoints conserva exactamente un push nativo total por caso.

Histórico transparente de las dos ejecuciones F7.3 y los cuatro controles: **56 ejecuciones de casos y 65 ZIP de entrada** (29 + 31 + 5). Incluye los casos repetidos para verificar la corrección; no se presenta como 56 escenarios distintos ni como prueba de capacidad.

| Escenario F7.3 / nombre de filtro | Casos | ZIP por caso | Comprobación |
| --- | ---: | ---: | --- |
| AuthorizedBatchVerifiesRemoteAndPublishesExactlyOnce | 2 | 1 / 5 | Un batch, un commit y un push, SHA remoto exacto, receipt/intents/batch Pushed; centinela ajeno excluido; reinicio sin duplicados |
| CommitPermissionNeverImpliesPush | 2 | 1 | AutoCommitBatch y AutoCommitAndPushBatch sin AllowPush conservan Committed; cero push nativo |
| InterruptedPublicationRecoversSameOperationWithoutAnotherCommitOrPush | 6 | 1 | before-push, before_push, before_push_command, after_push, pushed y push-returned; mismo OperationId/SHA, un commit y un push total |
| OfflineRetryIsDurablyBoundedAndExplicitRetryRechecksRemote | 1 | 1 | Bare temporal indisponible, tres intentos persistidos entre reinicios; conserva Committed/Completed; retry explícito posterior publica una vez |
| RejectedPushRetainsCommittedAndNextAuthorizedTurnRetriesNativeEngine | 1 | 1 | Rechazo real del bare por receive.hideRefs temporal, un intento rechazado sin avance remoto, retry normal autorizado y SHA confirmado |
| ChangedBindingOrReceiptBlocksWithoutRemoteMutation | 5 | 1 | Identidad, upstream, URL/fingerprint, remoto avanzado y receipt corrupto: Blocked; cero push NAP y HEAD/index/tip conservados |
| RevocationAndNewGrantNeverReactivateOldCommittedBatch | 1 | 1 | Revocación impide publicación; nueva revisión/AuthorizationId no reactiva; retry explícito automático también rechazado |
| ManualAndExternalPushReconcileOnlyTheExactBoundOperation | 2 | 1 | Push manual NAP concilia al terminar; push externo exacto se reconoce sin push nativo redundante |
| ManualModeRequiresExplicitPushAndDoesNotAdoptAnUnrelatedReceipt | 1 | 1 | Manual no actúa en turno automático; commit y push expresos; receipt ajeno a Outbox no crea/adopta batch |
| ExternalPushInCommitOnlyModeIsRecognizedWithoutPushPermission | 1 | 1 | AllowPush=false, SHA externo exacto reconocido bajo autorización original; ningún comando push NAP |
| NewManualGrantRequiresExplicitPushDecisionForOldBatch | 1 | 1 | Política nueva Manual no ejecuta automáticamente; decisión explícita Push resuelve la operación antigua conservando autorización original en intents |
| DurablePushedEvidenceCanReconcileUnderNewCommitOnlyGrantWithoutPublishing | 1 | 1 | Receipt Pushed previo, revisión nueva sin AllowPush: solo verificación, sin republicación ni migración de intents |
| ManualAndPolicyWriterCannotRaceOwnerAtTheIrreversibleBoundary | 1 | 1 | Busy bajo Production, hilo manual y escritor de política rechazados durante el turno; owner finaliza con un push, sin deadlock |
| NativePushRechecksAuthorizationBeforeCommandAndAlreadyPushedReceipt | 2 | 1 | Callback nativa deniega antes de comando y antes de marcar AlreadyPushed; cero push y receipt conserva Committed |

En los casos F7.3 con batch, el JSON del TRX registra base HEAD, HEAD/CommitSha, tip real consultado al bare, OperationId, PlanHash, revisión/autorización e IDs de intent/Job/QueueItem/WorkflowAttempt/Batch, estados y comandos. Las aserciones comparan parent/base y número de commits, conservación de Completed, invariantes de lote, SHA y efectos negativos. El caso Manual sin batch también emite evidencia de su receipt/SHA/tip y Outbox vacío.

Los cuatro controles F7.2 seleccionados son `DisabledCompletedHasNoGitEffectsAndNoOutbox`, `SecondBatchIsBlockedByNativeRequireNoPending`, `FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit` y `OrphanReceiptTempBlocksWithoutAdoption`. Cubren Disabled, barrera nativa al segundo lote, mismo commit congelado tras fallo y rechazo de huérfanos. No se ejecuta toda la suite F7.2 ni pruebas históricas generales. Estos cuatro controles conservan las aserciones nativas existentes y sus outcomes en TRX; no se les atribuye instrumentación JSON por SHA de fixture que no emitían.

## Comandos exactos

Desde `/workspace/NAP`, sobre el SHA final indicado, sin ejecutar workflows:

```sh
/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter 'FullyQualifiedName~NAP.Tests.GitOutboxF73Tests' --settings tests/functional.runsettings --logger 'trx;LogFileName=f73-final.trx' --results-directory /tmp/nap-f73-results --nologo -v minimal > /tmp/nap-f73-final.log 2>&1

/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~DisabledCompletedHasNoGitEffectsAndNoOutbox|FullyQualifiedName~SecondBatchIsBlockedByNativeRequireNoPending|FullyQualifiedName~FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit|FullyQualifiedName~OrphanReceiptTempBlocksWithoutAdoption' --settings tests/functional.runsettings --logger 'trx;LogFileName=f73-f72-guards.trx' --results-directory /tmp/nap-f73-results --nologo -v minimal > /tmp/nap-f73-f72-guards.log 2>&1

git diff --check
```

El primer comando compila Core/Tests y proyectos referenciados; solo ejecuta el filtro. El segundo reutiliza esos binarios. No se ejecuta `dotnet test` sin filtro ni `NAP.sln`/CI general.

## Iteración previa conservada

SHA **`0a08d4c010e35c75c2e384cec6f60a03e330226a`**: **25 casos, 24 Passed / 1 Failed / 0 omitidos**, 29 ZIP de entrada en total, máximo cinco por escenario, duración de casos resumida por VSTest **2 m 59 s**.

```sh
/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter 'FullyQualifiedName~NAP.Tests.GitOutboxF73Tests' --settings tests/functional.runsettings --logger 'trx;LogFileName=f73-initial.trx' --results-directory /tmp/nap-f73-results --nologo -v minimal > /tmp/nap-f73-initial.log 2>&1
```

El caso `ChangedBindingOrReceiptBlocksWithoutRemoteMutation(fault: "remote")` obtuvo `git-push-retry` donde exigía `git-blocked`: el inspector intentaba consultar un remoto cambiado e inaccesible antes de comparar el fingerprint congelado. No se observó un push indebido; falló la clasificación fail-closed requerida. Se añadió comprobación local de configuración congelada **antes de la primera consulta remota**, manteniendo las verificaciones nativas posteriores. El TRX fallido y log se conservan íntegros salvo saneamiento de prefijos locales.

Durante revisión se añadió el reconocimiento sin escritura remota para un push externo en AutoCommitBatch bajo autorización original. Un receipt Committed antiguo de otra revisión sigue sin adoptarse automáticamente; para reconciliar bajo una autorización nueva se exige Pushed durable o decisión manual explícita. Se añadieron dos escenarios para esta distinción y se corrigieron siete avisos xUnit2031 de estilo de aserción. Las repeticiones se deben a estos cambios y al fallo anterior, sin simulación de volumen.

Build preliminar, anterior al congelado del SHA técnico (working tree en desarrollo):

```sh
/tmp/nap-dotnet/dotnet build tests/NAP.Tests/NAP.Tests.csproj --nologo -v minimal > /tmp/nap-f73-build.log 2>&1
```

Terminó con **0 errores / 11 warnings**; no ejecutó tests y no se le atribuye un SHA final. Las compilaciones contenidas en los comandos de test acreditan los respectivos SHA. Los hashes de los binarios iniciales fueron guardados y comprobados antes del rebuild final; los cambios de fuentes posteriores al inicio de esos tests no modificaron los binarios de la primera ejecución.

## Evidencia, advertencias y límites

`docs/f7-3-evidence/` conserva TRX, logs, resumen JSON por escenario, SHA de código y manifiesto SHA-256 de artefactos saneados. Se sustituyen prefijos locales/hostname y se normalizan finales de línea a LF para que los hashes correspondan a los bytes publicados por Git; se preservan resultados, fallos y warnings. El resumen conserva también el SHA-256 de cada original previo al saneamiento. No se incluyen binarios, ZIP/outputs de assets, configuración de lanzamiento, claves HMAC, licencias ni credenciales.

Persisten los cinco avisos NU1902/NU1903 de ImageSharp 3.2.0 y las advertencias de licencia ausente para Release. No se suprimen, no se cambian paquetes ni se usa una licencia. No se declara análisis de seguridad limpio.

No se prueba autenticación real HTTPS/SSH/GCM, dos procesos nuevos independientes, host completo con Git habilitado, WPF visual, Windows/NTFS/MSI/Release, corte eléctrico ni kill real. Los crashes usan excepciones en checkpoints y recreación de servicios. El mutex Production y la autoridad owner se ejercitan con hilos concretos, sin estrés. Las reservas de almacenamiento/ACL/rollback y límites de estado compartido continúan según `NAP_2_0_F7_3_IMPLEMENTACION.md`.

No NAP CI, workflows, CUTOVER, producción, main, merge, F7.4 ni cierre de #47/#49. Los commits de implementación se publican únicamente en la rama autorizada como gestión de código. **La aceptación de F7.3 queda pendiente de auditoría independiente de ChatGPT.**
