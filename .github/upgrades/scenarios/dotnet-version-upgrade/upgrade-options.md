# Upgrade Options

Review and confirm the settings below before the upgrade plan is generated.
To change a selection, move `(selected)` to a different row or tell me what to adjust.

---

## Strategy

### Upgrade Strategy

| Value | Description | Why |
|-------|-------------|-----|
| **All-at-Once** (selected) | Upgrade all 4 projects simultaneously in a single pass | 4 projects, 2-tier depth, all on modern .NET (net9.0), all SDK-style — low complexity, no incremental approach needed |
| Top-Down | Upgrade apps first, multi-target libraries temporarily | For larger solutions or when CI must stay green throughout |

---

## Project Structure

### Package Management

| Value | Description | Why |
|-------|-------------|-----|
| **Central Package Management (CPM)** (selected) | Create `Directory.Packages.props`, move versions out of project files | All projects are SDK-style, same ecosystem (net9→net10), assessment shows version divergence (`System.Resources.Extensions` at both 9.0.8 and 9.0.9 across projects) |
| Per-Project (defer CPM) | Each project keeps its own versions | Suitable for Framework migrations or active multi-targeting |

---

## Compatibility

### Unsupported Packages

2 packages have no compatible version for net10.0:
- `Equin.ApplicationFramework.BindingListView` (used in HostsFileEditor.WinForm)
- `H.NotifyIcon.WinUI` (used in HostsFileEditor.WinUI)

| Value | Description | Why |
|-------|-------------|-----|
| **Resolve Inline** (selected) | Research and resolve each incompatible package within the same task | Only 2 incompatible packages — small enough to handle inline |
| Defer Resolution | Stub out the package, create follow-up tasks | For large counts (>3 packages without known replacements) |
| Compatibility Mode | Keep reference, suppress NU1701 | Only for transitive deps not directly called |

### Unsupported API Handling

Binary and source incompatible APIs detected (WinForms, GDI+, Legacy Controls, Legacy Configuration).

| Value | Description | Why |
|-------|-------------|-----|
| **Fix Inline** (selected) | Resolve every API change in the same task | Most WinForms binary-incompatible issues are mechanical (type forwarding, platform-specific) — resolvable inline |
| Defer Complex Changes | Apply simple replacements inline, stub complex ones | For >5 complex API changes across many projects |

---

*Confirm when ready: reply `confirm`, `start`, or `looks good`*
