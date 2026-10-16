using System.Windows;
using System.Windows.Controls;
using DIPlayer.Storage;
namespace DIPlayer;

public static class Dialogs
{
    static Window Window(Window owner, string title, StackPanel panel)
    {
        var w = new Window { Owner = owner, Title = title, Width = 480, MinWidth = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel, ResizeMode = ResizeMode.NoResize };
        w.SetResourceReference(System.Windows.FrameworkElement.StyleProperty, typeof(Window));
        return w;
    }
    public static string? Text(Window owner, string title, string label, string initial = "")
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var input = new System.Windows.Controls.TextBox { Text = initial, MaxLength = 2048 };
        panel.Children.Add(input);
        var ok = new System.Windows.Controls.Button { Content = "Сохранить", IsDefault = true, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(ok);
        var w = Window(owner, title, panel);
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return w.ShowDialog() == true ? input.Text : null;
    }
    public static PlaylistHeader? Pick(Window owner, List<PlaylistHeader> lists)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        var box = new System.Windows.Controls.ComboBox { ItemsSource = lists, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(box);
        var ok = new System.Windows.Controls.Button { Content = "Добавить", IsDefault = true };
        panel.Children.Add(ok);
        var w = Window(owner, "Добавить в плейлист", panel);
        ok.Click += (_, _) => w.DialogResult = true;
        return w.ShowDialog() == true ? box.SelectedItem as PlaylistHeader : null;
    }
    public static bool Configure(Window owner, Settings settings)
    {
        bool changed = false;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Сервер каталога (HTTPS)", Margin = new Thickness(0, 0, 0, 8) });
        var input = new System.Windows.Controls.TextBox { Text = settings.Server ?? "", MaxLength = 2048 };
        panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = "Для публичной сборки используется сервер с ключами SoundCloud. Пользователям вход не нужен.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var save = new System.Windows.Controls.Button { Content = "Подключить сервер" };
        panel.Children.Add(save);
        var local = new System.Windows.Controls.Button { Content = "Личный тест: импортировать файл ключей", Margin = new Thickness(3, 16, 3, 3) };
        panel.Children.Add(local);
        panel.Children.Add(new TextBlock { Text = "Ключи шифруются Windows DPAPI для текущего пользователя. Не передавайте файл или ключи другим людям.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var w = Window(owner, "DIPlayer · Подключение", panel);
        save.Click += (_, _) => { try { using var check = new DIPlayer.Core.RemoteCatalog(input.Text); settings.Server = input.Text.Trim(); settings.Save(); changed = true; w.Close(); } catch { System.Windows.MessageBox.Show(w, "Укажите корректный HTTPS-адрес сервера."); } };
        local.Click += (_, _) => { var open = new Microsoft.Win32.OpenFileDialog { Filter = "Файл ключей (*.txt;*.env)|*.txt;*.env|Все файлы|*.*" }; if (open.ShowDialog(w) != true) return; try { settings.ImportCredentials(open.FileName); changed = true; w.Close(); } catch { System.Windows.MessageBox.Show(w, "Не удалось импортировать файл. Нужны поля client id: … и client secret: …"); } };
        w.ShowDialog();
        return changed;
    }
}
