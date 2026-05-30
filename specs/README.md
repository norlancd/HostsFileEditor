# Feature Specifications

Each file covers one feature end-to-end: objective, data model, behaviors, security notes, edge cases, and exact files to create/modify — grounded in the actual codebase architecture.

Note: Todos los cambios deben ser en el proyecto de winform.

## Implementation Order

| Priority | Spec | Rationale |
|---|---|---|
| P0 | [SPEC-09 Audit Log](SPEC-09-audit-log.md) | Security baseline — must exist before adding new write paths |
| P1 | [SPEC-11 Auto Backup](SPEC-11-auto-backup.md) | Zero-regression safety net, minimal complexity |
| P1 | [SPEC-06 Conflict Detection](SPEC-06-conflict-detection.md) | Read-only analysis, no new write paths |
| P2 | [SPEC-01 Profiles + Hotkeys](SPEC-01-profiles-global-hotkeys.md) | Highest daily-use value |
| P2 | [SPEC-02 Diff Before Switch](SPEC-02-diff-before-switch.md) | Required companion to SPEC-01 |
| P2 | [SPEC-03 Rollback Timer](SPEC-03-rollback-timer.md) | Requires SPEC-01 + SPEC-02 |
| P3 | [SPEC-04 Continuous Ping Monitor](SPEC-04-continuous-ping-monitor.md) | Extends existing ping infrastructure |
| P3 | [SPEC-05 DNS Resolution Column](SPEC-05-dns-resolution-column.md) | Additive column, no write path |
| P3 | [SPEC-07 IP Aliases](SPEC-07-ip-aliases.md) | New abstraction layer, medium complexity |
| P4 | [SPEC-08 CLI / PowerShell Module](SPEC-08-cli-powershell-module.md) | Separate binary, requires Core project extraction |
| P4 | [SPEC-10 Import from URL](SPEC-10-import-from-url.md) | Network I/O, security review required |
| P5 | [SPEC-12 Entry Groups](SPEC-12-entry-groups.md) | UX polish, no functional impact |

## Architectural Invariants

All specs respect these constraints from the existing codebase:

1. Every write to the live hosts file must call `NativeMethods.FlushDns()` — done via `HostsFile.Instance.Save()`.
2. Bulk operations must use `UndoManager.Instance.SuspendUndoRedo()` to avoid polluting undo history.
3. Multi-step operations must use `UndoManager.Instance.BatchActions()` for single-step undo.
4. UI updates from background threads must use `SynchronizationContext.Post` (established pattern in `HostsEntry.OnPingCompleted`).
5. All file writes to `C:\Windows\System32\drivers\etc\` require the app to be running elevated.
