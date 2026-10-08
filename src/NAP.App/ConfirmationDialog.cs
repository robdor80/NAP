using System.Windows;
using System.Windows.Controls;
using NAP.Presentation;
namespace NAP.App;
internal sealed class WpfConfirmation : IUserConfirmation
{
    public Task<bool> ConfirmAsync(UiConfirmation confirmation, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var dialog = new ConfirmationDialog(confirmation) { Owner = Application.Current.MainWindow };
        var accepted = dialog.ShowDialog() == true;
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult(accepted);
    }
}
internal sealed class ConfirmationDialog : Window
{
    internal ConfirmationDialog(UiConfirmation plan)
    {
        Title = "NAP · " + plan.Title; Width = 720; Height = 560; MinWidth = 580; MinHeight = 420;
        Style = (Style)FindResource(typeof(Window));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        var execute = new Button { Content = "Confirmar operación", Style = (Style)FindResource("Danger") };
        execute.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(execute); DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        header.Children.Add(new TextBlock { Text = plan.Title, Style = (Style)FindResource("Heading") });
        header.Children.Add(new TextBlock { Text = "UNIVERSO: " + plan.UniverseId.Value, Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush"), Margin = new Thickness(0, 12, 0, 12) });
        header.Children.Add(new TextBlock { Text = plan.Explanation, Style = (Style)FindResource("Muted") });
        DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBox
        { Text = string.Join(Environment.NewLine, plan.Details), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true } });
        Content = body;
    }
}
