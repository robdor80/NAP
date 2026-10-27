# F7.4 — Pruebas focalizadas y evidencia

Adenda posterior: [corrección focalizada F74-A1 y su evidencia](NAP_2_0_F7_4_F74_A1.md). Los SHA/resultados siguientes describen la entrega inicial y se conservan como referencia histórica.

27-10-2026. Rama `chatgpt/nap-2-f7-4-git-ui`, PR #56 Draft. **Código final probado: `9fb4f1703f5b7fa8c62bac0822898834d02d4e46`.** No aceptación de fase ni certificación productiva.

Entrega: **17 Passed, 0 Failed, 0 Skipped**, VSTest 20 s. Compilación cruzada WPF/Debug correcta, cero errores (9,16 s). Linux x86_64, SDK .NET 8.0.427, Git 2.52.0. Solo bare temporales locales e identidades ficticias `.invalid`; máximo dos ZIP por caso. No GitHub como remoto de prueba.

## Comandos exactos

Desde `/workspace/NAP`. SDK aislado en `/tmp/nap-dotnet`, sin modificar credenciales/licencias/MSI. Restore/build compilan referencias sin ejecutar tests adicionales.

```sh
/tmp/nap-dotnet/dotnet restore tests/NAP.Tests/NAP.Tests.csproj --nologo -v minimal > /tmp/nap-f74-restore.log 2>&1
/tmp/nap-dotnet/dotnet build src/NAP.Presentation/NAP.Presentation.csproj --no-restore --nologo -v minimal > /tmp/nap-f74-build.log 2>&1
/tmp/nap-dotnet/dotnet build tests/NAP.Tests/NAP.Tests.csproj --no-restore --nologo -v minimal > /tmp/nap-f74-tests-build.log 2>&1

# Sobre 9fb4f1703f5b7fa8c62bac0822898834d02d4e46:
/tmp/nap-dotnet/dotnet test tests/NAP.Tests/NAP.Tests.csproj --no-restore --filter 'FullyQualifiedName~NAP.Tests.GitUiF74Tests|FullyQualifiedName~GitRequiresRealSessionEvidenceAndIndependentCommitPushConfirmations|FullyQualifiedName~RealZipPublishesOnlyAfterRealAuditAndExplicitConfirmation|FullyQualifiedName~F73A2DurablePushedReceiptRecoversOutboxAfterThirdVerificationInterruption|FullyQualifiedName~RevocationAndNewGrantNeverReactivateOldCommittedBatch|FullyQualifiedName~SecondBatchIsBlockedByNativeRequireNoPending' --settings tests/functional.runsettings --logger 'trx;LogFileName=f74-delivery.trx' --results-directory /tmp/nap-f74-results --nologo -v minimal > /tmp/nap-f74-delivery.log 2>&1
/tmp/nap-dotnet/dotnet build src/NAP.App/NAP.App.csproj --no-restore --nologo -v minimal > /tmp/nap-f74-wpf-delivery.log 2>&1
git diff --check
```

Primera compilación cruzada: `/tmp/nap-dotnet/dotnet build src/NAP.App/NAP.App.csproj --nologo -v minimal > /tmp/nap-f74-wpf-crossbuild.log 2>&1`, sobre `1f3b93c…`, restaurando WindowsDesktop.App.Ref. Ambas pasaron; ninguna ejecutó WPF.

## Casos finales

Prefijo `GitUiF74Tests` salvo indicación; máximo 1 ZIP excepto el control de RequireNoPending.

| Prueba | Casos | ZIP/caso | Evidencia |
| --- | ---: | ---: | --- |
| FourModesRequireAReviewedExplicitSave | 4 | 0 | Cuatro modos, lectura/propuesta sin grant/archivos nuevos, solo Save concede, flags exactos, no push |
| InvalidPermissionsPrefixesAndChangedReviewNeverGrantOrRewriteEvidence | 1 | 0 | Permisos/prefijos/máximo, identidad cambiada, CAS antiguo y universo distinto; snapshots preservados |
| RevocationWithoutRemoteBlocksPendingAndCorruptAnchorStopsWithoutRepair | 1 | 1 | Pending durable, remoto offline, revocación sin red, ID/revisión nuevos, Pending bloqueado, Completed y ancla corrupta sin reparación |
| OutboxProjectionPreservesCorrelationAndRejectsReceiptDiscordanceReadOnly | 1 | 1 | Correlación lote/intent, read-only, receipt faltante rechazado |
| EditorSelectionCancellationRevocationAndOwnerReasonsAreExplicit | 1 | 0 | Selector provisional, flags, cancelar revisión/save/revoke, editar invalida, owner ausente, revocar tras inspección offline y corrupción distinta de Disabled |
| ManualCommitAndPushHaveIndependentCancelPathsAndNativeEvidence | 1 | 1 | Pipeline/outputs nativos, cancelación commit preserva evidencia, commit no publica, cancelar push conserva bare, selección OperationId |
| LateGitReadCannotCrossUniversesAndResetDropsTheProposal | 1 | 0 | Fake tardío ignorando token, reset de propuesta/actor/selección, selección por ID en refresh y contadores de cuatro estados |
| AuthenticatedIpcKeepsProposalOnHostDeduplicatesSaveAndRetriesWithRealOwner | 1 | 1 | Pipe autenticado, handles, cross-universe/forgery/Path rechazados, Save replay único, identidad fuera del ledger, owner real, retry offline→Pushed, revocación, host activo/ausente sin fallback |
| ProfessionalUiTests.GitRequiresRealSessionEvidenceAndIndependentCommitPushConfirmations | 2 | 1 | Grant explícito y servicio nativo, commit cancelado/aceptado y push independiente |
| ProfessionalUiTests.RealZipPublishesOnlyAfterRealAuditAndExplicitConfirmation | 1 | 1 | ZIP/Audit/producción siguen separados de autorización Git |
| GitOutboxF73Tests.F73A2DurablePushedReceiptRecoversOutboxAfterThirdVerificationInterruption | 1 | 1 | Receipt Pushed durable tras última reserva: mismo ID/SHA y cero nuevos push/ls-remote al reiniciar |
| GitOutboxF73Tests.RevocationAndNewGrantNeverReactivateOldCommittedBatch | 1 | 1 | Revocar/reautorizar no reactiva Committed antiguo |
| GitOutboxF72Tests.SecondBatchIsBlockedByNativeRequireNoPending | 1 | 2 | Segundo lote bloqueado, primero intacto y sin push automático |
| **Total** | **17** | **11 entradas ZIP** | **0 omitidos** |

Negativos comparan inventarios/hashes o política/ledger/receipt/HEAD/index/tip relevantes, no solo excepción. IPC observa exactamente **un push nativo** para el retry autorizado y registra OperationId/PlanHash/CommitSha/RemoteTip/autorización original en stdout del TRX. F73 conserva sus observaciones JSON en stdout. No se copian políticas/keys ni datos de fixtures al repositorio.

## Historial de ejecuciones y correcciones

Los fallos fueron de fixtures nuevos: política de procesamiento ficticia inactiva en IPC y ZIP manual cuyo nombre no coincidía con asset_id (`package_root_name_mismatch`). Se ajustó también el prefijo del fixture manual a `icons`. No se relajó el motor. Logs/TRX fallidos se conservan.

| Ejecución | SHA | Resultado | ZIP de entrada |
| --- | --- | --- | ---: |
| f74-initial | `1f3b93c313e1e51006f59dbcd77ead06c557f8c6` | 9 Passed / 2 Failed, 11 casos, 7 s | 4 |
| f74-corrected | `fe0c6e255749d98b8e26c7b6726b71d70b27fc8e` | 2 Passed / 1 Failed, 3 casos, 3 s | 2 |
| f74-manual-diagnostic | `fc3f3a38a5e2746a711508130a5be9871a0a5663` | 0 Passed / 1 Failed, solo manual | 1 |
| f74-final | `2995a77948dc6fbc709a1f0f774a285b2414bc93` | 17/17 Passed, 13 s | 11 |
| f74-delivery | `9fb4f1703f5b7fa8c62bac0822898834d02d4e46` | 17/17 Passed, 20 s, tras diagnósticos cerrados y negativo adicional | 11 |

El resumen de un único caso fallido indica `< 1 ms`; no es medición de rendimiento. Duraciones individuales figuran en TRX. Histórico: 49 ejecuciones de casos (45 Passed, 4 Failed), **29 entradas ZIP** en cinco invocaciones motivadas por correcciones/verificación; no 49 casos distintos ni carga.

Filtros anteriores, mismo proyecto y `--no-restore --settings tests/functional.runsettings --results-directory /tmp/nap-f74-results --nologo -v minimal`:

```text
initial: --filter 'FullyQualifiedName~NAP.Tests.GitUiF74Tests'
corrected: --filter 'FullyQualifiedName~NAP.Tests.GitUiF74Tests.ManualCommitAndPushHaveIndependentCancelPathsAndNativeEvidence|FullyQualifiedName~NAP.Tests.GitUiF74Tests.AuthenticatedIpcKeepsProposalOnHostDeduplicatesSaveAndRetriesWithRealOwner|FullyQualifiedName~NAP.Tests.GitUiF74Tests.LateGitReadCannotCrossUniversesAndResetDropsTheProposal'
manual-diagnostic: --filter 'FullyQualifiedName~NAP.Tests.GitUiF74Tests.ManualCommitAndPushHaveIndependentCancelPathsAndNativeEvidence'
final: mismo filtro literal de seis alternativas que delivery
```

Logger de cada ejecución: `--logger 'trx;LogFileName=f74-<nombre>.trx'`; redirección `> /tmp/nap-f74-<nombre>.log 2>&1`. Solo se realizaron esas cinco invocaciones, siempre filtradas.

## Matriz y límites

| IDs de aceptación | Evidencia / reserva |
| --- | --- |
| U01–U09 | Modos/editor/CAS/revocación y regresión grant antiguo. Identidad/scope probados; no se multiplicaron fixtures para cada cambio posible de refs/config. |
| U10–U11 | Contadores/correlación/read-only y receipt faltante; parser/HMAC intactos. |
| U12 | Caso manual nuevo y dos regresiones. Recover conserva motor y añade confirmación; no nuevo crash de cada estado Prepared. |
| U13–U14 | RetryPublication con owner real; rechazo por revocación, host activo/ausente. RetryBlocked comparte mediación y mantiene máquina nativa; sin reensayo exhaustivo. |
| U15–U16 | Fake tardío, scope/generación, pipe real, handles/dedup/payload estricto; no nueva prueba de dos procesos. |
| U17 | A2 y RequireNoPending seleccionados; A1/budgets intactos, sin repetir suite F7.3. Blobs idénticos en manifiesto. |
| U18 | App compila cruzado; falta revisión visual/ejecución real Windows, escalado, teclado y MSI. No certificado. |

Esto referencia evidencia focalizada, **no cobertura de todos los negativos posibles ni aceptación independiente**. El retry UI de lotes históricos Pushed está restringido conservadoramente; véase implementación. Symlinks/configs diversos se siguen rechazando por el motor y adaptador; no se repitió cada negativo histórico.

## Artefactos y reservas

[Directorio de evidencia](f7-4-evidence/): cinco TRX, cinco logs de test, restore/build y ambas compilaciones cruzadas, `test-summary.json`, `source-manifest.json`, `binary-sha256.json`, `SHA256SUMS`. Originales en `/tmp`; resumen conserva SHA-256 previo al saneado. Se sustituyeron hostname y `/home/agent` por identidades sintéticas y normalizaron LF, sin ocultar resultados/warnings. El manifiesto verifica los bytes saneados comprometidos.

Persisten cinco NU1902/NU1903 ImageSharp 3.2.0 y licencia ausente para Release; no cambios de dependencias ni supresión. Linux/Debug y compilación cruzada no certifican Windows/WPF/NTFS/ACL/MSI/Release/producción, autenticación real, corte eléctrico ni hardware. #47/#49 abiertas. Sin workflows, NAP CI, suites generales/estrés/volumen, main/merge, CUTOVER, producción ni fases posteriores.
