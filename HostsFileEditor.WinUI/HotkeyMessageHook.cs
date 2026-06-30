using HostsFileEditor.Win32;
using System.Runtime.InteropServices;

namespace HostsFileEditor;

/// <summary>
/// Subclasses a WinUI window's native message loop to catch WM_HOTKEY — unlike
/// WinForms' <c>Form.WndProc</c>, WinUI has no managed hook for this, so the window
/// procedure pointer has to be swapped out directly via SetWindowLongPtr.
/// </summary>
internal sealed class HotkeyMessageHook : IDisposable
{
    private const int GwlWndProc = -4;
    private const uint WmHotkey = 0x0312;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _originalWndProc;
    private readonly NativeMethods.WndProc _newWndProc; // field, not local — GC would otherwise collect the delegate
    private readonly Action<int> _onHotkeyPressed;
    private bool _disposed;

    public HotkeyMessageHook(IntPtr hwnd, Action<int> onHotkeyPressed)
    {
        _hwnd = hwnd;
        _onHotkeyPressed = onHotkeyPressed;
        _newWndProc = WndProc;
        _originalWndProc = NativeMethods.SetWindowLongPtr(hwnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_newWndProc));
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            _onHotkeyPressed(wParam.ToInt32());
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_originalWndProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(_hwnd, GwlWndProc, _originalWndProc);
        }
    }
}
