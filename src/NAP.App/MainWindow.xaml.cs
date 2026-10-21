using System.Windows;
using NAP.Presentation;
namespace NAP.App;
public partial class MainWindow : Window
{
    private readonly TrayHost _tray;
    public MainWindow(ShellViewModel shell, bool smokeTest = false, string? smokeOutput = null, bool smokeImport = false, string? smokeNormalizationProfile = null, bool automationSmoke = false, string? phase6Preview = null)
    {
        var diagnostics = new BindingDiagnostics(); diagnostics.Attach();
        InitializeComponent(); DataContext = shell; _tray = new(this, shell);
        if (phase6Preview is not null && Content is System.Windows.UIElement previewContent) previewContent.IsEnabled = false;
        var hostRefresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        hostRefresh.Tick += async (_, _) => { if (shell.Automation is not null) await shell.Automation.Refresh.ExecuteAsync(); };
        Closing += (_, e) => { if (shell.IsExecuting && shell.Automation is null) { e.Cancel = true; MessageBox.Show(this, "Espera a que termine la operación explícita del Core.", "NAP", MessageBoxButton.OK, MessageBoxImage.Information); } };
        Closed += (_, _) => { hostRefresh.Stop(); _tray.Dispose(); diagnostics.Detach(); shell.Dispose(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && shell.MinimizeToTray) _tray.Hide(); };
        Loaded += async (_, _) =>
        {
            await shell.InitializeAsync();
            if (phase6Preview is not null)
            {
                // Enable presentation for its capture; no input is dispatched before immediate shutdown.
                if (Content is System.Windows.UIElement captureContent) captureContent.IsEnabled = true;
                try { await Phase6Preview.CaptureAsync(this, diagnostics, phase6Preview, smokeOutput!); Application.Current.Shutdown(0); }
                catch (Exception error) { Console.Error.WriteLine("phase6_preview_failed:" + error); Application.Current.Shutdown(5); }
                return;
            }
            if (shell.Automation is not null) hostRefresh.Start();
            if (automationSmoke)
            {
                try { await AutomationSmokeProbe.RunAsync(this, shell, diagnostics, smokeOutput); Application.Current.Shutdown(0); }
                catch (Exception error) { Console.Error.WriteLine("automation_smoke_failed:" + error); Application.Current.Shutdown(4); }
                return;
            }
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
