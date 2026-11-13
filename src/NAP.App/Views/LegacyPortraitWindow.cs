using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAP.Core;

namespace NAP.App.Views;

/// <summary>Read-only browser of approved pre-NAP WebP assets; never touches SQLite or TeraBox.</summary>
internal sealed class LegacyPortraitWindow : Window
{
    private readonly string _assetsRoot;
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _details = new();
    private readonly Image _preview = new();
    private readonly TextBox _info = new();
    private readonly TextBox _prompt = new();
    private LegacyPortrait? _selected;
    private int _selectionGeneration;

    public LegacyPortraitWindow(string assetsRoot)
    {
        _assetsRoot = assetsRoot;
        Title = "NAP — Biblioteca histórica de retratos de Treskal";
        Width = 1250;
        Height = 830;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(5, 19, 26));
        Foreground = new SolidColorBrush(Color.FromRgb(234, 246, 248));

        var frame = new Grid { Margin = new Thickness(22) };
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = frame;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock
        {
            Text = "Retratos históricos de Treskal", FontSize = 24, FontWeight = FontWeights.SemiBold
        });
        _status.Text = "Leyendo el inventario existente, sin modificar archivos…";
        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(_status);
        Grid.SetRow(header, 0);
        frame.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        frame.Children.Add(body);

        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        left.Children.Add(new TextBlock { Text = "WebP originales del repositorio", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _list.DisplayMemberPath = nameof(LegacyPortrait.Label);
        _list.SelectionChanged += PortraitSelected;
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        left.Children.Add(_list);
        _list.SetValue(DockPanel.DockProperty, Dock.Bottom);
        var leftDock = new DockPanel();
        DockPanel.SetDock(left.Children[0], Dock.Top);
        // Move the two controls to a DockPanel so the list consumes remaining height.
        var label = left.Children[0];
        left.Children.RemoveAt(0);
        left.Children.Remove(_list);
        DockPanel.SetDock(label, Dock.Top);
        leftDock.Children.Add(label);
        leftDock.Children.Add(_list);
        Grid.SetColumn(leftDock, 0);
        body.Children.Add(leftDock);

        var right = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var top = new StackPanel();
        _details.Text = "Selecciona un retrato para ver sus datos y localizar su WebP.";
        _details.TextWrapping = TextWrapping.Wrap;
        _details.FontSize = 14;
        top.Children.Add(_details);
        Grid.SetRow(top, 0);
        right.Children.Add(top);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        var copy = new Button { Content = "Copiar ruta WebP", MinWidth = 140, Margin = new Thickness(0, 0, 10, 0) };
        copy.Click += (_, _) => { if (_selected is not null) Clipboard.SetText(_selected.WebpPath); };
        tools.Children.Add(copy);
        var folder = new Button { Content = "Abrir carpeta", MinWidth = 130 };
        folder.Click += (_, _) =>
        {
            if (_selected is null) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + _selected.WebpPath + "\"",
                UseShellExecute = true
            });
        };
        tools.Children.Add(folder);
        Grid.SetRow(tools, 1);
        right.Children.Add(tools);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel();
        _preview.Stretch = Stretch.Uniform;
        _preview.Height = 330;
        _preview.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(_preview);
        _info.IsReadOnly = true;
        _info.AcceptsReturn = true;
        _info.TextWrapping = TextWrapping.Wrap;
        _info.MinHeight = 150;
        _info.MaxHeight = 280;
        _info.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _prompt.IsReadOnly = true;
        _prompt.AcceptsReturn = true;
        _prompt.TextWrapping = TextWrapping.Wrap;
        _prompt.MinHeight = 150;
        _prompt.MaxHeight = 280;
        _prompt.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        content.Children.Add(new Expander { Header = "Información original", Content = _info, Margin = new Thickness(0, 10, 0, 0) });
        content.Children.Add(new Expander { Header = "Prompt original", Content = _prompt, Margin = new Thickness(0, 8, 0, 0) });
        scroll.Content = content;
        Grid.SetRow(scroll, 2);
        right.Children.Add(scroll);
        Loaded += LoadInventory;
    }

    private async void LoadInventory(object sender, RoutedEventArgs args)
    {
        try
        {
            var inventory = await Task.Run(() => LegacyPortraitReader.Scan(_assetsRoot));
            if (!IsLoaded) return;
            _list.ItemsSource = inventory.Portraits;
            _status.Text = $"{inventory.Portraits.Count} retratos históricos disponibles · lectura directa del repositorio · no se ha modificado SQLite ni TeraBox.";
            if (inventory.Issues.Count > 0)
                _status.Text += $" {inventory.Issues.Count} incidencias detectadas: " +
                    string.Join(" | ", inventory.Issues.Take(3));
            if (inventory.Portraits.Count > 0) _list.SelectedIndex = 0;
        }
        catch (Exception error)
        {
            _status.Text = "No se puede leer el repositorio seleccionado: " + error.Message;
        }
    }

    private async void PortraitSelected(object sender, SelectionChangedEventArgs args)
    {
        if (_list.SelectedItem is not LegacyPortrait value) return;
        _selected = value;
        var current = ++_selectionGeneration;
        _details.Text = $"{value.AssetId}\n{value.Culture} · {value.Location} · {value.Profession} · {value.Sex}" +
            $"\nWebP {value.Width}×{value.Height} · {value.WebpBytes:N0} bytes · SHA-256: {value.WebpSha256}" +
            $"\nRuta relativa: {value.RelativeDirectory}\nEstado: histórico aprobado; sin auditoría NAP inventada.";
        _preview.Source = null;
        _info.Text = File.ReadAllText(value.InfoPath);
        _prompt.Text = File.ReadAllText(value.PromptPath);
        try
        {
            var bytes = await Task.Run(() => LegacyPortraitReader.PreviewPng(value));
            if (current != _selectionGeneration || !IsLoaded) return;
            using var data = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = data;
            bitmap.EndInit();
            bitmap.Freeze();
            _preview.Source = bitmap;
        }
        catch (Exception error)
        {
            if (current == _selectionGeneration)
                _details.Text += "\nNo se pudo mostrar la vista previa: " + error.Message;
        }
    }
}
