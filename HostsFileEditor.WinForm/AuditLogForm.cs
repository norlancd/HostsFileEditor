using System.Text;
using System.Text.Json;

namespace HostsFileEditor;

internal class AuditLogForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _filterBox = new();
    private readonly TextBox _detailBox = new();
    private readonly Button _exportBtn = new() { Text = "Export CSV…" };
    private readonly Button _refreshBtn = new() { Text = "Refresh" };
    private readonly Label _integrityLabel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private readonly IAuditLogger _auditLogger;
    private List<AuditEntry> _allEntries = [];

    public AuditLogForm(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
        Text = "Audit Log";
        Icon = Properties.Resources.HostsFileEditor;
        Size = new Size(900, 600);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;

        BuildUI();
        LoadEntries();
    }

    private void BuildUI()
    {
        // --- top toolbar ---
        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        var filterLabel = new ToolStripLabel("Filter: ");
        var filterHost = new ToolStripControlHost(_filterBox) { Size = new Size(200, 23) };
        _filterBox.Width = 200;
        _filterBox.TextChanged += (_, _) => ApplyFilter();
        var exportHost = new ToolStripControlHost(_exportBtn);
        _exportBtn.Click += OnExportCsv;
        var refreshHost = new ToolStripControlHost(_refreshBtn);
        _refreshBtn.Click += (_, _) => LoadEntries();
        toolbar.Items.AddRange([filterLabel, filterHost, new ToolStripSeparator(), exportHost, refreshHost]);

        // --- grid ---
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionChanged += OnSelectionChanged;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Seq", HeaderText = "#", Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Timestamp", HeaderText = "Timestamp", Width = 165 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Action", Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Summary",
            HeaderText = "Summary",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        // --- detail box ---
        _detailBox.Multiline = true;
        _detailBox.ReadOnly = true;
        _detailBox.ScrollBars = ScrollBars.Vertical;
        _detailBox.Dock = DockStyle.Bottom;
        _detailBox.Height = 130;
        _detailBox.Font = new Font("Consolas", 9f);
        _detailBox.BackColor = SystemColors.Info;
        _detailBox.BorderStyle = BorderStyle.FixedSingle;

        // --- status strip ---
        _statusStrip.Items.Add(_statusLabel);
        _integrityLabel.AutoSize = true;
        _integrityLabel.Dock = DockStyle.Bottom;
        _integrityLabel.Padding = new Padding(4, 2, 0, 2);

        Controls.Add(_grid);
        Controls.Add(_detailBox);
        Controls.Add(_integrityLabel);
        Controls.Add(toolbar);
        Controls.Add(_statusStrip);
    }

    private void LoadEntries()
    {
        _allEntries = _auditLogger.ReadEntries()
            .Reverse()
            .ToList();

        ApplyFilter();

        var ok = _auditLogger.VerifyChain();
        _integrityLabel.Text = ok
            ? "  ✓ Chain integrity verified"
            : "  ⚠ Integrity check failed — log may have been tampered with";
        _integrityLabel.ForeColor = ok ? Color.DarkGreen : Color.DarkRed;
    }

    private void ApplyFilter()
    {
        var text = _filterBox.Text.Trim();
        _grid.Rows.Clear();

        var source = string.IsNullOrEmpty(text)
            ? _allEntries
            : _allEntries.Where(e =>
                e.Action.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.Source.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                BuildSummary(e).Contains(text, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in source)
        {
            _grid.Rows.Add(entry.Seq, FormatTimestamp(entry.Timestamp), entry.Action, entry.Source, BuildSummary(entry));
            _grid.Rows[^1].Tag = entry;
        }

        _statusLabel.Text = $"{_grid.Rows.Count} entries";
        _detailBox.Clear();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AuditEntry entry)
        {
            _detailBox.Clear();
            return;
        }
        _detailBox.Text = BuildDetail(entry);
    }

    private void OnExportCsv(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            FileName = "audit_export",
            Filter = "CSV (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = "csv"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Seq,Timestamp,Action,Source,Actor,AppVersion,Machine,Summary");
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not AuditEntry entry) continue;
                sb.AppendLine(string.Join(",",
                    CsvEscape(entry.Seq.ToString()),
                    CsvEscape(entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                    CsvEscape(entry.Action),
                    CsvEscape(entry.Source),
                    CsvEscape(entry.Actor),
                    CsvEscape(entry.AppVersion),
                    CsvEscape(entry.MachineHostname),
                    CsvEscape(BuildSummary(entry))));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show(this, $"Exported {_grid.Rows.Count} entries.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string FormatTimestamp(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today) return local.ToString("HH:mm:ss") + " today";
        if (local.Date == today.AddDays(-1)) return local.ToString("HH:mm:ss") + " yesterday";
        return local.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string BuildSummary(AuditEntry e) => e.Action switch
    {
        nameof(AuditActionType.ProfileSwitch) or nameof(AuditActionType.TimedProfileSwitch)
            => $"{e.Detail?.ProfileFrom} → {e.Detail?.ProfileTo}",
        nameof(AuditActionType.EntryAdded)
            => $"{e.Detail?.Entry?.Ip}  {e.Detail?.Entry?.Hostnames}",
        nameof(AuditActionType.EntryRemoved)
            => $"{e.Detail?.Entry?.Ip}  {e.Detail?.Entry?.Hostnames}",
        nameof(AuditActionType.EntryModified)
            => $"{e.Detail?.Before?.Hostnames}  {e.Detail?.Before?.Ip} → {e.Detail?.After?.Ip}",
        nameof(AuditActionType.FileImported)
            => e.Detail?.SourcePath is string p ? Path.GetFileName(p) : string.Empty,
        nameof(AuditActionType.RollbackExecuted)
            => $"reverted to {e.Detail?.ProfileReverted}",
        _ => string.Empty
    };

    private static string BuildDetail(AuditEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Seq: {e.Seq}   Timestamp: {e.Timestamp:u}   Actor: {e.Actor}");
        sb.AppendLine($"Action: {e.Action}   Source: {e.Source}   App: {e.AppVersion}   Machine: {e.MachineHostname}");
        if (e.Detail is { } d)
        {
            if (d.ProfileFrom != null || d.ProfileTo != null)
                sb.AppendLine($"Profile: {d.ProfileFrom} → {d.ProfileTo}");
            if (d.RollbackTimerMinutes.HasValue)
                sb.AppendLine($"Rollback timer: {d.RollbackTimerMinutes} min");
            if (d.Entry != null)
                sb.AppendLine($"Entry: {d.Entry.Ip}  {d.Entry.Hostnames}  enabled={d.Entry.Enabled}");
            if (d.Before != null)
                sb.AppendLine($"Before: {d.Before.Ip}  {d.Before.Hostnames}  enabled={d.Before.Enabled}");
            if (d.After != null)
                sb.AppendLine($"After:  {d.After.Ip}  {d.After.Hostnames}  enabled={d.After.Enabled}");
            if (d.EntriesAdded?.Count > 0)
            {
                sb.AppendLine($"Added ({d.EntriesAdded.Count}):");
                foreach (var item in d.EntriesAdded) sb.AppendLine($"  + {item.Ip}  {item.Hostnames}");
            }
            if (d.EntriesRemoved?.Count > 0)
            {
                sb.AppendLine($"Removed ({d.EntriesRemoved.Count}):");
                foreach (var item in d.EntriesRemoved) sb.AppendLine($"  − {item.Ip}  {item.Hostnames}");
            }
            if (d.EntriesModified?.Count > 0)
            {
                sb.AppendLine($"Modified ({d.EntriesModified.Count}):");
                foreach (var m in d.EntriesModified)
                    sb.AppendLine($"  ~ {m.Before?.Hostnames}  {m.Before?.Ip} → {m.After?.Ip}");
            }
            if (d.SourcePath != null) sb.AppendLine($"Source file: {d.SourcePath}");
            if (d.SourceUrl != null) sb.AppendLine($"Source URL: {d.SourceUrl}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
