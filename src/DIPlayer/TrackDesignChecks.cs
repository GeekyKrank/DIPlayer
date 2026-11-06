using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    async Task VerifyTrackDesign(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Track design requires isolated storage");
        CheckInteraction(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(new System.Windows.Interop.WindowInteropHelper(this).Handle), new IntPtr(-4)),
            "The player window is not per-monitor DPI aware; Windows could stretch its text as a bitmap");
        youtubeTimer.Stop(); youtubeLyricsTimer.Stop();
        browserMode = true; youtubeQueueView = false; youtubeCollection = null;
        YouTubeNativePanel.Visibility = YouTubeRail.Visibility = Visibility.Visible;
        NativePlayer.Visibility = SoundCloudRail.Visibility = LibraryPanel.Visibility = SearchHeader.Visibility = Visibility.Collapsed;
        youtubeState = new("https://music.youtube.com/", "addiction (Slowed)", "LONOWN", null, 37, 180, true, .5, false, 0, false, [], VideoId: "track-design-fixture");
        Width = 1200; Height = 780;
        OpenTrackLyrics(this, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        CheckInteraction(youtubeLyricsOpen && YouTubeLyricsOverlay.IsVisible, "Clicking the current title did not open the track screen");
        SyncLyricsMetadata(); youtubeLyricsTrack = CurrentLyricsTrack(); ResetLyricsChoices();
        lyricsChoices.Add(new("youtube", "YouTube Music", Enumerable.Range(0, 24).Select(i => new YouTubeLyricsLine(new[] { "I'm shattered, I'm breathing", "In ruins shaped like you", "Your name is all that's bleeding", "A wound I'm falling through" }[i % 4], false, 2 + i * 5)).ToArray(), "Synced · local timing fixture"));
        lyricsChoices.Add(new("fixture-variant", "LRCLIB · alternate", [new("A second real-timing fixture", false, 0), new("Choose the version that fits", false, 7)], "Synced · local alternate fixture"));
        ShowSelectedLyrics(); SyncTrackScreenPlayback(); UpdateLayout(); await Task.Delay(250);
        var art = new DrawingGroup();
        using (var draw = art.Open())
        {
            draw.DrawRectangle(new LinearGradientBrush(Color.FromRgb(96, 37, 69), Color.FromRgb(36, 71, 116), 45), null, new Rect(0, 0, 500, 500));
            draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(173, 142, 94)), null, new Point(190, 240), 130, 130);
            draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(29, 30, 38)), null, new Point(240, 180), 115, 115);
        }
        art.Freeze(); var image = new DrawingImage(art); image.Freeze();
        LyricsArtwork.Source = LyricsBackdrop.Source = image;
        YouTubeNowArtwork.Source = image;
        await Task.Delay(150); UpdateLayout();
        CheckInteraction(LyricsArtwork.Stretch == Stretch.Uniform && LyricsArtworkFrame.ActualWidth > 200, "Track cover is cropped or missing");
        CheckInteraction(LyricsPlay.IsVisible && LyricsPosition.IsVisible && LyricsVolume.IsVisible && LyricsVariantLabel.Text.Contains("1/2"), "Track view controls are incomplete");
        CheckInteraction(LyricsBackdrop.RenderTransform is TransformGroup { Children.Count: 3 }, "Cover colors are not animated");
        var lyric = youtubeLyricBlocks[youtubeActiveLyric];
        CheckInteraction(lyric.FontFamily.Source.Contains("Segoe UI Variable Display") && lyric.FontWeight == FontWeights.Bold &&
            TextOptions.GetTextFormattingMode(lyric) == TextFormattingMode.Ideal && TextOptions.GetTextHintingMode(lyric) == TextHintingMode.Fixed &&
            TextOptions.GetTextRenderingMode(lyric) == TextRenderingMode.Grayscale &&
            lyric.RenderTransform.Value.IsIdentity && Math.Abs(lyric.LineHeight - lyric.FontSize * 1.3) < .01,
            $"Track lyrics lost the clip typography or glyphs were scaled: {lyric.FontFamily.Source}, {lyric.FontWeight}, " +
            $"{TextOptions.GetTextFormattingMode(lyric)}, {TextOptions.GetTextHintingMode(lyric)}, {lyric.RenderTransform.Value}, {lyric.LineHeight}/{lyric.FontSize}");
        CheckInteraction(lyric.Effect is System.Windows.Media.Effects.DropShadowEffect { RenderingBias: System.Windows.Media.Effects.RenderingBias.Quality, BlurRadius: 4, ShadowDepth: 1 },
            "Track lyrics lost the soft clip-style text shadow");
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
        {
            var textTypeface = new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal);
            CheckInteraction(textTypeface.TryGetGlyphTypeface(out var face) && face.Weight == weight
                && "ПривітЇЄҐЁЙLyrics".All(character => face.CharacterToGlyphMap.ContainsKey(character)),
                "Restored typeface lost its real weight or Cyrillic glyphs: " + weight);
        }
        CheckInteraction(SearchBox.FontFamily.Source == "Segoe UI" && LyricsTitle.FontFamily.Source == "Segoe UI"
            && TextOptions.GetTextRenderingMode(SearchBox) == TextRenderingMode.ClearType
            && TextOptions.GetTextRenderingMode(LyricsTitle) == TextRenderingMode.ClearType,
            $"Search/track typography: {SearchBox.FontFamily.Source}, {LyricsTitle.FontFamily.Source}, " +
            $"{TextOptions.GetTextRenderingMode(SearchBox)}, {TextOptions.GetTextRenderingMode(LyricsTitle)}");
        Capture(Path.Combine(directory, "track-design.png"));
        if (Environment.GetCommandLineArgs().Contains("--preview-text"))
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "text-preview-ready.txt"), "Native-resolution text preview ready");
            await Task.Delay(30000);
        }
        Width = 860; await Task.Delay(180); UpdateLayout();
        CheckInteraction(LyricsArtworkFrame.ActualWidth <= YouTubeLyricsArtworkPanel.ViewportWidth + 1, "Medium-width track view clips the cover");
        Capture(Path.Combine(directory, "track-design-medium.png"));

        CycleLyricsVersion(new Button { Tag = "1" }, new RoutedEventArgs());
        CheckInteraction(youtubeLyricBlocks[0].Text == "A second real-timing fixture" && LyricsVariantLabel.Text.Contains("2/2"), "Lyrics version selection ignored the requested timeline");
        CycleLyricsVersion(new Button { Tag = "-1" }, new RoutedEventArgs());
        youtubeState = youtubeState with { Position = 6.95 }; ObserveLyricsPlayback(); ApplyLyricsHighlight();
        CheckInteraction(youtubeActiveLyric == 0, "Baseline synchronization is wrong");
        ChangeLyricsOffset(new Button { Tag = "0.1" }, new RoutedEventArgs());
        CheckInteraction(youtubeActiveLyric == 1 && Math.Abs(settings.LyricsOffsets[CurrentLyricsTrack()] - .1) < .001, "Timing adjustment did not change playback highlighting or save per track");
        ChangeLyricsOffset(new Button { Tag = "0" }, new RoutedEventArgs());
        CheckInteraction(lyricsOffset == 0 && !settings.LyricsOffsets.ContainsKey(CurrentLyricsTrack()), "Timing reset did not clear the adjustment");
        Width = 460; Height = 680; await Task.Delay(250); UpdateLayout();
        LyricsCompactArtwork.Source = image;
        await Task.Delay(100); UpdateLayout();
        CheckInteraction(!YouTubeLyricsArtworkPanel.IsVisible && LyricsCompactInfo.IsVisible && LyricsCompactPlay.IsVisible, "Compact track header is missing");
        CheckInteraction(LyricsVariantLabel.ActualWidth <= 100 && YouTubeLyricsScroll.ViewportWidth > 250, "Compact lyrics controls overlap");
        CheckInteraction(youtubeLyricBlocks[0].FontSize == 24 && youtubeLyricBlocks[0].Margin.Bottom == 20,
            "Compact track lyrics did not adopt the compact clip typography");
        Capture(Path.Combine(directory, "track-design-narrow.png"));

        StopYouTubeLyrics();
        CheckInteraction(LyricsBackdrop.RenderTransform == Transform.Identity && !youtubeLyricsTimer.IsEnabled && !visualEnergyInstalled, "Closing track view retained animation or analyzer work");
        Width = 1200; Height = 780; await Task.Delay(200); UpdateLayout();
        YouTubeNowTitle.Text = "addiction (Slowed)"; YouTubeNowArtist.Text = "LONOWN";
        YouTubePosition.Maximum = 180; YouTubePosition.Value = 54; await Task.Delay(300);
        var fill = InterfaceChildren<UI.PlaybackFill>(YouTubePlayerSurface).Single();
        CheckInteraction(Math.Abs(fill.VisibleFraction - .3) < .001, "Player surface does not follow the elapsed fraction");
        YouTubePosition.ApplyTemplate();
        var seekTrack = (System.Windows.Controls.Primitives.Track)YouTubePosition.Template.FindName("PART_Track", YouTubePosition);
        foreach (double fraction in new[] { 0d, .1, .5, .9, 1 })
        {
            YouTubeSeekPointer(9 + fraction * (YouTubePosition.ActualWidth - 18)); UpdateLayout();
            double center = seekTrack.Thumb.TranslatePoint(new Point(seekTrack.Thumb.ActualWidth / 2, 0), fill).X;
            CheckInteraction(Math.Abs(fill.FilledWidth - center) < .75 && Math.Abs(fill.VisibleFraction - fraction) < .001, "Seek pointer, thumb and elapsed surface disagree at " + fraction);
            if (fraction > 0)
            {
                var line = InterfaceChildren<System.Windows.Controls.Border>(seekTrack.DecreaseRepeatButton).Single();
                var dot = InterfaceChildren<System.Windows.Shapes.Ellipse>(seekTrack.Thumb).Single();
                var endpoint = line.TranslatePoint(new Point(line.ActualWidth, line.ActualHeight / 2), fill);
                var dotCenter = dot.TranslatePoint(new Point(dot.ActualWidth / 2, dot.ActualHeight / 2), fill);
                CheckInteraction(Math.Abs(endpoint.X - dotCenter.X) < .75 && Math.Abs(endpoint.Y - dotCenter.Y) < .75, "The rendered line ends before the actual thumb dot at " + fraction);
                CheckInteraction(endpoint.Y <= 2 && YouTubePosition.Opacity == 1,
                    "Footer progress line is hidden or leaves an empty strip above it");
            }
        }
        YouTubePosition.Value = 54; UpdateLayout();
        CheckInteraction(YouTubeShuffle.TranslatePoint(new Point(), this).X < YouTubeRepeat.TranslatePoint(new Point(), this).X && YouTubeRepeat.TranslatePoint(new Point(), this).X > YouTubeTransport.TranslatePoint(new Point(), this).X, "Player mode buttons are not grouped on the right");
        CheckInteraction(!InterfaceChildren<TextBlock>(YouTubeHomeIntro).Any(text => text.Text.Contains("Find your next") || text.Text.Contains("Good music") || text.Text.Contains("DISCOVER YOUR")), "Removed home slogans are still visible");
        Capture(Path.Combine(directory, "player-fill.png"));
        Width = 460; Height = 680; await Task.Delay(150); UpdateLayout();
        CheckInteraction(YouTubeFooterDetails.ActualWidth <= YouTubeFooter.ActualWidth && YouTubeLyricsButton.IsVisible && YouTubeEqualizerButton.IsVisible, "Player actions overflow the narrow window");
        Capture(Path.Combine(directory, "player-fill-narrow.png"));
        var typeface = new Typeface(UI.Symbols.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        CheckInteraction(typeface.TryGetGlyphTypeface(out var font) && font.CharacterToGlyphMap.ContainsKey(UI.Symbols.Lyrics[0]), "Embedded Flaticon glyphs fell back to another font");
        Width = 1200; Height = 780; browserMode = false;
        await File.WriteAllTextAsync(Path.Combine(directory, "track-design-check.txt"), "PASS isolated local fixtures: title opens track view, full cover, animated cover backdrop, transport/seek/volume, independent lyric versions, per-track offset/reset, compact header, shutdown cleanup, elapsed footer fill and right-side mode controls, removed home slogans, embedded Uicons glyph.");
    }
}
