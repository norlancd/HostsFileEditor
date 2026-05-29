# SPEC-04 — Continuous Ping Monitor with Visual Status

**Status:** Proposed  
**Priority:** P3  
**Depends on:** *(none — extends existing `HostsEntry.Ping()` infrastructure)*

---

## Objective

Replace the current one-shot `HostsEntry.Ping()` (triggered only when an IP changes and `AutoPingIPAddress` is true) with a continuous background monitor showing live green/red/gray status per entry, last-seen-online timestamp, and state-change tray notifications.

---

## Current State

`HostsEntry` holds a `Ping` instance. `Ping()` is called once in `ValidateIpAddress()` when `AutoPingIPAddress` is true. There is no recurring loop. `OnPingCompleted` sets an error string on failure but there is no success state, no timestamp, and no notification on state change. `AutoPingIPAddress` is a static flag (all-or-nothing).

---

## New State on `HostsEntry`

These are in-memory only — never persisted to the hosts file or `UnparsedText`.

```csharp
public PingStatus PingStatus { get; private set; }       // Unknown | Pending | Reachable | Unreachable
public DateTime? LastReachableAt { get; private set; }   // UTC
public int? LastRoundtripMs { get; private set; }
public DateTime? StatusChangedAt { get; private set; }   // UTC, set on every state transition
```

`PingStatus` transitions:
- `Unknown` → initial state, or entry is disabled/invalid/loopback.
- `Pending` → ping sent, awaiting reply.
- `Reachable` → last ping succeeded (`IPStatus.Success`).
- `Unreachable` → last ping failed or timed out.

`HostsEntry` raises `PropertyChanged` for `PingStatus` on every state transition so the grid refreshes via existing data binding.

---

## `PingMonitorService` (new singleton)

Background service. Started in `Program.Main` after `MainForm` is created, stopped on `Application.ApplicationExit`.

### Scheduling

Uses a single `System.Threading.Timer` with a 5-second tick. On each tick:

1. Take a snapshot of `HostsFile.Instance.Entries` (thread-safe read).
2. For each entry where `ShouldMonitor(entry)` is true and its last ping was more than `PingIntervalSeconds` ago: enqueue for ping.
3. Send pings up to `MaxConcurrentPings` (default: 10) simultaneously via `SendPingAsync`.

`ShouldMonitor(entry)` returns false if:
- `entry.Enabled == false`
- `entry.Valid == false`
- `entry.IpAddress` is `127.0.0.1`, `::1`, or any loopback range (`127.x.x.x`)
- `HostsFile.IsEnabled == false` (entire hosts file disabled)

### Ping Result Handling

```csharp
private async Task PingEntryAsync(HostsEntry entry)
{
    entry.PingStatus = PingStatus.Pending;
    var reply = await new Ping().SendPingAsync(entry.IpAddress, timeoutMs: 2000);
    var wasReachable = entry.PingStatus == PingStatus.Reachable;
    var isReachable = reply.Status == IPStatus.Success;

    entry.UpdatePingResult(isReachable, reply.RoundtripTime);  // marshals to UI thread

    if (wasReachable != isReachable)
        OnStatusChanged(entry, isReachable);
}
```

`UpdatePingResult` is called via `SynchronizationContext.Post` (same pattern as existing `OnPingCompleted`).

### State-Change Notification

`OnStatusChanged(entry, isReachable)`:
- Check debounce: if `entry.StatusChangedAt` was less than `NotificationDebounceMinutes` (default: 5) ago, skip tray notification.
- Tray balloon: `"{hostname} is no longer reachable"` or `"{hostname} is back online"`.
- If all monitored entries become unreachable within a 10-second window: suppress individual notifications and show one balloon: "Network connectivity lost — monitoring paused." Resume individual notifications when at least one entry recovers.

---

## UI Changes

### New `Status` Column in `HostsEntryDataGridView`

- Rendered as a colored dot (custom `DataGridViewCell` with `OnPaint` override).
- Colors: green = `Reachable`, red = `Unreachable`, gray = `Unknown` or disabled, animated yellow = `Pending`.
- Column width: 24px, not resizable.
- Tooltip on hover: `"Last reachable: 2 min ago (12ms)"` or `"Unreachable since: 14:32 — 8 min ago"` or `"Not monitored (disabled entry)"`.
- Column is hidden when `PingMonitorEnabled` setting is false.

### Settings UI

- Existing "Auto-ping IP Addresses" checkbox in Tools menu: replaced (or supplemented) by "Continuous Ping Monitor" toggle.
- New "Monitor Settings…" dialog: interval (10s / 30s / 60s / 5min), notification debounce, max concurrent pings.

---

## Settings Added

```
PingMonitorEnabled         : bool   (default: false)
PingIntervalSeconds        : int    (default: 30)
NotificationDebounceMinutes: int    (default: 5)
MaxConcurrentPings         : int    (default: 10)
```

`AutoPingIPAddresses` (existing) is kept for backward compatibility but no longer drives the continuous monitor — it still controls the one-shot ping on IP edit.

---

## Threading Model

All `PingMonitorService` work runs on the thread pool. The 5-second tick callback is non-reentrant (uses a `SemaphoreSlim(1,1)` to prevent overlapping ticks). Results are marshalled to the UI thread via `SynchronizationContext.Post` on the captured `SynchronizationContext` from startup — same pattern already used in `HostsEntry.OnPingCompleted`.

The service holds a weak reference to `HostsFile.Instance.Entries` to avoid preventing GC if the singleton is ever reset (e.g. during tests).

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Entry IP is `127.0.0.1` | `ShouldMonitor` returns false. Status stays `Unknown`. Tooltip: "Loopback — not monitored." |
| IPv6 address | `SendPingAsync` handles IPv6 natively. No special casing needed. |
| Profile switched mid-monitor | On `HostsFile.Instance.Entries.ListChanged`: re-evaluate all entries' `ShouldMonitor`. New entries start at `Unknown`. |
| Machine sleeps and wakes | First tick after wake-up: all entries' last-ping timestamps are stale. All are re-queued immediately. |
| `PingMonitorEnabled` toggled off | Service pauses its timer. All entry `PingStatus` values are reset to `Unknown`. Status column hidden. |

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/PingMonitorService.cs` | New — background singleton |
| `src/PingStatus.cs` | New — enum |
| `src/HostsEntry.cs` | Add `PingStatus`, `LastReachableAt`, `LastRoundtripMs`, `StatusChangedAt`, `UpdatePingResult()` |
| `src/Controls/HostsEntryDataGridView.cs` | Add `Status` column, custom cell renderer |
| `src/Program.cs` | Start/stop `PingMonitorService` |
| `src/MainForm.cs` | Wire monitor toggle in menu, add "Monitor Settings…" |
| `src/Properties/Settings.settings` | Add four new settings |
