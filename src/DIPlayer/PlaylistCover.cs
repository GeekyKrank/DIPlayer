using System.Windows;
using System.Windows.Media.Imaging;
using DIPlayer.Core;

namespace DIPlayer;

public partial class MainWindow
{
    byte[]? actionCoverBytes;
    bool actionCoverChanged;
    int coverLoadGeneration;

    static BitmapImage DecodeCover(byte[] bytes, int size)
    {
        using var input = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new CatalogException("The image is too large. Maximum: 40 megapixels.");
        input.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = size;
        else bitmap.DecodePixelHeight = size;
        bitmap.StreamSource = input;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
    static byte[] PrepareCover(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is 0 or > 20 * 1024 * 1024) throw new CatalogException("Choose an image up to 20 MB.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        var image = DecodeCover(bytes, 512);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
    async void ChoosePlaylistCover(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Playlist cover", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        string? target = actionPlaylistId;
        int generation = ++coverLoadGeneration;
        ChooseCoverButton.IsEnabled = false;
        ActionConfirm.IsEnabled = false;
        try
        {
            var bytes = await Task.Run(() => PrepareCover(dialog.FileName));
            if (generation != coverLoadGeneration || playlistAction != PlaylistAction.Rename || actionPlaylistId != target) return;
            actionCoverBytes = bytes;
            actionCoverChanged = true;
            ActionCoverPreview.Source = DecodeCover(bytes, 128);
        }
        catch (Exception error) { if (actionPlaylistId == target) ShowActionError(error is CatalogException ? error.Message : "Could not open the image. Try JPG or PNG."); }
        finally { ChooseCoverButton.IsEnabled = true; ActionConfirm.IsEnabled = true; }
    }
    void ResetPlaylistCover(object sender, RoutedEventArgs e)
    {
        coverLoadGeneration++;
        actionCoverBytes = null;
        actionCoverChanged = true;
        ActionCoverPreview.Source = null;
    }
}
