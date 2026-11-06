using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DIPlayer.UI;

public sealed class VideoArtworkStretch : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("i.ytimg.com", StringComparison.OrdinalIgnoreCase)
            ? Stretch.UniformToFill : Stretch.Uniform;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class VideoArtworkZoom : IMultiValueConverter
{
    static readonly Transform insetThumbnail = CreateZoom();
    static Transform CreateZoom() { var zoom = new ScaleTransform(4d / 3, 4d / 3); zoom.Freeze(); return zoom; }
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // YouTube's 4:3 HQ/SD posters can contain a 16:9 frame with baked-in
        // black bands. Zoom that central frame as well as filling its container.
        return values.Length > 1 && values[0] is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.Equals("i.ytimg.com", StringComparison.OrdinalIgnoreCase)
            && values[1] is ImageSource { Width: > 0, Height: > 0 } source && Math.Abs(source.Width / source.Height - 4d / 3) < .025
            ? insetThumbnail : Transform.Identity;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
