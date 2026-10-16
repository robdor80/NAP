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
