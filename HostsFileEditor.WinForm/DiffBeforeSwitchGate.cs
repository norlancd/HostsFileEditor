namespace HostsFileEditor;

/// <summary>
/// Owns the "show a diff before switching profiles" gate: the diff computation +
/// <see cref="DiffPreviewForm"/> prompt wired into <see cref="ProfileSwitcher.DiffBeforeSwitch"/>.
/// Extracted out of MainForm so this concern can be reasoned about independently
/// of clipboard/menu/audit logic living in the rest of the form — its only
/// dependency is an owner window for dialogs.
/// </summary>
internal sealed class DiffBeforeSwitchGate
{
    private readonly IWin32Window _owner;
    private readonly string _ownerCaption;

    public DiffBeforeSwitchGate(IWin32Window owner, string ownerCaption)
    {
        _owner = owner;
        _ownerCaption = ownerCaption;
        ProfileSwitcher.DiffBeforeSwitch = ShowDiffPreview;
    }

    private bool ShowDiffPreview(HostsProfile profile)
    {
        if (!Properties.Settings.Default.DiffBeforeSwitchEnabled) return true;

        if (!File.Exists(profile.FilePath))
        {
            MessageBox.Show(_owner,
                $"Profile file not found:\n{profile.FilePath}",
                _ownerCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        HostsEntryList incoming;
        try
        {
            incoming = new HostsEntryList(File.ReadAllLines(profile.FilePath), filterDefault: false);
        }
        catch (IOException ex)
        {
            MessageBox.Show(_owner, $"Could not read profile:\n{ex.Message}",
                _ownerCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        var diff = ProfileDiff.Compute(HostsFile.Instance.Entries, incoming);

        if (diff.IsEmpty)
        {
            // No DNS changes — allow the switch, just skip the dialog.
            // (The generic "switched to profile" balloon still fires separately.)
            return true;
        }

        using var form = new DiffPreviewForm(profile.FileName, diff);
        return form.ShowDialog(_owner) == DialogResult.OK;
    }
}
