namespace HostsFileEditor;

internal partial class MainForm
{
    private string _currentProfileName = "current";

    // Per-entry last-known state. Updated whenever the entry fires PropertyChanged.
    private readonly Dictionary<HostsEntry, AuditEntrySnapshot> _entrySnapshots = [];

    // Changes buffered between saves: (before, after) pairs.
    private readonly List<(AuditEntrySnapshot Before, AuditEntrySnapshot After)> _pendingModifications = [];

    private static AuditEntrySnapshot ToSnapshot(HostsEntry e) => new()
    {
        Ip = e.IpAddress,
        Hostnames = e.HostNames,
        Enabled = e.Enabled
    };

    // ── Baseline ────────────────────────────────────────────────────────────

    internal void TakeBaselineSnapshot()
    {
        _entrySnapshots.Clear();
        _pendingModifications.Clear();

        foreach (var entry in HostsFile.Instance.Entries)
            TrackEntry(entry);

        // Subscribe to list changes so newly added entries are also tracked
        HostsFile.Instance.Entries.ListChanged -= OnEntriesListChanged;
        HostsFile.Instance.Entries.ListChanged += OnEntriesListChanged;
    }

    private void TrackEntry(HostsEntry entry)
    {
        _entrySnapshots[entry] = ToSnapshot(entry);
        entry.PropertyChanged -= OnEntryPropertyChanged;
        entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void UntrackEntry(HostsEntry entry)
    {
        entry.PropertyChanged -= OnEntryPropertyChanged;
        _entrySnapshots.Remove(entry);
    }

    // ── Change detection ────────────────────────────────────────────────────

    private void OnEntriesListChanged(object? sender, System.ComponentModel.ListChangedEventArgs e)
    {
        switch (e.ListChangedType)
        {
            case System.ComponentModel.ListChangedType.ItemAdded:
                if (e.NewIndex >= 0 && e.NewIndex < HostsFile.Instance.Entries.Count)
                    TrackEntry(HostsFile.Instance.Entries[e.NewIndex]);
                break;

            case System.ComponentModel.ListChangedType.ItemDeleted:
                // Entry is already gone; clean up orphaned snapshots
                var missing = _entrySnapshots.Keys
                    .Except(HostsFile.Instance.Entries)
                    .ToList();
                foreach (var gone in missing)
                    UntrackEntry(gone);
                break;

            case System.ComponentModel.ListChangedType.Reset:
                // Bulk replace (e.g. archive load) — re-baseline all
                var stale = _entrySnapshots.Keys
                    .Except(HostsFile.Instance.Entries)
                    .ToList();
                foreach (var s in stale) UntrackEntry(s);
                foreach (var entry in HostsFile.Instance.Entries)
                    TrackEntry(entry);
                break;
        }
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only care about the fields that appear in the audit snapshot
        if (e.PropertyName is not (
            nameof(HostsEntry.IpAddress) or
            nameof(HostsEntry.HostNames) or
            nameof(HostsEntry.Enabled)))
            return;

        if (sender is not HostsEntry entry) return;
        if (!_entrySnapshots.TryGetValue(entry, out var before)) return;

        var after = ToSnapshot(entry);

        // Only record if something actually changed
        if (before.Ip == after.Ip &&
            before.Hostnames == after.Hostnames &&
            before.Enabled == after.Enabled)
            return;

        _pendingModifications.Add((before, after));
        _entrySnapshots[entry] = after;  // advance baseline to new state
    }

    // ── Flush on Save ───────────────────────────────────────────────────────

    internal void LogSaveChanges(string source = AuditSource.MainForm)
    {
        foreach (var (before, after) in _pendingModifications)
        {
            AuditLogger.Instance.Log(new AuditEntry
            {
                Action = nameof(AuditActionType.EntryModified),
                Source = source,
                Detail = new AuditDetail { Before = before, After = after }
            });
        }
        _pendingModifications.Clear();
    }

    // ── Diff helper (for ProfileSwitch) ─────────────────────────────────────

    internal static (List<AuditEntrySnapshot> Added, List<AuditEntrySnapshot> Removed, List<AuditEntryModification> Modified)
        ComputeDiff(IReadOnlyList<AuditEntrySnapshot> before, IReadOnlyList<AuditEntrySnapshot> after)
    {
        var beforeMap = before
            .Where(e => !string.IsNullOrWhiteSpace(e.Hostnames))
            .GroupBy(e => e.Hostnames, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var afterMap = after
            .Where(e => !string.IsNullOrWhiteSpace(e.Hostnames))
            .GroupBy(e => e.Hostnames, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var added = afterMap.Keys
            .Where(k => !beforeMap.ContainsKey(k))
            .Select(k => afterMap[k]).ToList();

        var removed = beforeMap.Keys
            .Where(k => !afterMap.ContainsKey(k))
            .Select(k => beforeMap[k]).ToList();

        var modified = beforeMap.Keys
            .Where(k => afterMap.ContainsKey(k) && (
                !string.Equals(beforeMap[k].Ip, afterMap[k].Ip, StringComparison.OrdinalIgnoreCase) ||
                beforeMap[k].Enabled != afterMap[k].Enabled))
            .Select(k => new AuditEntryModification { Before = beforeMap[k], After = afterMap[k] })
            .ToList();

        return (added, removed, modified);
    }

    // ── UI & notifications ───────────────────────────────────────────────────

    private void OnViewAuditLogClick(object? sender, EventArgs e)
    {
        using var form = new AuditLogForm();
        form.ShowDialog(this);
    }

    private void SetupAuditLoggerNotifications()
    {
        AuditLogger.Instance.IntegrityFailed += OnAuditIntegrityFailed;
        AuditLogger.Instance.LogError += OnAuditLogError;
        FormClosed += (_, _) =>
        {
            AuditLogger.Instance.IntegrityFailed -= OnAuditIntegrityFailed;
            AuditLogger.Instance.LogError -= OnAuditLogError;
        };
    }

    private void OnAuditIntegrityFailed(object? sender, string message)
    {
        if (InvokeRequired) { Invoke(() => OnAuditIntegrityFailed(sender, message)); return; }
        notifyIcon.ShowBalloonTip(5000, "Audit Log Warning", message, ToolTipIcon.Warning);
    }

    private void OnAuditLogError(object? sender, string message)
    {
        if (InvokeRequired) { Invoke(() => OnAuditLogError(sender, message)); return; }
        notifyIcon.ShowBalloonTip(3000, "Audit Log Error", message, ToolTipIcon.Error);
    }
}
