namespace HostsFileEditor;

internal class DiffPreviewForm : Form
{
    private static readonly Color ModifiedBg = Color.FromArgb(255, 248, 225);
    private static readonly Color ModifiedFg = Color.FromArgb(150, 100, 0);
    private static readonly Color PublicRedirectBg = Color.FromArgb(253, 226, 226);
    private static readonly Color PublicRedirectFg = Color.FromArgb(180, 30, 30);
    private static readonly Color AddedBg = Color.FromArgb(228, 246, 230);
    private static readonly Color AddedFg = Color.FromArgb(30, 120, 50);
    private static readonly Color RemovedBg = Color.FromArgb(252, 232, 232);
    private static readonly Color RemovedFg = Color.FromArgb(170, 40, 40);
    private static readonly Color ToggledBg = Color.FromArgb(228, 236, 250);
    private static readonly Color ToggledFg = Color.FromArgb(40, 80, 170);
    private static readonly Color HeaderBg = Color.FromArgb(245, 246, 248);
    private static readonly Color GridLineColor = Color.FromArgb(225, 226, 230);

    public DiffPreviewForm(string profileName, ProfileDiffResult diff)
    {
        Text = "Profile Switch Preview";
        Icon = Properties.Resources.HostsFileEditor;
        Size = new Size(700, 540);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        // ── Header ────────────────────────────────────────────────────────
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(14, 10, 14, 0) };
        headerPanel.Controls.Add(new Label
        {
            Text = $"Switching to profile: \"{profileName}\"",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 10)
        });
        var changeText = diff.TotalChanges == 1
            ? "1 change to active DNS resolution"
            : $"{diff.TotalChanges} changes to active DNS resolution";
        headerPanel.Controls.Add(new Label
        {
            Text = changeText,
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(14, 32)
        });

        // ── Buttons ───────────────────────────────────────────────────────
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var cancelBtn = new Button { Text = "Cancel", Size = new Size(90, 28), DialogResult = DialogResult.Cancel };
        var applyBtn = new Button { Text = "Apply Changes", Size = new Size(110, 28), DialogResult = DialogResult.OK };
        cancelBtn.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        applyBtn.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        cancelBtn.Location = new Point(btnPanel.Width - 210, 9);
        applyBtn.Location = new Point(btnPanel.Width - 118, 9);
        btnPanel.Controls.AddRange([cancelBtn, applyBtn]);
        AcceptButton = applyBtn;
        CancelButton = cancelBtn;

        var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        // ── One unified, sortable table for every change ───────────────────
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            GridColor = GridLineColor,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EnableHeadersVisualStyles = false,
            EditMode = DataGridViewEditMode.EditProgrammatically,
            Font = new Font("Segoe UI", 9.5f),
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            StandardTab = true
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 62, 68);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 6, 0, 6);
        grid.RowTemplate.Height = 28;
        grid.DefaultCellStyle.Padding = new Padding(4, 2, 0, 2);
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.Window;
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.WindowText;

        // DataGridView.Columns.Add()/Rows.Add()/DefaultCellStyle all act on the underlying
        // native control via window messages, same as RichTextBox.Select/SelectionColor did.
        // Parenting the grid and forcing Handle creation BEFORE adding columns/rows is what
        // was missing previously — without it, columns/rows were silently not taking effect.
        Controls.Add(grid);
        _ = grid.Handle;

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Name = "Type", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hostname", Name = "Hostname", Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Old IP", Name = "OldIp", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "New IP", Name = "NewIp", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note", Name = "Note", Width = 160 });

        void AddRow(object[] values, Color bg, Color fg)
        {
            var rowIndex = grid.Rows.Add(values);
            var row = grid.Rows[rowIndex];
            row.DefaultCellStyle.BackColor = bg;
            row.Cells[0].Style.ForeColor = fg;
            row.Cells[0].Style.Font = new Font(grid.Font, FontStyle.Bold);
        }

        foreach (var p in diff.Modified)
        {
            var isPublicRedir = IsPublicHostname(p.Before.HostNames) && IsPrivateOrLoopback(p.After.IpAddress);
            var note = isPublicRedir ? "⚠ Public hostname redirected" : string.Empty;
            AddRow(
                ["Modified", p.Before.HostNames, p.Before.IpAddress, p.After.IpAddress, note],
                isPublicRedir ? PublicRedirectBg : ModifiedBg,
                isPublicRedir ? PublicRedirectFg : ModifiedFg);
        }

        foreach (var e in diff.Added)
            AddRow(["Added", e.HostNames, string.Empty, e.IpAddress, string.Empty], AddedBg, AddedFg);

        foreach (var e in diff.Removed)
            AddRow(["Removed", e.HostNames, e.IpAddress, string.Empty, string.Empty], RemovedBg, RemovedFg);

        foreach (var p in diff.Toggled)
        {
            var stateChange = p.After.Enabled ? "disabled → enabled" : "enabled → disabled";
            AddRow(["Toggled", p.Before.HostNames, p.Before.IpAddress, p.Before.IpAddress, stateChange], ToggledBg, ToggledFg);
        }

        // Don't show any row as pre-selected when the dialog opens
        grid.ClearSelection();
        grid.CurrentCell = null;

        Controls.AddRange([headerPanel, sep, btnPanel]);
        ActiveControl = cancelBtn;
    }

    // ── Public hostname / private IP detection ────────────────────────────────

    private static bool IsPublicHostname(string hostname)
    {
        var h = hostname.Trim().ToLowerInvariant();
        if (h == "localhost") return false;
        string[] localSuffixes = [".local", ".internal", ".test", ".dev", ".example",
                                   ".localhost", ".localdomain", ".lan", ".home", ".corp"];
        return !localSuffixes.Any(s => h.EndsWith(s));
    }

    private static bool IsPrivateOrLoopback(string ip)
    {
        if (ip.StartsWith("127.") || ip == "::1" || ip == "0.0.0.0") return true;
        if (ip.StartsWith("10.")) return true;
        if (ip.StartsWith("192.168.")) return true;
        if (ip.StartsWith("172.") &&
            int.TryParse(ip.Split('.').ElementAtOrDefault(1), out var b) &&
            b >= 16 && b <= 31) return true;
        return false;
    }
}
