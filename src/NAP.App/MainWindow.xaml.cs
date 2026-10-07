using System.Windows;
using NAP.Presentation;

namespace NAP.App;

public partial class MainWindow : Window
{
    public MainWindow(ExplorerViewModel viewModel, bool smokeTest = false)
    {
        InitializeComponent(); DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
        Loaded += async (_, _) =>
        {
            await viewModel.InitializeAsync();
            if (!smokeTest) return;
            try
            {
                await VisualSmokeProbe.RunAsync(this, viewModel);
                Application.Current.Shutdown(viewModel.State == ExplorerState.Error ? 2 : 0);
            }
            catch (Exception ex) { Console.WriteLine("smoke_failed: " + ex.Message); Application.Current.Shutdown(3); }
        };
    }
}
