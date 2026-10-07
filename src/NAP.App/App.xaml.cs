using System.Windows;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var profile = UniverseProfileLoader.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "config", "universes", "nimroel", "profile.json"));
            var settings = e.Args.FirstOrDefault(a => a.StartsWith("--settings=", StringComparison.Ordinal))?[11..] ?? LocalUniverseSettingsStore.DefaultPath;
            var vm = new ExplorerViewModel([profile], new(settings), new ExplorerService(), new FolderPicker());
            var window = new MainWindow(vm, e.Args.Contains("--smoke-test", StringComparer.Ordinal)); MainWindow = window; window.Show();
        }
        catch (Exception ex)
        {
            var error = ExplorerError.From(ex); MessageBox.Show(error.Message + "\nCódigo: " + error.Code, "NAP — Inicio detenido", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private sealed class FolderPicker : IFolderPicker
    {
        public string? Pick(string title, string current)
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            if (System.IO.Directory.Exists(current)) dialog.InitialDirectory = current;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
