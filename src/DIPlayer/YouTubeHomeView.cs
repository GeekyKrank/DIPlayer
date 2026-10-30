using System.Windows;
using System.Windows.Controls;

namespace DIPlayer;

public partial class MainWindow
{
    string youtubeQuickPicksSignature = "";

    void SyncYouTubeHomePresentation()
    {
        bool home = !youtubeQueueView && youtubeCollection == null
            && new Uri(youtubeDisplayedUrl).AbsolutePath == "/";
        YouTubeHomeIntro.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        if (!home) return;

        // These are actual recommendations from the current page, never invented history.
        var picks = youtubeItems.Where(item => item.Kind == "Track" && !item.IsVideo)
            .DistinctBy(item => item.Key).Take(2).ToArray();
        string signature = string.Join('\n', picks.Select(item => item.Key + item.Title + item.Subtitle + item.Artwork));
        if (signature != youtubeQuickPicksSignature)
        {
            youtubeQuickPicksSignature = signature;
            YouTubeQuickPicks.ItemsSource = picks;
        }
        YouTubeQuickPicksSection.Visibility = picks.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void ScrollYouTubeShelf(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Parent is not StackPanel { Parent: Grid grid }) return;
        var scroll = grid.Children.OfType<ScrollViewer>().FirstOrDefault();
        if (scroll == null) return;
        double direction = button.Tag as string == "previous" ? -1 : 1;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + direction * Math.Max(204, scroll.ViewportWidth * .85));
    }

    void YouTubeQuickPicksSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ItemsControl picks) picks.Tag = e.NewSize.Width < 640 ? 1 : 2;
    }

    void YouTubeLyricsLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (YouTubeLyricsArtworkPanel == null) return;
        bool compact = e.NewSize.Width < 700;
        YouTubeLyricsArtworkColumn.Width = compact ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        YouTubeLyricsArtworkPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        LyricsCompactInfo.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        double coverSize = Math.Max(120, Math.Min(280, Math.Min(e.NewSize.Height * .46, Math.Min(480, e.NewSize.Width * .4) - 74)));
        LyricsArtworkFrame.Width = LyricsArtworkFrame.Height = coverSize;
        LyricsVariantLabel.MaxWidth = compact ? 100 : 220;
        foreach (var line in youtubeLyricBlocks) line.FontSize = compact ? 26 : 32;
    }

    void YouTubeCollectionHeaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (YouTubeCollectionArtwork == null) return;
        bool compact = e.NewSize.Width < 620;
        double size = compact ? 112 : 270;
        YouTubeCollectionCoverColumn.Width = new GridLength(size);
        YouTubeCollectionGapColumn.Width = new GridLength(compact ? 16 : 32);
        YouTubeCollectionArtwork.Width = YouTubeCollectionArtwork.Height = size;
        YouTubeCollectionTitle.FontSize = compact ? 21 : 29;
        YouTubeCollectionDescription.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

}
