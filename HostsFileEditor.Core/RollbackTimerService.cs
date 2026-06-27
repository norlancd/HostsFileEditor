using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HostsFileEditor;

public class RollbackTimerService
{
    private static readonly Lazy<RollbackTimerService> _instance = new(() => new RollbackTimerService());
    public static RollbackTimerService Instance => _instance.Value;

    private static readonly string _timerJsonPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HostsFileEditor", "rollback-timer.json");

    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private SynchronizationContext? _uiContext;
    private static string? _cachedVersion;

    public RollbackTimer? ActiveTimer { get; private set; }

    /// <summary>Set by RecoverFromRestart when an auto-revert happened before the UI was ready.</summary>
    public string? PendingStartupNotification { get; private set; }

    /// <summary>Fires on the UI thread when the timer expires.</summary>
    public event EventHandler<RollbackTimer>? TimerExpired;

    /// <summary>Fires when a tray notification should be shown.</summary>
    public event EventHandler<string>? Notification;

    /// <summary>Fires after any state change (start / snooze / cancel / complete).</summary>
    public event EventHandler? StateChanged;

    private RollbackTimerService() { }

    public void SetUiContext(SynchronizationContext? ctx) => _uiContext = ctx;

    // ── Start ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshots the current hosts file, sets up the timer JSON, calls
    /// <paramref name="profileActivation"/> to do the actual switch, then starts the countdown.
    /// Returns false if the profile activation callback throws.
    /// </summary>
    public bool Start(string profileName, TimeSpan duration, Action profileActivation)
    {
        string? snapshotPath = null;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var snapshotName = $"__rollback_{now:yyyyMMdd_HHmmss}";
            snapshotPath = Path.Combine(HostsProfileList.EffectiveProfileDirectory, snapshotName);

            // Snapshot current state before switching
            Directory.CreateDirectory(HostsProfileList.EffectiveProfileDirectory);
            File.Copy(HostsFile.DefaultHostFilePath, snapshotPath, overwrite: true);
            var hash = "sha256:" + ComputeHash(File.ReadAllBytes(snapshotPath));

            ActiveTimer = new RollbackTimer
            {
                ActivatedProfileName = profileName,
                SnapshotFileName = snapshotName,
                ActivatedAt = now,
                ExpiresAt = now + duration,
                Status = RollbackTimerStatus.Active,
                SnapshotHash = hash,
                AppVersion = GetAppVersion()
            };

            SaveTimerJson();
        }

        // Activate profile OUTSIDE the lock (may call back into service via audit log)
        try
        {
            profileActivation();
        }
        catch
        {
            // Activation failed — clean up snapshot and timer state
            if (snapshotPath != null)
                try { File.Delete(snapshotPath); } catch { }
            lock (_lock) { CleanupState(RollbackTimerStatus.Cancelled); }
            return false;
        }

        lock (_lock)
        {
            // Audit log
            AuditLogger.Instance.Log(AuditActionType.TimedProfileSwitch, AuditSource.MainForm, new AuditDetail
            {
                ProfileTo = profileName,
                RollbackTimerMinutes = (int)Math.Round(ActiveTimer!.ExpiresAt.Subtract(ActiveTimer.ActivatedAt).TotalMinutes)
            });

            StartCountdown(ActiveTimer!.ExpiresAt - DateTime.UtcNow);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    // ── Revert ───────────────────────────────────────────────────────────────

    public void ExecuteRevert(bool autoReverted = true)
    {
        RollbackTimer? snapshot;
        string? snapshotPath;
        string? profileFrom;

        lock (_lock)
        {
            if (ActiveTimer == null) return;

            snapshot = ActiveTimer;
            profileFrom = snapshot.ActivatedProfileName;
            snapshotPath = Path.Combine(HostsProfileList.EffectiveProfileDirectory, snapshot.SnapshotFileName);

            // Verify snapshot exists
            if (!File.Exists(snapshotPath))
            {
                Notification?.Invoke(this, $"Rollback snapshot is missing. Auto-revert for '{profileFrom}' cancelled.");
                AuditLogger.Instance.Log(AuditActionType.RollbackExecuted, AuditSource.RollbackTimer,
                    new AuditDetail { AutoReverted = autoReverted, ProfileReverted = profileFrom });
                CleanupState(RollbackTimerStatus.Cancelled);
                return;
            }

            // Verify hash
            var content = File.ReadAllBytes(snapshotPath);
            var actualHash = "sha256:" + ComputeHash(content);
            if (actualHash != snapshot.SnapshotHash)
            {
                Notification?.Invoke(this, $"Rollback snapshot is corrupted. Auto-revert for '{profileFrom}' cancelled.");
                CleanupState(RollbackTimerStatus.Cancelled);
                return;
            }

            CleanupState(RollbackTimerStatus.Completed);
        }

        // Execute revert OUTSIDE lock
        HostsFile.Instance.Import(snapshotPath!);
        HostsFile.Instance.Save();

        try { File.Delete(snapshotPath!); } catch (IOException) { }

        AuditLogger.Instance.Log(AuditActionType.RollbackExecuted, AuditSource.RollbackTimer,
            new AuditDetail { AutoReverted = autoReverted, ProfileReverted = profileFrom });

        Notification?.Invoke(this, $"Hosts file reverted to configuration before '{profileFrom}' was activated.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Snooze ───────────────────────────────────────────────────────────────

    public void Snooze(TimeSpan additional)
    {
        lock (_lock)
        {
            if (ActiveTimer == null) return;

            var snoozeUntil = DateTime.UtcNow + additional;
            ActiveTimer.Status = RollbackTimerStatus.Snoozed;
            ActiveTimer.SnoozeUntil = snoozeUntil;
            SaveTimerJson();

            StartCountdown(additional);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Cancel ───────────────────────────────────────────────────────────────

    public void CancelTimer()
    {
        string? profileName;
        string? snapshotPath;

        lock (_lock)
        {
            if (ActiveTimer == null) return;

            profileName = ActiveTimer.ActivatedProfileName;
            snapshotPath = Path.Combine(HostsProfileList.EffectiveProfileDirectory, ActiveTimer.SnapshotFileName);

            CleanupState(RollbackTimerStatus.Cancelled);
        }

        try { File.Delete(snapshotPath!); } catch (IOException) { }

        AuditLogger.Instance.Log(AuditActionType.TimerCancelled, AuditSource.MainForm,
            new AuditDetail { ProfileKept = profileName });

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Restart recovery ─────────────────────────────────────────────────────

    /// <summary>
    /// Called before MainForm is shown. Returns a notification string if a revert was
    /// executed automatically (to show after the UI is ready).
    /// </summary>
    public string? RecoverFromRestart()
    {
        if (!File.Exists(_timerJsonPath)) return null;

        RollbackTimer? timer;
        try
        {
            var json = File.ReadAllText(_timerJsonPath);
            timer = JsonSerializer.Deserialize(json, RollbackTimerJsonContext.Default.RollbackTimer);
        }
        catch
        {
            try { File.Delete(_timerJsonPath); } catch { }
            return null;
        }

        if (timer == null) return null;

        if (timer.Status is RollbackTimerStatus.Cancelled or RollbackTimerStatus.Completed)
        {
            try { File.Delete(_timerJsonPath); } catch { }
            return null;
        }

        var effectiveExpiry = timer.Status == RollbackTimerStatus.Snoozed
            ? (timer.SnoozeUntil ?? timer.ExpiresAt)
            : timer.ExpiresAt;

        if (effectiveExpiry <= DateTime.UtcNow)
        {
            // Expired while app was closed — revert immediately
            lock (_lock) { ActiveTimer = timer; }
            ExecuteRevert(autoReverted: true);
            var msg = $"Profile '{timer.ActivatedProfileName}' timer expired while the app was closed. Hosts file has been auto-reverted.";
            PendingStartupNotification = msg;
            return msg;
        }

        // Still valid — restore in-process timer
        lock (_lock)
        {
            ActiveTimer = timer;
            StartCountdown(effectiveExpiry - DateTime.UtcNow);
        }

        return null;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void StartCountdown(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        _timer?.Dispose();
        _timer = new System.Threading.Timer(OnTimerCallback, null, delay, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private void OnTimerCallback(object? _)
    {
        RollbackTimer? timer;
        lock (_lock)
        {
            timer = ActiveTimer;
            if (timer?.Status is not (RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed))
                return;
        }

        if (_uiContext != null)
            _uiContext.Post(__ => TimerExpired?.Invoke(this, timer), null);
        else
            TimerExpired?.Invoke(this, timer);
    }

    private void SaveTimerJson()
    {
        var dir = Path.GetDirectoryName(_timerJsonPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(ActiveTimer, RollbackTimerJsonContext.Default.RollbackTimer);
        File.WriteAllText(_timerJsonPath, json, Encoding.UTF8);
    }

    private void CleanupState(RollbackTimerStatus finalStatus)
    {
        _timer?.Dispose();
        _timer = null;
        if (ActiveTimer != null) ActiveTimer.Status = finalStatus;
        try { File.Delete(_timerJsonPath); } catch (IOException) { }
        ActiveTimer = null;
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetAppVersion()
    {
        if (_cachedVersion != null) return _cachedVersion;
        _cachedVersion = typeof(RollbackTimerService).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        return _cachedVersion;
    }
}
