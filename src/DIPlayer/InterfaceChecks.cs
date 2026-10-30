using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DIPlayer;

public partial class MainWindow
{
    static IEnumerable<T> InterfaceChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in InterfaceChildren<T>(child)) yield return descendant;
        }
    }

    async Task VerifyInterface(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Interface checks require isolated storage");
        youtubeTimer.Stop();
        browserMode = true;
        YouTubeNativePanel.Visibility = YouTubeRail.Visibility = Visibility.Visible;
        SoundCloudRail.Visibility = NativePlayer.Visibility = SearchHeader.Visibility = LibraryPanel.Visibility = Visibility.Collapsed;
        YouTubeCards.ItemsSource = youtubeItems;
        string[] titles = ["Midnight City", "Something About Us", "A Moment Apart", "Open Eye Signal", "Weightless", "Kerala", "Awake", "Dayvan Cowboy"];
        string[] artists = ["M83", "Daft Punk", "ODESZA", "Jon Hopkins", "Marconi Union", "Bonobo", "Tycho", "Boards of Canada"];
        SetYouTubeItems(Enumerable.Range(0, 24).Select(i => new YouTubeItem("/watch?v=visual-" + i,
            titles[i % titles.Length], artists[i % artists.Length], null, "Track", i < 8 ? "Recommended for you" : i < 16 ? "A little more to discover" : "Set the mood")));
        UpdateYouTubeShelves("https://music.youtube.com/");
        YouTubeEmpty.Visibility = Visibility.Collapsed;
        await Task.Delay(200); UpdateLayout();

        void FillArtwork()
        {
            int i = 0;
            // Deterministic local covers keep this rendering check independent of the network.
            foreach (var artwork in InterfaceChildren<UI.Artwork>(YouTubeNativePanel))
            {
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                {
                    byte shade = (byte)(38 + i++ % 5 * 18);
                    context.DrawRectangle(new SolidColorBrush(Color.FromRgb(shade, shade, shade)), null, new Rect(0, 0, 200, 200));
                    context.DrawEllipse(new SolidColorBrush(Color.FromRgb((byte)(shade + 40), (byte)(shade + 40), (byte)(shade + 40))), null, new Point(85, 100), 62, 62);
                    context.DrawEllipse(new SolidColorBrush(Color.FromRgb(shade, shade, shade)), null, new Point(105, 80), 52, 52);
                }
                drawing.Freeze(); artwork.Source = new DrawingImage(drawing);
            }
        }
        FillArtwork(); await Task.Delay(100); UpdateLayout();
        var card = InterfaceChildren<Button>(YouTubeShelves).First(button => button.Tag is YouTubeItem);
        CheckInteraction(card.Foreground is SolidColorBrush { Color.R: > 220, Color.G: > 220, Color.B: > 220 }, "Cover-card title foreground is dark");
        var title = InterfaceChildren<TextBlock>(card).First(text => text.Text == titles[0]);
        CheckInteraction(title.Foreground is SolidColorBrush { Color.R: > 220 }, "Rendered title did not inherit the light card foreground");
        var cover = (FrameworkElement)card.Template.FindName("CoverFrame", card);
        CheckInteraction(cover.Clip is RectangleGeometry { RadiusX: 8, RadiusY: 8 } clip && clip.Bounds.Width == 184, "Cover artwork is not really clipped to rounded corners");
        CheckInteraction(InterfaceChildren<UI.Artwork>(card).Single().Stretch == Stretch.Uniform, "Cover rendering crops away part of the source image");
        CheckInteraction(!InterfaceChildren<Button>(YouTubeNativePanel).Any(button => button.Content as string == "Load more"), "Manual Load more button remains");
        Capture(Path.Combine(directory, "interface-home.png"));

        var horizontal = InterfaceChildren<ScrollViewer>(YouTubeShelves).First(scroll => scroll.ScrollableWidth > 0);
        var next = InterfaceChildren<Button>(YouTubeShelves).First(button => button.Tag as string == "next");
        next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        await Task.Delay(100); UpdateLayout();
        CheckInteraction(horizontal.HorizontalOffset > 100, "Shelf next arrow did not scroll recommendations");
        horizontal.ScrollToLeftEnd();
        card.Focus(); await Task.Delay(250); UpdateLayout();
        CheckInteraction(((UIElement)card.Template.FindName("CoverCue", card)).Opacity > .9, "Keyboard focus does not expose the cover action");
        Capture(Path.Combine(directory, "interface-cover-focus.png"));

        Width = 460; Height = 680; YouTubeShelvesScroll.ScrollToTop(); await Task.Delay(200); UpdateLayout(); FillArtwork();
        CheckInteraction((int)YouTubeQuickPicks.Tag == 1, "Quick picks did not adapt to the narrow window");
        CheckInteraction(YouTubeSearch.ActualWidth > 40 && YouTubeAccountButton.IsVisible, "Narrow search controls overlap");
        Capture(Path.Combine(directory, "interface-home-narrow.png"));
        CheckInteraction(YouTubeLyricsButton.Width == 30 && YouTubeLyricsButton.TranslatePoint(new Point(), this).X < YouTubeEqualizerButton.TranslatePoint(new Point(), this).X, "Footer icons must have Lyrics to the left of Equalizer");
        ToggleEqualizer(this, new RoutedEventArgs()); await Task.Delay(100); UpdateLayout();
        CheckInteraction(EqualizerOverlay.IsVisible && EqualizerBands.Items.Count == 6, "Six-band equalizer panel did not open");
        EqualizerPreset(new Button { Tag = "bass" }, new RoutedEventArgs());
        CheckInteraction(settings.EqualizerEnabled && settings.EqualizerGains[0] == 5, "Equalizer preset was not applied");
        Capture(Path.Combine(directory, "interface-equalizer-narrow.png"));
        Width = 1200; Height = 780;
        await Task.Delay(100); UpdateLayout();
        Capture(Path.Combine(directory, "interface-equalizer.png"));
        CloseEqualizer(this, new RoutedEventArgs());
        browserMode = false;
        YouTubeNativePanel.Visibility = YouTubeRail.Visibility = Visibility.Collapsed;
        NativePlayer.Visibility = SearchHeader.Visibility = SoundCloudRail.Visibility = Visibility.Visible;
        SetView("home");
        Width = 460; Height = 680; await Task.Delay(100); UpdateLayout();
        CheckInteraction(LyricsButton.IsVisible && EqualizerButton.IsVisible && LyricsButton.TranslatePoint(new Point(), this).X < EqualizerButton.TranslatePoint(new Point(), this).X, "SoundCloud footer lost Lyrics / Equalizer icon order");
        Capture(Path.Combine(directory, "interface-soundcloud-narrow.png"));
        youtubeLyricsOpen = true; YouTubeLyricsOverlay.Visibility = EqualizerOverlay.Visibility = Visibility.Visible;
        SetView("search");
        CheckInteraction(!youtubeLyricsOpen && !YouTubeLyricsOverlay.IsVisible && !EqualizerOverlay.IsVisible, "Navigation leaves a playback panel covering the destination");
        Width = 1200; Height = 780;
        if (Environment.GetCommandLineArgs().Contains("--hold-interface")) await Task.Delay(45000);
        await File.WriteAllTextAsync(Path.Combine(directory, "interface-check.txt"),
            "PASS readable rendered card titles, real rounded image clipping, shelf navigation, visible keyboard cover action, narrow quick picks and search, no Load more control. Local fixture only.");
        browserMode = false;
    }
}
