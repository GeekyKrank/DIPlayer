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
    static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.IsVisibleChanged -= VisibilityChanged;
        if ((bool)args.NewValue) element.IsVisibleChanged += VisibilityChanged;
    }
    static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (FrameworkElement)sender;
        if (!element.IsVisible || !GetEnabled(element) || !SystemParameters.ClientAreaAnimation) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        if (element.RenderTransform != Transform.Identity && element.RenderTransform is not TranslateTransform) return;
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
