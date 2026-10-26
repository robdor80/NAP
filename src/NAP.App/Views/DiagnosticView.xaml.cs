using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAP.Presentation;
namespace NAP.App.Views;
public partial class DiagnosticView : UserControl
{
    public DiagnosticView() { InitializeComponent(); if (App.IsAcceptance) TransferNotice.Text = "Prueba en memoria: exportación a archivos no disponible."; }
    private async void ShowLocal(object sender,RoutedEventArgs e) => await ((DiagnosticViewModel)DataContext).ShowLocalAsync();
    private void CopyReport(object sender,RoutedEventArgs e)
    {
        try { Clipboard.SetText(((DiagnosticViewModel)DataContext).Report); TransferNotice.Text="Informe copiado"; }
        catch(Exception) { TransferNotice.Text="No se pudo copiar el informe"; }
    }
    private async void ExportReport(object sender,RoutedEventArgs e)
    {
        if (App.IsAcceptance) { TransferNotice.Text = "Aceptación en memoria: no se guardan archivos."; return; }
        var dialog=new SaveFileDialog{Title="Exportar diagnóstico anonimizado",Filter="Informe de texto|*.txt",FileName=$"NAP-diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.txt",DefaultExt=".txt",AddExtension=true,OverwritePrompt=false};
        if(dialog.ShowDialog()!=true)return;
        try
        {
            // Only the explicit report destination is written; the source has no mutation API.
            var text=((DiagnosticViewModel)DataContext).Report;
            var shell=(ShellViewModel)Window.GetWindow(this).DataContext;
            var roots=shell.Explorer.Configurations.ToArray();
            await Task.Run(()=>NAP.Core.DiagnosticReportWriter.Export(dialog.FileName,text,roots,System.IO.Path.GetDirectoryName(NAP.Core.LicenseRegister.DefaultPath)!));
            TransferNotice.Text="Informe exportado";
        }
        catch(Exception){TransferNotice.Text="Elige un archivo nuevo fuera del almacenamiento de NAP";}
    }
}
