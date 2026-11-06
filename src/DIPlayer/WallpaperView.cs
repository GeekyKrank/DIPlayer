using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DIPlayer;

public partial class MainWindow
{
    const int WallpaperFps = 24;
    readonly DispatcherTimer wallpaperTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(1000d / WallpaperFps)
    };
    readonly Stopwatch wallpaperClock = new();
    ZipArchive? wallpaperArchive;
    ZipArchiveEntry[] wallpaperFrames = [];
    int wallpaperFrame = -1;
    bool wallpaperUnavailable;
    string? albumBackdropArtwork;

    void InitializeAlbumBackdrop()
    {
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Image.SourceProperty, typeof(UI.Artwork))
            .AddValueChanged(YouTubeCollectionArtwork, (_, _) => UpdateAlbumBackdropImage());
    }

    void UpdateAlbumBackdropImage()
    {
        // The small header cover is released when it scrolls out of view.
        // Keep this reference for the album page until its artwork URL changes.
        if (AlbumBackdrop.Visibility == Visibility.Visible && YouTubeCollectionArtwork.Url == albumBackdropArtwork
            && YouTubeCollectionArtwork.Source is { } image) AlbumBackdropImage.Source = image;
    }

    void SyncWallpaper()
    {
        bool album = browserMode && !youtubeQueueView && youtubeCollection is { Kind: "Album", Artwork.Length: > 0 };
        string? artwork = album ? youtubeCollection!.Artwork : null;
        if (artwork != albumBackdropArtwork)
        {
            albumBackdropArtwork = artwork;
            AlbumBackdropImage.Source = null;
        }
        AlbumBackdrop.Visibility = album ? Visibility.Visible : Visibility.Collapsed;
        WallpaperLayer.Visibility = album ? Visibility.Collapsed : Visibility.Visible;
        UpdateAlbumBackdropImage();
        bool visible = IsVisible && WindowState != WindowState.Minimized;
        if (album || !settings.AnimatedWallpaper || !visible || quitting)
        {
            StopWallpaper();
            return;
        }

        if (wallpaperUnavailable || wallpaperArchive != null) return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "liquid-backdrop.zip");
            wallpaperArchive = ZipFile.OpenRead(path);
            wallpaperFrames = wallpaperArchive.Entries.OrderBy(frame => frame.Name, StringComparer.Ordinal).ToArray();
            if (wallpaperFrames.Length == 0) throw new InvalidDataException("No wallpaper frames");
            wallpaperClock.Restart();
            DrawWallpaperFrame();
            if (wallpaperArchive != null) wallpaperTimer.Start();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            wallpaperUnavailable = true;
            StopWallpaper();
        }
    }

    void DrawWallpaperFrame()
    {
        if (wallpaperFrames.Length == 0) return;
        int index = (int)(wallpaperClock.Elapsed.TotalSeconds * WallpaperFps % wallpaperFrames.Length);
        if (index == wallpaperFrame) return;
        try
        {
            using var frame = wallpaperFrames[index].Open();
            using var bytes = new MemoryStream();
            frame.CopyTo(bytes);
            bytes.Position = 0;
            WallpaperVideo.Source = BitmapFrame.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            WallpaperPhoto.Visibility = Visibility.Collapsed;
            wallpaperFrame = index;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            wallpaperUnavailable = true;
            StopWallpaper();
        }
    }

    void StopWallpaper()
    {
        wallpaperTimer.Stop();
        wallpaperClock.Stop();
        WallpaperVideo.Source = null;
        WallpaperPhoto.Visibility = Visibility.Visible;
        wallpaperArchive?.Dispose();
        wallpaperArchive = null;
        wallpaperFrames = [];
        wallpaperFrame = -1;
    }
}
