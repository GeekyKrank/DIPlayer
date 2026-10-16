using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DIPlayer;

public partial class MainWindow
{
    string? pressedTrackId;
    Point trackPressPoint;
    void TrackPointerDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        pressedTrackId = RowAt(e.OriginalSource)?.Track.Id;
        trackPressPoint = e.GetPosition(Tracks);
    }
    async void TrackPointerUp(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        string? pressed = pressedTrackId;
        pressedTrackId = null;
        var point = e.GetPosition(Tracks);
        if (e.ClickCount > 1 || pressed == null || RowAt(e.OriginalSource) is not { } row || row.Track.Id != pressed ||
            Math.Abs(point.X - trackPressPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(point.Y - trackPressPoint.Y) > SystemParameters.MinimumVerticalDragDistance) return;
        Tracks.SelectedItem = row;
        await Guard(() => PlayRow(row));
    }
    DisplayRow? RowAt(object source)
    {
        if (source is not DependencyObject element) return null;
        for (var node = element; node != null && node is not ListBoxItem; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase) return null;
        return ItemsControl.ContainerFromElement(Tracks, element) is ListBoxItem item ? item.Content as DisplayRow : null;
    }
    void TrackMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            Tracks.SelectedItem = item.Content;
            item.ContextMenu = BuildTrackMenu(item);
        }
    }
    ContextMenu BuildTrackMenu(ListBoxItem item)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("TrackContextMenu"), FontFamily = FontFamily, FontWeight = FontWeight };
        var actions = new List<(string, RoutedEventHandler)> { ("Add to queue", EnqueueSelected), ("Add to playlist", AddToPlaylist) };
        if (view is "library" or "queue")
        {
            actions.Add((view == "library" ? "Remove from playlist" : "Remove from queue", RemoveSelected));
            actions.Add(("Move up", MoveUp));
            actions.Add(("Move down", MoveDown));
        }
        foreach (var (name, action) in actions)
        {
            var entry = new MenuItem { Header = name, Style = (Style)FindResource("TrackContextMenuItem") };
            entry.Click += (s, e) => { Tracks.SelectedItem = item.Content; action(s, e); };
            menu.Items.Add(entry);
        }
        return menu;
    }
    void OpenTrackMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ItemsControl.ContainerFromElement(Tracks, button) is not ListBoxItem item) return;
        Tracks.SelectedItem = item.Content;
        item.ContextMenu = BuildTrackMenu(item);
        item.ContextMenu.PlacementTarget = button;
        item.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
    async void TrackKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Selected is not { } row) return;
        e.Handled = true;
        await Guard(() => PlayRow(row));
    }

    sealed record ViewSnapshot(string View, int Page, string? Playlist, string? Next, string Heading, DisplayRow[] Rows, int Selected, bool Overview);
    ViewSnapshot? queueReturnView;
    ViewSnapshot CaptureView() => new(view, page, playlistId, next, Heading.Text, view == "search" ? rows.ToArray() : [], Tracks.SelectedIndex, libraryOverview);
    void ToggleQueue(object s, RoutedEventArgs e)
    {
        if (view != "queue") { QueueView(s, e); return; }
        var previous = queueReturnView;
        queueReturnView = null;
        if (previous == null) { SetView("home"); return; }
        SetView(previous.View);
        page = previous.Page;
        if (view == "search")
        {
            Heading.Text = previous.Heading;
            ShowRows(previous.Rows);
            next = previous.Next;
            Paging();
        }
        else if (view == "library")
        {
            RefreshPlaylists(previous.Playlist);
            if (previous.Overview) ShowLibraryOverview(); else ShowLibrary();
        }
        if (view is "search" or "library") Tracks.SelectedIndex = previous.Selected;
    }
}
