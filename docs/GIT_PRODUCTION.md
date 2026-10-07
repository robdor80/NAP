# Git de ProductionRoot — Fase 13

Fase 13 — HECHA. 13.1 — HECHO; 13.2 — HECHO; 13.3 — HECHO; 13.4 — HECHO; 13.5 — HECHO.
Siguiente: Fase 14 — UI profesional.

El motor Core publica únicamente outputs verificados de NAP en el Git ya configurado del universo activo. Toda llamada recibe `UniverseContext`; no existe una raíz Git arbitraria ni un runner público genérico. `GitBackupProcess` y los contratos de Fase 12 conservan su comportamiento. SQLite v1, pipeline, WPF y dependencias NuGet permanecen intactos.

## API explícita

```csharp
var inspector = new GitProductionInspector();
GitRepositoryStatus status = inspector.Inspect(context);
var git = new GitProductionService();
GitCommitPlan plan = git.PrepareCommit(context, verifiedProductionResults, "Publish verified asset");
GitCommitResult commit = git.Commit(context, plan);
// Acción posterior e independiente, decidida por el caller:
GitPushResult push = git.Push(context, commit);
```

`ListOperations(context)`, `ReadReceipt(context, operationId)`, `Recover(context, operationId)`, `RetryCommit(context, operationId)` y `Push(context, operationId)` permiten recuperación explícita desde StateRoot. Ninguna llamada conecta auto-Git a `AssetExecutionCoordinator`. No hay botones Git ni UI de Fase 14.

## Inspección y frontera

La raíz autorizada es exclusivamente `context.Storage.ProductionRoot`: existente, validada por `ProductionStorageRootValidator`, toplevel exacto, `.git` directorio local ordinario. Se reutilizan las comprobaciones físicas de backup para rechazar enlaces, entradas especiales, alternates, object stores externos, partial clones/promisor, gitdir files y worktrees. Se rechazan includes locales antes de invocar Git, repos bare, shallow history, reemplazos y grafts. HEAD debe ser una rama normal con commits; merge, rebase, cherry-pick, revert, bisect, sequencer y locks incompatibles requieren revisión externa.

El inspector usa `status --porcelain=v2 -z --branch --untracked-files=all --ignore-submodules=none`. El parser separa NUL y solo divide el prefijo estructurado con un número acotado de campos: conserva espacios, Unicode, comillas, guiones, controles válidos en nombres Unix y el par nuevo/antiguo de renames. No interpreta salida humana. `GitChangeEntry` expone RelativePath, OriginalRelativePath, IndexStatus, WorktreeStatus, ChangeKind, IsTracked, IsUntracked, IsConflict e IsSubmodule. Colecciones read-only y orden Ordinal.

`GitRepositoryStatus` congela universo, binding, HEAD, branch, upstream real, remote, remote branch, fingerprint del remoto, tip observado, sincronización e index/worktree. `Synchronized`, `Ahead`, `Behind`, `Diverged`, `RemoteBranchMissing` y `Unknown` son estados factuales. Cuando el objeto remoto no está localmente disponible, se devuelve `Unknown`: no se inventa ancestry ni se hace fetch. Fallo de consulta devuelve `git_remote_unavailable` + Error/Stop. Detached/unborn/operación incompatible devuelven códigos específicos.

Inspección y preparación no escriben working tree, index, HEAD, refs, config, `.git` ni timestamps deliberados. `GIT_OPTIONAL_LOCKS=0`. Los clean filters descubiertos se desactivan incluso durante status, porque status puede ejecutarlos. La v1 exige publicación sin filtros y puede rechazar o mostrar diferencias físicas en repos que dependan de ellos.

## Upstream, transporte y privacidad

`for-each-ref` obtiene refs y campos upstream/remotename/remoteref con delimitadores explícitos. La rama local debe seguir una rama remota del mismo nombre; NAP no crea upstream ni modifica remotes/config. La configuración efectiva se fotografía mediante un hash; incluye identidad y políticas del host sin persistir los valores de autenticación.

HTTPS, SSH y SCP-like SSH son los únicos transports productivos. Se prohíben `file`, `git://`, `ext::`, protocolos arbitrarios, URL vacías/controladas, HTTPS con userinfo, SSH con password/userinfo ambiguo, query/fragment, múltiples URL, pushurl diferente, mirror, push refspecs configurados, URL rewrites, comandos upload/receive-pack, core.sshCommand y desviaciones de raíz. Overrides locales credential/HTTP requieren revisión externa. Los helpers de autenticación configurados por el host siguen disponibles; NAP no almacena ni muestra sus credenciales.

El remote.fetch debe ser el mapeo estándar único `+refs/heads/*:refs/remotes/<remote>/*`, aunque no se ejecute fetch: Git push puede actualizar localmente tracking refs mediante ese mapeo. Se rechazan mappings hacia ramas locales/otros namespaces, custom remote.vcs, push options y proxies por remoto. Esto evita desviar escrituras locales o comandos de transporte.

`ls-remote --heads -- <remote> refs/heads/<branch>` consulta el tip real sin fetch. El SHA-256 del URL normalizado identifica el remoto; el receipt conserva exclusivamente el fingerprint. No se imprime ni persiste el URL. Antes de preparar y ejecutar commit se exige `HEAD == tip remoto`. Ahead/behind/diverged/missing/unreachable causan STOP. Push consulta nuevamente el remoto.

SSH usa BatchMode y StrictHostKeyChecking; `GIT_TERMINAL_PROMPT=0` y `GCM_INTERACTIVE=never` impiden interacción habitual Git/GCM. core.askPass y los mecanismos GIT_ASKPASS/SSH_ASKPASS se vacían para la invocación; SSH_ASKPASS_REQUIRE=never. Un mecanismo externo del host que ignore esas opciones queda acotado por timeout. La v1 no instala credenciales, hosts ni claves. Una seam interna habilita exclusivamente repos locales en tests y aísla allí la configuración global; no existe un switch público para relajar transports.

## Ownership y plan

`GitCommitSelection` y `GitCommitPlan` tienen constructores internos. La selección solo se deriva de `ProductionAssetResult(s)`: UniverseAssetKey, RelativeDirectory y FilesVerified. Se comprueba destino exacto, contención, ausencia de `.git`/traversal/reparse, fichero regular, SHA-256 y tamaño. Todos los outputs suministrados, incluidos los que no cambiaron, se revalidan; solo los paths realmente dirty se stagean.

`DirtyPaths == OwnedPathsThatActuallyChanged`. README manual, untracked desconocidos, otro asset/job pendiente y staging previo causan STOP. Conflicts, submodules, renames, deletions y type changes nunca se publican automáticamente. `check-ignore -z --stdin` detecta outputs ignorados: no se usa add -f ni se ocultan cambios mediante ignore.

El plan read-only congela universo/binding de ProductionRoot y StateRoot, HEAD/branch/upstream/remoto/fingerprints/tip, status porcelain exacto, mensaje, selección y SHA/tamaño de todos los outputs. El caller aporta un único subject trim, no vacío, máximo 200 caracteres, sin controles/newline/NUL/surrogates. Cualquier cambio relevante produce `git_plan_stale` antes de escritura; no se refresca el plan. La v1 no permite iniciar otra operación mientras haya un receipt Prepared/Committed pendiente: debe recuperarse, reintentarse o publicarse explícitamente.

## Commit asistido

Se verifica identidad Git explícita existente (`user.name`, `user.email`, author/committer IDENT), sin inventarla ni guardarla en settings. `GitProductionHooks` crea para cada commit un directorio temporal exclusivo `nap-git-hooks-*`, fuera del repositorio, sin entradas y sin permiso de crear scripts: ACL Windows protegida para el propietario con lectura/ejecución/eliminación; modo Unix 0500. No acepta paths ni scripts del caller. Se comprueba que sea ordinario, no reparse y vacío inmediatamente antes de arrancar Git. El commit recibe `-c core.hooksPath=<directorio vacío NAP>`; al terminar se elimina sin borrado recursivo. Las demás invocaciones mantienen `core.hooksPath=/dev/null`. Firma commit/push deshabilitada solo para la invocación. Config permanente intacta.

Secuencia bajo mutex Production: revalidar y comprobar attributes de todos los outputs → receipt Prepared durable → revalidar y volver a comprobar attributes de todos los outputs → comprobar attributes inmediatamente antes de cada `add -- <un path exacto>` → verificar conjunto staged y ausencia de residual → verificar mode/tamaño/SHA y comparación literal de bytes de blobs staged → `write-tree` → congelar el hash exacto del commit esperado en Prepared → revalidar de nuevo → commit → comprobar HEAD/parent/árbol/mensaje/paths/limpieza/outputs → receipt Committed.

`--literal-pathspecs` y `--` impiden que nombres con comodines o guiones se interpreten como opciones/pathspecs. check-ignore/check-attr reciben nombres literales por stdin NUL y no aceptan el switch global literal-pathspecs. No existe `git add .`, `git add -A .` ni `commit -a`.

`GitProductionAttributes.Require` usa `check-attr -z --stdin filter working-tree-encoding ident text eol crlf`, con paths separados/terminados por NUL; valida cada triple path/atributo/valor y solo acepta `unspecified` o `unset`. Prepare inspecciona todos los outputs autorizados, incluidos los que no cambiaron; Commit repite la comprobación antes del primer staging y cada Add la exige para su path. Filters custom, LFS, encoding, ident, text (incluido auto), eol y crlf activos producen STOP antes de staging. También se rechaza cualquier sección filter local. `core.autocrlf=false` se impone solo para la invocación, preservando bytes CRLF sin modificar config permanente. Todos los filtros efectivos descubiertos tienen clean/smudge/process vacíos y required=false, incluso en status, que puede intentar ejecutar clean.

Como defensa adicional, `diff --cached --name-status --no-renames -z` compara el staged set con la selección; `ls-files --stage -z`, `cat-file -s` y `cat-file blob` verifican mode/stage, tamaño, SHA-256 y comparan literalmente bytes del blob con los bytes físicos verificados. Un blob alterado, incluso del mismo tamaño, produce STOP y rollback exclusivo del index propio. No se acepta un pointer LFS en lugar del output físico.

Si falla antes de mover HEAD, se restaura exclusivamente el staging de paths intentados por NAP presentes en el index o en el HEAD base, con `restore --staged --source=<base> -- <path exacto>` individual. Esto incluye una eliminación staged inesperada de un path propio sin recrear su archivo físico; excluye outputs nuevos ya retirados del index. No toca bytes físicos, otros paths ni HEAD. Rollback fallido o staging ajeno residual produce `git_rollback_failed`; nunca se declara un index limpio falsamente. Si HEAD pudo haberse movido, se conserva para recuperación. No hay reset, amend ni reescritura.

## Receipt v1 y recuperación

Finales: `StateRoot/git-production/g_<32 lowercase hex>.json`. Parser cerrado con rechazo de campos desconocidos/duplicados/tipos/versiones, paths y estados incoherentes. Binding hash incluye universo, raíz de producción y raíz de estado normalizadas; se rechaza copiar un receipt entre raíces o universos.

Campos: schema_version, operation_id, universe_id, repository_binding, state, base_head, branch, upstream, remote_name, remote_branch, remote_fingerprint, remote_tip, configuration_fingerprint, porcelain, message, paths, owned_files(relative_path/sha256/size_bytes), expected_tree, expected_commit_sha, commit_sha, author_ident, committer_ident. Los IDENT congelados son hechos necesarios del commit (incluyen autor/email y fecha Git), no settings ni credenciales. No se guardan URL, tokens, passwords ni roots absolutos.

Publicación: temp sibling CreateNew → Flush(true) → cierre → revalidación del final previo → Move/replace controlado → lectura cerrada y comparación. Orphan temps/corruptos no se adoptan ni eliminan automáticamente. El inventario exige revisión de entradas ajenas/temps.

Prepared en base HEAD no acredita commit terminado; `Recover` valida evidencia y deja Prepared. `RetryCommit` puede retirar únicamente staging propio demostrado, revalidar la fotografía original y reintentar la misma operación. Si el commit esperado ya fue congelado, se conservan identidades/fechas exactas. Prepared con HEAD distinto solo pasa a Committed si coincide con expected_commit_sha y se verifica completo: objeto Git, árbol, único parent, subject, paths, blobs y outputs/limpieza. El parser recalcula el hash completo del objeto esperado (SHA-1/SHA-256). Un commit ajeno con mismo parent/paths/subject pero diferente identidad no se adopta. Ninguna recuperación mueve HEAD.

## Push explícito

Push exige GitCommitResult propio o ID de receipt Committed/Pushed. Se revalida universo/binding/config/branch/upstream/remoto, HEAD exacto, commit propio y working tree/index limpios. Si el tip remoto es BaseHead: push normal único `push --porcelain --no-verify --recurse-submodules=no -- <remote> HEAD:refs/heads/<branch>`. followTags/signing/hooks deshabilitados. No force, lease, mirror, all, tags, delete ni creación intencionada de refs.

Se exige SHA remoto exactamente CommitSha mediante ls-remote posterior y se revalida también el repo local antes de marcar Pushed. Exit code por sí solo no acredita éxito. Tip remoto ya CommitSha + repo/receipt coherentes devuelve AlreadyPushed sin otro push. Otro tip causa STOP. Rechazo/red/auth/protección conserva commit local y receipt Committed; el caller puede reintentar tras revisión.

La comprobación previa y un push normal no constituyen una transacción con el servidor: si un administrador borra/recrea la rama exactamente durante esa ventana, se depende de las reglas del servidor. Git CLI sin force/lease no proporciona compare-and-swap remoto adicional. NAP no implementa sandbox frente a un actor con control total del host; sí elimina los desvíos/config/scripts de repos detectables y documenta los límites.

## Allowlist exacta

Todos los comandos son métodos internos tipados que construyen ArgumentList; el único dispatch con verbos/args es privado.

| Grupo | Comando fijo |
| --- | --- |
| Lectura local | rev-parse --show-toplevel; --absolute-git-dir; --is-bare-repository; --verify HEAD |
| Lectura local | status --porcelain=v2 -z --branch --untracked-files=all --ignore-submodules=none |
| Lectura local | config --null --list; for-each-ref --format=refname/NUL/upstream/NUL/remotename/NUL/remoteref refs/heads/ |
| Lectura local | ls-files -z --stage; check-ignore -z --stdin; check-attr -z --stdin filter working-tree-encoding ident text eol crlf |
| Lectura local | var GIT_AUTHOR_IDENT; var GIT_COMMITTER_IDENT; merge-base --is-ancestor <sha> <sha> |
| Lectura local | diff --cached --name-status --no-renames -z --no-ext-diff --no-textconv |
| Lectura local | diff-tree --no-commit-id --name-status --no-renames -r -z <base> <commit> -- |
| Lectura local | hash-object -t commit --stdin (sin -w); cat-file commit/blob <sha>; cat-file -s <sha>; ls-tree -r -z <sha> |
| Staging | add -- <path exacto>; write-tree |
| Commit | commit --no-gpg-sign --cleanup=verbatim --message <subject> |
| Remoto lectura | ls-remote --heads -- <remote> refs/heads/<branch> |
| Remoto escritura | push --porcelain --no-verify --recurse-submodules=no -- <remote> HEAD:refs/heads/<branch> |
| Rollback acotado | restore --staged --source=<base> -- <path propio presente en index o HEAD base> |

`add` modifica únicamente entradas index de la selección y crea blobs; `write-tree` crea objetos tree sin mover HEAD; `commit` crea el commit y avanza únicamente la rama normal congelada, con index exacto; `push` publica únicamente HEAD en la rama upstream existente, sin otros refs/tags; `restore --staged` restaura únicamente esas entradas index sin tocar working tree/HEAD. Git puede actualizar sus metadatos operativos asociados a cada acción; ninguna acción cambia permanentemente config/remotes, ejecuta hooks ni borra outputs.

`reset`, `clean`, `checkout`, `switch`, `merge`, `rebase`, `pull`, fetch arbitrario, branch -D, tag, remote set-url, config --global, push --force y force-with-lease no tienen una entrada operativa en la API. La función config disponible es exclusivamente lectura, sin parámetros del caller. GitBackupProcess mantiene su allowlist privada de backup y sin red.

## Runner, mutex y límites v1

ProcessStartInfo, UseShellExecute=false, CreateNoWindow=true, ArgumentList, stdin binario acotado donde corresponde, stdout/stderr drenados binarios, timeout 2 minutos por invocación, 8 MiB máximo por stream y kill entire process tree. La salida de error nunca se adjunta a excepciones públicas. Los blobs individuales mayores que el límite requieren revisión externa en v1; no se truncan ni publican parcialmente. Receipt máximo 4 MiB; JSON MaxDepth 16. No shell/cmd/PowerShell runner, HTTP propio, GitHub API ni paquetes nuevos.

Inspect, Prepare, Commit, Push, inventario/lectura/recuperación/retry adquieren `ExecutionMutex.Acquire("Production", ProductionRoot)`, no otro scope. Serializa ProductionAssetExecutor, RepositorySnapshotService, GitBundleService y operaciones Git del mismo root. Busy es no bloqueante. La jerarquía Job → Production → Archive → Catalog no cambia; el servicio solo adquiere Production. Los cambios de programas externos no sujetos al mutex se detectan por revalidación, pero no se ofrece exclusión global frente a editores Git externos.

## Tests

Fixtures de producción real + repos Git temporales SHA-1/SHA-256 y bare locales; cero Internet. Detección NUL, nombres raros, estado index/worktree, conflicts/submodules/operaciones, límites físicos y config hostil. Ownership, planes stale, contenido staged, hooks/signing, rollback, receipts cerrados y crashes, push/rechazo/verificación/idempotencia, multiuniverso y mutexes contra escritores/backups/otras operaciones. Las operaciones genéricas de fixtures viven exclusivamente en tests y exigen raíz temporal; no se usa NAP ni un ProductionRoot real del usuario como víctima.

Validación y auditoría de entrega: [PHASE13_AUDIT.md](PHASE13_AUDIT.md).
