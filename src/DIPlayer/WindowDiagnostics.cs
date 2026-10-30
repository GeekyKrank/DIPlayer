namespace DIPlayer;
public partial class MainWindow
{
    void LogBrowserStartupFailure(string stage,Exception error)
    {
        try
        {
            string directory=DiagnosticLibraryDirectory()??Storage.LibraryStore.DataDirectory;
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"browser-startup.log");
            if(File.Exists(path)&&new FileInfo(path).Length>32768)File.Move(path,path+".old",true);
            File.AppendAllText(path,DateTimeOffset.UtcNow.ToString("O")+" "+stage+" "+error.GetType().Name+" HResult="+error.HResult+Environment.NewLine+System.Text.RegularExpressions.Regex.Replace(error.Message,@"https?://\S+","[URL]")+Environment.NewLine+error.StackTrace+Environment.NewLine);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void LogWindowTransition(string reason)
    {
        try
        {
            string directory=DiagnosticLibraryDirectory()??Storage.LibraryStore.DataDirectory;
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"window-events.log");
            if(File.Exists(path)&&new FileInfo(path).Length>32768)File.Move(path,path+".old",true);
            File.AppendAllText(path,DateTimeOffset.UtcNow.ToString("O")+" "+reason+" state="+WindowState+" visible="+IsVisible+Environment.NewLine);
        }
        catch(IOException){} catch(UnauthorizedAccessException){}
    }
}
