using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;
namespace NAP.App.Views;
public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
    private void OpenInbox(object sender, RoutedEventArgs e)
    {
        var path = (Window.GetWindow(this)?.DataContext as ShellViewModel)?.Automation?.InboxPath;
        try
        {
            if (path is null || !Directory.Exists(path)) throw new IOException("Entrada no está disponible. Revisa las ubicaciones en Ajustes.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error) { MessageBox.Show(Window.GetWindow(this), error.Message, "NAP · Entrada", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
