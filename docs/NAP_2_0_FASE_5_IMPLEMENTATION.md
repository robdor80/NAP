# NAP 2.0 — Fase 5: implementation and acceptance log

Authorization: 2026-10-16, explicit approval of P5.1–P5.10 and a separate internal host process. Base: `cd7532f011eea8a72e89848234b259f1f5293e15` (PR #46). Work branch: `codex/nap-2-fase-5-automation-host`.

Only synthetic isolated universes are permitted. No service installation, real credentials, account/ACL changes, Nimroel data changes, production activation or CUTOVER is authorized. One product/installation remains the target. No merge is authorized.

## P5.1 — universe ownership

Dedicated bounded mailbox and native owner thread per universe. Workflow delegates run synchronously on that thread; no owner crosses an await. Shutdown drains accepted work and releases ownership on the acquiring thread. Failed startup and individual command failures complete callers explicitly. The delegate API is only for trusted in-process composition, never IPC.

Validation: `AutomationRuntimeTests`, Windows x64/.NET runtime 8.0.8, **3/3 passed**, no skips (`artifacts/phase5/p51/p51.trx`). Isolated build outputs avoid replacing baseline testhost DLLs. The first overlapping build failed copying locked DLLs and was not a test execution. Existing baseline symlink tests cannot create symlinks without a privilege unavailable in this session; no system changes are made to accommodate them. Existing ImageSharp warnings remain visible. P5.2–P5.10 are not yet accepted.
