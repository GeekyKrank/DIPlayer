using System.Windows;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    async Task VerifyProgressMotion(string directory)
    {
        CheckInteraction(DiagnosticLibraryDirectory() != null, "Progress checks require isolated storage");
        youtubeTimer.Stop(); youtubeLyricsTimer.Stop();
        browserMode = true; YouTubeNativePanel.Visibility = Visibility.Visible; NativePlayer.Visibility = Visibility.Collapsed;
        var state = new YouTubeState("https://music.youtube.com/", "Motion fixture", "Local fixture", null, 30, 180, false, .5, false, 0, false, [], VideoId: "progress-fixture");
        progressTrack = ""; settings.MotionEnabled = true; youtubeSeeking = false;
        ObserveProgressFrame(state); UpdateLayout();
        double before = YouTubePosition.Value;
        await Task.Delay(140); UpdateLayout();
        double middle = YouTubePosition.Value;
        CheckInteraction(middle > before + .05 && middle < before + .5, "Progress did not interpolate between browser snapshots");
        var fill = InterfaceChildren<UI.PlaybackFill>(YouTubePlayerSurface).Single();
        CheckInteraction(Math.Abs(fill.VisibleFraction - middle / 180) < .0001, "Progress fill uses a different animation clock");
        ObserveProgressFrame(state with { Position = middle });
        CheckInteraction(Math.Abs(YouTubePosition.Value - middle) < .08, "A browser update made smooth progress jump");
        ObserveProgressFrame(state with { Position = 42, Paused = true }); await Task.Delay(160);
        CheckInteraction(YouTubePosition.Value == 42 && !progressFrameTimer.IsEnabled, "Paused progress kept advancing");
        ObserveProgressFrame(state with { Position = 55, Buffering = true }); await Task.Delay(100);
        CheckInteraction(YouTubePosition.Value == 55 && !progressFrameTimer.IsEnabled, "Buffering progress kept advancing");
        ObserveProgressFrame(state with { Position = 90 });
        CheckInteraction(Math.Abs(YouTubePosition.Value - 90) < .05, "Seeking was interpolated from the old location");
        youtubeSeeking = true; YouTubePosition.Value = 20; await Task.Delay(140);
        CheckInteraction(YouTubePosition.Value == 20, "Animation moved the thumb during a pointer seek");
        youtubeSeeking = false; settings.MotionEnabled = false; ObserveProgressFrame(state with { Position = 70 }); await Task.Delay(120);
        CheckInteraction(YouTubePosition.Value == 70 && !progressFrameTimer.IsEnabled, "Motion preference did not disable interpolation");
        settings.MotionEnabled = true; ObserveProgressFrame(state with { Position = 70, Paused = true });
        var converter = new UI.VideoArtworkStretch();
        CheckInteraction((Stretch)converter.Convert("https://i.ytimg.com/vi/fixture/hqdefault.jpg", typeof(Stretch), null!, System.Globalization.CultureInfo.InvariantCulture) == Stretch.UniformToFill
            && (Stretch)converter.Convert("https://lh3.googleusercontent.com/album", typeof(Stretch), null!, System.Globalization.CultureInfo.InvariantCulture) == Stretch.Uniform, "Video fallback crop affects normal album artwork");
        var poster = new DrawingGroup();
        using (var draw = poster.Open())
        {
            draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 400, 300));
            draw.DrawRectangle(Brushes.SeaGreen, null, new Rect(0, 37.5, 400, 225));
            draw.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(160, 37.5, 80, 225));
        }
        poster.Freeze(); var posterImage = new DrawingImage(poster); posterImage.Freeze();
        var zoom = (Transform)new UI.VideoArtworkZoom().Convert(["https://i.ytimg.com/vi/fixture/hqdefault.jpg", posterImage], typeof(Transform), null!, System.Globalization.CultureInfo.InvariantCulture);
        var square = new System.Windows.Controls.Border { Width = 280, Height = 280, ClipToBounds = true };
        square.Child = new System.Windows.Controls.Image { Source = posterImage, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = zoom };
        square.Measure(new Size(280, 280)); square.Arrange(new Rect(0, 0, 280, 280)); square.UpdateLayout();
        var rendered = new System.Windows.Media.Imaging.RenderTargetBitmap(280, 280, 96, 96, PixelFormats.Pbgra32); rendered.Render(square);
        byte[] pixel = new byte[4];
        foreach (int y in new[] { 3, 140, 276 })
        {
            rendered.CopyPixels(new Int32Rect(140, y, 1, 1), pixel, 4, 0);
            CheckInteraction(pixel[1] > 100, "The centered video poster still has black bands at " + y);
            CheckInteraction(pixel[0] > 200 && pixel[2] > 80, "The video poster shows an off-center portion of the source");
        }
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
        using (var output = File.Create(Path.Combine(directory, "video-poster-fill.png"))) encoder.Save(output);
        progressFrameTimer.Stop(); progressPlaying = false; browserMode = false;
        await File.WriteAllTextAsync(Path.Combine(directory, "progress-motion-check.txt"), "PASS local clock: movement between browser snapshots, one shared position for fill and thumb, snapshot continuity, immediate seek, paused state, drag ownership, reduced motion, centered crop only for YouTube thumbnail fallback. No remote playback.");
    }
}
