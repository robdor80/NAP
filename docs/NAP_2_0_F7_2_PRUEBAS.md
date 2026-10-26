# F7.2 — Evidencias focalizadas

Fecha: 2026-10-26. SDK temporal `/tmp/nap-dotnet`: .NET **8.0.427**, Linux x64, Debug.
SHA del código final probado: **`b2636106488d1de252b59ba9303137e0425428d2`**. Los commits posteriores de documentación no cambian ese código.

No se ejecutó NAP CI, workflow alguno, CUTOVER, batería general ni pruebas de volumen. No se modificó main, MSI, licencia, dependencias ni credenciales. Los fixtures son `nap-preparation-<guid>` bajo el directorio temporal, con universos ficticios, identidad `f72@example.invalid`, Git init y remotos bare locales. La facilidad de transporte local sigue siendo exclusivamente interna de tests, habilitada por reflexión como en fixtures históricos.

## Selección y resultado final

**19 casos aprobados, 0 fallos, 0 omitidos**, en dos comandos con objetivos funcionales distintos. Un máximo de **5 ZIP de entrada por caso**; ningún caso 10/20, carga, estrés ni repetición para simular volumen. Primer comando: 14 casos, 19 ZIP de entrada en total. Segundo: 5 casos, 5 ZIP de entrada. Se cuentan ZIP de entrada creados, no sus copias nativas de staging/received. No se acredita capacidad de volumen.

| Caso / filtro por nombre | Casos | ZIP por caso | Evidencia comprobada |
| --- | ---: | ---: | --- |
| DisabledCompletedHasNoGitEffectsAndNoOutbox | 1 | 1 | Completed permanece; no receipt/commit/Outbox ni inferencia de consentimiento ZIP |
| FiveCompletedJobsBecomeOneExactBatchAndReconcileIdempotently | 1 | 5 | Un lote, parent base exacto, receipt Committed, working tree limpio, centinela ajeno excluido, assets/archive/catalog intactos; reinicio sin duplicación |
| InterruptedPublicationResumesExactReservedOperation | 5 | 1 | Interrupciones reconciled, batch-reserved, prepared, after_commit y commit-returned; mismo ID reservado cuando existe, único commit y receipt |
| UnsafeEvidenceOrGitChangeBlocksAndPreservesCompleted | 4 | 1 | Dirty ajeno, index ajeno, WebP alterado e identidad cambiada: Blocked, HEAD/index ajeno intactos, Completed conservado |
| SecondBatchIsBlockedByNativeRequireNoPending | 1 | 2 | Segundo Completed registra intent, el receipt previo Committed bloquea; ningún segundo commit |
| CorruptPolicyAndRevocationFailClosed | 1 | 1 | Revocación tras Pending bloquea, historial conserva revisión y una alteración del archivo autenticado detiene el subsistema |
| OrphanReceiptTempBlocksWithoutAdoption | 1 | 1 | Temp nativa preservada, lote Blocked y HEAD sin mover |
| FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit | 1 | 1 | Fallo Git inyectado antes de commit; rollback acotado del índice propio, Completed intacto; retry explícito mismo ID y expected_commit_sha |
| ManualCommitNeedsNewGrantAndPushNeedsIndependentPermission | 1 | 1 | Modo Manual sin ejecución automática; plan viejo rechazado tras nueva autorización; commit explícito y push denegado sin AllowPush |
| LateCompletionIsReconciledByBoundedPagesWithoutRepeatingProduction | 1 | 2 | Dos páginas de un elemento, sin formar lote incompleto; reinicio de servicio conserva cursor y luego crea un lote de dos |
| ForeignOwnerThreadCannotEnterManualGit | 1 | 0 | Hilo explícito distinto recibe rechazo mientras AutomationOwner está retenido; hilo propietario sigue operando |
| CatalogAndFilesAloneNeverSubstituteForNativeOwnership | 1 | 1 | Catálogo PASS y outputs presentes sin receipt original de publicación: proyección rechazada, Outbox Blocked, cero commit |

En el caso de cinco Jobs se usa **AutoCommitAndPushBatch con AllowPush=true**, pero el tip bare remoto permanece en BaseHead. Los pushes de inicialización del fixture solo crean su base ficticia y upstream; ninguna ejecución del orquestador realiza push.

## Comandos exactos sobre el SHA final

Ejecutados desde `/workspace/NAP`. La primera orden compila los proyectos referenciados pero solo ejecuta los casos filtrados. La segunda reutiliza esos binarios. Ambos usan el `functional.runsettings` existente (excluye volumen) además del filtro explícito.

```sh
/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter 'FullyQualifiedName~NAP.Tests.GitOutboxF72Tests&FullyQualifiedName!~FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit&FullyQualifiedName!~ManualCommitNeedsNewGrantAndPushNeedsIndependentPermission&FullyQualifiedName!~LateCompletionIsReconciledByBoundedPagesWithoutRepeatingProduction&FullyQualifiedName!~ForeignOwnerThreadCannotEnterManualGit&FullyQualifiedName!~CatalogAndFilesAloneNeverSubstituteForNativeOwnership' --logger 'trx;LogFileName=f72-final-core.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-final-core.log 2>&1

/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit|FullyQualifiedName~ManualCommitNeedsNewGrantAndPushNeedsIndependentPermission|FullyQualifiedName~LateCompletionIsReconciledByBoundedPagesWithoutRepeatingProduction|FullyQualifiedName~ForeignOwnerThreadCannotEnterManualGit|FullyQualifiedName~CatalogAndFilesAloneNeverSubstituteForNativeOwnership' --logger 'trx;LogFileName=f72-final-guards.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-final-guards.log 2>&1
```

Resultados: **14/14 en 30 s**, **5/5 en 8 s**, respectivamente (duraciones resumidas por vstest). TRX completos y `results.json` se adjuntan en `docs/f7-2-evidence/`. Solo se sustituyeron prefijos de rutas locales por marcadores XML escapados; no se eliminaron fallos ni resultados.

## Iteraciones previas y correcciones (sin atribuirles el SHA final)

Todas sobre el checkout de preparación más cambios de trabajo aún no comprometidos. No se puede atribuirles un SHA de implementación independiente. Sus logs/TRX se conservan para no ocultar los fallos. Las repeticiones obedecieron a correcciones o a cambios de la ruta nativa, **no** a simulación de carga.

| Ejecución | Resultado | ZIP de entrada totales | Razón de seguimiento |
| --- | --- | ---: | --- |
| f72-initial | 12 aprobados / 2 fallos | 19 | InvalidDataException de autorización no se convertía en Blocked; cursor inicial usaba secuencia local por item y omitía segunda completion |
| f72-corrected | 14/14 aprobados | 19 | Captura explícita del rechazo y cursor por rowid global; historial de revisión y revocación añadido |
| f72-additional | 3 aprobados / 1 fallo | 4 | La prueba Task.Run + espera síncrona podía ejecutarse inline en el mismo hilo; no demostraba concurrencia |
| f72-owner-corrected | 1/1 aprobado | 0 | Sustitución por Thread explícito y Join acotado; rechazo en hilo diferente |
| f72-final-core | 14/14 aprobados | 19 | SHA final, incluyendo ruta sin recodificar WebP y política ligada a todas las raíces |
| f72-final-guards | 5/5 aprobados | 5 | SHA final, pruebas concretas de permisos/retry/páginas/ownership/owner |

Total histórico transparente: 66 ZIP de entrada creados a lo largo de seis ejecuciones focalizadas; máximo 19 por comando y 5 por escenario. No se reejecutaron tests históricos ni se dividió un escenario de volumen.

Comandos exactos de las iteraciones previas:

```sh
/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter FullyQualifiedName~NAP.Tests.GitOutboxF72Tests --logger 'trx;LogFileName=f72-initial.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-tests-initial.log 2>&1

/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter FullyQualifiedName~NAP.Tests.GitOutboxF72Tests --logger 'trx;LogFileName=f72-corrected.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-tests-corrected.log 2>&1

/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter 'FullyQualifiedName~FailedGitCommitKeepsPreparedAndExplicitRetryUsesSameExpectedCommit|FullyQualifiedName~ManualCommitNeedsNewGrantAndPushNeedsIndependentPermission|FullyQualifiedName~LateCompletionIsReconciledByBoundedPagesWithoutRepeatingProduction|FullyQualifiedName~ForeignOwnerThreadCannotEnterManualGit' --logger 'trx;LogFileName=f72-additional.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-tests-additional.log 2>&1

/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter FullyQualifiedName~ForeignOwnerThreadCannotEnterManualGit --logger 'trx;LogFileName=f72-owner-corrected.trx' --results-directory /tmp/nap-f72-results --nologo -v minimal > /tmp/nap-f72-tests-owner-corrected.log 2>&1
```

Compilaciones separadas, sin ejecutar tests:

```sh
/tmp/nap-dotnet/dotnet build tests/NAP.Tests/NAP.Tests.csproj --nologo -v minimal > /tmp/nap-f72-build.log 2>&1
/tmp/nap-dotnet/dotnet build tests/NAP.Tests/NAP.Tests.csproj --no-restore --nologo -v minimal > /tmp/nap-f72-build-final.log 2>&1
git diff --check
```

Ambos builds pasaron (0 errores). El segundo precede a los últimos cambios que luego fueron compilados por `f72-final-core`; su nombre de log se conserva como `build-before-final-tests.log` para evitar confusión. Avisos existentes NU1902/NU1903 de ImageSharp 3.2.0 y licencia SixLabors ausente para Release. No se modificaron paquetes ni licencia. La advertencia de test CS8625 inicial y xUnit1031 del ensayo Task.Run fueron corregidas; los comandos finales no las presentan.

## Lo que no se ha acreditado

No se han ejecutado Windows/WPF/MSI/Release, kill de proceso o caída eléctrica reales, dos procesos independientes, host completo con política Git habilitada, nuevos escenarios de remoto avanzado/branch/config/filtros/LFS/symlink, ni suites Git históricas. Se conservan sus comprobaciones nativas por código; **no se declara que se hayan repetido sus pruebas**. Los crashes se modelan con checkpoints y servicios reconstruidos; after_commit conserva HEAD y receipt Prepared para recuperación exacta.

La seguridad contra cambios ajenos y el segundo lote se acredita en estos fixtures. Los límites de almacenamiento, revisión de política, host y confianza local figuran en `NAP_2_0_F7_2_IMPLEMENTACION.md`. Esta evidencia permite auditoría, no certificación productiva. #47/#49 no se cierran; F7.3/F7.4 no se implementan.
