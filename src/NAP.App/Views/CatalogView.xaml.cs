using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App.Views;

public partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();

    private async void ImportHistoricalPortraits(object sender, RoutedEventArgs e)
    {
        var explorer = (Content as FrameworkElement)?.DataContext as ExplorerViewModel;
        var context = explorer?.Context;
        if (context is null)
        {
            MessageBox.Show("Selecciona primero el universo Nimroel y configura sus raíces.", "NAP — Históricos");
            return;
        }
        var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GitHub", "Videojuego_Nimroel", "Worldbuilding", "Direccion artistica", "Assets");
        var chooser = new OpenFolderDialog
        {
            Title = "Importar WebP históricos de Nimroel al catálogo integrado",
            InitialDirectory = Directory.Exists(guess) ? guess :
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };
        if (chooser.ShowDialog() != true) return;
        var button = sender as Button;
        try
        {
            if (button is not null) button.IsEnabled = false;
            var count = await Task.Run(() => new LegacyPortraitIndex(context).Import(chooser.FolderName));
            await explorer!.RefreshAllAsync();
            MessageBox.Show($"{count} retratos históricos incorporados al catálogo de NAP.\n\n" +
                "Los WebP originales no se han modificado. La procedencia histórica es distinta de una publicación auditada por NAP.\n" +
                "Se almacenan en un índice SQLite de compatibilidad; aparecen junto a las publicaciones nuevas.",
                "NAP — Importación terminada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show("No se completó la importación:\n" + error.Message,
                "NAP — Importación histórica", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (button is not null) button.IsEnabled = true; }
    }
}
