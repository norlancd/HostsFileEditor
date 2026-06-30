using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HostsFileEditor.Win32;

public static partial class NativeMethods
{
    public const int HwndBroadcast = 0xffff;

    [LibraryImport("user32", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

    public static int RegisterWindowMessage(string format, params object[] args)
    {
        var message = string.Format(format, args);
        return RegisterWindowMessage(message);
    }

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static partial uint DnsFlushResolverCache();

    public static void FlushDns()
    {
        var result = DnsFlushResolverCache();
        Debug.WriteLine($"DnsFlushResolverCache: {result}");
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Matches the native window-procedure callback signature — used when subclassing
    /// a window (e.g. WinUI's <c>HotkeyMessageHook</c>) to intercept messages WinUI has
    /// no managed hook for, such as WM_HOTKEY.
    /// </summary>
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
