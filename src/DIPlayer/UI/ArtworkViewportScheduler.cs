using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DIPlayer.UI;

// One batched viewport pass per dispatcher. Registrations never retain removed images.
internal static class ArtworkViewportScheduler
{
    static readonly ConditionalWeakTable<Dispatcher, Monitor> monitors = new();
    static readonly List<WeakReference<Monitor>> all = [];
    static readonly object gate = new();

    static ArtworkViewportScheduler() => EventManager.RegisterClassHandler(typeof(ScrollViewer),
        ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((sender, _) => Request(((ScrollViewer)sender).Dispatcher)), true);

    static Monitor For(Dispatcher dispatcher) => monitors.GetValue(dispatcher, value =>
    {
        var monitor = new Monitor(value);
        lock (gate) all.Add(new(monitor));
        return monitor;
    });
    internal static int Register(Artwork image) => For(image.Dispatcher).Register(image);
    internal static void Unregister(Dispatcher dispatcher, int id)
    {
        if (monitors.TryGetValue(dispatcher, out var monitor)) monitor.Unregister(id);
    }
    internal static void Request(Dispatcher dispatcher)
    {
        if (monitors.TryGetValue(dispatcher, out var monitor)) monitor.Request();
    }
    internal static void RefreshAll()
    {
        lock (gate)
        {
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].TryGetTarget(out var monitor)) monitor.Request(); else all.RemoveAt(i);
        }
    }

    sealed class Monitor
    {
        readonly Dispatcher dispatcher;
        readonly DispatcherTimer timer;
        readonly Dictionary<int, WeakReference<Artwork>> images = [];
        readonly List<WeakReference<Window>> windows = [];
        int nextId;

        internal Monitor(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
            timer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => Refresh();
        }
        internal int Register(Artwork image)
        {
            int id = ++nextId;
            images[id] = new(image);
            if (Window.GetWindow(image) is Window window && !windows.Any(reference => reference.TryGetTarget(out var value) && ReferenceEquals(value, window)))
            {
                windows.Add(new(window));
                window.LayoutUpdated += LayoutChanged;
                window.StateChanged += LayoutChanged;
                window.IsVisibleChanged += VisibilityChanged;
                window.Closed += Closed;
            }
            Request(); return id;
        }
        internal void Unregister(int id)
        {
            images.Remove(id);
            if (images.Count == 0) timer.Stop();
        }
        void LayoutChanged(object? sender, EventArgs args) => Request();
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Request();
        void Closed(object? sender, EventArgs args)
        {
            if (sender is Window window)
            {
                window.LayoutUpdated -= LayoutChanged; window.StateChanged -= LayoutChanged;
                window.IsVisibleChanged -= VisibilityChanged; window.Closed -= Closed;
            }
            windows.RemoveAll(reference => !reference.TryGetTarget(out var value) || ReferenceEquals(value, sender));
            Request();
        }
        internal void Request()
        {
            if (dispatcher.HasShutdownStarted) return;
            if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(Request, DispatcherPriority.Background); return; }
            if (images.Count > 0 && !timer.IsEnabled) timer.Start();
        }
        void Refresh()
        {
            timer.Stop();
            foreach (var pair in images.ToArray())
                if (pair.Value.TryGetTarget(out var image)) image.RefreshViewport(); else images.Remove(pair.Key);
        }
    }
}
