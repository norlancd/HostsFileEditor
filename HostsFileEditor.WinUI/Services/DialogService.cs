using HostsFileEditor;
using HostsFileEditor.Win32;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using System.IO;

namespace HostsFileEditor.Services;

public sealed record ExportProfilesResult(List<HostsProfile> SelectedProfiles, bool BundleConfigFiles);

public sealed record ProfileSettingsResult(
    string Description, string Color, int SortOrder, int HotkeyModifiers, int HotkeyKey,
    string ConfigSourcePath, string ConfigDestinationPath);

public enum RollbackExpiryChoice { Revert, Snooze, Keep }

public sealed record RollbackExpiryResult(RollbackExpiryChoice Choice, bool WasAutoReverted);

public class DialogService
{
    public async Task ShowErrorAsync(XamlRoot xamlRoot, string title, string message)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };

        await dlg.ShowAsync();
    }

    public async Task<bool> ShowConfirmationAsync(XamlRoot xamlRoot, string title, string message, string primaryText = "Yes", string closeText = "No")
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task<string?> ShowInputAsync(XamlRoot xamlRoot, string title, string placeholder, string okText = "OK", string cancelText = "Cancel")
    {
        var input = new TextBox { PlaceholderText = placeholder };
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = input,
            PrimaryButtonText = okText,
            CloseButtonText = cancelText
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return input.Text;
        }

        return null;
    }

    public async Task<ProfileSettingsResult?> ShowProfileSettingsAsync(
        XamlRoot xamlRoot, IntPtr hwnd, string profileFileName, string description, string color, int sortOrder, int hotkeyModifiers, int hotkeyKey,
        string configSourcePath, string configDestinationPath)
    {
        var txtDescription = new TextBox { Header = "Description", Text = description };
        var txtColor = new TextBox { Header = "Color (hex, e.g. #3399FF — leave blank for none)", Text = color, PlaceholderText = "#RRGGBB" };
        var numSortOrder = new NumberBox
        {
            Header = "Sort order (lower shows first)",
            Minimum = 0,
            Maximum = 999,
            Value = sortOrder,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        int capturedModifiers = hotkeyModifiers;
        int capturedKey = hotkeyKey;

        var txtHotkey = new TextBox
        {
            Header = "Hotkey (click here, then press keys — Backspace to clear)",
            IsReadOnly = true,
            Text = FormatHotkey(capturedModifiers, capturedKey)
        };
        txtHotkey.KeyDown += (_, e) =>
        {
            e.Handled = true;

            if (e.Key == VirtualKey.Back)
            {
                capturedModifiers = 0;
                capturedKey = 0;
                txtHotkey.Text = FormatHotkey(0, 0);
                return;
            }

            // Ignore modifier-only keystrokes
            if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows)
                return;

            int mods = 0;
            if (IsKeyDown(VirtualKey.Control)) mods |= 2;
            if (IsKeyDown(VirtualKey.Shift)) mods |= 4;
            if (IsKeyDown(VirtualKey.Menu)) mods |= 1;

            capturedModifiers = mods;
            capturedKey = (int)e.Key;
            txtHotkey.Text = FormatHotkey(capturedModifiers, capturedKey);
        };

        // Optional — copies a config file (VPN/SSH/kubeconfig, etc.) needed to talk to this
        // profile's servers into place automatically when it activates. Leave either blank
        // to do nothing, which is what most profiles will want.
        var (configSourceField, txtConfigSource) = BuildPathField("Config source file (optional)", configSourcePath,
            () => Win32FileDialogs.OpenFileDialog(hwnd, "All Files (*.*)|*.*"));
        var (configDestinationField, txtConfigDestination) = BuildPathField("Overwrite this file (optional)", configDestinationPath,
            () => Win32FileDialogs.SaveFileDialog(hwnd, configDestinationPath, "All Files (*.*)|*.*"));

        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(txtDescription);
        panel.Children.Add(txtColor);
        panel.Children.Add(numSortOrder);
        panel.Children.Add(txtHotkey);
        panel.Children.Add(configSourceField);
        panel.Children.Add(configDestinationField);

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Profile Settings — {profileFileName}",
            // Six fields can exceed the dialog's default height and get clipped without
            // this — the two config-file fields were silently unreachable without it.
            Content = new ScrollViewer { Content = panel, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        return new ProfileSettingsResult(
            txtDescription.Text,
            txtColor.Text.Trim(),
            double.IsNaN(numSortOrder.Value) ? 0 : (int)numSortOrder.Value,
            capturedModifiers,
            capturedKey,
            txtConfigSource.Text.Trim(),
            txtConfigDestination.Text.Trim());
    }

    private static (FrameworkElement Field, TextBox Input) BuildPathField(string header, string initialValue, Func<string?> browse)
    {
        var textBox = new TextBox { Text = initialValue };
        var browseButton = new Button { Content = "…" };
        browseButton.Click += (_, _) =>
        {
            var path = browse();
            if (path != null) textBox.Text = path;
        };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textBox, 0);
        Grid.SetColumn(browseButton, 1);
        row.Children.Add(textBox);
        row.Children.Add(browseButton);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = header, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(row);

        return (stack, textBox);
    }

    public async Task<TimeSpan?> ShowTimerDurationAsync(XamlRoot xamlRoot)
    {
        TimeSpan? selected = null;

        var presetsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var presets = new (string Label, int Minutes)[] { ("15 min", 15), ("30 min", 30), ("1 hour", 60), ("2 hours", 120) };

        ContentDialog dlg = new()
        {
            XamlRoot = xamlRoot,
            Title = "Activate with Rollback Timer",
            PrimaryButtonText = "Start",
            CloseButtonText = "Cancel"
        };

        foreach (var (label, minutes) in presets)
        {
            var btn = new Button { Content = label, Tag = minutes };
            btn.Click += (_, _) =>
            {
                selected = TimeSpan.FromMinutes((int)btn.Tag);
                dlg.Hide();
            };
            presetsPanel.Children.Add(btn);
        }

        var customMinutes = new NumberBox
        {
            Header = "Custom (minutes)",
            Minimum = 1,
            Maximum = 1440,
            Value = 30,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Revert automatically after:" });
        panel.Children.Add(presetsPanel);
        panel.Children.Add(customMinutes);
        dlg.Content = panel;

        var result = await dlg.ShowAsync();
        if (selected != null) return selected;

        if (result == ContentDialogResult.Primary && !double.IsNaN(customMinutes.Value))
        {
            return TimeSpan.FromMinutes(customMinutes.Value);
        }

        return null;
    }

    public async Task<RollbackExpiryResult> ShowRollbackExpiryAsync(XamlRoot xamlRoot, string profileName, DateTime activatedAtUtc, bool externallyModified)
    {
        var activatedAgo = DateTime.UtcNow - activatedAtUtc;
        var agoText = activatedAgo.TotalMinutes < 90
            ? $"{(int)activatedAgo.TotalMinutes} minutes"
            : $"{activatedAgo.TotalHours:F1} hours";

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Profile \"{profileName}\" was activated {agoText} ago.\nRevert to previous configuration?",
            TextWrapping = TextWrapping.Wrap
        });

        if (externallyModified)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "⚠ The hosts file was modified externally after this profile was activated.\nReverting may overwrite those changes.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 140, 0)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var progressBar = new ProgressBar { Minimum = 0, Maximum = 60, Value = 60 };
        var countdownText = new TextBlock { Text = "Auto-reverts in 60s" };
        panel.Children.Add(progressBar);
        panel.Children.Add(countdownText);

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "⏱ Timed profile expiring",
            Content = panel,
            PrimaryButtonText = "Revert Now",
            SecondaryButtonText = "Keep 30 min more",
            CloseButtonText = "Keep Permanently",
            DefaultButton = ContentDialogButton.Primary
        };

        bool autoReverted = false;
        int secondsLeft = 60;
        var countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdownTimer.Tick += (_, _) =>
        {
            secondsLeft--;
            progressBar.Value = Math.Max(0, secondsLeft);
            countdownText.Text = $"Auto-reverts in {secondsLeft}s";
            if (secondsLeft <= 0)
            {
                countdownTimer.Stop();
                autoReverted = true;
                dlg.Hide();
            }
        };
        countdownTimer.Start();

        var result = await dlg.ShowAsync();
        countdownTimer.Stop();

        if (autoReverted) return new RollbackExpiryResult(RollbackExpiryChoice.Revert, true);

        return result switch
        {
            ContentDialogResult.Primary => new RollbackExpiryResult(RollbackExpiryChoice.Revert, false),
            ContentDialogResult.Secondary => new RollbackExpiryResult(RollbackExpiryChoice.Snooze, false),
            _ => new RollbackExpiryResult(RollbackExpiryChoice.Keep, false)
        };
    }

    public async Task<bool> ShowProfileDiffAsync(XamlRoot xamlRoot, string profileName, ProfileDiffResult diff)
    {
        var modifiedBg = Color.FromArgb(255, 255, 248, 225);
        var modifiedFg = Color.FromArgb(255, 150, 100, 0);
        var publicRedirectBg = Color.FromArgb(255, 253, 226, 226);
        var publicRedirectFg = Color.FromArgb(255, 180, 30, 30);
        var addedBg = Color.FromArgb(255, 228, 246, 230);
        var addedFg = Color.FromArgb(255, 30, 120, 50);
        var removedBg = Color.FromArgb(255, 252, 232, 232);
        var removedFg = Color.FromArgb(255, 170, 40, 40);
        var toggledBg = Color.FromArgb(255, 228, 236, 250);
        var toggledFg = Color.FromArgb(255, 40, 80, 170);

        var rows = new StackPanel { Spacing = 1 };

        void AddRow(string type, string hostname, string oldIp, string newIp, string note, Color bg, Color fg)
        {
            var row = new Grid
            {
                Background = new SolidColorBrush(bg),
                Padding = new Thickness(8, 6, 8, 6),
                ColumnSpacing = 10
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var typeText = new TextBlock { Text = type, Foreground = new SolidColorBrush(fg), FontWeight = FontWeights.Bold };
            Grid.SetColumn(typeText, 0);

            var hostText = new TextBlock { Text = hostname, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(hostText, 1);

            var ipText = new TextBlock
            {
                Text = string.IsNullOrEmpty(oldIp) ? newIp : string.IsNullOrEmpty(newIp) ? oldIp : $"{oldIp} → {newIp}",
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(ipText, 2);

            row.Children.Add(typeText);
            row.Children.Add(hostText);
            row.Children.Add(ipText);
            rows.Children.Add(row);

            if (!string.IsNullOrEmpty(note))
            {
                rows.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(fg),
                    Margin = new Thickness(8, 0, 0, 2)
                });
            }
        }

        foreach (var p in diff.Modified)
        {
            var isPublicRedir = IsPublicHostname(p.Before.HostNames) && IsPrivateOrLoopback(p.After.IpAddress);
            var note = isPublicRedir ? "⚠ Public hostname redirected" : string.Empty;
            AddRow("Modified", p.Before.HostNames, p.Before.IpAddress, p.After.IpAddress, note,
                isPublicRedir ? publicRedirectBg : modifiedBg, isPublicRedir ? publicRedirectFg : modifiedFg);
        }

        foreach (var e in diff.Added)
            AddRow("Added", e.HostNames, string.Empty, e.IpAddress, string.Empty, addedBg, addedFg);

        foreach (var e in diff.Removed)
            AddRow("Removed", e.HostNames, e.IpAddress, string.Empty, string.Empty, removedBg, removedFg);

        foreach (var p in diff.Toggled)
        {
            var stateChange = p.After.Enabled ? "disabled → enabled" : "enabled → disabled";
            AddRow("Toggled", p.Before.HostNames, p.Before.IpAddress, p.Before.IpAddress, stateChange, toggledBg, toggledFg);
        }

        var changeText = diff.TotalChanges == 1
            ? "1 change to active DNS resolution"
            : $"{diff.TotalChanges} changes to active DNS resolution";

        var panel = new StackPanel { Spacing = 8, MinWidth = 480 };
        panel.Children.Add(new TextBlock { Text = changeText, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)) });
        panel.Children.Add(new ScrollViewer
        {
            Content = rows,
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Switching to profile: \"{profileName}\"",
            Content = panel,
            PrimaryButtonText = "Apply Changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static bool IsPublicHostname(string hostname)
    {
        var h = hostname.Trim().ToLowerInvariant();
        if (h == "localhost") return false;
        string[] localSuffixes = [".local", ".internal", ".test", ".dev", ".example",
                                   ".localhost", ".localdomain", ".lan", ".home", ".corp"];
        return !localSuffixes.Any(s => h.EndsWith(s));
    }

    private static bool IsPrivateOrLoopback(string ip)
    {
        if (ip.StartsWith("127.") || ip == "::1" || ip == "0.0.0.0") return true;
        if (ip.StartsWith("10.")) return true;
        if (ip.StartsWith("192.168.")) return true;
        if (ip.StartsWith("172.") &&
            int.TryParse(ip.Split('.').ElementAtOrDefault(1), out var b) &&
            b >= 16 && b <= 31) return true;
        return false;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static string FormatHotkey(int modifiers, int key)
    {
        if (key == 0) return "(none)";

        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(((VirtualKey)key).ToString());
        return string.Join(" + ", parts);
    }

    public async Task<ExportProfilesResult?> ShowExportProfilesAsync(XamlRoot xamlRoot, IEnumerable<HostsProfile> profiles)
    {
        var profileList = profiles.OrderBy(p => p.IsDefault ? 0 : 1).ThenBy(p => p.FileName).ToList();

        var checks = new List<(HostsProfile Profile, CheckBox Box)>();
        var profilesPanel = new StackPanel { Spacing = 6 };
        foreach (var p in profileList)
        {
            var cb = new CheckBox
            {
                Content = p.FileName,
                IsChecked = !p.IsDefault
            };
            checks.Add((p, cb));
            profilesPanel.Children.Add(cb);
        }

        var bundleConfigCheck = new CheckBox
        {
            Content = "Also include config file contents (may contain secrets — only for trusted transfers)",
            IsChecked = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var panel = new StackPanel { Spacing = 6, MinWidth = 320 };
        panel.Children.Add(new TextBlock { Text = "Profiles to export:" });
        panel.Children.Add(new ScrollViewer
        {
            Content = profilesPanel,
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        panel.Children.Add(bundleConfigCheck);

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Export Profiles",
            Content = panel,
            PrimaryButtonText = "Export…",
            CloseButtonText = "Cancel"
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        var selected = checks
            .Where(x => x.Box.IsChecked == true)
            .Select(x => x.Profile)
            .ToList();

        if (selected.Count == 0)
        {
            await ShowErrorAsync(xamlRoot, "Export Profiles", "Select at least one profile to export.");
            return null;
        }

        return new ExportProfilesResult(selected, bundleConfigCheck.IsChecked == true);
    }

    public async Task<List<string>?> ShowImportProfilesAsync(XamlRoot xamlRoot, ProfileExportManifest manifest)
    {
        var checks = new List<(string FileName, CheckBox Box)>();
        var profilesPanel = new StackPanel { Spacing = 6 };
        foreach (var entry in manifest.Profiles)
        {
            var note = entry.OriginalConfigSourcePath != null
                ? $"  (config: {Path.GetFileName(entry.OriginalConfigSourcePath)})"
                : string.Empty;
            var cb = new CheckBox { Content = entry.FileName + note, IsChecked = true };
            checks.Add((entry.FileName, cb));
            profilesPanel.Children.Add(cb);
        }

        var infoText = $"From: {manifest.MachineName}   Exported: {manifest.ExportedAtUtc.ToLocalTime():g}";

        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = infoText,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Profiles to import (renamed on collision, not activated automatically):",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new ScrollViewer
        {
            Content = profilesPanel,
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Import Profiles",
            Content = panel,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel"
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        return checks
            .Where(x => x.Box.IsChecked == true)
            .Select(x => x.FileName)
            .ToList();
    }
}
