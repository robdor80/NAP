using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace NAP.App.Views;
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private void ConfigureGemini_Click(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).PersonalHostClient is not { } client)
        { MessageBox.Show("Configura el Host personal para usar Gemini desde NAP.", "NAP"); return; }
        new GeminiSettingsWindow(client) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        // Solo lectura: el navegador de releases no instala ni ejecuta binarios.
        // Para el canal y hash verificados, NAP07.Update.ps1 -Action Check.
        try
        {
            var script = Path.Combine(AppContext.BaseDirectory, "updater", "NAP07.Update.ps1");
            if (!File.Exists(script))
            {
                MessageBox.Show("El paquete actual no incluye el actualizador NAP07.", "NAP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-File", script,
                "-Action", "Check", "-Channel", "stable" }) start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        catch (Exception error)
        {
            MessageBox.Show("No se pudo consultar el canal estable: " + error.Message, "NAP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
