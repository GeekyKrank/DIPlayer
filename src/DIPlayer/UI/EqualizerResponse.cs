using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using DIPlayer.Audio;

namespace DIPlayer.UI;

/// <summary>A cached frequency-response drawing; no timer or audio visualization work.</summary>
public sealed class EqualizerResponse : FrameworkElement
{
    static readonly Brush background = FrozenBrush(0x12, 0x12, 0x12);
    static readonly Brush faint = FrozenBrush(0x33, 0x33, 0x33);
    static readonly Brush labels = FrozenBrush(0x88, 0x88, 0x88);
    static readonly Pen gridPen = FrozenPen(faint, .7);
    static readonly Pen curvePen = FrozenPen(FrozenBrush(0xEB, 0xEB, 0xEB), 1.8);
    static readonly Pen disabledPen = FrozenPen(FrozenBrush(0x70, 0x70, 0x70), 1.4);
    double[] gains = new double[EqualizerSampleProvider.BandCount];
    (double Frequency, double Decibels)[] response = EqualizerMath.Curve(new double[6]);
    bool enabled, headroom;
    StreamGeometry? curve;

    public double HeadroomDecibels { get; private set; }

    public EqualizerResponse()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        AutomationProperties.SetName(this, "Equalizer frequency response");
    }

    public void Configure(double[] values, bool isEnabled, bool automaticHeadroom)
    {
        var normalized = EqualizerSampleProvider.NormalizeGains(values);
        if (enabled == isEnabled && headroom == automaticHeadroom && gains.SequenceEqual(normalized) && curve != null) return;
        gains = normalized; enabled = isEnabled; headroom = automaticHeadroom;
        response = EqualizerMath.Curve(enabled ? gains : new double[6]);
        HeadroomDecibels = enabled && headroom ? EqualizerMath.HeadroomDb(gains) : 0;
        Rebuild();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
        InvalidateVisual();
    }

    void Rebuild()
    {
        curve = null;
        if (ActualWidth <= 30 || ActualHeight <= 30) return;
        double width = ActualWidth - 28, height = ActualHeight - 29;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            for (int point = 0; point < response.Length; point++)
            {
                var position = new Point(14 + width * point / (response.Length - 1),
                    9 + height * (.5 - Math.Clamp(response[point].Decibels + HeadroomDecibels, -24, 24) / 48));
                if (point == 0) drawing.BeginFigure(position, false, false);
                else drawing.LineTo(position, true, false);
            }
        }
        geometry.Freeze();curve = geometry;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth <= 30 || ActualHeight <= 30) return;
        drawing.DrawRoundedRectangle(background, null, new Rect(RenderSize), 8, 8);
        double width = ActualWidth - 28, height = ActualHeight - 29;
        foreach (int decibels in new[] { -12, 0, 12 })
        {
            double y = 9 + height * (.5 - decibels / 48d);
            drawing.DrawLine(gridPen, new Point(14, y), new Point(ActualWidth - 14, y));
        }
        foreach (var (frequency, label) in new[] { (60d, "60"), (1000d, "1k"), (10000d, "10k") })
        {
            double x = 14 + width * Math.Log(frequency / 20) / Math.Log(1000);
            drawing.DrawLine(gridPen, new Point(x, 9), new Point(x, 9 + height));
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                9, labels, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawing.DrawText(text, new Point(x - text.Width / 2, ActualHeight - 16));
        }
        if (curve == null) Rebuild();
        if (curve != null) drawing.DrawGeometry(null, enabled ? curvePen : disabledPen, curve);
    }

    static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));brush.Freeze();return brush;
    }
    static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();return pen;
    }
}
