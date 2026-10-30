using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DIPlayer.Core;

namespace DIPlayer;

public partial class MiniPlayer : Window
{
    readonly Func<int, Task> command;
    readonly Action restore;
    readonly Action sync;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    public MiniPlayer(Func<int, Task> command, Action restore, Action sync)
    {
        this.command = command; this.restore = restore; this.sync = sync;
        InitializeComponent();
        timer.Tick += (_, _) => sync();
        SourceInitialized += (_, _) => { int rounded = 2; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref rounded, sizeof(int)); };
        Closed += (_, _) => timer.Stop();
    }
    public void ShowAbove(Window main)
    {
        if (IsVisible) { sync(); return; }
        // Create the source before converting monitor pixels to this window's DIP units.
        new WindowInteropHelper(this).EnsureHandle();
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var area = SystemParameters.WorkArea;
        if (GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(main).Handle, 2), ref info))
        {
            var transform = PresentationSource.FromVisual(main)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var first = transform.Transform(new Point(info.Work.Left, info.Work.Top));
            var last = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
            area = new System.Windows.Rect(first, last);
        }
        Width = Math.Min(400, area.Width - 16);
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 16;
        sync(); Show();
    }
    public void Update(Track? track, bool playing, bool previous, bool next)
    {
        if (!IsVisible) return;
        TrackTitle.Text = track?.Title ?? "DIPlayer";
        TrackArtist.Text = track?.Artist ?? "Choose a track";
        if (Cover.Url != track?.Artwork) Cover.Url = track?.Artwork;
        ToggleButton.Content = playing ? UI.Symbols.Pause : UI.Symbols.Play;
        PreviousButton.IsEnabled = previous;
        NextButton.IsEnabled = next;
        PreviousButton.ToolTip = previous ? "Previous" : "Start of queue";
        NextButton.ToolTip = next ? "Next" : "End of queue";
    }
    void MiniVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (timer == null) return;
        if (IsVisible) { sync(); timer.Start(); } else timer.Stop();
    }
    async void PreviousClick(object sender, RoutedEventArgs e) { await command(-1); sync(); }
    async void NextClick(object sender, RoutedEventArgs e) { await command(1); sync(); }
    async void ToggleClick(object sender, RoutedEventArgs e) { await command(0); sync(); }
    void HideClick(object sender, RoutedEventArgs e) => Hide();
    void RestoreClick(object sender, RoutedEventArgs e) { e.Handled = true; restore(); }
    void MiniPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element)
            for (var node = element; node != null && node != this; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is System.Windows.Controls.Primitives.ButtonBase) return;
        if (e.ClickCount == 2) { restore(); e.Handled = true; }
    }
}
