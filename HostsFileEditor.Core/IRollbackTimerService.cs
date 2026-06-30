namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="RollbackTimerService"/>'s instance surface — extracted
/// so consumers can eventually depend on this instead of the static <c>Instance</c>
/// accessor, without changing any behavior today.
/// </summary>
public interface IRollbackTimerService
{
    RollbackTimer? ActiveTimer { get; }

    string? PendingStartupNotification { get; }

    event EventHandler<RollbackTimer>? TimerExpired;

    event EventHandler<string>? Notification;

    event EventHandler? StateChanged;

    void SetUiContext(SynchronizationContext? ctx);

    void SetAuditLogger(IAuditLogger auditLogger);

    Task<bool> StartAsync(string profileName, TimeSpan duration, Func<Task> profileActivation);

    void ExecuteRevert(bool autoReverted = true);

    void Snooze(TimeSpan additional);

    void CancelTimer();

    string? RecoverFromRestart();
}
