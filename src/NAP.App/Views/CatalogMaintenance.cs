using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;
namespace NAP.App.Views;
internal static class CatalogMaintenance
{
    public static async Task ImportHistoricalMasters(ExplorerViewModel? explorer, Button? button)
    {
        var context = explorer?.Context;
        if (context is null || context.Id.Value != "nimroel")
        {
            MessageBox.Show("Configura Nimroel y sus raíces antes de vincular maestros.", "NAP — Maestros");
            return;
        }
        var bundledManifest = Path.Combine(AppContext.BaseDirectory,
            "NIMROEL_TRESKAL_39_MAESTROS_LOCAL_20261113.json");
        var evidence = new OpenFileDialog
        {
            Title = "Selecciona el expediente de los 39 PNG maestros",
            Filter = "Evidencia de maestros (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = File.Exists(bundledManifest) ? AppContext.BaseDirectory :
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = File.Exists(bundledManifest) ? Path.GetFileName(bundledManifest) : ""
        };
        if (evidence.ShowDialog() != true) return;
        var suggested = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Videojuego Nimroel - ASSETS MAESTROS PNG");
        var originals = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta descargada con los 39 PNG originales (solo lectura)",
            InitialDirectory = Directory.Exists(suggested) ? suggested :
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };
        if (originals.ShowDialog() != true) return;
        try
        {
            if (button is not null) button.IsEnabled = false;
            var count = await Task.Run(() => new LegacyMasterEvidenceIndex(context)
                .Import(evidence.FileName, originals.FolderName));
            await explorer!.RefreshAllAsync();
            MessageBox.Show($"{count} evidencias de PNG históricos incorporadas a NAP.\n\n" +
                "14 coincidencias con SHA histórico; 1 coincidencia con PNG de Git histórico documentada; " +
                "24 líneas base nuevas. Los archivos originales y AssetCatalog.db no se han modificado.\n" +
                "TeraBox remoto NO ha sido verificado por API.",
                "NAP — Maestros históricos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show("No se completó la vinculación:\n" + error.Message,
                "NAP — Maestros históricos", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (button is not null) button.IsEnabled = true; }
    }

    public static async Task ImportHistoricalPortraits(ExplorerViewModel? explorer, Button? button)
    {
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
