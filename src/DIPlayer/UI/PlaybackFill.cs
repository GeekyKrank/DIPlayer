using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DIPlayer.UI;

/// <summary>The elapsed portion of the player is a quiet surface behind its controls.</summary>
public sealed class PlaybackFill : FrameworkElement
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(PlaybackFill), new PropertyMetadata(0d, Changed));
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(nameof(Duration), typeof(double), typeof(PlaybackFill), new PropertyMetadata(1d, Changed));
    static readonly DependencyProperty FractionProperty = DependencyProperty.Register("Fraction", typeof(double), typeof(PlaybackFill), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    static readonly Brush fill = CreateFill();
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => (double)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double VisibleFraction => (double)GetValue(FractionProperty);
    static Brush CreateFill() { var brush = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)); brush.Freeze(); return brush; }
    static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var view = (PlaybackFill)target;
        double fraction = double.IsFinite(view.Position) && double.IsFinite(view.Duration) && view.Duration > 0 ? Math.Clamp(view.Position / view.Duration, 0, 1) : 0;
        double before = view.VisibleFraction;
        view.BeginAnimation(FractionProperty, null);
        view.SetValue(FractionProperty, fraction);
        if (view.IsVisible && Motion.GetEnabled(view) && Math.Abs(fraction - before) < .035)
            view.BeginAnimation(FractionProperty, new DoubleAnimation(before, fraction, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
    }
    protected override void OnRender(DrawingContext context) => context.DrawRectangle(fill, null, new Rect(0, 0, ActualWidth * VisibleFraction, ActualHeight));
}
