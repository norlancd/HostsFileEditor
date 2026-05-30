namespace HostsFileEditor;

internal partial class MainForm
{
    private ToolStripStatusLabel? _statusProfileLabel;
    private ToolStripStatusLabel? _statusTimerLabel;
    private System.Windows.Forms.Timer? _timerCountdownTick;

    private void SetupRollbackTimer()
    {
        var svc = RollbackTimerService.Instance;

        svc.SetUiContext(SynchronizationContext.Current);
        svc.TimerExpired += OnRollbackTimerExpired;
        svc.Notification += OnRollbackNotification;
        svc.StateChanged += OnRollbackStateChanged;

        // Subscribe to profile changes to keep the status bar up to date
        ProfileSwitcher.ActiveArchiveChanged += RefreshStatusBar;

        BuildStatusBarLabels();

        _timerCountdownTick = new System.Windows.Forms.Timer { Interval = 1000 };
        _timerCountdownTick.Tick += (_, _) => RefreshTimerCountdown();

        FormClosed += (_, _) =>
        {
            svc.TimerExpired -= OnRollbackTimerExpired;
            svc.Notification -= OnRollbackNotification;
            svc.StateChanged -= OnRollbackStateChanged;
            ProfileSwitcher.ActiveArchiveChanged -= RefreshStatusBar;
            _timerCountdownTick?.Dispose();
        };

        RefreshStatusBar();

        if (svc.PendingStartupNotification is { } notice)
            notifyIcon.ShowBalloonTip(5000, "Rollback Timer", notice, ToolTipIcon.Info);
    }

    // ── Status bar ───────────────────────────────────────────────────────────

    private void BuildStatusBarLabels()
    {
        // Spring item pushes everything after it to the right
        var spring = new ToolStripStatusLabel { Spring = true };

        _statusProfileLabel = new ToolStripStatusLabel
        {
            BorderStyle = Border3DStyle.SunkenOuter,
            BorderSides = ToolStripStatusLabelBorderSides.Left,
            AutoSize = true,
            Padding = new Padding(6, 0, 6, 0)
        };

        _statusTimerLabel = new ToolStripStatusLabel
        {
            ForeColor = Color.FromArgb(140, 80, 0),
            BorderStyle = Border3DStyle.SunkenOuter,
            BorderSides = ToolStripStatusLabelBorderSides.Left,
            AutoSize = true,
            Padding = new Padding(6, 0, 6, 0),
            Visible = false,
            IsLink = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            ToolTipText = ""
        };
        _statusTimerLabel.Click += OnTimerLabelClick;

        statusStrip.Items.AddRange([spring, _statusProfileLabel, _statusTimerLabel]);
        statusStrip.ShowItemToolTips = true;
    }

    private void RefreshStatusBar()
    {
        if (InvokeRequired) { Invoke(RefreshStatusBar); return; }
        if (_statusProfileLabel == null) return;

        var active = ProfileSwitcher.ActiveArchive;
        _statusProfileLabel.Text = active != null
            ? $"Profile: {active.FileName}"
            : "Profile: Default";

        RefreshTimerCountdown();
    }

    private void RefreshTimerCountdown()
    {
        if (_statusTimerLabel == null) return;

        var timer = RollbackTimerService.Instance.ActiveTimer;

        if (timer?.Status is not (RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed))
        {
            _statusTimerLabel.Visible = false;
            _timerCountdownTick?.Stop();
            UpdateTrayTooltip();
            return;
        }

        var expiry = timer.Status == RollbackTimerStatus.Snoozed
            ? (timer.SnoozeUntil ?? timer.ExpiresAt)
            : timer.ExpiresAt;

        var remaining = expiry - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var countdown = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
            : remaining.TotalMinutes >= 1
                ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s"
                : $"{remaining.Seconds}s";

        var snoozeSuffix = timer.Status == RollbackTimerStatus.Snoozed ? " (snoozed)" : string.Empty;
        _statusTimerLabel.Text = $"⏱  Reverts in {countdown}{snoozeSuffix}";
        _statusTimerLabel.Visible = true;
        _statusTimerLabel.ToolTipText =
            $"Will revert to: state before \"{timer.ActivatedProfileName}\" was activated\n" +
            $"Activated at: {timer.ActivatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
            $"Click for snooze / cancel options";

        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        var timer = RollbackTimerService.Instance.ActiveTimer;

        if (timer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed)
        {
            var expiry = timer.Status == RollbackTimerStatus.Snoozed
                ? (timer.SnoozeUntil ?? timer.ExpiresAt)
                : timer.ExpiresAt;

            var remaining = expiry - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            var countdown = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
                : $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s";

            var tip = $"Hosts — \"{timer.ActivatedProfileName}\" reverts in {countdown}";
            notifyIcon.Text = tip.Length > 63 ? tip[..63] : tip;
        }
        else
        {
            notifyIcon.Text = "Hosts File Editor";
        }
    }

    // ── Timer label click → snooze / cancel menu ─────────────────────────────

    private void OnTimerLabelClick(object? sender, EventArgs e)
    {
        var svc = RollbackTimerService.Instance;
        if (svc.ActiveTimer?.Status is not (RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed))
            return;

        var menu = new ContextMenuStrip();

        void AddSnooze(string label, TimeSpan duration)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => svc.Snooze(duration);
            menu.Items.Add(item);
        }

        AddSnooze("Snooze 15 minutes", TimeSpan.FromMinutes(15));
        AddSnooze("Snooze 30 minutes", TimeSpan.FromMinutes(30));
        AddSnooze("Snooze 1 hour", TimeSpan.FromHours(1));
        AddSnooze("Snooze 2 hours", TimeSpan.FromHours(2));
        menu.Items.Add(new ToolStripSeparator());

        var cancelItem = new ToolStripMenuItem("Cancel timer — keep permanently");
        cancelItem.Click += (_, _) =>
        {
            var profileName = svc.ActiveTimer?.ActivatedProfileName ?? string.Empty;
            if (MessageBox.Show(this,
                    $"Keep \"{profileName}\" as the active configuration permanently?\nThe rollback timer will be cancelled.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                svc.CancelTimer();
        };
        menu.Items.Add(cancelItem);

        // Show the menu just above the status strip
        var pos = statusStrip.PointToScreen(new Point(
            _statusTimerLabel!.Bounds.Left,
            0));
        menu.Show(pos);
    }

    // ── State changes ─────────────────────────────────────────────────────────

    private void OnRollbackStateChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired) { Invoke(() => OnRollbackStateChanged(sender, e)); return; }

        var isActive = RollbackTimerService.Instance.ActiveTimer?.Status
            is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed;

        if (isActive)
        {
            RefreshTimerCountdown();
            _timerCountdownTick?.Start();
        }
        else
        {
            _statusTimerLabel!.Visible = false;
            _timerCountdownTick?.Stop();
            UpdateTrayTooltip();
        }

        RebuildProfilesMenus();
    }

    // ── Notifications + expiry ───────────────────────────────────────────────

    private void OnRollbackNotification(object? sender, string message)
    {
        if (InvokeRequired) { Invoke(() => OnRollbackNotification(sender, message)); return; }
        notifyIcon.ShowBalloonTip(5000, "Rollback Timer", message, ToolTipIcon.Info);
    }

    private void OnRollbackTimerExpired(object? sender, RollbackTimer timer)
    {
        if (InvokeRequired) { Invoke(() => OnRollbackTimerExpired(sender, timer)); return; }

        bool externallyModified =
            File.Exists(HostsFile.DefaultHostFilePath) &&
            File.GetLastWriteTimeUtc(HostsFile.DefaultHostFilePath) > timer.ActivatedAt;

        using var form = new RollbackExpiryForm(timer, externallyModified);
        form.ShowDialog(this);

        switch (form.ChosenResult)
        {
            case RollbackExpiryForm.ExpiryResult.Revert:
                RollbackTimerService.Instance.ExecuteRevert(autoReverted: form.WasAutoReverted);
                break;
            case RollbackExpiryForm.ExpiryResult.Snooze:
                RollbackTimerService.Instance.Snooze(TimeSpan.FromMinutes(30));
                break;
            case RollbackExpiryForm.ExpiryResult.Keep:
                RollbackTimerService.Instance.CancelTimer();
                break;
        }

        HostsArchiveList.Instance.Refresh();
    }

    // ── Activate with timer ───────────────────────────────────────────────────

    private void OnActivateWithTimerClick(HostsArchive archive)
    {
        var svc = RollbackTimerService.Instance;

        if (svc.ActiveTimer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed)
        {
            var confirm = MessageBox.Show(this,
                $"A rollback timer is already active for profile '{svc.ActiveTimer.ActivatedProfileName}'.\nCancel it and start a new one?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;
            svc.CancelTimer();
        }

        using var durationForm = new TimerDurationForm();
        if (durationForm.ShowDialog(this) != DialogResult.OK || durationForm.SelectedDuration == null)
            return;

        svc.Start(
            archive.FileName,
            durationForm.SelectedDuration.Value,
            () => ProfileSwitcher.Activate(archive, ProfileSwitcher.TriggerSource.TrayMenu));
    }
}
