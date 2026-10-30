using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIPlayer;

public partial class MainWindow
{
    sealed record LyricsChoice(string Id, string Label, YouTubeLyricsLine[] Lines, string Source);
    readonly List<LyricsChoice> lyricsChoices = [];
    string lyricsSelectedId = "";
    bool lyricsManualVersion, lyricsSeeking, lyricsVolumeSync, visualEnergyReading, visualEnergyInstalled;
    int visualEnergyGeneration = -1, visualEnergyRevision;
    double lyricsOffset, visualEnergy;
    DateTime visualEnergyReadAt;

    void OpenTrackLyrics(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!youtubeLyricsOpen) ToggleYouTubeLyrics(sender, new RoutedEventArgs());
    }
    async void OpenLyricsArtist(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StopYouTubeLyrics();
        if (browserMode) await Guard(OpenCurrentYouTubeArtistPage);
        else if (current is { } track) { SetView("search"); SearchBox.Text = track.Artist; }
    }
    void LyricsPlaybackControl(object sender, RoutedEventArgs e)
    {
        if (browserMode) { YouTubeControl(sender, e); return; }
        switch ((sender as Button)?.Tag as string)
        {
            case "play": ToggleClick(sender, e); break;
            case "previous": PreviousTrack(sender, e); break;
            case "next": NextTrack(sender, e); break;
            case "shuffle": ToggleShuffle(sender, e); break;
            case "repeat": CycleRepeat(sender, e); break;
        }
        SyncTrackScreenPlayback();
    }
    void SyncTrackScreenPlayback()
    {
        if (!youtubeLyricsOpen) return;
        var state = EffectiveLyricsState();
        double duration = state?.Duration ?? 0;
        double position = Math.Clamp((state?.Position ?? 0) + (browserMode && state?.Paused == false ? youtubeLyricsClock.Elapsed.TotalSeconds : 0), 0, Math.Max(0, duration));
        LyricsPosition.IsEnabled = duration > 0 && state?.IsAd != true;
        if (!lyricsSeeking)
        {
            LyricsPosition.Maximum = Math.Max(1, duration);
            LyricsPosition.Value = position;
            LyricsElapsed.Text = Format(position);
        }
        LyricsDuration.Text = Format(duration);
        LyricsPlay.Content = LyricsCompactPlay.Content = state?.Paused == false ? UI.Symbols.Pause : UI.Symbols.Play;
        LyricsRepeat.Content = settings.RepeatMode == 2 ? UI.Symbols.RepeatOne : UI.Symbols.Repeat;
        LyricsRepeat.Foreground = settings.RepeatMode == 0 ? Brushes.Gray : Brushes.White;
        LyricsShuffle.Foreground = settings.Shuffle ? Brushes.White : Brushes.Gray;
        lyricsVolumeSync = true;
        try { LyricsVolume.Value = settings.Volume; }
        finally { lyricsVolumeSync = false; }
        if (settings.MotionEnabled && settings.SoftBackdrop)
        {
            double energy = state?.Paused == false ? browserMode ? visualEnergy : player.VisualEnergy : 0;
            LyricsPulse.Opacity += (Math.Clamp(energy, 0, 1) * .055 - LyricsPulse.Opacity) * .35;
            if (browserMode && !visualEnergyReading && DateTime.UtcNow - visualEnergyReadAt > TimeSpan.FromMilliseconds(250))
                _ = RefreshVisualEnergy();
        }
        else LyricsPulse.Opacity = 0;
    }
    void LyricsVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lyricsVolumeSync || !youtubeLyricsOpen) return;
        if (browserMode) YouTubeVolume.Value = e.NewValue;
        else VolumeSlider.Value = e.NewValue;
    }
    void UpdateLyricsSeek(double x)
    {
        double fraction = Math.Clamp((x - 9) / Math.Max(1, LyricsPosition.ActualWidth - 18), 0, 1);
        LyricsPosition.Value = fraction * LyricsPosition.Maximum;
        LyricsElapsed.Text = Format(LyricsPosition.Value);
    }
    void LyricsSeekDown(object sender, MouseButtonEventArgs e)
    {
        if (!LyricsPosition.IsEnabled) return;
        e.Handled = true; lyricsSeeking = true; LyricsPosition.Focus(); LyricsPosition.CaptureMouse(); UpdateLyricsSeek(e.GetPosition(LyricsPosition).X);
    }
    void LyricsSeekMove(object sender, MouseEventArgs e)
    { if (lyricsSeeking && LyricsPosition.IsMouseCaptured) { e.Handled = true; UpdateLyricsSeek(e.GetPosition(LyricsPosition).X); } }
    void LyricsSeekLost(object sender, MouseEventArgs e) => lyricsSeeking = false;
    async void LyricsSeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!lyricsSeeking) return;
        e.Handled = true; UpdateLyricsSeek(e.GetPosition(LyricsPosition).X);
        double target = LyricsPosition.Value; lyricsSeeking = false; LyricsPosition.ReleaseMouseCapture();
        await Guard(() => SeekToLyric(target + lyricsOffset, CurrentLyricsTrack()));
    }
    async void TrackSeekKey(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled || e.Key is not (Key.Left or Key.Right or Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)) return;
        e.Handled = true;
        double target = e.Key switch
        {
            Key.Home => 0, Key.End => slider.Maximum,
            Key.Left or Key.Down => slider.Value - 5,
            Key.Right or Key.Up => slider.Value + 5,
            Key.PageDown => slider.Value - 15, _ => slider.Value + 15
        };
        target = Math.Clamp(target, 0, slider.Maximum);
        await Guard(() => SeekToLyric(target + lyricsOffset, CurrentLyricsTrack()));
        if (!browserMode) RefreshPosition();
        SyncTrackScreenPlayback();
    }
    void ResetLyricsChoices()
    {
        lyricsChoices.Clear(); lyricsSelectedId = ""; lyricsManualVersion = false;
        settings.LyricsOffsets ??= [];
        lyricsOffset = settings.LyricsOffsets.TryGetValue(CurrentLyricsTrack(), out double value) && double.IsFinite(value) ? Math.Clamp(value, -30, 30) : 0;
        SyncLyricsVersionControls();
    }
    void SyncLyricsVersionControls()
    {
        int index = lyricsChoices.FindIndex(choice => choice.Id == lyricsSelectedId);
        LyricsVariantLabel.Text = index < 0 ? "Lyrics" : lyricsChoices[index].Label + (lyricsChoices.Count > 1 ? $"  {index + 1}/{lyricsChoices.Count}" : "");
        LyricsVariantLabel.ToolTip = index < 0 ? "Available synchronized lyrics versions" : lyricsChoices[index].Source;
        LyricsPreviousVersion.IsEnabled = LyricsNextVersion.IsEnabled = lyricsChoices.Count > 1;
        bool timed = index >= 0 && lyricsChoices[index].Lines.Length > 0 && lyricsChoices[index].Lines.All(line => line.Start.HasValue);
        LyricsOffsetMinus.IsEnabled = LyricsOffsetPlus.IsEnabled = timed;
        LyricsOffsetLabel.IsEnabled = timed || lyricsOffset != 0;
        LyricsOffsetLabel.Content = lyricsOffset.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "s";
    }
    void ShowSelectedLyrics(string? message = null)
    {
        var selected = lyricsChoices.FirstOrDefault(choice => choice.Id == lyricsSelectedId);
        if (selected == null || !lyricsManualVersion)
        {
            selected = lyricsChoices.FirstOrDefault(choice => choice.Id == "youtube" && choice.Lines.Any(line => line.Start.HasValue || line.Active))
                ?? lyricsChoices.FirstOrDefault(choice => choice.Id != "youtube") ?? lyricsChoices.FirstOrDefault();
            lyricsSelectedId = selected?.Id ?? "";
        }
        SyncLyricsVersionControls();
        RenderLyrics(selected?.Lines ?? [], selected?.Source ?? "", message);
    }
    void CycleLyricsVersion(object sender, RoutedEventArgs e)
    {
        if (lyricsChoices.Count < 2) return;
        int step = (sender as Button)?.Tag as string == "-1" ? -1 : 1;
        int index = Math.Max(0, lyricsChoices.FindIndex(choice => choice.Id == lyricsSelectedId));
        lyricsSelectedId = lyricsChoices[(index + step + lyricsChoices.Count) % lyricsChoices.Count].Id;
        lyricsManualVersion = true; youtubeLyricsFollowing = true; youtubeActiveLyric = -2; ShowSelectedLyrics();
    }
    void ChangeLyricsOffset(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse((sender as Button)?.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double step)) return;
        lyricsOffset = step == 0 ? 0 : Math.Round(Math.Clamp(lyricsOffset + step, -30, 30), 1);
        string track = CurrentLyricsTrack();
        settings.LyricsOffsets ??= [];
        if (lyricsOffset == 0) settings.LyricsOffsets.Remove(track);
        else
        {
            if (settings.LyricsOffsets.Count >= 64 && !settings.LyricsOffsets.ContainsKey(track)) settings.LyricsOffsets.Remove(settings.LyricsOffsets.Keys.First());
            settings.LyricsOffsets[track] = lyricsOffset;
        }
        SyncLyricsVersionControls(); youtubeActiveLyric = -2; ApplyLyricsHighlight();
        if (DiagnosticLibraryDirectory() == null) _ = Guard(() => { settings.Save(); return Task.CompletedTask; });
    }
    void LyricsScreenVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncTrackBackdrop();
    void SyncTrackBackdrop()
    {
        if (LyricsBackdrop == null) return;
        bool active = youtubeLyricsOpen && YouTubeLyricsOverlay.IsVisible && WindowState != WindowState.Minimized;
        if (!active) youtubeLyricsTimer.Stop();
        else if (youtubeLyricsTimerReady) youtubeLyricsTimer.Start();
        bool animate = active && settings.MotionEnabled && settings.SoftBackdrop;
        UI.CoverMotion.Start(LyricsBackdrop, animate);
        if (!animate) { LyricsPulse.Opacity = 0; StopVisualEnergy(); }
    }
    async Task RefreshVisualEnergy()
    {
        if (musicBrowser?.CoreWebView2 is not { } core || !youtubeLyricsOpen || !YouTubeLyricsOverlay.IsVisible || !IsVisible || WindowState == WindowState.Minimized) return;
        visualEnergyReading = true; visualEnergyReadAt = DateTime.UtcNow;
        int generation = browserGeneration, revision = visualEnergyRevision;
        try
        {
            string script = !visualEnergyInstalled || visualEnergyGeneration != generation ? YouTubeVisualEnergyBridge.Install : YouTubeVisualEnergyBridge.Read;
            string result = await core.ExecuteScriptAsync(script);
            if (youtubeLyricsOpen && generation == browserGeneration && revision == visualEnergyRevision && YouTubeLyricsOverlay.IsVisible && WindowState != WindowState.Minimized)
            {
                visualEnergyInstalled = result != "null"; visualEnergyGeneration = generation;
                visualEnergy = double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double energy) && double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { visualEnergy = 0; }
        finally { visualEnergyReading = false; }
    }
    void StopVisualEnergy()
    {
        bool hadWork = visualEnergyInstalled || visualEnergyReading;
        visualEnergyRevision++;
        visualEnergy = 0; visualEnergyInstalled = false;
        if (hadWork && musicBrowser?.CoreWebView2 is { } core) _ = Guard(async () => { await core.ExecuteScriptAsync(YouTubeVisualEnergyBridge.Stop); });
    }
}
