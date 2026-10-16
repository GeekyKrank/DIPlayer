using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    const double FrameRadius = 24;
    (int Width, int Height, int Diameter, bool Maximized)? lastWindowShape;
    [StructLayout(LayoutKind.Sequential)]
    struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    void UpdateWindowCorners()
    {
        if (WindowFrame == null || WindowState == WindowState.Minimized)
            return;
        bool maximized = WindowState == WindowState.Maximized;
        double radius = maximized ? 0 : FrameRadius;
        WindowFrame.CornerRadius = new CornerRadius(radius);
        if (WindowFrame.ActualWidth > 0 && WindowFrame.ActualHeight > 0)
        {
            var clip = new RectangleGeometry(new Rect(0, 0, WindowFrame.ActualWidth, WindowFrame.ActualHeight), radius, radius);
            clip.Freeze();
            WindowFrame.Clip = maximized ? null : clip;
        }
        // An opaque native window region keeps the normal WPF rendering path.
        // Update only when size, DPI or maximize state changes; no timer is involved.
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var bounds))
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int diameter = (int)Math.Round(FrameRadius * 2 * dpi.DpiScaleX);
        var shape = (bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, diameter, maximized);
        if (lastWindowShape == shape)
            return;
        lastWindowShape = shape;
        if (maximized)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }
        var region = CreateRoundRectRgn(0, 0, shape.Item1 + 1, shape.Item2 + 1, diameter, diameter);
        // On success Windows owns the region. Only release it ourselves on failure.
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
            lastWindowShape = null;
        }
    }
}
