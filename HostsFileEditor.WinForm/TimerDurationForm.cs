namespace HostsFileEditor;

internal class TimerDurationForm : Form
{
    public TimeSpan? SelectedDuration { get; private set; }

    private readonly NumericUpDown _customMinutes;
    private readonly Button _okBtn;

    public TimerDurationForm()
    {
        Text = "Activate with Rollback Timer";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 170);

        Controls.Add(new Label
        {
            Text = "Revert automatically after:",
            AutoSize = true,
            Location = new Point(15, 15)
        });

        // Preset buttons
        var presets = new (string Label, int Minutes)[]
        {
            ("15 min", 15), ("30 min", 30), ("1 hour", 60), ("2 hours", 120)
        };

        int x = 15;
        foreach (var (label, minutes) in presets)
        {
            var btn = new Button
            {
                Text = label,
                Size = new Size(65, 28),
                Location = new Point(x, 42),
                Tag = minutes
            };
            btn.Click += OnPresetClick;
            Controls.Add(btn);
            x += 70;
        }

        Controls.Add(new Label
        {
            Text = "Custom (minutes):",
            AutoSize = true,
            Location = new Point(15, 85)
        });

        _customMinutes = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1440,
            Value = 30,
            Location = new Point(135, 82),
            Size = new Size(80, 23)
        };
        Controls.Add(_customMinutes);

        _okBtn = new Button
        {
            Text = "OK",
            Size = new Size(80, 28),
            Location = new Point(145, 125),
            DialogResult = System.Windows.Forms.DialogResult.OK
        };
        _okBtn.Click += OnOkClick;
        Controls.Add(_okBtn);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 28),
            Location = new Point(235, 125),
            DialogResult = System.Windows.Forms.DialogResult.Cancel
        };
        Controls.Add(cancelBtn);

        AcceptButton = _okBtn;
        CancelButton = cancelBtn;
    }

    private void OnPresetClick(object? sender, EventArgs e)
    {
        if (sender is Button { Tag: int minutes })
        {
            SelectedDuration = TimeSpan.FromMinutes(minutes);
            DialogResult = System.Windows.Forms.DialogResult.OK;
            Close();
        }
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectedDuration = TimeSpan.FromMinutes((double)_customMinutes.Value);
    }
}
