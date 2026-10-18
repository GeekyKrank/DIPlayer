using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using DIPlayer.Storage;
namespace DIPlayer;

public partial class MainWindow
{
    void LoadBrandLogo()
    {
        // Keep the personal-use font local. Render its actual glyphs so the
        // title cannot silently switch to a system fallback during formatting.
        string[] folders = [Path.Combine(LibraryStore.DataDirectory, "fonts"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "DIPlayer", "fonts")];
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(folder, "DirtyStains-DEMO.otf");
            if (!File.Exists(path)) continue;
            try
            {
                var face = new GlyphTypeface(new Uri(path));
                const double size = 14;
                var indices = new List<ushort>();
                var advances = new List<double>();
                foreach (char character in "DIPlayer")
                {
                    if (!face.CharacterToGlyphMap.TryGetValue(character, out ushort glyph))
                        glyph = face.CharacterToGlyphMap[char.ToUpperInvariant(character)];
                    indices.Add(glyph); advances.Add(face.AdvanceWidths[glyph] * size);
                }
                var run = new GlyphRun(face, 0, false, size, indices, new Point(0, face.Baseline * size), advances,
                    null, null, null, null, null, XmlLanguage.GetLanguage("en-US"));
                var drawing = new DrawingImage(new GlyphRunDrawing(Brushes.WhiteSmoke, run));
                drawing.Freeze();
                BrandTitle.FontFamily = new FontFamily(new Uri(path).AbsoluteUri + "#Dirty Stains");
                BrandLogo.Source = drawing; BrandLogo.Width = Math.Ceiling(advances.Sum());
                BrandLogo.Visibility = Visibility.Visible; BrandTitle.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException or KeyNotFoundException) { }
        }
    }
}
