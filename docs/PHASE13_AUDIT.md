# Auditoría de entrega — Fase 13

Informe para revisión de ChatGPT. Los cambios permanecen exclusivamente en working tree; no autoriza commit ni publicación del repositorio NAP.

## Estado y archivos

1. **Rama:** `feature/git-production-v1`, verificada en preflight y auditoría final.
2. **HEAD inicial/final:** `bf915bb59765009b3101661374548470601f32c1`; no se ha movido.
3. **Working tree inicial:** limpio (`git status --short` sin salida).
4. **Archivos nuevos:** Core: GitProductionModels.cs, GitProductionProcess.cs, GitProductionSafety.cs, GitProductionInspector.cs, GitProductionReceiptStore.cs, GitProductionService.cs, GitProductionHooks.cs, GitProductionAttributes.cs. Tests: GitProductionTestSupport.cs, GitProductionInspectionTests.cs, GitProductionCommitTests.cs, GitProductionRecoveryPushTests.cs, GitProductionBoundaryTests.cs, GitProductionSecurityTests.cs. Docs: GIT_PRODUCTION.md y PHASE13_AUDIT.md.
5. **Archivos modificados:** src/NAP.Core/NapIssueCodes.cs; README.md; CHANGELOG.md; docs/NAP_MASTER_SPEC.md; docs/PRODUCTION_STORAGE.md; docs/BACKUPS.md; docs/MULTI_UNIVERSE_ARCHITECTURE.md. Ningún csproj, schema, workflow CI, writer Fase 9 ni runner Fase 12 modificado.
6. **Arquitectura:** motor Core explícito, frontera UniverseContext/ProductionRoot, inspector y modelos inmutables, runner tipado privado, receipt store cerrado; GitBackupProcess separado e intacto.
7. **API 13.1:** `GitProductionInspector.Inspect(UniverseContext)` → GitRepositoryStatus; lectura bajo mutex Production.
8. **Formato status:** porcelain v2, NUL, branch, untracked-files=all, ignore-submodules=none.
9. **Parser:** registros 1/2/u/? y headers branch; separación NUL, prefijos de tamaño fijo y rename nuevo/antiguo. Sin interpretación de texto humano ni quoting heurístico.
10. **Estados:** clean, untracked, added, modified, deleted, renamed/copied, typechanged, staged, staged+unstaged, conflicted, submodule; sincronización Synchronized/Ahead/Behind/Diverged/Unknown/RemoteBranchMissing. Operaciones incompatibles, detached/unborn producen STOP con códigos propios.
11. **Branch/upstream:** rama normal con commits, upstream configurado; nombre local == remoto. for-each-ref obtiene identidad upstream real. No crear upstream/ramas.
12. **Remote validation:** URL/pushurl única y coherente, sin secretos/controles/query, mirror/push refspecs/includes locales/rewrites/helpers ext/commands desviados. remote.fetch limitado al mapping estándar único hacia refs/remotes del remoto; custom remote.vcs/proxy/push options STOP. Config global de autenticación del host disponible; overrides locales sensibles STOP.
13. **Protocolos:** HTTPS, SSH, SCP-like SSH. file solo seam interna de fixtures; runtime rechaza file/git://ext::/arbitrarios.
14. **Fingerprint remoto:** SHA-256 del URL normalizado, sin URL en receipts/logs. Config efectiva también ligada por hash.
15. **Tip remoto:** ls-remote --heads del ref exacto, sin fetch ni API/HTTP propio.
16. **HEAD vs remoto:** igualdad exacta obligatoria para preparar y antes de mover HEAD; ningún auto-pull/merge/rebase.
17. **Read-model 13.2:** GitChangeEntry get-only, paths relativos con `/`, OriginalRelativePath nullable, estados index/worktree, kind/tracked/untracked/conflict/submodule y colecciones Ordinal read-only.
18. **Ownership:** todos los dirty paths deben pertenecer a la operación; no hay selección pública de paths arbitrarios.
19. **ProductionAssetResult:** se comprueban universo, RelativeDirectory, FilesVerified, DestinationPath, Digest y SizeBytes; uno o varios resultados legítimos.
20. **Cambios ajenos:** README/untracked/staging/otro asset/job/rename/delete/conflict/submodule → STOP sin ignorarlos ni absorberlos.
21. **SHA/tamaño:** comprobación física antes de plan, revalidación, stage, commit y push; todos los outputs suministrados, aunque algunos no cambien.
22. **Ignored outputs:** check-ignore NUL/stdin; STOP sin add -f.
23. **API 13.3:** `PrepareCommit(context, IEnumerable<ProductionAssetResult>, subject)`; no escribe Git ni receipt.
24. **Plan:** status completo, universo/root binding, HEAD/branch/upstream/remoto/fingerprints/tip, subject, selección exacta y SHA/tamaño.
25. **Stale validation:** identity, HEAD/branch/upstream/config/remoto/tip, porcelain exacto, index, archivos/hashes/sizes/ignore; git_plan_stale antes de escritura, sin actualización implícita.
26. **Subject:** trim, no vacío, máximo 200 chars, sin controles/CR/LF/NUL/surrogates; ArgumentList.
27. **API 13.4:** `Commit(context, GitCommitPlan)`; no Commit(message) genérico.
28. **Staging:** add individual con --literal-pathspecs y -- antes del path exacto; archivos nunca se expanden como glob/opciones.
29. **Ausencia add global:** no add ., add -A ., add -f ni commit -a en runtime Fase 13.
30. **Staged set:** diff cached NUL sin renames + status completo; igualdad exacta, no extra/missing/residual.
31. **Staged bytes:** ls-files stage/mode/blob SHA, cat-file size y contenido, SHA-256 contra producción. Después de commit se verifican objetos del árbol.
32. **Filters/LFS:** status también puede llamar filters; runner desactiva todos los efectivos descubiertos por contexto. `GitProductionAttributes.Require` valida por path los seis attributes filter/working-tree-encoding/ident/text/eol/crlf, aceptando solo unspecified/unset, en Prepare, antes del primer staging y antes de cada Add. Toda transformación activa (incluidos LFS/text=auto/CRLF) y config filter local causan STOP antes de add. core.autocrlf=false efímero evita normalización implícita. Los blobs conservan comprobación SHA/tamaño más comparación literal de bytes; fallo posterior devuelve exclusivamente el index propio al HEAD base.
33. **Hooks:** commit usa -c core.hooksPath=<directorio temporal vacío privado de NAP fuera del repo>; ACL Windows protegida sin permisos de crear scripts/modo Unix 0500, revalidación vacío/ordinario antes de arrancar Git y eliminación no recursiva al terminar. Otras invocaciones mantienen /dev/null, pre-push adicionalmente no-verify. Tests instalan scripts ejecutables en .git/hooks y hooksPath externo; controles positivos con git hook run prueban que cada script podría crear su marcador; el commit NAP no ejecuta ninguno ni modifica config.
34. **Signing:** commit.gpgSign=false, push.gpgSign=false y --no-gpg-sign, solo invocación; config permanente intacta.
35. **Identity:** user.name/email explícitos existentes + var AUTHOR/COMMITTER_IDENT válidos antes de staging. NAP no inventa ni guarda identidad en settings.
36. **Resultado commit:** OldHead, CommitSha, Branch, CommittedPaths, Message, OperationId y UniverseId. HEAD exacto, único parent, árbol/subject/paths/blobs y outputs/limpieza verificados, sin push implícito.
37. **Receipt durable:** StateRoot/git-production; ID g_ + 32 lowercase hex; temp sibling CreateNew, Flush(true), rename controlado y relectura.
38. **Schema receipt:** v1 cerrado; campos exactos documentados en GIT_PRODUCTION.md, sin tokens/URL/roots; estado Prepared/Committed/Pushed. IDENT author/committer necesario para congelar/reintentar el mismo objeto Git, no settings.
39. **Recovery crash:** Prepared en base HEAD permanece Prepared y puede RetryCommit explícito; Prepared con HEAD esperado valida hash completo/parent/tree/subject/paths/blobs/limpieza y pasa a Committed. No adopta commit ajeno por coincidencia parcial ni mueve HEAD. Temps/corruptos quedan para revisión.
40. **API 13.5:** `Push(context, GitCommitResult)` o `Push(context, GitOperationId)` de receipt validado; no PushCurrentBranch.
41. **Pre-push:** receipt/universo/binding/config/branch/upstream/remoto y commit/HEAD/outputs/clean exactos; remote tip == BaseHead, o CommitSha para AlreadyPushed.
42. **Push/refspec:** push --porcelain --no-verify --recurse-submodules=no -- remote HEAD:refs/heads/branch. Rama existente de igual nombre.
43. **No force:** no force, force-with-lease, mirror, all, tags, delete ni otros refspecs.
44. **Post-push:** ls-remote SHA == CommitSha, repo local nuevamente verificado antes de receipt Pushed. Exit code por sí solo insuficiente.
45. **AlreadyPushed:** receipt Committed/Pushed coherente + remote/local CommitSha devuelve AlreadyPushed sin otro push.
46. **Push fallido:** rechazo, remoto cambiado, auth/red → STOP; commit y receipt Committed conservados para retry, sin reset/amend.
47. **Allowlist exacta:** tabla siguiente y contrato GIT_PRODUCTION.md; métodos internos fijos, dispatch privado.
48. **Comandos imposibles:** API sin verbo/args arbitrarios; reset/clean/checkout/switch/merge/rebase/pull/fetch/branch -D/tag/remote set-url/config --global/push --force no tienen entrada operativa. config tipado solo lectura. Tests de estructura además de ejecuciones.
49. **Process runner:** ProcessStartInfo sin shell, ArgumentList, ventana oculta, stdout/stderr binarios redirigidos, stdin NUL/controlado y kill process tree.
50. **Límites:** timeout 2 minutos por Git; 8 MiB por stream, nunca truncar; receipt 4 MiB, JSON profundidad 16. Outputs/blob mayores requieren revisión externa v1.
51. **Credenciales:** Git/host preconfigurado, GIT_TERMINAL_PROMPT=0, GCM_INTERACTIVE=never, core.askPass/GIT_ASKPASS/SSH_ASKPASS vacíos, SSH_ASKPASS_REQUIRE=never, SSH BatchMode/StrictHostKeyChecking; no storage NAP.
52. **Privacidad:** excepciones con código + mensaje estático sin stdout/stderr/URL/roots/credential output, SubjectPath/Detail null. URL/config solo fingerprints persistidos.
53. **Lock/concurrencia:** Production existente para todas las llamadas, busy no bloqueante; writers, snapshot, bundle, otro commit y push serializados. No scope paralelo nuevo ni inversión de jerarquía.
54. **Multiuniverso:** binding universo + ProductionRoot + StateRoot; results de otro root/universo, plan cruzado y receipt copiado STOP. Tests con mismo AssetId/paths/branch/remoto/mensaje y raíces diferentes.
55. **NapIssueCodes:** 26 códigos nuevos específicos de operación/rama/upstream/remoto/sync/conflict/ownership/ignore/stale/index/staging/content/identity/message/commit/receipt/push/rollback/busy/I/O; códigos Git backup existentes reutilizados para repository/unavailable/timeout/output.
56. **SQLite:** ningún cambio de schema/tablas/catalog.
57. **Dependencias:** cero NuGet nuevos; BCL + Git CLI existente.
58. **Baseline tests:** 3024 correctos, 0 fallidos/omitidos, medidos antes de editar. Git 2.52.0.windows.1.
59. **Tests nuevos:** 197 casos añadidos en Fase 13: los 177 iniciales más 20 de la auditoría puntual de hooks/attributes. GitProductionSecurityTests cubre hooks ejecutables y hooksPath externo con controles positivos, directorio vacío privado que rechaza crear scripts, clean custom/LFS con marcador (Prepare/Commit sin add y repo íntegro), attributes de .git/info cambiados después del plan/receipt/primer add, core.autocrlf true/input y blob staged alterado del mismo tamaño con rollback. GitProductionCommitTests cambia el caso CRLF para exigir STOP antes del staging.
60. **Tests finales:** 3221 correctos, 0 fallidos, 0 omitidos; dotnet test NAP.sln Release no-build, incluyendo los 3024 casos de regresión previos. Duración final 4 m 38 s.
61. **Git real:** init/commits/branch fixture/bare/push local reales exclusivamente en temporales. Public service nunca invocado contra NAP ni producción real; cero GitHub/Internet.
62. **Schemas:** 436 checks correctos, mismo resultado baseline/final; schemas sin modificación.
63. **Restore:** dotnet restore NAP.sln correcto; no nuevas dependencias.
64. **Build Release:** dotnet build NAP.sln --configuration Release --no-restore correcto, seis proyectos, 0 warnings/0 errores.
65. **Warnings/errors:** build final 0/0; tests finales 0 fallos. Una ejecución intermedia solapó rebuild y tests y Windows bloqueó DLLs; se corrigió serializando build/test. Las aserciones iniciales/fixtures y casos de seguridad detectados se corrigieron y se revalidaron. Git muestra avisos de conversión LF/CRLF según la copia Windows, sin whitespace errors.
66. **Diff check:** git diff --check final correcto, exit 0. Avisos LF/CRLF son política Git de la copia Windows, no whitespace errors.
67. **Git status:** inventario final al pie; solo siete archivos existentes modificados y dieciséis archivos nuevos autorizados; ningún staged change.
68. **Docs:** GIT_PRODUCTION + esta auditoría; README, CHANGELOG, MASTER_SPEC, PRODUCTION_STORAGE, BACKUPS y MULTI_UNIVERSE actualizados. Fase 13 HECHA y siguiente Fase 14.
69. **Limitaciones/riesgos a revisar:** límites de salida/receipt, SSH/SCP policy conservadora, filters/LFS/encoding STOP, Unknown ancestry sin fetch, Prepared/Committed pendiente impide nueva operación, filesystem Flush/rename sin fsync portable de directorios. Writers externos no sujetos al mutex pueden competir; se revalida pero no se promete sandbox contra control total del host. Un push normal no proporciona CAS entre ls-remote y el handshake: eliminación/recreación remota concurrente depende de las reglas del servidor. Tests ejecutados en Windows; CI Ubuntu preservada pero no ejecutada en este host. Casos de nombres exclusivamente Unix ejercitan parser en Windows y creación real condicionada en Unix.
70. **Fase 12 intacta:** GitBackupProcess, GitBundleService, RepositorySnapshotService, DB backup/restore/retention sin modificación; suite previa completa incluida.
71. **Fase 14:** no iniciada; sin UI Git/Backups profesional, RoDo, scheduler ni daemon.
72. **Repos reales usuario:** ninguna operación Git operativa de tests sobre esos repos; únicamente preflight/diff/status de lectura sobre NAP.
73. **NAP sin publicación:** NO commit, NO push, NO merge, NO pull/PR, NO branch switch, NO reset/clean. HEAD original preservado. Todos los cambios auditables en working tree.

## Allowlist exacta introducida

| Categoría | Comandos y argumentos fijos |
| --- | --- |
| Read-only local | rev-parse --show-toplevel / --absolute-git-dir / --is-bare-repository / --verify HEAD |
| Read-only local | status --porcelain=v2 -z --branch --untracked-files=all --ignore-submodules=none |
| Read-only local | config --null --list; for-each-ref --format=%(refname)%00%(upstream)%00%(upstream:remotename)%00%(upstream:remoteref) refs/heads/ |
| Read-only local | ls-files -z --stage; check-ignore -z --stdin; check-attr -z --stdin filter working-tree-encoding ident text eol crlf |
| Read-only local | var GIT_AUTHOR_IDENT / GIT_COMMITTER_IDENT; merge-base --is-ancestor SHA SHA |
| Read-only local | diff --cached --name-status --no-renames -z --no-ext-diff --no-textconv |
| Read-only local | diff-tree --no-commit-id --name-status --no-renames -r -z BASE COMMIT -- |
| Read-only local | hash-object -t commit --stdin (sin -w); cat-file commit/blob SHA; cat-file -s SHA; ls-tree -r -z SHA |
| Staging | add -- PATH (individual) modifica ese index entry y objetos blob; write-tree crea objetos tree. No mueve HEAD ni toca archivos físicos. |
| Commit | commit --no-gpg-sign --cleanup=verbatim --message SUBJECT crea objeto y avanza solo la rama validada; árbol exacto, hooks/firma deshabilitados. No push/amend/otros refs. |
| Remote read-only | ls-remote --heads -- REMOTE refs/heads/BRANCH no fetch ni refs locales. |
| Remote write | push --porcelain --no-verify --recurse-submodules=no -- REMOTE HEAD:refs/heads/BRANCH publica solo el commit propio, fast-forward normal. No force/otros refs/tags. |
| Rollback acotado | restore --staged --source=BASE -- PATH (individual) restituye exclusivamente paths intentados propios presentes en index o HEAD base, también ante eliminación staged inesperada. No working tree/HEAD/paths ajenos. |

Prefijos de todas las invocaciones: --no-pager, --literal-pathspecs (excepto check-ignore/check-attr), -C ProductionRoot y configuración efímera controlada fsmonitor=false, untrackedCache=false, hooksPath=/dev/null (commit: directorio vacío privado NAP), core.autocrlf=false, askPass vacío, commit/push.gpgSign=false, push.followTags=false, push.recurseSubmodules=no, gc.auto=0, maintenance.auto=false, i18n.commitEncoding=UTF-8. Cada filter descubierto se desactiva solo para la invocación con clean/smudge/process vacíos y required=false; config original se fotografía sin esos overrides. No config write.

## Validación final

Validación realizada en Windows el 2026-10-08 (Europe/Madrid), con Git 2.52.0.windows.1.

| Check | Resultado |
| --- | --- |
| Preflight rama | feature/git-production-v1 |
| HEAD inicial/final | bf915bb59765009b3101661374548470601f32c1 |
| Working tree inicial | Limpio |
| dotnet restore NAP.sln | Correcto, dependencias existentes actualizadas |
| dotnet build NAP.sln --configuration Release --no-restore | Correcto, 0 warnings/0 errores |
| Baseline tests | 3024 correctos, 0 fallidos/omitidos |
| dotnet test NAP.sln --configuration Release --no-build | 3221 correctos, 0 fallidos/omitidos, 4 m 38 s |
| Tests añadidos | 197 (20 de la auditoría puntual de seguridad) |
| pwsh -ExecutionPolicy Bypass -File scripts/Test-MultiUniverseSchemas.ps1 | 436 checks correctos, sin cambios de schemas |
| git diff --check | Correcto, exit 0 |
| git diff --cached --stat | Vacío; nada staged |
| Working tree final | 7 archivos modificados, 16 nuevos autorizados |
| Operaciones Git sobre NAP | Solo lectura; sin commit/push/merge/PR/switch/reset/clean |

Evidencia de tests: `tests/NAP.Tests/TestResults/phase13-security-final.trx` (artefacto generado/ignorado). Los dieciséis archivos nuevos se inventarían separadamente y no aparecen en git diff --stat hasta staging.

Inventario final `git status --short`:

```text
 M CHANGELOG.md
 M README.md
 M docs/BACKUPS.md
 M docs/MULTI_UNIVERSE_ARCHITECTURE.md
 M docs/NAP_MASTER_SPEC.md
 M docs/PRODUCTION_STORAGE.md
 M src/NAP.Core/NapIssueCodes.cs
?? docs/GIT_PRODUCTION.md
?? docs/PHASE13_AUDIT.md
?? src/NAP.Core/GitProductionAttributes.cs
?? src/NAP.Core/GitProductionHooks.cs
?? src/NAP.Core/GitProductionInspector.cs
?? src/NAP.Core/GitProductionModels.cs
?? src/NAP.Core/GitProductionProcess.cs
?? src/NAP.Core/GitProductionReceiptStore.cs
?? src/NAP.Core/GitProductionSafety.cs
?? src/NAP.Core/GitProductionService.cs
?? tests/NAP.Tests/GitProductionBoundaryTests.cs
?? tests/NAP.Tests/GitProductionCommitTests.cs
?? tests/NAP.Tests/GitProductionInspectionTests.cs
?? tests/NAP.Tests/GitProductionRecoveryPushTests.cs
?? tests/NAP.Tests/GitProductionSecurityTests.cs
?? tests/NAP.Tests/GitProductionTestSupport.cs
```

**Fase 13 — HECHA. 13.1 — HECHO; 13.2 — HECHO; 13.3 — HECHO; 13.4 — HECHO; 13.5 — HECHO. Siguiente: Fase 14 — UI profesional.**
