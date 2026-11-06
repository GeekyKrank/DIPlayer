using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DIPlayer.UI;

public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject value) => (bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value, bool enabled) => value.SetValue(EnabledProperty, enabled);
    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(Motion), new PropertyMetadata(false, Changed));
    public static bool GetReveal(DependencyObject value) => (bool)value.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject value, bool reveal) => value.SetValue(RevealProperty, reveal);

    /// <summary>Shared easing: a soft deceleration that never overshoots.</summary>
    public static readonly IEasingFunction Decelerate = Freeze(new QuinticEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Gentle = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Accelerate = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    static IEasingFunction Freeze(EasingFunctionBase easing) { easing.Freeze(); return easing; }

    public static bool Active(DependencyObject element) => GetEnabled(element) && SystemParameters.ClientAreaAnimation;

    static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.IsVisibleChanged -= VisibilityChanged;
        if ((bool)args.NewValue) element.IsVisibleChanged += VisibilityChanged;
    }
    static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        if (element.IsVisible) Reset(element);
        if (!element.IsVisible || !Active(element)) return;
        double target = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(220)) { EasingFunction = Gentle, FillBehavior = FillBehavior.Stop });
        if (element.RenderTransform != Transform.Identity && element.RenderTransform is not TranslateTransform) return;
        var shift = element.RenderTransform as TranslateTransform;
        if (shift == null || shift.IsFrozen) { shift = new TranslateTransform(); element.RenderTransform = shift; }
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = Decelerate, FillBehavior = FillBehavior.Stop });
    }

    sealed record ConcealState(DoubleAnimation Fade, bool HitTest);
    /// <summary>Fades an element out and collapses it; collapses immediately when motion is off.</summary>
    public static void Conceal(FrameworkElement element, Action? completed = null, int milliseconds = 170)
    {
        Reset(element);
        if (!element.IsVisible || !Active(element)) { element.Visibility = Visibility.Collapsed; completed?.Invoke(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Accelerate, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) =>
        {
            // A newer show/hide request replaced this fade.
            if (element.ReadLocalValue(ConcealingProperty) is not ConcealState state || !ReferenceEquals(state.Fade, fade)) return;
            Reset(element);
            element.Visibility = Visibility.Collapsed;
            completed?.Invoke();
        };
        element.SetValue(ConcealingProperty, new ConcealState(fade, element.IsHitTestVisible));
        element.IsHitTestVisible = false;
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
    /// <summary>Instantly cancels a pending <see cref="Conceal"/> and restores the element state.</summary>
    public static void Reset(FrameworkElement element)
    {
        if (element.ReadLocalValue(ConcealingProperty) is not ConcealState state) return;
        element.ClearValue(ConcealingProperty);
        element.IsHitTestVisible = state.HitTest;
        element.BeginAnimation(UIElement.OpacityProperty, null);
    }
    /// <summary>Turns a pending <see cref="Conceal"/> back into a short fade-in. Returns false when nothing was concealing.</summary>
    public static bool Restore(FrameworkElement element)
    {
        if (element.ReadLocalValue(ConcealingProperty) is not ConcealState state) return false;
        double current = element.Opacity;
        element.ClearValue(ConcealingProperty);
        element.IsHitTestVisible = state.HitTest;
        double target = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        element.BeginAnimation(UIElement.OpacityProperty, Active(element)
            ? new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180)) { EasingFunction = Gentle, FillBehavior = FillBehavior.Stop }
            : null);
        return true;
    }
    static readonly DependencyProperty ConcealingProperty = DependencyProperty.RegisterAttached("Concealing", typeof(object), typeof(Motion), new PropertyMetadata(null));
}
