# SPEC-05 — Live DNS Resolution Column

**Status:** Proposed  
**Priority:** P3  
**Depends on:** *(none — additive column, no write path)*

---

## Objective

Show what IP address the OS is **actually** resolving for each hostname, independent of the hosts file entry value, to surface conflicts, bypassed entries, and DNS cache staleness. This is a read-only diagnostic column — it never modifies the hosts file.

---

## Current State

No DNS resolution display exists. Users cannot tell, from within the app, whether the OS is actually using their hosts file entries.

---

## Why This Matters

The OS resolver processes DNS in a specific priority order. An entry in the hosts file should always win over external DNS — but it does not in several real scenarios:

- **DNS-over-HTTPS (DoH)** enabled in browsers (Chrome, Firefox, Edge) — these bypass the OS hosts file entirely.
- **Group Policy NRPT rules** (common in corporate VPNs) can override specific suffixes regardless of the hosts file.
- **DNS cache** — a previous resolution may still be cached even if the hosts file was just changed (`FlushDns` is called on save, but some resolvers cache independently).
- **Disabled entry not noticed** — user expects a redirect but the entry is commented out.

A mismatch between the hosts file IP and the actual resolved IP is a signal worth surfacing.

---

## Resolution Logic

`DnsResolverService` — a thin async wrapper around `System.Net.Dns.GetHostAddressesAsync`.

This API queries the **OS resolver**, which consults the hosts file first (when the OS is configured normally). The result reflects what `HttpClient`, `TcpClient`, and most .NET networking code will see.

```csharp
public async Task<DnsResolutionResult> ResolveAsync(string hostname, CancellationToken ct)
{
    try
    {
        var addresses = await Dns.GetHostAddressesAsync(hostname, ct);
        return new DnsResolutionResult { Addresses = addresses, Status = DnsStatus.Resolved };
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound)
    {
        return new DnsResolutionResult { Status = DnsStatus.NotFound };
    }
    catch (OperationCanceledException)
    {
        return new DnsResolutionResult { Status = DnsStatus.Timeout };
    }
}
```

Timeout: 3 seconds per hostname.

---

## Resolution States

```
DnsStatus : enum
├── NotResolved   // default, resolution not yet attempted
├── Resolving     // in-flight
├── Resolved      // got at least one IP
├── NotFound      // NXDOMAIN / HostNotFound
└── Timeout       // exceeded 3s
```

**Match evaluation** (compares `DnsResolutionResult.Addresses` against `HostsEntry.IpAddress`):

| State | Meaning | Cell Color |
|---|---|---|
| `Match` | Hosts file IP is in the resolved addresses | Green ✓ |
| `Mismatch` | Resolved, but IP differs from hosts file entry | Yellow ⚠ |
| `Bypass` | Entry is enabled, but resolved IP is the external DNS result (detected when entry is disabled + re-resolved; advanced heuristic — optional) | Orange ⚠⚠ |
| `NotFound` | Hostname does not resolve at all | Gray ❓ |
| `Timeout` | DNS query took > 3s | Gray ⏱ |
| `Disabled` | Entry is disabled — shows actual DNS resolution without the hosts override | Blue (informational) |
| `NotResolved` | Not yet queried | Empty |

---

## UI: `DNS Resolves To` Column

- New column in `HostsEntryDataGridView`, inserted after the `IP Address` column.
- Default width: 140px. Resizable.
- Cell displays: primary resolved IP (first `IPAddress` in results), colored by match state.
- Tooltip: full list of resolved addresses if multiple, plus "Last resolved: 14:32:01" timestamp.
- For `Mismatch`: tooltip explicitly states: "Hosts file entry: 192.168.1.10 — OS resolved: 93.184.216.34. This entry may be bypassed."

### Trigger Modes

Controlled by a setting `DnsResolutionMode`:

| Mode | Behavior |
|---|---|
| `Manual` (default) | Column shown but empty. Right-click row → "Resolve DNS" to query selected entries. "Resolve All" button in toolbar. |
| `OnLoad` | Resolve all enabled entries once when the app loads or a profile is switched. |
| `Continuous` | Re-resolve all entries every N minutes (configurable, default: 5 min). Resource-intensive; warn user on enable. |

In `Manual` mode a "Resolve All DNS" button appears in the toolbar (disabled when column is hidden).

### Column Visibility

Hidden by default. Enabled via View menu: "Show DNS Resolution Column". Persisted in settings.

---

## Performance Considerations

- Resolutions run concurrently, capped at 5 simultaneous queries (to avoid overwhelming the local DNS resolver).
- Results are cached per-hostname for 60 seconds. Re-resolution before 60s reuses the cache (with a visual indicator that the result is cached).
- `Continuous` mode skips entries whose hostname has not changed since the last resolution.
- The column does not run in `Manual` mode unless the user explicitly triggers it, keeping the default app footprint minimal.

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| Entry has multiple hostnames (space-separated) | Resolve each hostname independently. Cell shows worst-case state (e.g. if any mismatch, show yellow). Tooltip lists each hostname's result. |
| IPv6 entry | `GetHostAddressesAsync` returns both `AddressFamily` results. Compare against `HostsEntry.IpAddress` using `IPAddress.Equals` (handles canonical forms). |
| Entry hostname is `localhost` | Always resolves to `127.0.0.1` or `::1`. This is not a mismatch even if hosts file has a different IP — show `Mismatch` normally; user decides. |
| DoH enabled in browser | The app's resolution (via OS `Dns` class) will show `Match`, but the browser may still bypass it. This limitation is documented in a tooltip note: "Resolution reflects OS behavior. Browsers with DNS-over-HTTPS may use different results." |
| VPN connected with split-DNS | NRPT rules may cause `GetHostAddressesAsync` to return the VPN-DNS result, not the hosts file value. This is accurately shown as `Mismatch`. |

---

## Settings Added

```
DnsResolutionColumnVisible : bool              (default: false)
DnsResolutionMode          : DnsResolutionMode (default: Manual)
DnsResolutionIntervalMin   : int               (default: 5)
```

---

## Files to Create / Modify

| File | Change |
|---|---|
| `src/DnsResolverService.cs` | New — async resolution wrapper with cache |
| `src/DnsResolutionResult.cs` | New — result model + `DnsStatus` enum |
| `src/Controls/HostsEntryDataGridView.cs` | Add `DNS Resolves To` column, custom cell renderer |
| `src/MainForm.cs` | Add "Resolve All DNS" toolbar button, View menu toggle |
| `src/Properties/Settings.settings` | Add three new settings |
