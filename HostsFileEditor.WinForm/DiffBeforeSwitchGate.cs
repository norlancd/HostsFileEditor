namespace HostsFileEditor;

/// <summary>
/// Owns the "show a diff before switching profiles" gate: the diff computation +
/// <see cref="DiffPreviewForm"/> prompt wired into <see cref="IProfileSwitcher.DiffBeforeSwitch"/>.
/// Extracted out of MainForm so this concern can be reasoned about independently
/// of clipboard/menu/audit logic living in the rest of the form — its only
/// dependency is an owner window for dialogs.
/// </summary>
internal sealed class DiffBeforeSwitchGate
{
    private readonly IWin32Window _owner;
    private readonly string _ownerCaption;

    public DiffBeforeSwitchGate(IWin32Window owner, string ownerCaption, IProfileSwitcher profileSwitcher)
    {
        _owner = owner;
        _ownerCaption = ownerCaption;
        profileSwitcher.DiffBeforeSwitch = ShowDiffPreview;
    }

    private Task<bool> ShowDiffPreview(HostsProfile profile)
    {
        if (!Properties.Settings.Default.DiffBeforeSwitchEnabled) return Task.FromResult(true);

        if (!File.Exists(profile.FilePath))
        {
            MessageBox.Show(_owner,
                $"Profile file not found:\n{profile.FilePath}",
                _ownerCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Task.FromResult(false);
        }

        HostsEntryList incoming;
        try
        {
            incoming = new HostsEntryList(new Utilities.UndoManager(), File.ReadAllLines(profile.FilePath), filterDefault: false);
        }
        catch (IOException ex)
        {
            MessageBox.Show(_owner, $"Could not read profile:\n{ex.Message}",
                _ownerCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return Task.FromResult(false);
        }

        var diff = ProfileDiff.Compute(HostsFile.Instance.Entries, incoming);

        if (diff.IsEmpty)
        {
            // No DNS changes — allow the switch, just skip the dialog.
            // (The generic "switched to profile" balloon still fires separately.)
            return Task.FromResult(true);
        }

        using var form = new DiffPreviewForm(profile.FileName, diff);
        return Task.FromResult(form.ShowDialog(_owner) == DialogResult.OK);
    }
}
