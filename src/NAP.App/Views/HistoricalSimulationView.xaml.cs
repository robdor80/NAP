using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;
namespace NAP.App.Views;
public partial class HistoricalSimulationView : UserControl
{
    public HistoricalSimulationView() { InitializeComponent(); if (App.IsAcceptance) HistoricalTransferNotice.Text = "Prueba en memoria: exportación a archivos no disponible."; }
    private async void ExportReport(object sender, RoutedEventArgs e)
    {
        if (App.IsAcceptance) { HistoricalTransferNotice.Text = "Aceptación en memoria: no se guardan archivos."; return; }
        var dialog = new SaveFileDialog { Title = "Exportar simulación sintética", Filter = "Informe de texto|*.txt",
            FileName = "NAP-Treskal-simulacion.txt", DefaultExt = ".txt", AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var report = ((HistoricalSimulationViewModel)DataContext).Report;
            var shell = (ShellViewModel)Window.GetWindow(this).DataContext;
            var roots = shell.Explorer.Configurations.ToArray();
            await Task.Run(() => DiagnosticReportWriter.Export(dialog.FileName, report, roots, System.IO.Path.GetDirectoryName(LicenseRegister.DefaultPath)!));
            HistoricalTransferNotice.Text = "Simulación exportada · sin homologación para producción";
        }
        catch (Exception) { HistoricalTransferNotice.Text = "Elige un archivo nuevo fuera del almacenamiento de NAP"; }
    }
}
