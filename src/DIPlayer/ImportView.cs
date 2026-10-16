using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DIPlayer.Core;
using DIPlayer.Import;

namespace DIPlayer;

public partial class MainWindow
{
    PlaylistImportProgress? importProgress;
    string importMessage = "";
    string? importedPlaylistId;
    bool importError, importedLibraryDirty;

    void ImportClick(object s, RoutedEventArgs e)
    {
        SetView("import");
        if (importCancel == null) ImportUrl.Focus();
    }
    async void ImportUrlKey(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await StartPlaylistImport();
    }
    async void StartImportClick(object s, RoutedEventArgs e) => await StartPlaylistImport();

    async Task StartPlaylistImport()
    {
        if (importCancel != null) return;
        string url;
        try { url = SoundCloudCatalog.PublicPlaylistUrl(ImportUrl.Text.Trim()); }
        catch (CatalogException e)
        {
            importedPlaylistId = null;
            importProgress = null;
            SetImportMessage(e.Message, true);
            return;
        }
        importedPlaylistId = null;
        importProgress = null;
        importCancel = new();
        SetImportMessage("Loading public playlist…");
        importing = ImportPlaylist(url, importCancel.Token);
        await importing;
    }

    async Task ImportPlaylist(string url, CancellationToken ct)
    {
        try
        {
            var progress = new InlineProgress(message => SetImportMessage(message));
            var details = new ImportDetails(value =>
            {
                importProgress = value;
                importedPlaylistId = value.LocalId;
                SyncImportView();
            });
            string id = await new PlaylistImporter(Catalog(), store).Import(url, progress, ct, details);
            importedPlaylistId = id;
            bool complete = store.Playlists().Single(p => p.Id == id).Complete;
            SetImportMessage(complete ? "Done. The local playlist is saved. Audio is not downloaded." : "Partial list saved: SoundCloud did not return all listed tracks.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetImportMessage(importedPlaylistId == null ? "Import canceled." : "Import canceled. The imported tracks have been saved.");
        }
        catch (Exception e)
        {
            SetImportMessage(e switch
            {
                CatalogException => e.Message,
                HttpRequestException => "No connection. Check your network and retry the import.",
                OperationCanceledException => "The server timed out. Retry the import.",
                _ => "Could not finish the import. The imported tracks have been saved."
            }, true);
        }
        finally
        {
            importCancel?.Dispose();
            importCancel = null;
            importing = null;
            importedLibraryDirty = true;
            SyncImportView();
            RefreshImportedLibrary();
        }
    }

    void SetImportMessage(string message, bool error = false)
    {
        importMessage = message;
        importError = error;
        SetStatus(error ? message : "");
        SyncImportView();
    }
    void SyncImportView()
    {
        if (ImportPanel == null || view != "import" || !IsVisible || WindowState == WindowState.Minimized) return;
        bool running = importCancel != null;
        ImportUrl.IsReadOnly = running;
        ImportStart.IsEnabled = !running;
        ImportCancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ImportOpenPlaylist.Visibility = !running && importedPlaylistId != null ? Visibility.Visible : Visibility.Collapsed;
        ImportMessage.Text = importMessage;
        ImportMessage.Foreground = importError ? (Brush)FindResource("Accent") : Brushes.DarkGray;
        ImportProgressPanel.Visibility = importProgress != null ? Visibility.Visible : Visibility.Collapsed;
        if (importProgress is { } progress)
        {
            ImportProgressText.Text = $"Tracks added: {progress.Imported} / {progress.Total}";
            ImportProgressBar.Maximum = Math.Max(1, progress.Total);
            ImportProgressBar.Value = progress.Imported;
            ImportUnavailable.Text = $"Unavailable or missing: {progress.Unavailable}";
            ImportUnavailable.Visibility = progress.Unavailable > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    void RefreshImportedLibrary()
    {
        if (!importedLibraryDirty || view != "library" || !IsVisible || WindowState == WindowState.Minimized) return;
        importedLibraryDirty = false;
        RefreshPlaylists();
        if (libraryOverview) ShowLibraryOverview(); else ShowLibrary();
    }
    void OpenImportedPlaylist(object s, RoutedEventArgs e)
    {
        if (importedPlaylistId == null) return;
        RefreshPlaylists(importedPlaylistId);
        SetView("library");
        page = 0;
        importedLibraryDirty = false;
        ShowLibrary();
    }
    void CancelImportClick(object s, RoutedEventArgs e)
    {
        importCancel?.Cancel();
        SetImportMessage("Stopping import…");
    }
    sealed class ImportDetails(Action<PlaylistImportProgress> update) : IProgress<PlaylistImportProgress>
    {
        public void Report(PlaylistImportProgress value) => update(value);
    }
}
