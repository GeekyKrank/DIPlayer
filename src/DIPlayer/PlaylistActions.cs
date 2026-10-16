using System.Windows;
using System.Windows.Input;
using DIPlayer.Core;
using DIPlayer.Storage;

namespace DIPlayer;

public partial class MainWindow
{
    enum PlaylistAction { None, Create, Rename, Delete, Add, CreateAndAdd, Notice }
    PlaylistAction playlistAction;
    string? actionPlaylistId;
    Track? actionTrack;
    IInputElement? actionPreviousFocus;

    void CreatePlaylist(object s, RoutedEventArgs e) => OpenPlaylistAction(PlaylistAction.Create);
    void RenamePlaylist(object s, RoutedEventArgs e)
    {
        if (playlistId != null) OpenPlaylistAction(PlaylistAction.Rename, playlistId);
    }
    void DeletePlaylist(object s, RoutedEventArgs e)
    {
        if (playlistId != null) OpenPlaylistAction(PlaylistAction.Delete, playlistId);
    }
    void AddToPlaylist(object s, RoutedEventArgs e)
    {
        if (Selected is not { } row) { SetStatus("Choose a track."); return; }
        OpenPlaylistAction(store.Playlists().Count == 0 ? PlaylistAction.CreateAndAdd : PlaylistAction.Add, track: row.Track);
    }

    void OpenPlaylistAction(PlaylistAction action, string? id = null, Track? track = null, string? notice = null)
    {
        CloseActionPanel(false);
        actionPreviousFocus = Keyboard.FocusedElement;
        playlistAction = action;
        actionPlaylistId = id;
        actionTrack = track;
        var header = store.Playlists().FirstOrDefault(p => p.Id == id);
        bool name = action is PlaylistAction.Create or PlaylistAction.Rename or PlaylistAction.CreateAndAdd;
        ActionName.Visibility = name ? Visibility.Visible : Visibility.Collapsed;
        ActionPicker.Visibility = action == PlaylistAction.Add ? Visibility.Visible : Visibility.Collapsed;
        ActionInputLabel.Visibility = name || action == PlaylistAction.Add ? Visibility.Visible : Visibility.Collapsed;
        ActionInputLabel.Text = action == PlaylistAction.Add ? "Playlist" : "Playlist name";
        ActionCancel.Visibility = action == PlaylistAction.Notice ? Visibility.Collapsed : Visibility.Visible;
        ActionHeading.Text = action switch
        {
            PlaylistAction.Create or PlaylistAction.CreateAndAdd => "Create playlist",
            PlaylistAction.Rename => "Edit playlist",
            PlaylistAction.Delete => "Delete playlist?",
            PlaylistAction.Add => "Add to playlist",
            _ => "DIPlayer"
        };
        ActionDescription.Text = action switch
        {
            PlaylistAction.Rename => "Name and cover of your local playlist.",
            PlaylistAction.Delete => "Local playlist “" + header?.Name + "” will be deleted. Tracks in other playlists and the queue will be kept.",
            PlaylistAction.Add => track?.Title ?? "Choose a local playlist.",
            PlaylistAction.CreateAndAdd => "Create your first playlist and add “" + track?.Title + "».",
            PlaylistAction.Notice => notice ?? "Could not complete the action.",
            _ => "This playlist will be stored on this computer."
        };
        ActionConfirm.Content = action switch
        {
            PlaylistAction.Create => "Create",
            PlaylistAction.CreateAndAdd => "Create and add",
            PlaylistAction.Delete => "Delete",
            PlaylistAction.Add => "Add",
            PlaylistAction.Notice => "Close",
            _ => "Save"
        };
        ActionName.Text = action == PlaylistAction.Rename ? header?.Name ?? "" : "";
        ActionCoverPanel.Visibility = action == PlaylistAction.Rename ? Visibility.Visible : Visibility.Collapsed;
        ActionCoverPreview.Source = header == null ? null : LocalCover(header);
        if (action == PlaylistAction.Add)
        {
            var data = store.Playlists();
            ActionPicker.ItemsSource = data;
            ActionPicker.SelectedItem = data.FirstOrDefault(p => p.Id == playlistId) ?? data.FirstOrDefault();
        }
        ActionOverlay.Visibility = Visibility.Visible;
        if (name) { ActionName.Focus(); ActionName.SelectAll(); }
        else if (action == PlaylistAction.Add) ActionPicker.Focus();
        else if (action == PlaylistAction.Delete) ActionCancel.Focus();
        else ActionConfirm.Focus();
    }

    void ConfirmAction(object s, RoutedEventArgs e)
    {
        if (playlistAction == PlaylistAction.Notice) { CloseActionPanel(); return; }
        string name = ActionName.Text.Trim();
        if (playlistAction is PlaylistAction.Create or PlaylistAction.Rename or PlaylistAction.CreateAndAdd && (name.Length == 0 || name.Length > 120))
        {
            ShowActionError("Enter a name between 1 and 120 characters.");
            ActionName.Focus();
            return;
        }
        string? target = playlistAction == PlaylistAction.Add ? (ActionPicker.SelectedItem as PlaylistHeader)?.Id : actionPlaylistId;
        if (playlistAction is PlaylistAction.Add or PlaylistAction.Rename or PlaylistAction.Delete && !store.Playlists().Any(p => p.Id == target))
        {
            ShowActionError("Playlist not found. Choose another or create a new one.");
            return;
        }
        var action = playlistAction;
        var track = actionTrack;
        try
        {
            switch (action)
            {
                case PlaylistAction.Create:
                case PlaylistAction.CreateAndAdd:
                    string created = store.Create(name);
                    try { if (track != null) store.Add(created, [track]); }
                    catch { store.Delete(created); throw; }
                    CloseActionPanel(false);
                    RefreshPlaylists(created);
                    SetView("library");
                    page = 0;
                    ShowLibrary();
                    break;
                case PlaylistAction.Rename:
                    store.Edit(target!, name, actionCoverChanged, actionCoverBytes);
                    localCovers.Remove(target!);
                    CloseActionPanel(false);
                    RefreshPlaylists(target);
                    ShowLibrary();
                    break;
                case PlaylistAction.Delete:
                    store.Delete(target!);
                    CloseActionPanel(false);
                    playlistId = null;
                    RefreshPlaylists();
                    ShowLibraryOverview();
                    break;
                case PlaylistAction.Add:
                    if (track == null) return;
                    string title = store.Playlists().First(p => p.Id == target).Name;
                    store.Add(target!, [track]);
                    CloseActionPanel();
                    SetStatus("Added to “" + title + "»");
                    if (view == "library") { RefreshPlaylists(); ShowLibrary(); }
                    break;
            }
        }
        catch (Exception error) { ShowActionError(error is CatalogException ? error.Message : "Could not save changes. Please try again."); }
    }
    void ShowActionError(string message)
    {
        ActionError.Text = message;
        ActionError.Visibility = Visibility.Visible;
    }
    internal void ReportApplicationError(string message) => OpenPlaylistAction(PlaylistAction.Notice, notice: message);
    void CancelAction(object s, RoutedEventArgs e) => CloseActionPanel();
    void CloseActionPanel(bool restoreFocus = true)
    {
        if (ActionOverlay == null) return;
        ActionOverlay.Visibility = Visibility.Collapsed;
        ActionError.Visibility = Visibility.Collapsed;
        ActionError.Text = "";
        ActionPicker.ItemsSource = null;
        ActionName.Text = "";
        actionCoverBytes = null;
        coverLoadGeneration++;
        actionCoverChanged = false;
        ActionCoverPreview.Source = null;
        playlistAction = PlaylistAction.None;
        actionPlaylistId = null;
        actionTrack = null;
        if (restoreFocus && actionPreviousFocus is UIElement { IsVisible: true } element) element.Focus();
        actionPreviousFocus = null;
    }
    void ActionBackdropClick(object s, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, s)) CloseActionPanel();
    }
    void ActionOverlayKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseActionPanel(); }
    }
    void ActionInputKey(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ActionPicker.IsDropDownOpen) return;
        e.Handled = true;
        ConfirmAction(s, e);
    }
}
