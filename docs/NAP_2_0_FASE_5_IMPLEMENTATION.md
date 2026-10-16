# NAP 2.0 — Fase 5: implementation and acceptance log

Authorization: 2026-10-16, explicit approval of P5.1–P5.10 and a separate internal host process. Base: `cd7532f011eea8a72e89848234b259f1f5293e15` (PR #46). Work branch: `codex/nap-2-fase-5-automation-host`.

Only synthetic isolated universes are permitted. No service installation, real credentials, account/ACL changes, Nimroel data changes, production activation or CUTOVER is authorized. One product/installation remains the target. No merge is authorized.

## P5.1 — universe ownership

Dedicated bounded mailbox and native owner thread per universe. Workflow delegates run synchronously on that thread; no owner crosses an await. Shutdown drains accepted work and releases ownership on the acquiring thread. Failed startup and individual command failures complete callers explicitly. The delegate API is only for trusted in-process composition, never IPC.

Validation: `AutomationRuntimeTests`, Windows x64/.NET runtime 8.0.8, **3/3 passed**, no skips (`artifacts/phase5/p51/p51.trx`). Isolated build outputs avoid replacing baseline testhost DLLs. The first overlapping build failed copying locked DLLs and was not a test execution. Existing baseline symlink tests cannot create symlinks without a privilege unavailable in this session; no system changes are made to accommodate them. Existing ImageSharp warnings remain visible. P5.2–P5.10 are not yet accepted.

## P5.2 — durable control

Independent versioned SQLite control store, exact roots/profile binding, paused/stopped defaults, durable sessions, CAS commands, idempotent receipts, inhibition history and item cancellations. No workflow permission is inferred from control state. Unknown artifacts/schema, missing bindings and corrupt history fail closed without cleanup. IO is short and separate from universe ownership; transaction rollback leaves the command replayable.

Validation: runtime+control **6/6 passed**; additional rollback and updated control tests **4/4 passed** (`p52-final.trx`, `p52-rollback.trx`; overlapping totals are not summed). Baseline historical selection: **94 passed / 4 failed / 0 skipped**, all four failures are inability to create symlinks (`phase5-baseline.trx`), before workflow refactors. No tests or system permissions were weakened. P5.3–P5.10 pending.

## P5.3 — staged admission

Read-only async readiness observations leave the owner available. Owner-thread continuation rejects changed samples and retains all fresh hash/CRC/source/snapshot checks. Legacy Admit uses the same implementation. Batch observation binding and idempotent batch creation preserve queue schema v1. Fresh retention readers accept cancellation and always drain/dispose before returning.

Validation: staged admission **2/2 passed** (`p53-staged.trx`, normal output tree); admission selection **43 passed / 1 failed** (existing symlink privilege). An isolated-output broad name filter also selected a historical pinned-image test whose repository-relative fixture lookup is incompatible with that output path (**14 passed / 1 failed**); this is retained as a failed invocation, not software acceptance. Subsequent historical runs use the normal build layout. P5.4–P5.10 pending.

## P5.4 — asynchronous audit transport

The existing audit implementation is factored into BeginAudit (intent/budget), HTTP-only transport and owner-thread Complete/Fail. The synchronous public API delegates to those same stages. Completion binds epoch/item/attempt/Job/request/snapshot/grant/provider/model and rejects late/duplicate completion. A paused host may retain ResponseReady without publishing PASS. Late transport faults are observed without ledger callbacks; timeout explicitly leaves remote completion unknown and does not refund budget.

Validation: existing durable audit + staged tests **53/53 passed** (`p54.trx`); final shared-transport and revocation checks **5/5 passed** (`p54-shared-transport.trx`), no skips. P5.5–P5.10 pending.

## P5.5 — host scheduler

Explicit laboratory-only composition requires all roots under a generated `TEMP/nap-host-*` container; it cannot load real user settings. Controller instance lease, dedicated owners, bounded streaming discovery/probes, round-robin scheduling, policy-bound activation, one heavy operation and one reserved HTTP slot, preparation retry scheduling and individual scope blocking. Prior-session activation resumes only with its explicit persisted flag. Quota allocations divide the host retained budget across universes/admission/preservation. No arbitrary production mode is provided in this phase.

Validation: host startup/authority, two-universe same-AssetId execution with sentinels, competing deployment and laboratory-root rejection **3/3 passed** (`p55.trx`, 12 s). Cooperative execution suspension, durable stop and remaining integration are P5.6 onward, not claimed here.

## P5.6 — suspension and recovery boundaries

WorkflowRunControl inhibits only at complete admission/preparation receipts and outer execution-file/journal/catalog boundaries. Inner rename/publication callbacks cannot suspend before native ownership is recorded. Suspended execution retains Running/claim and returns a distinct result; resumption revalidates the same Job. Host stop persists per-universe Stop before draining, with a separate diagnostic if persistence fails; it never kills an owner. Control command replay preserves its original meaning across sessions without rolling current state back.

Validation: suspension after archive file/index, production native receipt, catalog commit and COMPLETED journal; original preservation/resume; host regressions **9/9 passed** (`p56.trx`, 28 s). Actual process-kill recovery, durable host cancellation and automatic-restart conditions will also be exercised in P5.10. No power-loss certification is implied.

## P5.7 — durable capture and projections

Versioned per-universe RuntimeState store records cohort intent before idempotent queue creation. A restart reuses the open cohort; queue-close/runtime-close interruptions reconcile without inventing another processing attempt. Read projections report current durable outcomes for closed cohorts and are paginated. Capture validation streams its rows.

The first end-to-end summary test exposed a scheduler defect: an already-completed idle turn could indefinitely postpone accepting an HTTP response in a single-universe host. The controller now collects that completed turn before accepting the response. The failed runs remain in `p57.trx` and `p57-diagnosis.trx`. Final batch+host tests **5/5 passed** (`p57-final.trx`, 9 s), including closed-cohort COMPLETED evidence. P5.8–P5.10 pending.

## P5.8 — process and authenticated local protocol

NAP.AutomationHost is a separate internal executable, with explicit laboratory launch configuration and no default settings/service/production mode. Named pipes enforce the same OS principal for this laboratory, with mutual HMAC-SHA256 challenges, random nonces, version/correlation validation, duplicate-field rejection, bounded frames and four connections. The principal comes from the pipe, never an actor field supplied by the client. Accepted handler operations drain after disconnect. Different-identity UI/service authorization, protected installation and real startup policy remain deployment work; this same-principal laboratory channel does not certify them.

Validation: wrong-key rejection without handler invocation, successful mutual authentication, unsupported version and disconnect/drain **2/2 passed** (`p58-final.trx`); host executable Release build **0 errors / 0 warnings**. A first compile exposed an ambiguous HMAC overload; corrected with an explicit byte-array input. Manual service dispatch and WPF integration follow in P5.9.

## P5.9 — desktop and manual compatibility

WPF explicitly connects using `--automation-config=...`; this mode never loads or writes real/default user settings or credentials and never falls back to a local writer. A separate launcher configuration remains authoritative. Host status/pause/resume/activation/stop use existing confirmation and commands; closing the client leaves the host running. Actor dispatch reuses the existing ProfessionalUiService/normalization/Core engines for preparation, publication, objectives, backups/restore/retention, Git and catalog. Incompatible mutations require a paused/drained universe. Manual and automatic HTTP share one slot. Read-only views are explicitly marked and rejected at native mutation sinks; actual plans/results stay behind typed, universe-bound server handles. Large read data uses bounded authenticated chunks; thumbnails are transferred to a separate client cache. Native/manual writer entry points share the same owner exclusion without creating a queue.

Desktop command intent precedes effects, stores principal and a request fingerprint, and replays the bound response. An intent without a bound response after interruption reports unknown outcome rather than executing again. These are not distributed transactions. Native journals/receipts remain authoritative. Handle/session and read-buffer budgets are bounded; read projections cannot substitute for server evidence.

Validation: initial desktop+miniature selection **23/23 passed** after fixing same-universe concurrent thumbnail requests; wider automation/manual/normalization/four historical WPF selection **84/84 passed** (`p59-regression.trx`). Final direct desktop routes and the new independent-host WPF subprocess **5/5 passed** (`p59-final.trx`, 12 s): manual COMPLETED, catalog/documents/objectives/database backup+restore, normalization preview/decision, explicit commit/push against a local disposable bare repository, and client close with host still alive. Git local transport is injected only by the test; launch mode does not enable it. Screenshot `artifacts/phase5/wpf/remote-shell/automation-wpf.png` was visually inspected. Builds of the full Release solution passed with zero errors/warnings (incremental; existing dependency advisories remain known).

Failures retained: the initial manual fixture used a ZIP basename incompatible with the historical manual package-root contract; the fixture was corrected, not the validator. The initial Git bridge assertions passed but teardown failed on read-only Git object attributes; cleanup now checks its absolute TEMP fixture roots and skips reparse points before resetting only fixture file attributes. A first thumbnail ownership gate rejected simultaneous grid/detail reads; they now serialize local ownership without weakening native exclusion. P5.10 acceptance and full regression remain pending.

## P5.10 — partial regression corrections; paused by user

Controlled stop ordered on 2026-10-16 because of quota. No new development or extended test run was started after that order. No testhost, dotnet, AutomationHost or WPF process was running at the stop inspection. This phase is incomplete and is not accepted for production.

The broad preacceptance run on `98cbca5` (excluding volume tests) executed **3742: 3724 passed, 18 failed, 0 skipped**. Five failures require Windows symlink creation privilege unavailable in this session; they remain unverified, with no privilege changes. Thirteen failures concerned historical exception contracts, a reflection helper selecting constructors, a startup race and a 30-second host test deadline under full parallel load. Native entry points now preserve IOException/CatalogBusy/BackupBusy/GitBusy contracts. The helper selects exactly one matching constructor by argument signature; historical safety assertions were not relaxed. Host test functional deadlines are 120 seconds, not a throughput claim; historical volume limits were not changed.

Targeted corrections: **101/101 passed** (`p510-compatibility-corrections.trx`); latest scheduler/runtime/batch selection **8/8 passed** (`p510-scheduler-corrections.trx`). These runs overlap and must not be summed as unique coverage. No full rerun after corrections has occurred.

Additional partial changes: bounded owner-acquisition retry; disposal/signal race handling; alternating admission/workflow with pending-item backpressure and idle backoff; a first completed-item startup verification pass; read-only control query and WPF command availability. The laboratory checkpoint callback is scaffolding only: launch configuration/program and process-kill acceptance scenarios are NOT implemented. Completed-item recovery and new control availability still require dedicated integration tests. All these changes are conserved, not declared complete.

Continuation details, unverified guarantees and exact next steps: `NAP_2_0_FASE_5_CONTINUITY.md`. A committed TRX manifest (`NAP_2_0_FASE_5_TEST_EVIDENCE.json`) records all retained run counters, failed test names and SHA-256 hashes; raw TRX files and WPF PNG remain local ignored artifacts, not uploaded evidence. `NAP_2_0_FASE_5_CHANGED_FILES.txt` inventories implementation files relative to PR #46; this stop also adds continuity/evidence/inventory documentation.
