using System.Windows;
using DIPlayer.Core;
using DIPlayer.Import;

namespace DIPlayer;

public partial class MainWindow
{
    CancellationTokenSource? accountCancel;
    Task? accountTask;
    string accountMessage = "";
    bool accountImported;
    async void SignInAccount(object sender, RoutedEventArgs e)
    {
        if (accountCancel != null || importCancel != null) return;
        accountCancel = new(); accountImported = false;
        accountMessage = "Opening secure SoundCloud sign-in…";
        SyncAccountView(); SyncImportView();
        accountTask = ImportAccount(accountCancel.Token);
        await accountTask;
    }
    async Task ImportAccount(CancellationToken ct)
    {
        try
        {
            using var importer = new AccountLibraryImporter(serverOverride ?? settings.Server ?? DIPlayer.Storage.Settings.DefaultServer, store);
            await importer.Run(new InlineProgress(message => { accountMessage = message; SyncAccountView(); }), ct);
            accountImported = true;
        }
        catch (OperationCanceledException) { accountMessage = "Import canceled. Any received lists remain saved locally and incomplete lists are marked."; }
        catch (Exception error) { accountMessage = error is CatalogException ? error.Message : "Could not import the account library. Check your network and try again. Any received tracks are saved locally."; }
        finally
        {
            accountCancel?.Dispose(); accountCancel = null; accountTask = null;
            importedLibraryDirty = true;
            SyncAccountView(); SyncImportView(); RefreshImportedLibrary();
        }
    }
    void CancelAccountImport(object sender, RoutedEventArgs e)
    {
        accountCancel?.Cancel(); accountMessage = "Stopping account import…"; SyncAccountView();
    }
    void OpenAccountLibrary(object sender, RoutedEventArgs e) => LibraryView(sender, e);
    void SyncAccountView()
    {
        SyncLibraryImportStatus();
        if (AccountSignIn == null || view != "import" || !IsVisible || WindowState == WindowState.Minimized) return;
        AccountSignIn.IsEnabled = accountCancel == null && importCancel == null;
        AccountCancel.Visibility = accountCancel != null ? Visibility.Visible : Visibility.Collapsed;
        AccountOpenLibrary.Visibility = accountImported ? Visibility.Visible : Visibility.Collapsed;
        AccountMessage.Text = accountMessage;
    }
    void SyncLibraryImportStatus()
    {
        if (LibraryImportStatus == null) return;
        LibraryImportStatus.Text = accountCancel != null || (importCancel == null && accountMessage.Length > 0)
            ? accountMessage : importMessage;
    }
}
