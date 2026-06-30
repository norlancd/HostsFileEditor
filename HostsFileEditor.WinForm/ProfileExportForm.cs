namespace HostsFileEditor;

/// <summary>
/// Lets the user pick which profiles to include in an export package, and whether
/// to also bundle the actual config-copy file contents (opt-in — may hold secrets).
/// </summary>
internal sealed class ProfileExportForm : Form
{
    private readonly CheckedListBox _list;
    private readonly CheckBox _chkBundleConfig;

    public List<HostsProfile> SelectedProfiles { get; private set; } = [];
    public bool BundleConfigFiles { get; private set; }

    public ProfileExportForm(IEnumerable<HostsProfile> profiles)
    {
        Text = "Export Profiles";
        Icon = Properties.Resources.HostsFileEditor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 320);
        Padding = new Padding(12);

        Controls.Add(new Label { Text = "Profiles to export:", AutoSize = true, Location = new Point(12, 12) });

        _list = new CheckedListBox
        {
            Location = new Point(12, 34),
            Size = new Size(356, 200),
            CheckOnClick = true,
            DisplayMember = "FileName"
        };

        var profileList = profiles.OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.FileName).ToList();
        foreach (var profile in profileList)
        {
            // Default already exists on every machine (auto-created) — leave it
            // unchecked by default so a plain "export everything" doesn't produce
            // a confusing "Default (Imported)" duplicate on the target machine.
            _list.Items.Add(profile, !profile.IsDefault);
        }
        Controls.Add(_list);

        _chkBundleConfig = new CheckBox
        {
            Text = "Also include config file contents (may contain secrets — only for trusted transfers)",
            AutoSize = false,
            Size = new Size(356, 36),
            Location = new Point(12, 242)
        };
        Controls.Add(_chkBundleConfig);

        var btnOk = new Button { Text = "Export…", DialogResult = DialogResult.OK, Size = new Size(90, 28), Location = new Point(196, 284) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 28), Location = new Point(292, 284) };
        btnOk.Click += OnOkClick;
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectedProfiles = _list.CheckedItems.Cast<HostsProfile>().ToList();
        BundleConfigFiles = _chkBundleConfig.Checked;

        if (SelectedProfiles.Count == 0)
        {
            MessageBox.Show(this, "Select at least one profile to export.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
