using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DIPlayer.SystemIntegration;

static class WindowActivation
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    public static void Restore(Window window)
    {
        // Normalize before Show so IsVisibleChanged cannot hide it as still minimized.
        window.WindowState = WindowState.Normal;
        window.Show();
        IntPtr handle = new WindowInteropHelper(window).Handle;
        IntPtr previousForeground = GetForegroundWindow();
        Raise(window, handle);
        // One retry after WPF has processed showing the window. Do not compete with
        // a new window the user deliberately selected in the meantime.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (window.IsVisible && window.WindowState != WindowState.Minimized && GetForegroundWindow() == previousForeground)
                Raise(window, handle);
        }));
    }

    static void Raise(Window window, IntPtr handle)
    {
        const uint flags = 0x0001 | 0x0002 | 0x0010; // No size/move or implicit activation.
        if (!window.Topmost)
        {
            // Briefly promote in z-order, then immediately return to the normal
            // window group. The player is never left permanently always-on-top.
            SetWindowPos(handle, (IntPtr)(-1), 0, 0, 0, 0, flags);
            SetWindowPos(handle, (IntPtr)(-2), 0, 0, 0, 0, flags);
        }
        else
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, flags);
        SetForegroundWindow(handle);
        window.Activate();
        window.Focus();
    }
}
