using System.Text.Json;
using Microsoft.Web.WebView2.Core;
namespace DIPlayer;

public partial class MainWindow
{
    bool browserConfiguringBlocker;
    string? browserBlockerOrigin;
    string browserBlockerDiagnostics = "";

    async Task ConfigureBrowserBlocker(CoreWebView2 core, string extensionId)
    {
        browserBlockerOrigin = "chrome-extension://" + extensionId;
        browserConfiguringBlocker = true;
        var loaded = new TaskCompletionSource<bool>();
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(browserBlockerOrigin + "/dashboard.html");
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15))) throw new IOException("Blocker configuration page could not load.");
            await core.ExecuteScriptAsync("""
                (()=>{
                  window.__diplayerBlockerSetup={pending:true};
                  (async()=>{
                    const send=request=>chrome.runtime.sendMessage(request);
                    await send({what:'setFilteringMode',hostname:'youtube.com',level:3});
                    await send({what:'setFilteringMode',hostname:'music.youtube.com',level:3});
                    const mode=await send({what:'getFilteringMode',hostname:'music.youtube.com'});
                    const scripts=await chrome.scripting.getRegisteredContentScripts();
                    const rulesets=await chrome.declarativeNetRequest.getEnabledRulesets();
                    const permissions=await chrome.permissions.getAll();
                    window.__diplayerBlockerSetup={ready:mode===3&&scripts.length>0&&rulesets.length>0,mode,scripts:scripts.length,rulesets:rulesets.length,origins:permissions.origins};
                  })().catch(error=>{window.__diplayerBlockerSetup={error:String(error)};});
                })()
                """);
            for (int i = 0; i < 60; i++)
            {
                string json = await core.ExecuteScriptAsync("window.__diplayerBlockerSetup");
                browserBlockerDiagnostics = json;
                using var result = JsonDocument.Parse(json);
                if (result.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (result.RootElement.TryGetProperty("ready", out var ready) && ready.GetBoolean()) return;
                    if (result.RootElement.TryGetProperty("error", out _)) break;
                }
                await Task.Delay(200);
            }
            throw new IOException("YouTube blocker could not enter complete filtering mode.");
        }
        finally
        {
            core.NavigationCompleted -= Completed;
            browserConfiguringBlocker = false;
        }
    }
}
