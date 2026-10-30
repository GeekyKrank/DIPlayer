using System.Diagnostics;
using System.Windows;

namespace DIPlayer;

public partial class MainWindow
{
    void OpenIconCredits(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://www.flaticon.com/uicons") { UseShellExecute = true });
}
