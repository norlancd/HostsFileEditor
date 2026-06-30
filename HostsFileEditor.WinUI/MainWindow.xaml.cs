using HostsFileEditor.Services;
using HostsFileEditor.Utilities;
using HostsFileEditor.Win32;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Windows.System;
using WinRT;
using WinRT.Interop;

namespace HostsFileEditor;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    internal ObservableCollection<HostsEntry> Entries { get; } = [];

    internal ObservableCollection<HostsProfile> Archives { get; } = [];

    private IEnumerable<HostsEntry>? _clipboardEntries;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPingIPs { get; private set; }

    public bool IsRemoveDefaultText { get; private set; }

    public bool IsDiffBeforeSwitchEnabled { get; private set; }

    public GridLength ArchivesColumnWidth { get; private set; } = new(0);

    public bool IsArchiveVisible { get; private set; }

    public bool IsBackEnabled => IsArchiveVisible;

    public Visibility ArchivesEmptyVisibility => Archives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TimerStatusVisibility =>
        _rollbackTimerService.ActiveTimer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string TimerStatusText
    {
        get
        {
            var timer = _rollbackTimerService.ActiveTimer;
            if (timer?.Status is not (RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed))
                return string.Empty;

            var expiry = timer.Status == RollbackTimerStatus.Snoozed ? (timer.SnoozeUntil ?? timer.ExpiresAt) : timer.ExpiresAt;
            var remaining = expiry - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            var countdown = remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m"
                : remaining.TotalMinutes >= 1
                    ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s"
                    : $"{remaining.Seconds}s";

            var snoozeSuffix = timer.Status == RollbackTimerStatus.Snoozed ? " (snoozed)" : string.Empty;
            return $"⏱ Reverts in {countdown}{snoozeSuffix}";
        }
    }

    public Visibility MainViewVisibility => IsArchiveVisible ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ArchiveViewVisibility => IsArchiveVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EntriesEmptyVisibility => _hostsFile.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EntriesFilteredVisibility => _hostsFile.Entries.Count > 0 && Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsFilterCommentsHidden { get; private set; }

    public bool IsFilterDisabledHidden { get; private set; }

    public int ActiveFilterCount => (IsFilterCommentsHidden ? 1 : 0) + (IsFilterDisabledHidden ? 1 : 0);

    public Visibility ActiveFiltersBadgeVisibility => ActiveFilterCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    // ── Profile indicator (DropDownButton, right of MenuBar) ────────────────────
    public string StatusProfileLabel =>
        _profileSwitcher.IsHostsDisabled ? "Hosts Disabled"
        : _profileSwitcher.ActiveProfile is { IsDefault: false } p ? p.FileName
        : "Default";

    public bool IsTimerRunning =>
        _rollbackTimerService.ActiveTimer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed;

    public Brush ProfileDotBrush
    {
        get
        {
            if (_profileSwitcher.IsHostsDisabled)
                return StatusBarHelper.Dot(StatusBarHelper.DotColor.Red);
            if (IsTimerRunning)
                return StatusBarHelper.Dot(StatusBarHelper.DotColor.Yellow);
            if (_profileSwitcher.ActiveProfile is { IsDefault: false })
                return StatusBarHelper.Dot(StatusBarHelper.DotColor.Green);
            return StatusBarHelper.Dot(StatusBarHelper.DotColor.Gray);
        }
    }

    private MicaController? _micaController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private Grid? _titleBarHost;

    private bool _isAnimatingArchive; // prevent re-entrant animations

    private readonly DialogService _dialogService;
    private readonly AnimationService _animationService;
    private readonly SelectionStateService _selectionService;
    private readonly IHostsFile _hostsFile;
    private readonly IUndoManager _undoManager;
    private readonly IHostsProfileList _profileList;
    private readonly IProfileSwitcher _profileSwitcher;
    private readonly IHotkeyRegistry _hotkeyRegistry;
    private readonly IRollbackTimerService _rollbackTimerService;
    private readonly IAuditLogger _auditLogger;
    private readonly IProfileExportImportService _exportImportService;
    private HotkeyMessageHook? _hotkeyHook;
    private DispatcherTimer? _timerCountdownTick;
    private DispatcherTimer? _notificationTimer;

    public MainWindow(
        DialogService dialogService, AnimationService animationService,
        IHostsFile hostsFile, IUndoManager undoManager, IHostsProfileList profileList, IProfileSwitcher profileSwitcher,
        IHotkeyRegistry hotkeyRegistry, IRollbackTimerService rollbackTimerService, IAuditLogger auditLogger,
        IProfileExportImportService exportImportService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _animationService = animationService ?? throw new ArgumentNullException(nameof(animationService));
        _hostsFile = hostsFile;
        _undoManager = undoManager;
        _profileList = profileList;
        _profileSwitcher = profileSwitcher;
        _hotkeyRegistry = hotkeyRegistry;
        _auditLogger = auditLogger;
        _rollbackTimerService = rollbackTimerService;
        _exportImportService = exportImportService;
        _profileSwitcher.ProfileError = msg => { if (Content?.XamlRoot is { } root) _ = _dialogService.ShowErrorAsync(root, "Profile Error", msg); };
        _profileSwitcher.DiffBeforeSwitch = ShowDiffPreviewAsync;
        _hotkeyRegistry.HotkeyConflictNotify += OnHotkeyConflict;

        _auditLogger.IntegrityFailed += OnAuditIntegrityFailed;
        _auditLogger.LogError += OnAuditLogError;
        Closed += (_, _) =>
        {
            _auditLogger.IntegrityFailed -= OnAuditIntegrityFailed;
            _auditLogger.LogError -= OnAuditLogError;
        };
        _auditLogger.Initialize(); // after subscribing, so IntegrityFailed is handled

        InitializeComponent();

        _selectionService = new SelectionStateService(
            hasSelection: () => EntriesList is not null && EntriesList.SelectedItems.Count > 0,
            setRemoveEnabled: v => { if (RemoveButton is not null) RemoveButton.IsEnabled = v; },
            setDuplicateEnabled: v => { if (DuplicateButton is not null) DuplicateButton.IsEnabled = v; },
            setMoveUpEnabled: v => { if (MoveUpButton is not null) MoveUpButton.IsEnabled = v; },
            setMoveDownEnabled: v => { if (MoveDownButton is not null) MoveDownButton.IsEnabled = v; },
            setToggleEnabled: v => { },
            setCtxCopyVis: v => { if (CtxCopy is not null) CtxCopy.Visibility = v ? Visibility.Visible : Visibility.Collapsed; },
            setCtxCutVis: v => { if (CtxCut is not null) CtxCut.Visibility = v ? Visibility.Visible : Visibility.Collapsed; },
            setCtxPasteVis: v => { if (CtxPaste is not null) CtxPaste.Visibility = v ? Visibility.Visible : Visibility.Collapsed; },
            setCtxAddAboveVis: v => { if (CtxAddAbove is not null) CtxAddAbove.Visibility = v ? Visibility.Visible : Visibility.Collapsed; },
            setCtxAddBelowVis: v => { if (CtxAddBelow is not null) CtxAddBelow.Visibility = v ? Visibility.Visible : Visibility.Collapsed; },
            setUndoRedoVis: (undo, redo) =>
            {
                if (CtxUndo is not null) CtxUndo.Visibility = undo ? Visibility.Visible : Visibility.Collapsed;
                if (CtxRedo is not null) CtxRedo.Visibility = redo ? Visibility.Visible : Visibility.Collapsed;
            });

        TrySetAppWindowTitleBar();
        TryEnableMicaBackdrop();
        RefreshEntries();

        // Subscribe before RestoreFromSettings() so the dropdown/active-profile label
        // immediately reflect whatever it resolves — then refresh once more explicitly
        // for the edge case where it finds no match and never raises the event.
        _profileSwitcher.ActiveProfileChanged += OnActiveProfileChanged;
        Closed += (_, _) => _profileSwitcher.ActiveProfileChanged -= OnActiveProfileChanged;
        _profileSwitcher.RestoreFromSettings();
        RefreshArchives();

        // WinUI has no Form.WndProc-style hook for WM_HOTKEY, so subclass the native
        // window to catch it — same global hotkeys WinForm registers, shared via Core.
        var hwnd = GetHwnd();
        _hotkeyHook = new HotkeyMessageHook(hwnd, OnHotkeyPressed);
        _hotkeyRegistry.Initialize(hwnd);
        Closed += (_, _) =>
        {
            _hotkeyRegistry.UnregisterAll();
            _hotkeyHook?.Dispose();
        };

        // RecoverFromRestart() must run before subscribing to Notification, so the
        // auto-revert it may trigger (if the timer expired while the app was closed)
        // doesn't fire that event before anyone's listening — PendingStartupNotification
        // is checked explicitly afterward instead, showing it exactly once.
        _rollbackTimerService.SetAuditLogger(_auditLogger);
        var startupRevertMessage = _rollbackTimerService.RecoverFromRestart();
        if (startupRevertMessage != null)
            _profileSwitcher.SyncActiveAfterExternalWrite();

        _rollbackTimerService.TimerExpired += OnRollbackTimerExpired;
        _rollbackTimerService.Notification += OnRollbackNotification;
        _rollbackTimerService.StateChanged += OnRollbackStateChanged;
        Closed += (_, _) =>
        {
            _rollbackTimerService.TimerExpired -= OnRollbackTimerExpired;
            _rollbackTimerService.Notification -= OnRollbackNotification;
            _rollbackTimerService.StateChanged -= OnRollbackStateChanged;
            _timerCountdownTick?.Stop();
        };

        _timerCountdownTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerCountdownTick.Tick += (_, _) => OnPropertyChanged(nameof(TimerStatusText));
        RefreshTimerStatus();

        if (_rollbackTimerService.PendingStartupNotification is { } notice)
        {
            // XamlRoot isn't available yet — the window hasn't been Activate()d by
            // App.OnLaunched at this point in the constructor — so defer until it is.
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (Content?.XamlRoot is { } root)
                    _ = _dialogService.ShowErrorAsync(root, "Rollback Timer", notice);
            });
        }

        IsPingIPs = LocalSettings.GetBool("AutoPingIPs", defaultValue: false);
        IsRemoveDefaultText = LocalSettings.GetBool("RemoveDefaultText", defaultValue: false);
        IsDiffBeforeSwitchEnabled = LocalSettings.GetBool("DiffBeforeSwitchEnabled", defaultValue: false);
        IsArchiveVisible = LocalSettings.GetBool("ArchiveVisible", defaultValue: false);
        HostsEntry.AutoPingIPAddress = IsPingIPs;
        HostsFile.RemoveDefaultText = IsRemoveDefaultText;
        ArchivesColumnWidth = IsArchiveVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        OnPropertyChanged(nameof(IsBackEnabled));
        OnPropertyChanged(nameof(MainViewVisibility));
        OnPropertyChanged(nameof(ArchiveViewVisibility));
        OnPropertyChanged(nameof(ArchivesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesFilteredVisibility));
        OnPropertyChanged(nameof(IsFilterCommentsHidden));
        OnPropertyChanged(nameof(IsFilterDisabledHidden));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(ActiveFiltersBadgeVisibility));
        // Ensure buttons reflect current selection/state at startup
        _selectionService.UpdateSelectionDependentButtons();
        _selectionService.UpdateContextMenuItems();

        // Subscribe to undo history changes so we can update Undo/Redo visibility
        _undoManager.HistoryChanged += OnUndoHistoryChanged;

        // Subscribe to core entries changes so undo/redo of add/remove shows in UI
        _hostsFile.Entries.ListChanged += OnCoreEntriesListChanged;

        // Unsubscribe when window closes to avoid leaks
        Closed += (s, e) =>
        {
            _undoManager.HistoryChanged -= OnUndoHistoryChanged;
            _hostsFile.Entries.ListChanged -= OnCoreEntriesListChanged;
        };
    }

    private void OnCoreEntriesListChanged(object? sender, ListChangedEventArgs e)
    {
        // ItemChanged means a property on an existing item changed — x:Bind Mode=OneWay
        // handles those updates automatically via INotifyPropertyChanged. Rebuilding the
        // whole list on every keystroke causes visible lag, so we skip it here.
        if (e.ListChangedType == ListChangedType.ItemChanged) return;

        _ = DispatcherQueue.TryEnqueue(() => RefreshEntries(preserveSelection: true));
    }

    private void OnActiveProfileChanged()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            OnPropertyChanged(nameof(StatusProfileLabel));
            OnPropertyChanged(nameof(ProfileDotBrush));
            RefreshArchives();

            if (_profileSwitcher.IsHostsDisabled)
                ShowNotification("Hosts file disabled.", InfoBarSeverity.Warning);
            else if (_profileSwitcher.ActiveProfile is { IsDefault: false } p)
                ShowNotification($"Switched to: {p.FileName}", InfoBarSeverity.Success);
            else
                ShowNotification("Switched to default profile.", InfoBarSeverity.Informational);
        });
    }

    // Raised from HotkeyMessageHook's native callback — not the UI thread, so hop
    // back via the dispatcher before touching anything bound to the UI.
    private void OnHotkeyPressed(int id)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var profile = _hotkeyRegistry.GetProfileById(id);
            if (profile != null)
                _ = _profileSwitcher.ActivateAsync(profile, ProfileSwitcher.TriggerSource.TrayHotkey);
        });
    }

    private void OnHotkeyConflict(HostsProfile profile, HostsProfileMetadata metadata)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (Content?.XamlRoot is { } root)
                _ = _dialogService.ShowErrorAsync(root, "Hotkey Conflict", $"Could not register the hotkey for profile \"{profile.FileName}\" — it may already be in use by another application.");
        });
    }

    private void OnAuditIntegrityFailed(object? sender, string message)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (Content?.XamlRoot is { } root)
                _ = _dialogService.ShowErrorAsync(root, "Audit Log Warning", message);
        });
    }

    private void OnAuditLogError(object? sender, string message)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (Content?.XamlRoot is { } root)
                _ = _dialogService.ShowErrorAsync(root, "Audit Log Error", message);
        });
    }

    // ── Rollback timer ───────────────────────────────────────────────────────

    private void RefreshTimerStatus()
    {
        OnPropertyChanged(nameof(TimerStatusVisibility));
        OnPropertyChanged(nameof(TimerStatusText));

        bool isActive = _rollbackTimerService.ActiveTimer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed;
        if (isActive)
            _timerCountdownTick?.Start();
        else
            _timerCountdownTick?.Stop();

        RefreshTimerStatusFlyout();
    }

    private void RefreshTimerStatusFlyout()
    {
        if (TimerStatusFlyout is null) return;

        TimerStatusFlyout.Items.Clear();

        void AddSnooze(string label, TimeSpan duration)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => _rollbackTimerService.Snooze(duration);
            TimerStatusFlyout.Items.Add(item);
        }

        AddSnooze("Snooze 15 Minutes", TimeSpan.FromMinutes(15));
        AddSnooze("Snooze 30 Minutes", TimeSpan.FromMinutes(30));
        AddSnooze("Snooze 1 Hour", TimeSpan.FromHours(1));
        AddSnooze("Snooze 2 Hours", TimeSpan.FromHours(2));
        TimerStatusFlyout.Items.Add(new MenuFlyoutSeparator());

        var cancelItem = new MenuFlyoutItem { Text = "Cancel Timer — Keep Permanently" };
        cancelItem.Click += async (_, _) =>
        {
            var profileName = _rollbackTimerService.ActiveTimer?.ActivatedProfileName ?? string.Empty;
            if (Content?.XamlRoot is not { } root) return;

            var confirmed = await _dialogService.ShowConfirmationAsync(
                root, "Cancel Rollback Timer",
                $"Keep \"{profileName}\" as the active configuration permanently?\nThe rollback timer will be cancelled.");
            if (confirmed)
                _rollbackTimerService.CancelTimer();
        };
        TimerStatusFlyout.Items.Add(cancelItem);
    }

    private void OnRollbackStateChanged(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            RefreshTimerStatus();
            QueueRefreshProfileSwitcher();
        });
    }

    private void OnRollbackNotification(object? sender, string message)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (Content?.XamlRoot is { } root)
                _ = _dialogService.ShowErrorAsync(root, "Rollback Timer", message);
        });
    }

    private void OnRollbackTimerExpired(object? sender, RollbackTimer timer)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            if (Content?.XamlRoot is not { } root) return;

            bool externallyModified =
                File.Exists(HostsFile.DefaultHostFilePath) &&
                File.GetLastWriteTimeUtc(HostsFile.DefaultHostFilePath) > timer.ActivatedAt;

            var result = await _dialogService.ShowRollbackExpiryAsync(root, timer.ActivatedProfileName, timer.ActivatedAt, externallyModified);

            switch (result.Choice)
            {
                case RollbackExpiryChoice.Revert:
                    _rollbackTimerService.ExecuteRevert(autoReverted: result.WasAutoReverted);
                    _profileSwitcher.SyncActiveAfterExternalWrite();
                    break;
                case RollbackExpiryChoice.Snooze:
                    _rollbackTimerService.Snooze(TimeSpan.FromMinutes(30));
                    break;
                case RollbackExpiryChoice.Keep:
                    _rollbackTimerService.CancelTimer();
                    break;
            }

            _profileList.Refresh();
        });
    }

    private async void OnArchiveTimerClick(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is not HostsProfile profile) return;
        await ActivateWithTimerAsync(profile);
    }

    private async Task ActivateWithTimerAsync(HostsProfile profile)
    {
        if (Content?.XamlRoot is not { } root) return;

        if (_rollbackTimerService.ActiveTimer?.Status is RollbackTimerStatus.Active or RollbackTimerStatus.Snoozed)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                root, "Rollback Timer Already Active",
                $"A rollback timer is already active for profile '{_rollbackTimerService.ActiveTimer.ActivatedProfileName}'.\nCancel it and start a new one?");
            if (!confirmed) return;
            _rollbackTimerService.CancelTimer();
        }

        var duration = await _dialogService.ShowTimerDurationAsync(root);
        if (duration is null) return;

        await _rollbackTimerService.StartAsync(profile.FileName, duration.Value, () => _profileSwitcher.ActivateAsync(profile, ProfileSwitcher.TriggerSource.TrayMenu));
    }

    /// <summary>
    /// Wired into <see cref="IProfileSwitcher.DiffBeforeSwitch"/> — ActivateAsync only
    /// calls this when the "Show Diff Before Switching Profiles" setting is on, so no
    /// need to re-check it here (unlike WinForm's gate, which redundantly does).
    /// </summary>
    private async Task<bool> ShowDiffPreviewAsync(HostsProfile profile)
    {
        if (Content?.XamlRoot is not { } root) return true;

        if (!File.Exists(profile.FilePath))
        {
            await _dialogService.ShowErrorAsync(root, "Profile Error", $"Profile file not found:\n{profile.FilePath}");
            return false;
        }

        HostsEntryList incoming;
        try
        {
            incoming = new HostsEntryList(new UndoManager(), File.ReadAllLines(profile.FilePath), filterDefault: false);
        }
        catch (IOException ex)
        {
            await _dialogService.ShowErrorAsync(root, "Profile Error", $"Could not read profile:\n{ex.Message}");
            return false;
        }

        var diff = ProfileDiff.Compute(_hostsFile.Entries, incoming);

        if (diff.IsEmpty)
        {
            // No DNS changes — allow the switch, just skip the dialog.
            return true;
        }

        return await _dialogService.ShowProfileDiffAsync(root, profile.FileName, diff);
    }

    private void TrySetAppWindowTitleBar()
    {
        var hwnd = GetHwnd();
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Extend content and use custom title bar element from XAML
        ExtendsContentIntoTitleBar = true;
        if (Content is FrameworkElement root && root.FindName("AppTitleBar") is Grid fe)
        {
            _titleBarHost = fe;
            SetTitleBar(fe);
        }
        if (appWindow is not null)
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Keep content clear of the caption buttons area
            appWindow.Changed += (_, __) => UpdateTitleBarPadding(appWindow);
            SizeChanged += (_, __) => UpdateTitleBarPadding(appWindow);
            UpdateTitleBarPadding(appWindow);
        }
    }

    private void UpdateTitleBarPadding(AppWindow appWindow)
    {
        if (_titleBarHost is null)
        {
            return;
        }

        // Insets provided by the system for areas occupied by caption buttons and drag region.
        var leftInset = appWindow.TitleBar.LeftInset;
        var rightInset = appWindow.TitleBar.RightInset;

        // Keep some horizontal breathing room and avoid overlap with caption buttons.
        var baseLeft = 4d;
        var baseRight = 12d;
        _titleBarHost.Padding = new Thickness(baseLeft + leftInset, 0, baseRight + rightInset, 0);
    }

    private bool TryEnableMicaBackdrop()
    {
        if (!MicaController.IsSupported())
        {
            return false;
        }

        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Default
        };

        _micaController = new MicaController { Kind = MicaKind.BaseAlt };
        _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);

        Activated += (s, e) =>
        {
            if (_backdropConfiguration is not null)
            {
                _backdropConfiguration.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
            }
        };

        Closed += (s, e) =>
        {
            _micaController?.Dispose();
            _micaController = null;
            _backdropConfiguration = null;
        };

        return true;
    }

    private IntPtr GetHwnd() => WindowNative.GetWindowHandle(this);

    private void OnCopyAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        => TryInvokeUnlessTextBox(() => OnCopyClick(this, new RoutedEventArgs()), args);

    private void OnCutAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        => TryInvokeUnlessTextBox(() => OnCutClick(this, new RoutedEventArgs()), args);

    private void OnPasteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        => TryInvokeUnlessTextBox(() => OnPasteClick(this, new RoutedEventArgs()), args);

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Win32FileDialogs.OpenFileDialog(GetHwnd(), "All Files (*.*)|*.*");
            if (!string.IsNullOrWhiteSpace(path))
            {
                _hostsFile.Import(path);
                _auditLogger.Log(AuditActionType.FileImported, AuditSource.MainForm, new AuditDetail { SourcePath = path });
                RefreshEntries();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Error Saving Hosts File", $"An error occurred while saving the hosts file:\n\n{ex.Message}");
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _hostsFile.Save();
        ShowNotification("Hosts file saved.", InfoBarSeverity.Success);
    }

    private void OnSaveAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        => TryInvokeUnlessTextBox(() => OnSaveClick(this, new RoutedEventArgs()), args);

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Win32FileDialogs.SaveFileDialog(GetHwnd(), "hosts", "Hosts (*.txt;*.hosts)|*.txt;*.hosts|Text (*.txt)|*.txt|All Files (*.*)|*.*");
            if (!string.IsNullOrWhiteSpace(path))
            {
                _hostsFile.SaveAs(path);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Error Saving Hosts File", $"An error occurred while saving the hosts file:\n\n{ex.Message}");
        }
    }

    private void OnInsertBelowClick(object sender, RoutedEventArgs e)
    {
        var current = EntriesList.SelectedItems.Cast<HostsEntry>().FirstOrDefault();
        if (current != null)
        {
            _hostsFile.Entries.InsertAfter(current);
            RefreshEntries(true);
        }
    }

    private void OnInsertAboveClick(object sender, RoutedEventArgs e)
    {
        var current = EntriesList.SelectedItems.Cast<HostsEntry>().FirstOrDefault();
        if (current != null)
        {
            _hostsFile.Entries.InsertBefore(current);
            RefreshEntries(true);
        }
    }

    private void OnAddToTop(object sender, RoutedEventArgs e)
    {
        var first = _hostsFile.Entries.FirstOrDefault();
        if (first != null)
        {
            _hostsFile.Entries.InsertBefore(first);
        }
        else
        {
            _hostsFile.Entries.Add();
        }

        Entries.Insert(0, _hostsFile.Entries.First());
        OnPropertyChanged(nameof(EntriesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesFilteredVisibility));
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        var selected = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (selected.Count == 0) return;

        var entries = _hostsFile.Entries;
        var topIndex = selected.Select(s => entries.IndexOf(s)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (topIndex <= 0) return;

        entries.MoveBefore(selected, entries[topIndex - 1]);
        RefreshEntries(true);
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        var selected = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (selected.Count == 0) return;

        var entries = _hostsFile.Entries;
        var bottomIndex = selected.Select(s => entries.IndexOf(s)).Where(i => i >= 0).DefaultIfEmpty(-1).Max();
        if (bottomIndex < 0 || bottomIndex >= entries.Count - 1) return;

        entries.MoveAfter(selected, entries[bottomIndex + 1]);
        RefreshEntries(true);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var items = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (items.Count > 0)
        {
            _hostsFile.Entries.Remove(items);
            foreach (var i in items)
            {
                Entries.Remove(i);
            }
        }
        OnPropertyChanged(nameof(EntriesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesFilteredVisibility));

        // Update UI buttons when selection changes cause deletion
        _selectionService.UpdateSelectionDependentButtons();
        _selectionService.UpdateContextMenuItems();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var rows = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (rows.Count > 0)
        {
            _clipboardEntries = [.. rows.Select(r => new HostsEntry(r))];
        }
    }

    private void OnCutClick(object sender, RoutedEventArgs e)
    {
        var rows = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (rows.Count > 0)
        {
            _clipboardEntries = [.. rows.Select(r => new HostsEntry(r))];
            _hostsFile.Entries.Remove(rows);
            foreach (var r in rows)
            {
                Entries.Remove(r);
            }
        }

        OnPropertyChanged(nameof(EntriesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesFilteredVisibility));

        _selectionService.UpdateSelectionDependentButtons();
        _selectionService.UpdateContextMenuItems();
    }

    private void OnPasteClick(object sender, RoutedEventArgs e)
    {
        if (_clipboardEntries != null && EntriesList.SelectedItems.Count > 0)
        {
            var current = (HostsEntry)EntriesList.SelectedItem;
            _hostsFile.Entries.Insert(current, _clipboardEntries);
            RefreshEntries();
            _clipboardEntries = null;
        }

        _selectionService.UpdateContextMenuItems();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        var rows = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (rows.Count > 0)
        {
            foreach (var entry in rows)
            {
                _hostsFile.Entries.InsertAfter(entry, new HostsEntry(entry));
            }
            RefreshEntries(true);
        }
        else if (EntriesList.SelectedItem is HostsEntry current)
        {
            _hostsFile.Entries.InsertAfter(current, new HostsEntry(current));
            RefreshEntries(true);
        }
    }

    private void OnRefreshAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        => TryInvokeUnlessTextBox(() => OnRefreshClick(this, new RoutedEventArgs()), args);

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmationAsync("Reload hosts file?", "You will lose any unsaved changes. Continue?");
        if (confirmed)
        {
            _hostsFile.Refresh();
            RefreshEntries();
        }
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        _hostsFile.RestoreDefault();
        RefreshEntries();
    }

    private void OnOpenInTextEditorClick(object sender, RoutedEventArgs e) => Utilities.FileOpener.OpenTextFile(HostsFile.DefaultHostFilePath);

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e) => RefreshEntries(true);

    private void OnFilterCommentsClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            // IsChecked == true => hide comments
            IsFilterCommentsHidden = t.IsChecked == true;
            RefreshEntries(true);
            OnPropertyChanged(nameof(IsFilterCommentsHidden));
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(ActiveFiltersBadgeVisibility));
        }
    }

    private void OnFilterDisabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem t)
        {
            // IsChecked == true => hide disabled entries
            IsFilterDisabledHidden = t.IsChecked == true;
            RefreshEntries(true);
            OnPropertyChanged(nameof(IsFilterDisabledHidden));
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(ActiveFiltersBadgeVisibility));
        }
    }

    private void OnResetFiltersClick(object sender, RoutedEventArgs e)
    {
        var changed = IsFilterCommentsHidden || IsFilterDisabledHidden;
        IsFilterCommentsHidden = false;
        IsFilterDisabledHidden = false;
        if (changed)
        {
            RefreshEntries(true);
            OnPropertyChanged(nameof(IsFilterCommentsHidden));
            OnPropertyChanged(nameof(IsFilterDisabledHidden));
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(ActiveFiltersBadgeVisibility));
        }
    }

    private void OnCheckClick(object sender, RoutedEventArgs e)
    {
        var rows = EntriesList.SelectedItems.Cast<HostsEntry>().ToList();
        if (rows.Count > 0)
        {
            // If all selected are enabled, then uncheck; otherwise check
            var allEnabled = rows.All(r => r.Enabled);
            _hostsFile.Entries.SetEnabled(rows, isEnabled: !allEnabled);
        }
        else if (EntriesList.SelectedItem is HostsEntry current)
        {
            // Toggle single selection
            _hostsFile.Entries.SetEnabled([current], isEnabled: !current.Enabled);
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => _undoManager.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => _undoManager.Redo();

    private void OnUndoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxFocused())
        {
            return;
        }

        _undoManager.Undo();
        args.Handled = true;
    }

    private void OnRedoAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxFocused())
        {
            return;
        }

        _undoManager.Redo();
        args.Handled = true;
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        var name = _dialogService is not null
            ? await _dialogService.ShowInputAsync(Content.XamlRoot, "Save Current as Profile", "Profile name", "OK", "Cancel")
            : null;

        if (!string.IsNullOrWhiteSpace(name))
        {
            _hostsFile.SaveAsProfile(name.Trim());
            RefreshArchives();
            ShowNotification($"Profile saved: {name.Trim()}", InfoBarSeverity.Success);
        }
    }

    private async void OnNewEmptyProfileClick(object sender, RoutedEventArgs e)
    {
        var name = _dialogService is not null
            ? await _dialogService.ShowInputAsync(Content.XamlRoot, "New Empty Profile", "Profile name", "OK", "Cancel")
            : null;

        if (string.IsNullOrWhiteSpace(name)) return;

        var profile = new HostsProfile(name.Trim());
        Directory.CreateDirectory(HostsProfileList.ProfileDirectory);
        File.WriteAllLines(profile.FilePath, HostsEntryList.DefaultLines);
        _profileList.Add(profile);

        RefreshArchives();
        ShowNotification($"Profile created: {name.Trim()}", InfoBarSeverity.Success);
    }

    private async void OnArchiveLoadClick(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is HostsProfile archive)
        {
            await _profileSwitcher.ActivateAsync(archive, ProfileSwitcher.TriggerSource.TrayMenu);
            RefreshEntries();
        }
    }

    private async void OnArchiveDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is not HostsProfile profile) return;
        if (profile.IsDefault) return;
        await DeleteProfileAsync(profile);
    }

    private async Task DeleteProfileAsync(HostsProfile profile)
    {
        if (Content?.XamlRoot is not { } root) return;

        bool isActive = _profileSwitcher.ActiveProfile == profile;
        var prompt = isActive
            ? $"Delete profile '{profile.FileName}'?\n\nIt's currently active — the hosts file will be reset to Default."
            : $"Delete profile '{profile.FileName}'?";

        var confirmed = await _dialogService.ShowConfirmationAsync(root, "Delete Profile", prompt, "Delete", "Cancel");
        if (!confirmed) return;

        if (isActive)
        {
            var defaultProfile = _profileList.FirstOrDefault(p => p.IsDefault);
            if (defaultProfile != null)
                await _profileSwitcher.ActivateAsync(defaultProfile, ProfileSwitcher.TriggerSource.TrayMenu);
        }

        _profileList.Delete(profile);
        RefreshArchives();
    }

    private async void OnArchiveSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is not HostsProfile profile) return;
        await ShowProfileSettingsAsync(profile);
    }

    private async Task ShowProfileSettingsAsync(HostsProfile profile)
    {
        if (Content?.XamlRoot is not { } root) return;

        var metadata = profile.Metadata;
        var result = await _dialogService.ShowProfileSettingsAsync(
            root, GetHwnd(), profile.FileName, metadata?.Description ?? string.Empty, metadata?.Color ?? string.Empty, metadata?.SortOrder ?? 0,
            metadata?.HotkeyModifiers ?? 0, metadata?.HotkeyKey ?? 0,
            metadata?.ConfigSourcePath ?? string.Empty, metadata?.ConfigDestinationPath ?? string.Empty);

        if (result is null) return;

        if (!_hotkeyRegistry.TryAssignHotkey(profile, result.HotkeyModifiers, result.HotkeyKey, out var error))
        {
            await _dialogService.ShowErrorAsync(root, "Hotkey Conflict", error ?? "Could not assign that hotkey.");
            return;
        }

        var meta = metadata ?? new HostsProfileMetadata();
        meta.Name = profile.FileName;
        meta.Description = result.Description;
        meta.Color = result.Color;
        meta.SortOrder = result.SortOrder;
        meta.HotkeyModifiers = result.HotkeyKey == 0 ? 0 : result.HotkeyModifiers;
        meta.HotkeyKey = result.HotkeyKey;
        meta.ConfigSourcePath = result.ConfigSourcePath;
        meta.ConfigDestinationPath = result.ConfigDestinationPath;
        meta.Save(profile.FilePath);
        profile.ReloadMetadata();

        RefreshArchives();
    }

    private async Task CloneProfileAsync(HostsProfile source)
    {
        if (Content?.XamlRoot is not { } root) return;

        var name = await _dialogService.ShowInputAsync(root, $"Clone — {source.FileName}", "New profile name", "OK", "Cancel");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var destProfile = new HostsProfile(name.Trim());
            if (File.Exists(destProfile.FilePath))
            {
                await _dialogService.ShowErrorAsync(root, "Clone", $"A profile named \"{destProfile.FileName}\" already exists.");
                return;
            }

            File.Copy(source.FilePath, destProfile.FilePath);
            _profileList.Add(destProfile);
            RefreshArchives();

            await _profileSwitcher.ActivateAsync(destProfile, ProfileSwitcher.TriggerSource.TrayMenu);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Clone Failed", ex.Message);
        }
    }

    private async Task RawEditProfileAsync(HostsProfile profile)
    {
        if (Content?.XamlRoot is not { } root) return;

        if (!File.Exists(profile.FilePath))
        {
            await _dialogService.ShowErrorAsync(root, "Raw Edit", $"Profile file not found:\n{profile.FilePath}");
            return;
        }

        string content;
        try { content = File.ReadAllText(profile.FilePath); }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Raw Edit", $"Could not read profile:\n{ex.Message}");
            return;
        }

        var newContent = await _dialogService.ShowRawEditAsync(root, $"Raw Edit — {profile.FileName}", content);
        if (newContent is null) return;

        try { File.WriteAllText(profile.FilePath, newContent); }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Raw Edit", $"Could not save:\n{ex.Message}");
        }
    }

    private async void OnViewArchiveClick(object sender, RoutedEventArgs e)
    {
        var show = sender is AppBarToggleButton { IsChecked: true };
        await ToggleArchiveVisibilityAsync(show);
    }

    private async void OnBackClick(object sender, RoutedEventArgs e) => await ToggleArchiveVisibilityAsync(false);

    // Optimized to update Entries in-place to minimize ListView flicker.
    // Pseudocode:
    // 1. Capture filter text (trim).
    // 2. If preserveSelection: capture selected items into HashSet.
    // 3. Build filtered target list (newList) applying comment/disabled filters + text filter.
    // 4. Fast path: if counts equal AND all items in same order (reference equality), skip collection mutation.
    // 5. Otherwise perform minimal diff:
    //    For i from 0 .. newList.Count-1:
    //      a. If i >= Entries.Count -> Entries.Add(newItem)
    //      b. Else if Entries[i] != newItem:
    //           i.   Try find newItem in Entries at index j > i. If found -> move (remove at j, insert at i).
    //           ii.  Else insert newItem at i.
    //    After loop, remove any trailing items in Entries (while Entries.Count > newList.Count).
    // 6. Restore selection if preserveSelection: clear current selection and re-add items present in snapshot.
    //    If not preserving selection, emulate old behavior (clearing destroyed selection) by clearing selection explicitly.
    // 7. Raise property changed notifications for empty / filtered visibility (only if potentially changed).
    // 8. Update selection-dependent buttons & context menu.
    private void RefreshEntries(bool preserveSelection = false)
    {
        // 1. Filter text
        var text = string.Empty;
        if (Content is FrameworkElement root &&
            root.FindName("FilterTextBox") is TextBox ftb &&
            ftb.Text is string s)
        {
            text = s.Trim();
        }

        // 2. Capture selection if needed
        HashSet<HostsEntry>? selectedSnapshot = null;
        if (preserveSelection && EntriesList.SelectedItems.Count > 0)
        {
            selectedSnapshot = EntriesList.SelectedItems.Cast<HostsEntry>().ToHashSet();
        }

        // 3. Build target filtered list
        var newList = new List<HostsEntry>();
        foreach (var e in _hostsFile.Entries)
        {
            if (IsFilterCommentsHidden && e.HasCommentOnly)
                continue;
            if (IsFilterDisabledHidden && !e.Enabled && !e.HasCommentOnly)
                continue;

            if (string.IsNullOrEmpty(text) || e.ToString().Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                newList.Add(e);
            }
        }

        // 4. Fast path: identical sequence -> only adjust selection/properties
        var same =
            Entries.Count == newList.Count &&
            Entries.Zip(newList, (a, b) => ReferenceEquals(a, b)).All(eq => eq);

        if (!same)
        {
            // 5. Minimal diff updates
            // Build index lookup for faster future searches if needed
            // (We rebuild on-the-fly because collection changes shift indices)
            for (int i = 0; i < newList.Count; i++)
            {
                var desired = newList[i];

                if (i >= Entries.Count)
                {
                    Entries.Add(desired);
                    continue;
                }

                if (!ReferenceEquals(Entries[i], desired))
                {
                    // Try to find desired later in the list to move it
                    var existingIndex = -1;
                    for (int j = i + 1; j < Entries.Count; j++)
                    {
                        if (ReferenceEquals(Entries[j], desired))
                        {
                            existingIndex = j;
                            break;
                        }
                    }

                    if (existingIndex >= 0)
                    {
                        if (existingIndex != i)
                        {
                            Entries.Move(existingIndex, i);
                        }
                    }
                    else
                    {
                        // Insert new item
                        Entries.Insert(i, desired);
                    }
                }
            }

            // Remove trailing excess
            while (Entries.Count > newList.Count)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        // 6. Restore / clear selection
        if (preserveSelection && selectedSnapshot is not null)
        {
            // Rebuild selection to reflect items still present
            var toSelect = Entries.Where(selectedSnapshot.Contains).ToList();

            // Avoid unnecessary churn if already matches
            bool selectionDiffers =
                EntriesList.SelectedItems.Count != toSelect.Count ||
                EntriesList.SelectedItems.Cast<HostsEntry>().Except(toSelect).Any();

            if (selectionDiffers)
            {
                EntriesList.SelectedItems.Clear();
                foreach (var item in toSelect)
                {
                    EntriesList.SelectedItems.Add(item);
                }
            }
        }
        else
        {
            // Match original behavior (clearing collection previously removed selection)
            EntriesList.SelectedItems.Clear();
        }

        // 7. Property notifications (possible count / filter changes)
        OnPropertyChanged(nameof(EntriesEmptyVisibility));
        OnPropertyChanged(nameof(EntriesFilteredVisibility));

        // 8. Update dependent UI
        _selectionService.UpdateSelectionDependentButtons();
        _selectionService.UpdateContextMenuItems();
    }

    private bool _profileSwitcherRebuildQueued;

    private void RefreshArchives()
    {
        Archives.Clear();
        foreach (var a in _profileList.OrderBy(p => p.Metadata?.SortOrder ?? 0).ThenBy(p => p.FileName))
        {
            Archives.Add(a);
        }
        OnPropertyChanged(nameof(ArchivesEmptyVisibility));
        QueueRefreshProfileSwitcher();
    }

    private void QueueRefreshProfileSwitcher()
    {
        if (_profileSwitcherRebuildQueued) return;
        _profileSwitcherRebuildQueued = true;
        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { _profileSwitcherRebuildQueued = false; RefreshProfileSwitcher(); });
    }

    // File, Edit, View, Profiles (index 3), Settings, Help
    private const int ProfilesMenuBarIndex = 3;

    private void RefreshProfileSwitcher()
    {
        // Populate the title bar DropDownButton flyout
        if (ProfileDropButton?.Flyout is MenuFlyout dropFlyout)
            PopulateProfileMenuItems(dropFlyout.Items);

        // Swap the MenuBar "Profiles" item (clear+repopulate on a MenuBarItem causes
        // WinUI to ghost old items over new ones after the flyout has been opened once;
        // replacing the whole node avoids that visual bug).
        if (MainMenuBar is not null)
        {
            var newItem = new MenuBarItem { Title = "Profiles" };
            PopulateProfileMenuItems(newItem.Items);
            if (MainMenuBar.Items.Count > ProfilesMenuBarIndex)
                MainMenuBar.Items.RemoveAt(ProfilesMenuBarIndex);
            MainMenuBar.Items.Insert(ProfilesMenuBarIndex, newItem);
        }

        OnPropertyChanged(nameof(StatusProfileLabel));
        OnPropertyChanged(nameof(ProfileDotBrush));
    }

    private void PopulateProfileMenuItems(IList<MenuFlyoutItemBase> items)
    {
        items.Clear();

        bool hostsDisabled = _profileSwitcher.IsHostsDisabled;

        var saveCurrentItem = new MenuFlyoutItem { Text = "Save Current as Profile…", IsEnabled = !hostsDisabled };
        saveCurrentItem.Click += OnArchiveClick;
        items.Add(saveCurrentItem);

        var newEmptyItem = new MenuFlyoutItem { Text = "New Empty Profile…", IsEnabled = !hostsDisabled };
        newEmptyItem.Click += OnNewEmptyProfileClick;
        items.Add(newEmptyItem);

        var exportItem = new MenuFlyoutItem { Text = "Export Profiles…" };
        exportItem.Click += OnExportProfilesClick;
        items.Add(exportItem);

        var importItem = new MenuFlyoutItem { Text = "Import Profiles…" };
        importItem.Click += OnImportProfilesClick;
        items.Add(importItem);

        items.Add(new MenuFlyoutSeparator());

        var defaultProfile = Archives.FirstOrDefault(p => p.IsDefault);
        bool isDefaultActive = defaultProfile != null && _profileSwitcher.ActiveProfile == defaultProfile && !hostsDisabled;
        if (defaultProfile != null)
        {
            items.Add(BuildProfileMenuItem(defaultProfile, isDefaultActive, includeDelete: false));
            items.Add(new MenuFlyoutSeparator());
        }

        foreach (var profile in Archives.Where(p => !p.IsDefault))
        {
            bool isActive = _profileSwitcher.ActiveProfile == profile && !hostsDisabled;
            items.Add(BuildProfileMenuItem(profile, isActive, includeDelete: true));
        }

        items.Add(new MenuFlyoutSeparator());
        var disableItem = new ToggleMenuFlyoutItem { Text = "Hosts File Disabled", IsChecked = hostsDisabled };
        disableItem.Click += OnHostsDisabledToggleClick;
        items.Add(disableItem);
    }

    private MenuFlyoutSubItem BuildProfileMenuItem(HostsProfile profile, bool isActive, bool includeDelete)
    {
        var meta = profile.Metadata;
        var label = profile.IsDefault ? "Default" : profile.FileName;
        if (meta?.HasHotkey == true)
            label += $"  ({FormatHotkeyChord(meta.HotkeyModifiers, meta.HotkeyKey)})";
        if (isActive)
            label += "  ✓";

        var sub = new MenuFlyoutSubItem { Text = label };

        // Show a colored swatch icon when the profile has a color assigned
        var profileColor = Services.DialogService.ParseHexColor(meta?.Color);
        if (profileColor is { } pc)
        {
            sub.Icon = new PathIcon
            {
                Data = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 12, 12) },
                Foreground = new SolidColorBrush(pc)
            };
        }

        bool profileExists = File.Exists(profile.FilePath);

        var activateItem = new MenuFlyoutItem { Text = isActive ? "Reload" : "Activate", IsEnabled = profileExists };
        activateItem.Click += async (_, _) => await _profileSwitcher.ActivateAsync(profile, ProfileSwitcher.TriggerSource.TrayMenu);
        sub.Items.Add(activateItem);

        // Always shown (matches WinForm), but disabled for the currently active profile
        // since there is nothing to "temporarily switch to" when you're already on it.
        var timerItem = new MenuFlyoutItem { Text = "Activate Temporarily…", IsEnabled = profileExists && !isActive };
        timerItem.Click += async (_, _) => await ActivateWithTimerAsync(profile);
        sub.Items.Add(timerItem);

        sub.Items.Add(new MenuFlyoutSeparator());

        var cloneItem = new MenuFlyoutItem { Text = "Clone…" };
        cloneItem.Click += async (_, _) => await CloneProfileAsync(profile);
        sub.Items.Add(cloneItem);

        var rawEditItem = new MenuFlyoutItem { Text = "Raw Edit…" };
        rawEditItem.Click += async (_, _) => await RawEditProfileAsync(profile);
        sub.Items.Add(rawEditItem);

        var settingsItem = new MenuFlyoutItem { Text = "Profile Settings…" };
        settingsItem.Click += async (_, _) => await ShowProfileSettingsAsync(profile);
        sub.Items.Add(settingsItem);

        if (includeDelete)
        {
            sub.Items.Add(new MenuFlyoutSeparator());
            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += async (_, _) => await DeleteProfileAsync(profile);
            sub.Items.Add(deleteItem);
        }

        return sub;
    }

    private static string FormatHotkeyChord(int modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(((VirtualKey)key).ToString());
        return string.Join("+", parts);
    }

    private async void OnHostsDisabledToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem { IsChecked: true })
        {
            // Unchecked — fall back to Default, mirroring WinForm's behavior
            var defaultProfile = Archives.FirstOrDefault(p => p.IsDefault);
            if (defaultProfile != null)
                await _profileSwitcher.ActivateAsync(defaultProfile, ProfileSwitcher.TriggerSource.TrayMenu);
            return;
        }

        _profileSwitcher.DisableAll();
        _auditLogger.Log(AuditActionType.HostsFileDisabled, AuditSource.MainForm);
    }

    private async void OnExportProfilesClick(object sender, RoutedEventArgs e)
    {
        if (Content?.XamlRoot is not { } root) return;

        var result = await _dialogService.ShowExportProfilesAsync(root, _profileList);
        if (result is null || result.SelectedProfiles.Count == 0) return;

        var zipPath = Win32FileDialogs.SaveFileDialog(GetHwnd(), "profiles-export", "Profile Package (*.zip)|*.zip");
        if (string.IsNullOrWhiteSpace(zipPath)) return;

        try
        {
            var bundleConfigFor = result.BundleConfigFiles
                ? (IReadOnlySet<string>)result.SelectedProfiles
                    .Where(p => p.Metadata?.HasConfigFile == true)
                    .Select(p => p.FileName)
                    .ToHashSet()
                : new HashSet<string>();

            _exportImportService.Export(zipPath, result.SelectedProfiles, bundleConfigFor);
            await _dialogService.ShowErrorAsync(root, "Export Complete",
                $"Exported {result.SelectedProfiles.Count} profile(s) to:\n{zipPath}");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Export Failed", ex.Message);
        }
    }

    private async void OnImportProfilesClick(object sender, RoutedEventArgs e)
    {
        if (Content?.XamlRoot is not { } root) return;

        var zipPath = Win32FileDialogs.OpenFileDialog(GetHwnd(), "Profile Package (*.zip)|*.zip|All Files (*.*)|*.*");
        if (string.IsNullOrWhiteSpace(zipPath)) return;

        ProfileExportManifest manifest;
        try
        {
            manifest = _exportImportService.ReadManifest(zipPath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Import Failed", $"Could not read package:\n{ex.Message}");
            return;
        }

        var fileNamesToImport = await _dialogService.ShowImportProfilesAsync(root, manifest);
        if (fileNamesToImport is null || fileNamesToImport.Count == 0) return;

        List<string> notes;
        try
        {
            notes = _exportImportService.Import(zipPath, fileNamesToImport);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(root, "Import Failed", ex.Message);
            return;
        }

        RefreshArchives();

        if (notes.Count > 0)
        {
            var successMsg = $"Imported {fileNamesToImport.Count} profile(s).\n\n" + string.Join("\n\n", notes);
            await _dialogService.ShowErrorAsync(root, "Import Complete", successMsg);
        }
        else
        {
            ShowNotification($"Imported {fileNamesToImport.Count} profile(s).", InfoBarSeverity.Success);
        }
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // Underline focus visuals for filter text box
    private void OnFilterBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (Content is FrameworkElement root && root.FindName("FilterUnderline") is Border underline)
        {
            underline.Background = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(Colors.DodgerBlue);
        }
    }

    private void OnFilterBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (Content is FrameworkElement root && root.FindName("FilterUnderline") is Border underline)
        {
            underline.Background = new SolidColorBrush(Colors.White);
        }
    }

    private void OnPingIPsClick(object sender, RoutedEventArgs e) =>
        ApplyToggleSetting("AutoPingIPs", v => { IsPingIPs = v; HostsEntry.AutoPingIPAddress = v; }, nameof(IsPingIPs), sender);

    private void OnRemoveDefaultTextClick(object sender, RoutedEventArgs e) =>
        ApplyToggleSetting("RemoveDefaultText", v => { IsRemoveDefaultText = v; HostsFile.RemoveDefaultText = v; }, nameof(IsRemoveDefaultText), sender);

    private void OnDiffBeforeSwitchClick(object sender, RoutedEventArgs e) =>
        ApplyToggleSetting("DiffBeforeSwitchEnabled", v => IsDiffBeforeSwitchEnabled = v, nameof(IsDiffBeforeSwitchEnabled), sender);

    private void OnUndoHistoryChanged(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(() => _selectionService.UpdateContextMenuItems());

    private void OnEntriesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectionService.UpdateSelectionDependentButtons();
        _selectionService.UpdateContextMenuItems();
    }

    private void OnEntriesListGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject child) return;
        var item = FindListViewItemAncestor(child);
        if (item is null) return;

        var idx = EntriesList.IndexFromContainer(item);
        if (idx < 0) return;

        // Select the row if it isn't already part of the current selection,
        // so toolbar buttons (Move Up/Down, Delete…) activate immediately on focus.
        if (!EntriesList.SelectedItems.Contains(EntriesList.Items[idx]))
            EntriesList.SelectedIndex = idx;
    }

    private static ListViewItem? FindListViewItemAncestor(DependencyObject obj)
    {
        var current = VisualTreeHelper.GetParent(obj);
        while (current is not null)
        {
            if (current is ListViewItem lvi) return lvi;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArchiveList is not null)
        {
            var selected = ArchiveList.SelectedItem as HostsProfile;
            bool isActive = selected != null && _profileSwitcher.ActiveProfile == selected && !_profileSwitcher.IsHostsDisabled;
            if (ArchiveLoadButton is not null) ArchiveLoadButton.IsEnabled = selected is not null;
            if (ArchiveTimerButton is not null) ArchiveTimerButton.IsEnabled = selected is not null && !isActive;
            if (ArchiveCloneButton is not null) ArchiveCloneButton.IsEnabled = selected is not null;
            if (ArchiveDeleteButton is not null) ArchiveDeleteButton.IsEnabled = selected is { IsDefault: false };
            if (ArchiveSettingsButton is not null) ArchiveSettingsButton.IsEnabled = selected is not null;
        }
    }

    private async void OnArchiveCloneClick(object sender, RoutedEventArgs e)
    {
        if (ArchiveList.SelectedItem is not HostsProfile profile) return;
        await CloneProfileAsync(profile);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
        => Application.Current.Exit();

    private async void OnAuditLogClick(object sender, RoutedEventArgs e)
    {
        if (Content?.XamlRoot is not { } root) return;
        var entries = _auditLogger.ReadEntries().ToList();
        if (entries.Count == 0)
        {
            await _dialogService.ShowErrorAsync(root, "Audit Log", "No audit log entries found.");
            return;
        }

        var text = string.Join(Environment.NewLine,
            entries.Select(entry =>
                $"[{entry.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}]  {entry.Action,-30}  {entry.Source}"));
        await _dialogService.ShowRawEditAsync(root, "Audit Log", text);
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        if (Content?.XamlRoot is not { } root) return;
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
        await _dialogService.ShowErrorAsync(root, "About Hosts File Editor",
            $"Hosts File Editor  v{version}\n\nEdit and manage your Windows hosts file with profiles, rollback timers, and audit logging.");
    }

    private void ShowNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (NotificationBar is null) return;
        NotificationBar.Message = message;
        NotificationBar.Severity = severity;
        NotificationBar.IsOpen = true;

        _notificationTimer?.Stop();
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _notificationTimer.Tick += (_, _) =>
        {
            NotificationBar.IsOpen = false;
            _notificationTimer?.Stop();
        };
        _notificationTimer.Start();
    }
}
