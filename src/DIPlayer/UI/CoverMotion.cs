using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DIPlayer.UI;

/// <summary>Compositor-only movement over a cached, low-resolution cover.</summary>
public static class CoverMotion
{
    public static void Start(FrameworkElement image, bool enabled)
    {
        Stop(image);
        if (!enabled) return;
        var scale = new ScaleTransform(1.35, 1.35);
        var rotate = new RotateTransform(-4);
        var translate = new TranslateTransform();
        var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(rotate); group.Children.Add(translate);
        image.RenderTransformOrigin = new Point(.5, .5); image.RenderTransform = group;
        void Animate(Animatable node, DependencyProperty property, double from, double to, int seconds) => node.BeginAnimation(property,
            new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Animate(translate, TranslateTransform.XProperty, -35, 35, 19);
        Animate(translate, TranslateTransform.YProperty, 22, -22, 23);
        Animate(rotate, RotateTransform.AngleProperty, -4, 4, 31);
    }
    public static void Stop(FrameworkElement image) => image.RenderTransform = Transform.Identity;
}
