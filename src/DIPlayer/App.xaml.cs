using System.Windows;
using System.Runtime.InteropServices;
namespace DIPlayer;

public partial class App : System.Windows.Application
{
    Mutex? instance;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!e.Args.Contains("--selftest"))
        {
            instance = new Mutex(true, "Local\\DIPlayer", out bool created);
            if (!created)
            {
                var previous = FindWindow(null, "DIPlayer");
                if (previous != IntPtr.Zero)
                    PostMessage(previous, 0x802A, IntPtr.Zero, IntPtr.Zero);
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
                File.AppendAllText(log, args.Exception.GetType().FullName + "\n" + args.Exception.StackTrace + "\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!Environment.GetCommandLineArgs().Contains("--selftest"))
                System.Windows.MessageBox.Show("Не удалось выполнить действие. " + (args.Exception is DIPlayer.Core.CatalogException ? args.Exception.Message : "Перезапустите приложение; локальная библиотека сохранена."), "DIPlayer");
            args.Handled = true;
        };
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
