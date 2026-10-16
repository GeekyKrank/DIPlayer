using System.Windows;
using System.Runtime.InteropServices;
namespace DIPlayer;

public partial class App : System.Windows.Application
{
    Mutex? instance;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint process);
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!e.Args.Contains("--selftest"))
        {
            instance = new Mutex(true, "Local\\DIPlayer", out bool created);
            if (!created)
            {
                var previous = FindWindow(null, "DIPlayer");
                if (previous != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(previous, out uint process);
                    // The shortcut launch can transfer its foreground permission to
                    // the existing player before asking it to restore its window.
                    if (process != 0) AllowSetForegroundWindow(process);
                    PostMessage(previous, 0x802A, IntPtr.Zero, IntPtr.Zero);
                }
                Shutdown();
                return;
            }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                string dir = DIPlayer.Storage.LibraryStore.DataDirectory;
                Directory.CreateDirectory(dir);
                string log = Path.Combine(dir, "error.log");
                if (File.Exists(log) && new FileInfo(log).Length > 65536)
                    File.Move(log, log + ".old", true);
                var chain = new System.Text.StringBuilder();
                for (Exception? cause = args.Exception; cause != null; cause = cause.InnerException)
                {
                    chain.AppendLine(cause.GetType().FullName + " HResult=" + cause.HResult);
                    // XAML resource failures need their inner cause to be actionable.
                    // Restrict messages to this startup failure class; network messages
                    // can contain signed URLs and are deliberately excluded.
                    if (args.Exception is System.Windows.Markup.XamlParseException && cause is IOException)
                    {
                        string message = System.Text.RegularExpressions.Regex.Replace(cause.Message, @"https?://\S+", "[URL]");
                        chain.AppendLine(message);
                    }
                }
                File.AppendAllText(log, DateTimeOffset.UtcNow.ToString("O") + "\n" + chain + args.Exception.StackTrace + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!Environment.GetCommandLineArgs().Contains("--selftest"))
            {
                string message = "Could not complete the action. " + (args.Exception is DIPlayer.Core.CatalogException ? args.Exception.Message : "Your local library is saved. Please try again.");
                if (MainWindow is DIPlayer.MainWindow { IsLoaded: true } window && args.Exception is not System.Windows.Markup.XamlParseException)
                    window.ReportApplicationError(message);
                else System.Windows.MessageBox.Show(message, "DIPlayer");
            }
            args.Handled = true;
            // A failed main-window load cannot recover by staying alive without
            // a window: that would also block the next launch with the mutex.
            if (MainWindow == null || args.Exception is System.Windows.Markup.XamlParseException)
                Shutdown(1);
        };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
