using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIPlayer.UI;

/// <summary>
/// Frame-synchronised, eased scrolling for the mouse wheel and arrow buttons.
/// The rendering hook is attached only while at least one viewer is moving, so an
/// idle window does not render extra frames.
/// </summary>
public static class SmoothScroll
{
    sealed class Axis { public double Target, Position, Applied; public bool Running; }
    sealed class State { public readonly Axis Vertical = new(), Horizontal = new(); }

    static readonly ConditionalWeakTable<ScrollViewer, State> states = new();
    static readonly List<ScrollViewer> moving = [];
    static readonly Stopwatch clock = new();
    static bool hooked;
    // Exponential approach: ~95% of the remaining distance in ~190 ms.
    const double Response = 16;

    /// <summary>Handles wheel input for every pixel-based ScrollViewer below this element.</summary>
    public static readonly DependencyProperty WheelProperty = DependencyProperty.RegisterAttached(
        "Wheel", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, WheelChanged));
    public static bool GetWheel(DependencyObject element) => (bool)element.GetValue(WheelProperty);
    public static void SetWheel(DependencyObject element, bool value) => element.SetValue(WheelProperty, value);

    /// <summary>Marks a ScrollViewer that handles its own wheel input.</summary>
    public static readonly DependencyProperty ManualProperty = DependencyProperty.RegisterAttached(
        "Manual", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false));
    public static bool GetManual(DependencyObject element) => (bool)element.GetValue(ManualProperty);
    public static void SetManual(DependencyObject element, bool value) => element.SetValue(ManualProperty, value);

    static void WheelChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element) return;
        element.PreviewMouseWheel -= OnWheel;
        if ((bool)args.NewValue) element.PreviewMouseWheel += OnWheel;
    }

    public static bool Enabled(DependencyObject? element) =>
        element != null && Motion.GetEnabled(element) && SystemParameters.ClientAreaAnimation;

    public static double WheelDistance(ScrollViewer viewer) => SystemParameters.WheelScrollLines < 0
        ? viewer.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 32;

    static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    static bool PixelBased(ScrollViewer viewer) => !viewer.CanContentScroll ||
        viewer.TemplatedParent is ItemsControl items && VirtualizingPanel.GetScrollUnit(items) == ScrollUnit.Pixel;

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || !Enabled(sender as DependencyObject)) return;
        // The innermost viewer that can move vertically owns the wheel, exactly like
        // native WPF. Item-based and self-managed viewers keep their own behaviour.
        for (var node = e.OriginalSource as DependencyObject; node != null; node = Parent(node))
        {
            if (node is not ScrollViewer viewer || viewer.ScrollableHeight <= 0 || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
            if (GetManual(viewer) || !PixelBased(viewer)) return;
            e.Handled = true;
            VerticalBy(viewer, -e.Delta / 120.0 * WheelDistance(viewer));
            return;
        }
    }

    public static void VerticalBy(ScrollViewer viewer, double delta) => By(viewer, delta, true);
    public static void HorizontalBy(ScrollViewer viewer, double delta) => By(viewer, delta, false);

    static void By(ScrollViewer viewer, double delta, bool vertical)
    {
        var state = states.GetValue(viewer, _ => new State());
        var axis = vertical ? state.Vertical : state.Horizontal;
        double current = vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
        double limit = Math.Max(0, vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth);
        if (!axis.Running) axis.Position = axis.Target = axis.Applied = current;
        axis.Target = Math.Clamp(axis.Target + delta, 0, limit);
        if (!Enabled(viewer))
        {
            axis.Running = false;
            if (vertical) viewer.ScrollToVerticalOffset(axis.Target); else viewer.ScrollToHorizontalOffset(axis.Target);
            return;
        }
        axis.Running = true;
        if (!moving.Contains(viewer)) moving.Add(viewer);
        if (!hooked) { hooked = true; clock.Restart(); CompositionTarget.Rendering += Frame; }
    }

    /// <summary>Cancels any running animation, e.g. before a programmatic jump.</summary>
    public static void Stop(ScrollViewer? viewer)
    {
        if (viewer == null || !states.TryGetValue(viewer, out var state)) return;
        state.Vertical.Running = state.Horizontal.Running = false;
        moving.Remove(viewer);
    }

    static void Frame(object? sender, EventArgs e)
    {
        double elapsed = Math.Clamp(clock.Elapsed.TotalSeconds, 0, .05);
        clock.Restart();
        double blend = 1 - Math.Exp(-Response * elapsed);
        for (int i = moving.Count - 1; i >= 0; i--)
        {
            var viewer = moving[i];
            if (!states.TryGetValue(viewer, out var state) || !viewer.IsVisible)
            {
                if (state != null) state.Vertical.Running = state.Horizontal.Running = false;
                moving.RemoveAt(i); continue;
            }
            bool vertical = Step(viewer, state.Vertical, true, blend);
            bool horizontal = Step(viewer, state.Horizontal, false, blend);
            if (!vertical && !horizontal) moving.RemoveAt(i);
        }
        if (moving.Count == 0 && hooked) { hooked = false; CompositionTarget.Rendering -= Frame; clock.Reset(); }
    }

    static bool Step(ScrollViewer viewer, Axis axis, bool vertical, double blend)
    {
        if (!axis.Running) return false;
        double current = vertical ? viewer.VerticalOffset : viewer.HorizontalOffset;
        double limit = Math.Max(0, vertical ? viewer.ScrollableHeight : viewer.ScrollableWidth);
        // A scrollbar drag, keyboard command or navigation moved the viewer: yield to it.
        if (Math.Abs(current - Math.Min(axis.Applied, limit)) > 2) { axis.Running = false; return false; }
        axis.Target = Math.Min(axis.Target, limit);
        axis.Position += (axis.Target - axis.Position) * blend;
        if (Math.Abs(axis.Target - axis.Position) < .5) { axis.Position = axis.Target; axis.Running = false; }
        axis.Applied = axis.Position;
        if (vertical) viewer.ScrollToVerticalOffset(axis.Position); else viewer.ScrollToHorizontalOffset(axis.Position);
        return axis.Running;
    }
}
