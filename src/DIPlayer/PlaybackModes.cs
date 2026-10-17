using System.Windows;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    void CycleRepeat(object sender, RoutedEventArgs e)
    {
        settings.RepeatMode = (settings.RepeatMode + 1) % 3;
        SyncPlaybackModes();
    }
    void ToggleShuffle(object sender, RoutedEventArgs e)
    {
        settings.Shuffle = !settings.Shuffle;
        queue.ResetNavigation();
        SyncPlaybackModes();
    }
    void SyncPlaybackModes()
    {
        settings.RepeatMode = Math.Clamp(settings.RepeatMode, 0, 2);
        RepeatButton.Content = settings.RepeatMode == 2 ? "\uE8ED" : "\uE8EE";
        RepeatButton.ToolTip = settings.RepeatMode switch { 1 => "Repeat playlist", 2 => "Repeat one track", _ => "Repeat off" };
        ShuffleButton.ToolTip = settings.Shuffle ? "Shuffle on" : "Shuffle off";
        RepeatButton.Foreground = settings.RepeatMode == 0 ? Brushes.Gray : Brushes.White;
        ShuffleButton.Foreground = settings.Shuffle ? Brushes.White : Brushes.Gray;
        RepeatButton.Background = settings.RepeatMode == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(45, 45, 45));
        ShuffleButton.Background = settings.Shuffle ? new SolidColorBrush(Color.FromRgb(45, 45, 45)) : Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(RepeatButton, (string)RepeatButton.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(ShuffleButton, (string)ShuffleButton.ToolTip);
    }
}
