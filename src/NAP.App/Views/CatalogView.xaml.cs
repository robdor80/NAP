using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NAP.App.Views;

public partial class CatalogView : UserControl
{
    public CatalogView() => InitializeComponent();

    private void OpenHistoricalPortraits(object sender, RoutedEventArgs e)
    {
        var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GitHub", "Videojuego_Nimroel", "Worldbuilding", "Direccion artistica", "Assets");
        var chooser = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta Assets de Videojuego_Nimroel",
            InitialDirectory = Directory.Exists(guess)
                ? guess : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };
        if (chooser.ShowDialog() != true) return;
        var window = new LegacyPortraitWindow(chooser.FolderName)
        {
            Owner = Window.GetWindow(this)
        };
        window.Show();
    }
}
