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
using System.Globalization;
using System.IO;

namespace HostsFileEditor.Services;

public sealed record ExportProfilesResult(List<HostsProfile> SelectedProfiles, bool BundleConfigFiles);

public sealed record ProfileSettingsResult(
    string Description, string Color, int SortOrder, int HotkeyModifiers, int HotkeyKey,
    List<FileReplacement> FileReplacements, List<ProfileCommand> Commands);

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
        XamlRoot xamlRoot, IntPtr hwnd, string profileFileName, string description, string color, int sortOrder,
        int hotkeyModifiers, int hotkeyKey,
        List<FileReplacement> fileReplacements, List<ProfileCommand> commands)
    {
        var txtDescription = new TextBox { Header = "Description", Text = description };

        // Color picker: a swatch button that opens a flyout with a ColorPicker
        Color? pickedColor = ParseHexColor(color);

        var swatch = new Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80)),
            Background = pickedColor is { } c ? new SolidColorBrush(c) : null
        };

        var colorPicker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            Color = pickedColor ?? Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)
        };
        colorPicker.ColorChanged += (_, args) =>
        {
            pickedColor = args.NewColor;
            swatch.Background = new SolidColorBrush(args.NewColor);
        };

        var clearBtn = new HyperlinkButton { Content = "Clear color", Margin = new Thickness(0, 4, 0, 0) };
        var colorFlyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { colorPicker, clearBtn }
            }
        };
        clearBtn.Click += (_, _) =>
        {
            pickedColor = null;
            swatch.Background = null;
            colorFlyout.Hide();
        };

        var colorPickerBtn = new Button
        {
            Content = swatch,
            Flyout = colorFlyout,
            Padding = new Thickness(4)
        };
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        colorRow.Children.Add(colorPickerBtn);
        var colorRowOuter = new StackPanel { Spacing = 4 };
        colorRowOuter.Children.Add(new TextBlock { Text = "Color", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        colorRowOuter.Children.Add(colorRow);
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

        // Helper: section header row with title on left, add button on right
        Grid SectionHeader(string title, string addLabel, Action onAdd)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new TextBlock
            {
                Text = title,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            };
            var btn = new HyperlinkButton { Content = addLabel, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            btn.Click += (_, _) => onAdd();
            Grid.SetColumn(lbl, 0); Grid.SetColumn(btn, 1);
            g.Children.Add(lbl); g.Children.Add(btn);
            return g;
        }

        Border Divider() => new() { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(0, 4, 0, 0) };

        // ── File Replacements ────────────────────────────────────────────────
        var replacementRows = new List<(TextBox Src, TextBox Dst)>();
        var replacementsStack = new StackPanel { Spacing = 8 };

        void AddReplacementRow(string src = "", string dst = "")
        {
            var srcBox = new TextBox { PlaceholderText = "Source file…", Text = src };
            var srcBrowse = new Button { Content = "…", Width = 36, Padding = new Thickness(4, 2, 4, 2) };
            srcBrowse.Click += (_, _) => { var p = Win32FileDialogs.OpenFileDialog(hwnd, "All Files (*.*)|*.*"); if (p != null) srcBox.Text = p; };

            var dstBox = new TextBox { PlaceholderText = "Destination file…", Text = dst };
            var dstBrowse = new Button { Content = "…", Width = 36, Padding = new Thickness(4, 2, 4, 2) };
            dstBrowse.Click += (_, _) => { var p = Win32FileDialogs.SaveFileDialog(hwnd, dstBox.Text, "All Files (*.*)|*.*"); if (p != null) dstBox.Text = p; };

            var removeBtn = new Button { Content = "×", Width = 36, Padding = new Thickness(4, 2, 4, 2) };

            var entry = (srcBox, dstBox);
            replacementRows.Add(entry);

            // Src row: textbox + browse
            var srcRow = new Grid { ColumnSpacing = 4 };
            srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(srcBox, 0); Grid.SetColumn(srcBrowse, 1);
            srcRow.Children.Add(srcBox); srcRow.Children.Add(srcBrowse);

            // Dst row: textbox + browse + remove
            var dstRow = new Grid { ColumnSpacing = 4 };
            dstRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dstRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dstRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(dstBox, 0); Grid.SetColumn(dstBrowse, 1); Grid.SetColumn(removeBtn, 2);
            dstRow.Children.Add(dstBox); dstRow.Children.Add(dstBrowse); dstRow.Children.Add(removeBtn);

            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var inner = new StackPanel { Spacing = 6 };
            inner.Children.Add(new TextBlock { Text = "Source", FontSize = 11, Opacity = 0.6 });
            inner.Children.Add(srcRow);
            inner.Children.Add(new TextBlock { Text = "Destination", FontSize = 11, Opacity = 0.6 });
            inner.Children.Add(dstRow);
            card.Child = inner;

            removeBtn.Click += (_, _) => { replacementsStack.Children.Remove(card); replacementRows.Remove(entry); };
            replacementsStack.Children.Add(card);
        }

        foreach (var fr in fileReplacements)
            AddReplacementRow(fr.SourcePath, fr.DestinationPath);

        var replacementsSection = new StackPanel { Spacing = 6 };
        replacementsSection.Children.Add(SectionHeader("File Replacements", "+ Add", () => AddReplacementRow()));
        replacementsSection.Children.Add(replacementsStack);

        // ── Commands ─────────────────────────────────────────────────────────
        var commandRows = new List<(ComboBox Timing, TextBox Exe, TextBox Args, CheckBox Wait)>();
        var commandsStack = new StackPanel { Spacing = 8 };

        void AddCommandRow(ProfileCommandTiming timing = ProfileCommandTiming.After, string exe = "", string args = "", bool wait = true)
        {
            var timingBox = new ComboBox { MinWidth = 90 };
            timingBox.Items.Add("Before");
            timingBox.Items.Add("After");
            timingBox.SelectedIndex = timing == ProfileCommandTiming.Before ? 0 : 1;

            var exeBox = new TextBox { PlaceholderText = "Executable or script…", Text = exe };
            var exeBrowse = new Button { Content = "…", Width = 36, Padding = new Thickness(4, 2, 4, 2) };
            exeBrowse.Click += (_, _) => { var p = Win32FileDialogs.OpenFileDialog(hwnd, "All Files (*.*)|*.*"); if (p != null) exeBox.Text = p; };

            var argsBox = new TextBox { PlaceholderText = "Arguments (optional)", Text = args };
            var waitBox = new CheckBox { Content = "Wait for exit", IsChecked = wait };

            var removeBtn = new Button { Content = "×", Width = 36, Padding = new Thickness(4, 2, 4, 2) };

            var entry = (timingBox, exeBox, argsBox, waitBox);
            commandRows.Add(entry);

            // Top row: timing + exe + browse + remove
            var topRow = new Grid { ColumnSpacing = 4 };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(timingBox, 0); Grid.SetColumn(exeBox, 1); Grid.SetColumn(exeBrowse, 2); Grid.SetColumn(removeBtn, 3);
            topRow.Children.Add(timingBox); topRow.Children.Add(exeBox); topRow.Children.Add(exeBrowse); topRow.Children.Add(removeBtn);

            // Bottom row: args + wait
            var botRow = new Grid { ColumnSpacing = 8 };
            botRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            botRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(argsBox, 0); Grid.SetColumn(waitBox, 1);
            botRow.Children.Add(argsBox); botRow.Children.Add(waitBox);

            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var inner = new StackPanel { Spacing = 6 };
            inner.Children.Add(topRow);
            inner.Children.Add(new TextBlock { Text = "Arguments", FontSize = 11, Opacity = 0.6 });
            inner.Children.Add(botRow);
            card.Child = inner;

            removeBtn.Click += (_, _) => { commandsStack.Children.Remove(card); commandRows.Remove(entry); };
            commandsStack.Children.Add(card);
        }

        foreach (var cmd in commands)
            AddCommandRow(cmd.Timing, cmd.Executable, cmd.Arguments, cmd.WaitForExit);

        var commandsSection = new StackPanel { Spacing = 6 };
        commandsSection.Children.Add(SectionHeader("Commands", "+ Add", () => AddCommandRow()));
        commandsSection.Children.Add(commandsStack);

        // ── Dialog ────────────────────────────────────────────────────────────
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(txtDescription);
        panel.Children.Add(colorRowOuter);
        panel.Children.Add(numSortOrder);
        panel.Children.Add(txtHotkey);
        panel.Children.Add(Divider());
        panel.Children.Add(replacementsSection);
        panel.Children.Add(Divider());
        panel.Children.Add(commandsSection);

        // ContentDialog scrolls natively when content overflows — no inner ScrollViewer needed
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Profile Settings — {profileFileName}",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        var resultReplacements = replacementRows
            .Select(r => new FileReplacement { SourcePath = r.Src.Text.Trim(), DestinationPath = r.Dst.Text.Trim() })
            .Where(fr => fr.IsValid)
            .ToList();

        var resultCommands = commandRows
            .Select(r => new ProfileCommand
            {
                Timing = r.Timing.SelectedIndex == 0 ? ProfileCommandTiming.Before : ProfileCommandTiming.After,
                Executable = r.Exe.Text.Trim(),
                Arguments = r.Args.Text.Trim(),
                WaitForExit = r.Wait.IsChecked == true
            })
            .Where(c => c.IsValid)
            .ToList();

        return new ProfileSettingsResult(
            txtDescription.Text,
            pickedColor is { } pc ? $"#{pc.R:X2}{pc.G:X2}{pc.B:X2}" : string.Empty,
            double.IsNaN(numSortOrder.Value) ? 0 : (int)numSortOrder.Value,
            capturedModifiers,
            capturedKey,
            resultReplacements,
            resultCommands);
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

    public async Task<string?> ShowRawEditAsync(XamlRoot xamlRoot, string title, string content)
    {
        var textBox = new TextBox
        {
            Text = content,
            AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            IsSpellCheckEnabled = false,
            MinHeight = 320,
            MinWidth = 580
        };

        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new ScrollViewer
            {
                Content = textBox,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 480
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel"
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
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

    // WinForms ColorTranslator.ToHtml can produce named colors (e.g. "Red") for colors
    // picked from ColorDialog's basic palette. CSS color names → #RRGGBB mapping so we
    // can round-trip those values without depending on System.Drawing (not AOT-safe).
    private static readonly Dictionary<string, uint> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"]=0xF0F8FF,["antiquewhite"]=0xFAEBD7,["aqua"]=0x00FFFF,
        ["aquamarine"]=0x7FFFD4,["azure"]=0xF0FFFF,["beige"]=0xF5F5DC,
        ["bisque"]=0xFFE4C4,["black"]=0x000000,["blanchedalmond"]=0xFFEBCD,
        ["blue"]=0x0000FF,["blueviolet"]=0x8A2BE2,["brown"]=0xA52A2A,
        ["burlywood"]=0xDEB887,["cadetblue"]=0x5F9EA0,["chartreuse"]=0x7FFF00,
        ["chocolate"]=0xD2691E,["coral"]=0xFF7F50,["cornflowerblue"]=0x6495ED,
        ["cornsilk"]=0xFFF8DC,["crimson"]=0xDC143C,["cyan"]=0x00FFFF,
        ["darkblue"]=0x00008B,["darkcyan"]=0x008B8B,["darkgoldenrod"]=0xB8860B,
        ["darkgray"]=0xA9A9A9,["darkgreen"]=0x006400,["darkgrey"]=0xA9A9A9,
        ["darkkhaki"]=0xBDB76B,["darkmagenta"]=0x8B008B,["darkolivegreen"]=0x556B2F,
        ["darkorange"]=0xFF8C00,["darkorchid"]=0x9932CC,["darkred"]=0x8B0000,
        ["darksalmon"]=0xE9967A,["darkseagreen"]=0x8FBC8F,["darkslateblue"]=0x483D8B,
        ["darkslategray"]=0x2F4F4F,["darkslategrey"]=0x2F4F4F,["darkturquoise"]=0x00CED1,
        ["darkviolet"]=0x9400D3,["deeppink"]=0xFF1493,["deepskyblue"]=0x00BFFF,
        ["dimgray"]=0x696969,["dimgrey"]=0x696969,["dodgerblue"]=0x1E90FF,
        ["firebrick"]=0xB22222,["floralwhite"]=0xFFFAF0,["forestgreen"]=0x228B22,
        ["fuchsia"]=0xFF00FF,["gainsboro"]=0xDCDCDC,["ghostwhite"]=0xF8F8FF,
        ["gold"]=0xFFD700,["goldenrod"]=0xDAA520,["gray"]=0x808080,
        ["green"]=0x008000,["greenyellow"]=0xADFF2F,["grey"]=0x808080,
        ["honeydew"]=0xF0FFF0,["hotpink"]=0xFF69B4,["indianred"]=0xCD5C5C,
        ["indigo"]=0x4B0082,["ivory"]=0xFFFFF0,["khaki"]=0xF0E68C,
        ["lavender"]=0xE6E6FA,["lavenderblush"]=0xFFF0F5,["lawngreen"]=0x7CFC00,
        ["lemonchiffon"]=0xFFFACD,["lightblue"]=0xADD8E6,["lightcoral"]=0xF08080,
        ["lightcyan"]=0xE0FFFF,["lightgoldenrodyellow"]=0xFAFAD2,["lightgray"]=0xD3D3D3,
        ["lightgreen"]=0x90EE90,["lightgrey"]=0xD3D3D3,["lightpink"]=0xFFB6C1,
        ["lightsalmon"]=0xFFA07A,["lightseagreen"]=0x20B2AA,["lightskyblue"]=0x87CEFA,
        ["lightslategray"]=0x778899,["lightslategrey"]=0x778899,["lightsteelblue"]=0xB0C4DE,
        ["lightyellow"]=0xFFFFE0,["lime"]=0x00FF00,["limegreen"]=0x32CD32,
        ["linen"]=0xFAF0E6,["magenta"]=0xFF00FF,["maroon"]=0x800000,
        ["mediumaquamarine"]=0x66CDAA,["mediumblue"]=0x0000CD,["mediumorchid"]=0xBA55D3,
        ["mediumpurple"]=0x9370DB,["mediumseagreen"]=0x3CB371,["mediumslateblue"]=0x7B68EE,
        ["mediumspringgreen"]=0x00FA9A,["mediumturquoise"]=0x48D1CC,["mediumvioletred"]=0xC71585,
        ["midnightblue"]=0x191970,["mintcream"]=0xF5FFFA,["mistyrose"]=0xFFE4E1,
        ["moccasin"]=0xFFE4B5,["navajowhite"]=0xFFDEAD,["navy"]=0x000080,
        ["oldlace"]=0xFDF5E6,["olive"]=0x808000,["olivedrab"]=0x6B8E23,
        ["orange"]=0xFFA500,["orangered"]=0xFF4500,["orchid"]=0xDA70D6,
        ["palegoldenrod"]=0xEEE8AA,["palegreen"]=0x98FB98,["paleturquoise"]=0xAFEEEE,
        ["palevioletred"]=0xDB7093,["papayawhip"]=0xFFEFD5,["peachpuff"]=0xFFDAB9,
        ["peru"]=0xCD853F,["pink"]=0xFFC0CB,["plum"]=0xDDA0DD,
        ["powderblue"]=0xB0E0E6,["purple"]=0x800080,["red"]=0xFF0000,
        ["rosybrown"]=0xBC8F8F,["royalblue"]=0x4169E1,["saddlebrown"]=0x8B4513,
        ["salmon"]=0xFA8072,["sandybrown"]=0xF4A460,["seagreen"]=0x2E8B57,
        ["seashell"]=0xFFF5EE,["sienna"]=0xA0522D,["silver"]=0xC0C0C0,
        ["skyblue"]=0x87CEEB,["slateblue"]=0x6A5ACD,["slategray"]=0x708090,
        ["slategrey"]=0x708090,["snow"]=0xFFFAFA,["springgreen"]=0x00FF7F,
        ["steelblue"]=0x4682B4,["tan"]=0xD2B48C,["teal"]=0x008080,
        ["thistle"]=0xD8BFD8,["tomato"]=0xFF6347,["turquoise"]=0x40E0D0,
        ["violet"]=0xEE82EE,["wheat"]=0xF5DEB3,["white"]=0xFFFFFF,
        ["whitesmoke"]=0xF5F5F5,["yellow"]=0xFFFF00,["yellowgreen"]=0x9ACD32,
    };

    internal static Color? ParseHexColor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        // Named color (e.g. "Red" from WinForms ColorTranslator.ToHtml)
        if (!input.StartsWith('#') && _namedColors.TryGetValue(input, out uint rgb))
            return Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        // #RRGGBB hex
        var hex = input.TrimStart('#');
        if (hex.Length == 6
            && byte.TryParse(hex[0..2], NumberStyles.HexNumber, null, out byte r)
            && byte.TryParse(hex[2..4], NumberStyles.HexNumber, null, out byte g)
            && byte.TryParse(hex[4..6], NumberStyles.HexNumber, null, out byte b))
            return Color.FromArgb(0xFF, r, g, b);

        return null;
    }
}
