namespace HostsFileEditor;

internal sealed class ProfileSettingsForm : Form
{
    private static readonly HashSet<Keys> _dangerousKeys = new()
    {
        Keys.LWin | Keys.L,
        Keys.Alt | Keys.F4,
        Keys.Control | Keys.Alt | Keys.Delete,
    };

    private readonly HostsProfile _profile;
    private HostsProfileMetadata _metadata;

    private TextBox _txtDescription = null!;
    private TextBox _txtHotkey = null!;
    private NumericUpDown _nudSortOrder = null!;
    private Button _btnColorPicker = null!;
    private Panel _pnlColor = null!;
    private Label _lblError = null!;
    private Button _btnOk = null!;

    private int _hotkeyModifiers;
    private int _hotkeyKey;
    private string _color = string.Empty;

    public ProfileSettingsForm(HostsProfile profile)
    {
        _profile = profile;
        _metadata = profile.Metadata ?? new HostsProfileMetadata { Name = profile.FileName };
        _hotkeyModifiers = _metadata.HotkeyModifiers;
        _hotkeyKey = _metadata.HotkeyKey;
        _color = _metadata.Color;

        BuildUI();
        LoadValues();
    }

    private void BuildUI()
    {
        Text = $"Profile Settings — {_profile.FileName}";
        Icon = Properties.Resources.HostsFileEditor;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 250);
        Padding = new Padding(12);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(table);

        void AddRow(int row, string label, Control control)
        {
            table.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left | AnchorStyles.Top, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, row);
            table.Controls.Add(control, 1, row);
        }

        _txtDescription = new TextBox { Dock = DockStyle.Fill };
        AddRow(0, "Description:", _txtDescription);

        var colorRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
        _pnlColor = new Panel { Width = 20, Height = 20, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 4, 0) };
        _btnColorPicker = new Button { Text = "Pick…", AutoSize = true };
        _btnColorPicker.Click += OnPickColor;
        colorRow.Controls.Add(_pnlColor);
        colorRow.Controls.Add(_btnColorPicker);
        AddRow(1, "Color:", colorRow);

        _nudSortOrder = new NumericUpDown { Minimum = 0, Maximum = 999, Dock = DockStyle.Fill };
        AddRow(2, "Sort Order:", _nudSortOrder);

        _txtHotkey = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window };
        _txtHotkey.KeyDown += OnHotkeyKeyDown;
        _txtHotkey.GotFocus += (_, _) => _txtHotkey.SelectAll();
        AddRow(3, "Hotkey:", _txtHotkey);

        _lblError = new Label { ForeColor = Color.Red, AutoSize = true, MaximumSize = new Size(340, 0) };
        table.SetColumnSpan(_lblError, 2);
        table.Controls.Add(_lblError, 0, 4);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 36,
            Padding = new Padding(0, 4, 4, 0),
        };
        _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 75 };
        _btnOk.Click += OnOkClick;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 75 };
        buttonRow.Controls.Add(btnCancel);
        buttonRow.Controls.Add(_btnOk);
        Controls.Add(buttonRow);

        AcceptButton = _btnOk;
        CancelButton = btnCancel;
    }

    private void LoadValues()
    {
        _txtDescription.Text = _metadata.Description;
        _nudSortOrder.Value = Math.Clamp(_metadata.SortOrder, 0, 999);
        UpdateColorPanel();
        UpdateHotkeyText();
    }

    private void UpdateColorPanel()
    {
        if (!string.IsNullOrEmpty(_color))
        {
            try { _pnlColor.BackColor = ColorTranslator.FromHtml(_color); }
            catch { _pnlColor.BackColor = SystemColors.Window; }
        }
        else
        {
            _pnlColor.BackColor = SystemColors.Window;
        }
    }

    private void UpdateHotkeyText()
    {
        if (_hotkeyKey == 0)
        {
            _txtHotkey.Text = "(none — press keys to set, Backspace to clear)";
            _txtHotkey.ForeColor = SystemColors.GrayText;
        }
        else
        {
            _txtHotkey.Text = FormatChord(_hotkeyModifiers, _hotkeyKey);
            _txtHotkey.ForeColor = SystemColors.WindowText;
        }
    }

    private static string FormatChord(int modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(((Keys)key).ToString());
        return string.Join(" + ", parts);
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Back)
        {
            _hotkeyModifiers = 0;
            _hotkeyKey = 0;
            _lblError.Text = string.Empty;
            UpdateHotkeyText();
            return;
        }

        // Ignore modifier-only keystrokes
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        int mods = 0;
        if (e.Control) mods |= 2;
        if (e.Shift) mods |= 4;
        if (e.Alt) mods |= 1;

        int vk = (int)e.KeyCode;

        // Reject known dangerous chords
        if (IsDangerous(mods, vk))
        {
            _lblError.Text = "That key combination is reserved by the system.";
            return;
        }

        _hotkeyModifiers = mods;
        _hotkeyKey = vk;
        _lblError.Text = string.Empty;
        UpdateHotkeyText();
    }

    private static bool IsDangerous(int modifiers, int vk)
    {
        // Win+L (modifiers would include Win key — Keys.LWin is 91)
        if (vk == (int)Keys.L && (modifiers & 8) != 0) return true;
        // Alt+F4
        if (vk == (int)Keys.F4 && modifiers == 1) return true;
        // Ctrl+Alt+Del is intercepted by the OS before WM_HOTKEY, but reject it anyway
        if (vk == (int)Keys.Delete && modifiers == (2 | 1)) return true;
        return false;
    }

    private void OnPickColor(object? sender, EventArgs e)
    {
        using var dlg = new ColorDialog { Color = _pnlColor.BackColor };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _color = ColorTranslator.ToHtml(dlg.Color);
            UpdateColorPanel();
        }
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        // Actually attempt the registration now, synchronously, so a conflict — with
        // another profile OR with some other application's global hotkey — is reported
        // right here instead of via a tray balloon after this dialog has already closed.
        if (!HotkeyRegistry.TryAssignHotkey(_profile, _hotkeyModifiers, _hotkeyKey, out var error))
        {
            _lblError.Text = error;
            DialogResult = DialogResult.None;
            return;
        }

        var meta = _profile.Metadata ?? new HostsProfileMetadata();
        meta.Name = _profile.FileName;
        meta.Description = _txtDescription.Text;
        meta.SortOrder = (int)_nudSortOrder.Value;
        meta.Color = _color;
        meta.HotkeyModifiers = _hotkeyKey == 0 ? 0 : _hotkeyModifiers;
        meta.HotkeyKey = _hotkeyKey;
        meta.Save(_profile.FilePath);
        _profile.ReloadMetadata();
    }
}
