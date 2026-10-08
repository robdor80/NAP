using System.Windows;
using NAP.Presentation;
namespace NAP.App;
public partial class MainWindow : Window
{
    private readonly TrayHost _tray;
    public MainWindow(ShellViewModel shell, bool smokeTest = false, string? smokeOutput = null, bool smokeImport = false, string? smokeNormalizationProfile = null)
    {
        var diagnostics = new BindingDiagnostics(); diagnostics.Attach();
        InitializeComponent(); DataContext = shell; _tray = new(this, shell);
        Closing += (_, e) => { if (shell.IsExecuting) { e.Cancel = true; MessageBox.Show(this, "Espera a que termine la operación explícita del Core.", "NAP", MessageBoxButton.OK, MessageBoxImage.Information); } };
        Closed += (_, _) => { _tray.Dispose(); diagnostics.Detach(); shell.Dispose(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && shell.MinimizeToTray) _tray.Hide(); };
        Loaded += async (_, _) =>
        {
            await shell.InitializeAsync();
            if (!smokeTest) return;
            try { await VisualSmokeProbe.RunAsync(this, shell.Explorer, shell, diagnostics, smokeOutput, smokeImport, smokeNormalizationProfile); Application.Current.Shutdown(shell.Explorer.State == ExplorerState.Error ? 2 : 0); }
            catch (Exception ex) { Console.WriteLine("smoke_failed: " + ex.Message); Application.Current.Shutdown(3); }
        };
    }
    private void HideToTray(object sender, RoutedEventArgs e) => _tray.Hide();
    internal void VerifyTrayRoundTrip()
    {
        _tray.Hide(); if (IsVisible || !_tray.IsVisible) throw new InvalidOperationException("Native tray hide failed.");
        _tray.Restore(); if (!IsVisible || _tray.IsVisible) throw new InvalidOperationException("Native tray restore failed.");
    }
}
