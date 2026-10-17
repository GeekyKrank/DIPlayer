using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using DIPlayer.Core;

namespace DIPlayer;

public partial class MainWindow
{
    string? updateUrl;
    async Task CheckForUpdates()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DIPlayer/" + typeof(MainWindow).Assembly.GetName().Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync("https://api.github.com/repos/GeekyKrank/DIPlayer/releases?per_page=20", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return;
            var bytes = await SoundCloudCatalog.ReadLimited(response.Content, 256 * 1024, timeout.Token);
            using var json = JsonDocument.Parse(bytes);
            string installed = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            if (!quitting) ShowUpdate(ReleaseUpdates.Find(json.RootElement, installed));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or CatalogException or IOException) { }
    }
    void ShowUpdate(UpdateRelease? release)
    {
        updateUrl = release?.Url;
        UpdateButton.Content = release == null ? "Update available" : "Update · " + release.Tag.TrimStart('v');
        UpdateButton.Visibility = release == null ? Visibility.Collapsed : Visibility.Visible;
    }
    void OpenUpdate(object sender, RoutedEventArgs e)
    {
        if (updateUrl != null) Process.Start(new ProcessStartInfo(updateUrl) { UseShellExecute = true });
    }
}
