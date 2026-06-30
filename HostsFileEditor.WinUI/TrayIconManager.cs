using HostsFileEditor.Win32;
using System.Runtime.InteropServices;

namespace HostsFileEditor;

/// <summary>
/// Adds a system tray icon via Shell_NotifyIcon and subclasses the window's WndProc
/// to intercept the tray callback messages.  Must be created before HotkeyMessageHook
/// so that the hotkey hook is the outermost layer of the chain.
/// </summary>
internal sealed class TrayIconManager : IDisposable
{
    private const int  GwlWndProc       = -4;
    private const uint WM_TRAY          = 0x0401; // WM_APP + 1
    private const uint TRAY_ID          = 1;
    private const uint WM_LBUTTONUP     = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP     = 0x0205;

    // Fixed command IDs for main menu items
    private const uint CMD_SHOW         = 1;
    private const uint CMD_EXIT         = 2;
    private const uint CMD_DISABLE      = 3;   // "Disable / Enable Hosts File"

    // Profile commands occupy IDs 100 … 100+N
    private const uint CMD_PROFILES_BASE = 100;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _originalWndProc;
    private readonly NativeMethods.WndProc _newWndProc; // kept alive — GC must not collect the delegate
    private readonly Action _onShow;
    private readonly Action _onExit;

    // Optional profile submenu support
    private readonly Func<(IReadOnlyList<TrayProfile> profiles, bool hostsDisabled)>? _getMenuData;
    private readonly Action<string?> _activateProfile; // null → activate default profile
    private readonly Action _disableHosts;             // called when toggling disabled ON

    private IntPtr _hIcon;
    private bool _disposed;

    // ── Profile data snapshot captured when menu opens ────────────────────────
    private IReadOnlyList<TrayProfile>? _lastProfiles;

    public TrayIconManager(
        IntPtr hwnd,
        Action onShow,
        Action onExit,
        Func<(IReadOnlyList<TrayProfile> profiles, bool hostsDisabled)>? getMenuData = null,
        Action<string?>? activateProfile = null,
        Action? disableHosts = null)
    {
        _hwnd            = hwnd;
        _onShow          = onShow;
        _onExit          = onExit;
        _getMenuData     = getMenuData;
        _activateProfile = activateProfile ?? (_ => { });
        _disableHosts    = disableHosts    ?? (() => { });

        _newWndProc      = WndProc;
        _originalWndProc = NativeMethods.SetWindowLongPtr(hwnd, GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_newWndProc));

        _hIcon = LoadAppIcon();
        AddTrayIcon();
    }

    private static unsafe IntPtr LoadAppIcon()
    {
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(exePath)) return IntPtr.Zero;

        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        NativeMethods.ExtractIconEx(exePath, 0, &large, &small, 1);
        if (large != IntPtr.Zero && small == IntPtr.Zero)
            return large;
        return small;
    }

    private void AddTrayIcon()
    {
        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize           = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd             = _hwnd,
            uID              = TRAY_ID,
            uFlags           = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon            = _hIcon,
            szTip            = "Hosts File Editor",
            szInfo           = string.Empty,
            szInfoTitle      = string.Empty,
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK)
                _onShow();
            else if (mouseMsg == WM_RBUTTONUP)
                ShowContextMenu();
            return IntPtr.Zero;
        }
        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var hMenu = NativeMethods.CreatePopupMenu();

        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, new IntPtr(CMD_SHOW), "Show");
        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, string.Empty);

        if (_getMenuData != null)
        {
            var (profiles, hostsDisabled) = _getMenuData();
            _lastProfiles = profiles;

            if (profiles.Count > 0)
            {
                var hProfiles = BuildProfilesSubmenu(profiles, hostsDisabled);
                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_POPUP, hProfiles, "Profiles");
                NativeMethods.AppendMenu(hMenu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, string.Empty);
            }
        }

        NativeMethods.AppendMenu(hMenu, NativeMethods.MF_STRING, new IntPtr(CMD_EXIT), "Exit");

        NativeMethods.GetCursorPos(out var pt);
        NativeMethods.SetForegroundWindow(_hwnd);
        var cmd = NativeMethods.TrackPopupMenu(
            hMenu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_RIGHTALIGN,
            pt.x, pt.y, 0, _hwnd, IntPtr.Zero);

        NativeMethods.DestroyMenu(hMenu); // also destroys child submenus

        DispatchCommand(cmd);
    }

    private IntPtr BuildProfilesSubmenu(IReadOnlyList<TrayProfile> profiles, bool hostsDisabled)
    {
        var hSub = NativeMethods.CreatePopupMenu();

        for (int i = 0; i < profiles.Count; i++)
        {
            var p    = profiles[i];
            var name = p.IsDefault ? "Default" : p.Name;
            bool active = p.IsActive && !hostsDisabled;
            uint flags = NativeMethods.MF_STRING | (active ? NativeMethods.MF_CHECKED : 0);
            NativeMethods.AppendMenu(hSub, flags, new IntPtr(CMD_PROFILES_BASE + i), name);
        }

        NativeMethods.AppendMenu(hSub, NativeMethods.MF_SEPARATOR, IntPtr.Zero, string.Empty);

        uint disableFlags = NativeMethods.MF_STRING | (hostsDisabled ? NativeMethods.MF_CHECKED : 0);
        NativeMethods.AppendMenu(hSub, disableFlags, new IntPtr(CMD_DISABLE),
            hostsDisabled ? "Hosts File Disabled" : "Disable Hosts File");

        return hSub;
    }

    private void DispatchCommand(uint cmd)
    {
        if (cmd == CMD_SHOW)
        {
            _onShow();
        }
        else if (cmd == CMD_EXIT)
        {
            _onExit();
        }
        else if (cmd == CMD_DISABLE)
        {
            _disableHosts();
        }
        else if (cmd >= CMD_PROFILES_BASE && _lastProfiles != null)
        {
            int idx = (int)(cmd - CMD_PROFILES_BASE);
            if (idx < _lastProfiles.Count)
            {
                var p = _lastProfiles[idx];
                // null signals "activate the default profile"
                _activateProfile(p.IsDefault ? null : p.Name);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize      = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd        = _hwnd,
            uID         = TRAY_ID,
            szTip       = string.Empty,
            szInfo      = string.Empty,
            szInfoTitle = string.Empty,
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);

        if (_originalWndProc != IntPtr.Zero)
            NativeMethods.SetWindowLongPtr(_hwnd, GwlWndProc, _originalWndProc);
    }
}

/// <summary>Profile snapshot passed to the tray context menu builder.</summary>
internal sealed record TrayProfile(string Name, bool IsActive, bool IsDefault);
