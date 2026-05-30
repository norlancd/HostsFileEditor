namespace HostsFileEditor;

internal class RollbackExpiryForm : Form
{
    public enum ExpiryResult { Revert, Snooze, Keep }

    public ExpiryResult ChosenResult { get; private set; } = ExpiryResult.Revert;
    public bool WasAutoReverted { get; private set; }

    private readonly System.Windows.Forms.Timer _countdown = new() { Interval = 1000 };
    private int _secondsLeft = 60;

    private readonly Label _descriptionLabel;
    private readonly Label _warningLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _countdownLabel;
    private readonly Button _revertBtn;
    private readonly Button _snoozeBtn;
    private readonly Button _keepBtn;

    public RollbackExpiryForm(RollbackTimer timer, bool externallyModified)
    {
        Text = "⏱  Timed profile expiring";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 230);
        ShowInTaskbar = true;

        var activatedAgo = DateTime.UtcNow - timer.ActivatedAt;
        var agoText = activatedAgo.TotalMinutes < 90
            ? $"{(int)activatedAgo.TotalMinutes} minutes"
            : $"{activatedAgo.TotalHours:F1} hours";

        _descriptionLabel = new Label
        {
            Text = $"Profile \"{timer.ActivatedProfileName}\" was activated {agoText} ago.\nRevert to previous configuration?",
            AutoSize = false,
            Size = new Size(410, 40),
            Location = new Point(15, 15)
        };

        _warningLabel = new Label
        {
            Text = "⚠  The hosts file was modified externally after this profile was activated.\nReverting may overwrite those changes.",
            ForeColor = Color.DarkOrange,
            AutoSize = false,
            Size = new Size(410, 36),
            Location = new Point(15, 60),
            Visible = externallyModified
        };

        int afterWarning = externallyModified ? 104 : 64;

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 60,
            Value = 60,
            Style = ProgressBarStyle.Continuous,
            Size = new Size(410, 22),
            Location = new Point(15, afterWarning)
        };

        _countdownLabel = new Label
        {
            Text = "Auto-reverts in 60s",
            AutoSize = true,
            Location = new Point(15, afterWarning + 28)
        };

        _revertBtn = new Button
        {
            Text = "Revert Now",
            Size = new Size(110, 30),
            Location = new Point(15, afterWarning + 60),
            DialogResult = System.Windows.Forms.DialogResult.None
        };
        _revertBtn.Click += (_, _) => { ChosenResult = ExpiryResult.Revert; Close(); };

        _snoozeBtn = new Button
        {
            Text = "Keep 30 min more",
            Size = new Size(130, 30),
            Location = new Point(140, afterWarning + 60),
            DialogResult = System.Windows.Forms.DialogResult.None
        };
        _snoozeBtn.Click += (_, _) => { ChosenResult = ExpiryResult.Snooze; Close(); };

        _keepBtn = new Button
        {
            Text = "Keep Permanently",
            Size = new Size(130, 30),
            Location = new Point(285, afterWarning + 60),
            DialogResult = System.Windows.Forms.DialogResult.None
        };
        _keepBtn.Click += (_, _) => { ChosenResult = ExpiryResult.Keep; Close(); };

        // Resize form to fit content
        ClientSize = new Size(440, afterWarning + 105);

        Controls.AddRange([_descriptionLabel, _warningLabel, _progressBar, _countdownLabel, _revertBtn, _snoozeBtn, _keepBtn]);

        _countdown.Tick += OnTick;
        FormClosed += (_, _) => _countdown.Dispose();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _countdown.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _secondsLeft--;
        _progressBar.Value = Math.Max(0, _secondsLeft);
        _countdownLabel.Text = $"Auto-reverts in {_secondsLeft}s";

        if (_secondsLeft <= 0)
        {
            _countdown.Stop();
            ChosenResult = ExpiryResult.Revert;
            WasAutoReverted = true;
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _countdown.Stop();
        base.OnFormClosing(e);
    }
}
