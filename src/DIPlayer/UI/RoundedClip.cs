using System.Windows;
using System.Windows.Media;

namespace DIPlayer.UI;

// Border.CornerRadius paints a rounded border, but does not clip its children.
public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedClip), new PropertyMetadata(0d, RadiusChanged));

    public static double GetRadius(DependencyObject target) => (double)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target, double value) => target.SetValue(RadiusProperty, value);

    static void RadiusChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.SizeChanged -= SizeChanged;
        if ((double)args.NewValue > 0) element.SizeChanged += SizeChanged;
        Update(element);
    }

    static void SizeChanged(object sender, SizeChangedEventArgs args) => Update((FrameworkElement)sender);

    static void Update(FrameworkElement element)
    {
        double radius = GetRadius(element);
        if (radius <= 0) { element.ClearValue(UIElement.ClipProperty); return; }
        var clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
        clip.Freeze();
        element.Clip = clip;
    }
}
