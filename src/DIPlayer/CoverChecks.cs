using System.Windows;
using DIPlayer.Storage;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyPlaylistCover(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Cover check requires isolated storage");
        var uri = new Uri("pack://application:,,,/Assets/wallpaper.jpg");
        using var resource = System.Windows.Application.GetResourceStream(uri)!.Stream;
        string input = Path.Combine(directory, "input-cover.jpg");
        using (var file = File.Create(input)) resource.CopyTo(file);
        byte[] image = await Task.Run(() => PrepareCover(input));
        string id = store.Create("Playlist cover");
        OpenLocalPlaylist(id);
        RenamePlaylist(this, new RoutedEventArgs());
        CheckInteraction(ActionHeading.Text == "Edit playlist" && ActionCoverPanel.IsVisible, "Edit cover UI missing");
        actionCoverBytes = image; actionCoverChanged = true;
        ActionCoverPreview.Source = DecodeCover(image, 128);
        ActionName.Text = "Мой плейлист";
        UpdateLayout(); Capture(Path.Combine(directory, "edit-cover.png"));
        ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(store.Cover(id)!.SequenceEqual(image) && store.Playlists().Single(p => p.Id == id).Name == "Мой плейлист", "Cover/name save failed");
        using (var reopened = new LibraryStore(DiagnosticLibraryDirectory()))
            CheckInteraction(reopened.Cover(id)!.SequenceEqual(image) && reopened.Playlists().Single(p => p.Id == id).HasCover, "Cover did not survive storage reopen");
        RenamePlaylist(this, new RoutedEventArgs()); ResetPlaylistCover(this, new RoutedEventArgs()); CancelAction(this, new RoutedEventArgs());
        CheckInteraction(store.Cover(id) != null, "Cancel changed cover");
        RenamePlaylist(this, new RoutedEventArgs()); ResetPlaylistCover(this, new RoutedEventArgs()); ConfirmAction(this, new RoutedEventArgs());
        CheckInteraction(store.Cover(id) == null && MakeCard(store.Playlists().Single(p => p.Id == id)).CustomCover == null, "Reset did not restore collage");
        store.Edit(id, "Мой плейлист", true, image); localCovers.Clear(); ShowLibrary();
        UpdateLayout(); Capture(Path.Combine(directory, "custom-cover-detail.png"));
        LibraryView(this, new RoutedEventArgs()); UpdateLayout(); Capture(Path.Combine(directory, "custom-cover-library.png"));
        var decoded = DecodeCover(image, 256);
        CheckInteraction(decoded.PixelWidth <= 256 && decoded.PixelHeight <= 256, "Cover decode is unbounded");
        store.Delete(id);
        CheckInteraction(store.Cover(id) == null, "Deleting playlist leaves cover data");
        await File.WriteAllTextAsync(Path.Combine(directory, "playlist-cover-check.txt"), "PASS image resize, edit form preview, atomic name/cover save, persistence on storage reopen, cancel, reset to collage, detail/library display, bounded decode and cover removal on playlist deletion.");
        SetView("home");
    }
}
