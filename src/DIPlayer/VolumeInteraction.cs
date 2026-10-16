using System.Windows.Input;

namespace DIPlayer;

public partial class MainWindow
{
    bool volumeDragging;
    void UpdateVolumePointer(double x)
    {
        double fraction = Math.Clamp((x - 7) / Math.Max(1, VolumeSlider.ActualWidth - 14), 0, 1);
        VolumeSlider.Value = fraction;
        VolumeSlider.ToolTip = $"Volume: {fraction:P0}";
    }
    void VolumePointerDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        VolumeSlider.Focus();
        volumeDragging = VolumeSlider.CaptureMouse();
        UpdateVolumePointer(e.GetPosition(VolumeSlider).X);
    }
    void VolumePointerMove(object sender, MouseEventArgs e)
    {
        if (!volumeDragging || !VolumeSlider.IsMouseCaptured) return;
        e.Handled = true;
        UpdateVolumePointer(e.GetPosition(VolumeSlider).X);
    }
    void VolumePointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!volumeDragging) return;
        e.Handled = true;
        UpdateVolumePointer(e.GetPosition(VolumeSlider).X);
        volumeDragging = false;
        VolumeSlider.ReleaseMouseCapture();
    }
    void VolumeCaptureLost(object sender, MouseEventArgs e) => volumeDragging = false;
}
