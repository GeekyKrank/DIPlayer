using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace DIPlayer;

public partial class MainWindow
{
    readonly DispatcherTimer progressFrameTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch progressFrameClock = Stopwatch.StartNew();
    bool progressFrameReady, progressPlaying;
    string progressTrack = "";
    double progressAnchor, progressDuration, progressCorrection;

    double PredictedYouTubePosition => Math.Clamp(progressAnchor + (progressPlaying ? Math.Min(2, progressFrameClock.Elapsed.TotalSeconds) + progressCorrection * Math.Exp(-progressFrameClock.Elapsed.TotalSeconds / .6) : 0), 0, Math.Max(0, progressDuration));
    static string ProgressKey(YouTubeState state) => state.VideoId ?? state.Title + "|" + state.Artist;
    double LyricsPlaybackPosition(YouTubeState? state) => state == null ? 0
        : browserMode && settings.MotionEnabled && ProgressKey(state) == progressTrack ? PredictedYouTubePosition
        : state.Position + (browserMode && !state.Paused && !state.Buffering ? youtubeLyricsClock.Elapsed.TotalSeconds : 0);
    void SeekProgressFrame(double position)
    {
        progressAnchor = Math.Clamp(position, 0, Math.Max(0, progressDuration)); progressCorrection = 0; progressFrameClock.Restart();
        if (youtubeState is { } state) youtubeState = state with { Position = position };
        youtubeLyricsClock.Restart();
    }
    void ObserveProgressFrame(YouTubeState state)
    {
        string key = ProgressKey(state);
        double predicted = PredictedYouTubePosition;
        bool continuous = key == progressTrack && progressPlaying && !state.Paused && !state.Buffering && Math.Abs(predicted - state.Position) < .4;
        progressCorrection = continuous ? predicted - state.Position : 0;
        progressAnchor = Math.Max(0, state.Position);
        progressTrack = key; progressDuration = Math.Max(0, state.Duration); progressPlaying = !state.Paused && !state.Buffering;
        progressFrameClock.Restart();
        if (!youtubeSeeking)
        {
            YouTubePosition.Maximum = Math.Max(1, state.Duration);
            YouTubePosition.Value = settings.MotionEnabled ? PredictedYouTubePosition : Math.Clamp(state.Position, 0, YouTubePosition.Maximum);
            YouTubeTime.Text = Format(YouTubePosition.Value) + " / " + Format(state.Duration);
        }
        EnsureProgressFrames();
    }
    void EnsureProgressFrames()
    {
        if (!progressFrameReady)
        {
            progressFrameTimer.Tick += (_, _) => AdvanceProgressFrame();
            progressFrameReady = true;
        }
        bool playing = browserMode ? progressPlaying && progressDuration > 0 : player.Playing && !player.Paused;
        if (IsVisible && WindowState != WindowState.Minimized && settings.MotionEnabled && playing) progressFrameTimer.Start();
        else progressFrameTimer.Stop();
    }
    void AdvanceProgressFrame()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || !settings.MotionEnabled) { progressFrameTimer.Stop(); return; }
        if (!browserMode)
        {
            if (!player.Playing || player.Paused) { progressFrameTimer.Stop(); return; }
            if (!dragging) { PositionSlider.Value = player.Position; TimeLabel.Text = Format(player.Position) + " / " + Format(player.Duration); }
            return;
        }
        if (!progressPlaying) { progressFrameTimer.Stop(); return; }
        if (!youtubeSeeking)
        {
            YouTubePosition.Value = PredictedYouTubePosition;
            YouTubeTime.Text = Format(YouTubePosition.Value) + " / " + Format(progressDuration);
        }
    }
}
