namespace HostsFileEditor;

/// <summary>
/// Shows the manifest of an export package and lets the user pick which profiles
/// to actually import from it. Profiles are always renamed on collision — the user
/// sees this note up front so there are no surprises after clicking Import.
/// </summary>
internal sealed class ProfileImportForm : Form
{
    private readonly CheckedListBox _list;

    public List<string> SelectedFileNames { get; private set; } = [];

    public ProfileImportForm(ProfileExportManifest manifest)
    {
        Text = "Import Profiles";
        Icon = Properties.Resources.HostsFileEditor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 370);
        Padding = new Padding(12);

        var infoLabel = new Label
        {
            Text = $"Package from: {manifest.MachineName}\n" +
                   $"Exported: {manifest.ExportedAtUtc.ToLocalTime():g}   " +
                   $"App version: {manifest.AppVersion}\n" +
                   "Imported profiles are renamed if a name collision exists and are not activated automatically.",
            AutoSize = false,
            Size = new Size(396, 50),
            Location = new Point(12, 10)
        };
        Controls.Add(infoLabel);

        Controls.Add(new Label { Text = "Profiles to import:", AutoSize = true, Location = new Point(12, 68) });

        _list = new CheckedListBox
        {
            Location = new Point(12, 90),
            Size = new Size(396, 222),
            CheckOnClick = true
        };
        foreach (var entry in manifest.Profiles)
        {
            var display = entry.OriginalConfigSourcePath != null
                ? $"{entry.FileName}  (had config: {Path.GetFileName(entry.OriginalConfigSourcePath)})"
                : entry.FileName;
            _list.Items.Add(new ImportItem(entry.FileName, display), true);
        }
        Controls.Add(_list);

        var btnOk = new Button { Text = "Import", DialogResult = DialogResult.OK, Size = new Size(90, 28), Location = new Point(226, 328) };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 28), Location = new Point(322, 328) };
        btnOk.Click += OnOkClick;
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectedFileNames = _list.CheckedItems.Cast<ImportItem>().Select(i => i.FileName).ToList();

        if (SelectedFileNames.Count == 0)
        {
            MessageBox.Show(this, "Select at least one profile to import.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }

    private sealed record ImportItem(string FileName, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
