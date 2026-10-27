using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;

namespace NAP.App;

internal static class Phase81GitLab
{
    internal static void Start(App application, string[] args)
    {
        // Validation precedes construction of any view/model. There is no production composition here.
        var checkOnly = args.Length == 3 && args[2] == "--f81-verify-isolation";
        var contract = GitVisualLabContract.Validate(checkOnly ? args[..2] : args, AppContext.BaseDirectory);
        if (checkOnly)
        {
            Console.Out.WriteLine("F81_ISOLATION_CONTRACT_OK; composition=synthetic-only; ipc=none; network=none; app-writes=none");
            application.Shutdown(0);
            return;
        }
        var model = new GitVisualLabViewModel(new GitVisualLabService(contract));
        var diagnostics = new BindingDiagnostics();
        diagnostics.Attach();
        var window = new Window {
            Title = "NAP 2.0 · F8.1 · LABORATORIO GIT · DATOS FICTICIOS · SOLO LECTURA",
            Width = 1440, Height = 900, MinWidth = 800, MinHeight = 600,
            DataContext = model, Background = (System.Windows.Media.Brush)application.FindResource("BackgroundBrush")
        };
        var layout = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(header, Dock.Top);
        var banner = new TextBlock { TextWrapping = TextWrapping.Wrap };
        banner.SetBinding(TextBlock.TextProperty, "Notice");
        header.Children.Add(banner);
        header.Children.Add(new TextBlock { Text = "Escenario ficticio (cambia únicamente la memoria):", Margin = new Thickness(0, 8, 0, 4) });
        var scenarios = new ComboBox { ItemsSource = model.Scenarios, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
        scenarios.SetBinding(ComboBox.SelectedItemProperty, new System.Windows.Data.Binding("SelectedScenario") { Mode = System.Windows.Data.BindingMode.TwoWay });
        header.Children.Add(scenarios);
        layout.Children.Add(header);
        // The production DataTemplate creates Views/GitView.xaml with its real controls and bindings.
        layout.Children.Add(new ContentControl { Content = model.Git,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch });
        window.Content = layout;
        window.Closed += (_, _) => {
            Console.Error.WriteLine("F81_WPF_BINDING_ERRORS=" + diagnostics.Errors);
            diagnostics.Detach();
            if (diagnostics.Errors != 0) application.Shutdown(8);
        };
        application.MainWindow = window;
        window.Show();
    }
}
