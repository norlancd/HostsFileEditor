# SPEC-08 — CLI / PowerShell Module

**Status:** Proposed  
**Priority:** P4  
**Depends on:** SPEC-09 (Audit Log), SPEC-01 (Profiles), SPEC-06 (Conflict Detection)

---

## Objective

Expose core hosts file operations as command-line commands for use in project setup scripts, CI/CD pipelines, and terminal workflows — without requiring the GUI to be running. The CLI writes to the same files as the GUI and uses the same business logic.

---

## Distribution

A separate executable `hostseditor.exe` published alongside the main app in the same `bin\` directory. Built from the same solution as a second project `src\HostsFileEditor.Cli\`.

The CLI shares the core domain classes (`HostsFile`, `HostsEntry`, `HostsEntryList`, `HostsArchiveList`, `UndoManager`) via a shared class library project `src\HostsFileEditor.Core\`. The GUI project references `Core`; the CLI project references `Core`.

**Manifest:** `requestedExecutionLevel = requireAdministrator` — same as the GUI. All write commands require elevation. The CLI checks this at startup for write operations and prints a clear error if not elevated.

---

## Command Reference

All commands follow the pattern: `hostseditor <noun> <verb> [args] [options]`

### Profile Commands

```
hostseditor profile list
    Output: table of all profiles
    Columns: Name | Active | Hotkey | Entries | Last Modified

hostseditor profile switch <name> [--no-diff] [--timer <minutes>]
    Switches to the named profile.
    --no-diff     Skips diff confirmation. Flagged in audit log as BypassedDiff=true.
    --timer <n>   Activates rollback timer for N minutes (SPEC-03).
    Exit codes: 0=success, 1=error, 2=profile not found, 3=not elevated.

hostseditor profile snapshot <name>
    Saves current hosts file as a new named profile.
    Fails if a profile with that name already exists (no --force by design — prevents accidental overwrites in scripts).
```

### Entry Commands

```
hostseditor entry list [--enabled-only] [--filter <text>]
    Lists all entries in the active hosts file.

hostseditor entry add <ip> <hostname> [--comment <text>] [--disabled]
    Adds an entry. Idempotent: if ip+hostname pair already exists enabled, exits 0 without duplicating.
    If the pair exists disabled: enables it instead of adding a duplicate.

hostseditor entry remove <hostname>
    Removes ALL entries (any IP) where hostname matches. Case-insensitive.
    Prints count of removed entries.

hostseditor entry enable <hostname>
hostseditor entry disable <hostname>
    Enables/disables all entries matching hostname. Does not remove.
```

### Diagnostic Commands

```
hostseditor ping <hostname|ip> [--continuous] [--interval <seconds>]
    One-shot ping. --continuous runs until Ctrl+C with status updates.
    Does not require elevation.

hostseditor dns resolve <hostname>
    Resolves hostname via OS DNS and compares to active hosts file entry.
    Shows: Hosts file IP | Resolved IP | Status (Match/Mismatch/NotFound)
    Does not require elevation.

hostseditor watch [--profile <name>]
    Continuous ping monitor for all enabled entries. Refreshes every 30s.
    Tabular output with colored status (green/red). Ctrl+C to exit.
    Does not require elevation (ping does not need it).

hostseditor conflicts [--min-severity <info|warning|error>]
    Prints all detected conflicts.
    Exit code: 0=no errors, 1=has Error-level conflicts, 2=has Warnings only.
    Suitable for CI/CD pre-deploy gate: `hostseditor conflicts || exit 1`
```

### Rollback Commands

```
hostseditor rollback status
    Shows active rollback timer if any: profile name, expires at, time remaining.

hostseditor rollback cancel
    Cancels active rollback timer. Retains current profile.

hostseditor rollback execute
    Immediately executes revert without waiting for expiry.
```

---

## Output Formats

All commands support `--json` for machine-readable output (newline-delimited JSON or JSON array depending on command). Default output is human-readable table/text.

```
hostseditor profile list --json
[
  {"name":"default","active":true,"entries":12,"hotkey":null},
  {"name":"staging","active":false,"entries":8,"hotkey":"Ctrl+Shift+2"}
]
```

Errors go to `stderr`. Data goes to `stdout`. This allows `hostseditor conflicts --json 2>/dev/null | jq .` to work cleanly.

Exit codes are documented and stable across versions — scripts can rely on them.

---

## Input Validation & Security

All string arguments that become file path components (`<name>` for profile snapshot, archive names) must be validated:

```csharp
private static void ValidateProfileName(string name)
{
    if (name.Contains("..") || name.Contains('/') || name.Contains('\\')
        || name.Contains(':') || Path.GetInvalidFileNameChars().Any(name.Contains))
        throw new ArgumentException($"Invalid profile name: '{name}'. Must be a valid filename.");
}
```

`--no-diff` flag: must be logged to audit log (SPEC-09) with `BypassedDiff = true`. This is the only way to bypass diff confirmation in scripted contexts.

Elevation check for write commands:

```csharp
using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("This command requires administrator privileges. Run as Administrator.");
    Environment.Exit(3);
}
```

---

## Single Instance Coordination

The GUI enforces single-instance via a named mutex. The CLI must:
- **Not** acquire the same mutex (CLI and GUI can run simultaneously).
- Use file locking (`FileShare.None` with retry) when writing to the hosts file to avoid race conditions with the GUI.
- If the file is locked for > 2 seconds: print error "Hosts file is locked by another process. Is HostsFileEditor GUI running?" and exit 1.

The GUI does not need to know the CLI ran. The GUI will pick up changes on next `Refresh()`.

---

## `hostseditor watch` Output Format

```
HostsFileEditor Watch — Active profile: staging  [Ctrl+C to exit]
Refreshing every 30s. Last update: 14:32:01

  IP              Hostname           Status    Last OK       RTT
  ──────────────────────────────────────────────────────────────
  192.168.1.10    api.local          ● OK      14:32:01      4ms
  192.168.1.11    db.local           ● OK      14:32:01      2ms
  10.0.0.50       staging-api.local  ✕ DOWN    14:28:44      —
  127.0.0.1       blocked.com        — (skip)
```

Colored output using ANSI escape codes (disabled on non-TTY outputs for CI compatibility, detected via `Console.IsOutputRedirected`).

---

## PowerShell Module (Optional Extension)

A thin `HostsFileEditor.psd1` wrapper that re-exports CLI commands as PowerShell functions with proper `[Parameter]` attributes and tab completion:

```powershell
function Switch-HostsProfile {
    param(
        [Parameter(Mandatory)][string]$Name,
        [switch]$NoDiff,
        [int]$TimerMinutes
    )
    $args = @("profile", "switch", $Name)
    if ($NoDiff)          { $args += "--no-diff" }
    if ($TimerMinutes -gt 0) { $args += "--timer"; $args += $TimerMinutes }
    & hostseditor @args
    if ($LASTEXITCODE -ne 0) { throw "hostseditor exited with code $LASTEXITCODE" }
}
```

Module is optional and documented separately. The CLI is the primary interface.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| `hostseditor entry add` — hostname already exists with different IP | Adds a new entry (does not replace). Prints warning: "Warning: hostname 'api.local' already mapped to 192.168.1.1. Use 'entry remove' first to avoid conflict." |
| `hostseditor profile switch` — GUI is running with unsaved changes | File write uses file locking. If GUI has unsaved in-memory changes, they will be lost on next GUI `Refresh`. This is acceptable — unsaved GUI changes are not committed to disk and the CLI cannot know about them. |
| Profile name with spaces | Must be quoted in shell: `hostseditor profile switch "my profile"`. Name is passed as a single argument. |
| `--json` + non-zero exit code | Exit code still signals error; `stderr` has the error message; `stdout` has `{"error":"message"}`. |

---

## Files to Create

| File | Description |
|---|---|
| `src/HostsFileEditor.Core/` | New project — shared domain classes (moved from GUI project) |
| `src/HostsFileEditor.Cli/` | New project — CLI entry point + command handlers |
| `src/HostsFileEditor.Cli/Commands/ProfileCommands.cs` | Profile subcommands |
| `src/HostsFileEditor.Cli/Commands/EntryCommands.cs` | Entry subcommands |
| `src/HostsFileEditor.Cli/Commands/DiagnosticCommands.cs` | Ping, DNS, watch, conflicts |
| `src/HostsFileEditor.Cli/Commands/RollbackCommands.cs` | Rollback subcommands |
| `src/HostsFileEditor.Cli/OutputFormatter.cs` | Table + JSON output |
| `HostsFileEditor.slnx` | Add new projects to solution |
