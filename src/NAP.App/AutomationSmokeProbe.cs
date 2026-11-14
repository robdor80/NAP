using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.Presentation;

namespace NAP.App;

internal static class AutomationSmokeProbe
{
    internal static async Task RunAsync(MainWindow window, ShellViewModel shell, BindingDiagnostics diagnostics, string? output)
    {
        if (shell.Automation is null || shell.Context is null) throw new InvalidOperationException("Remote shell is unconfigured.");
        await shell.Automation.RefreshAsync(); shell.NavigateTo("Historial"); await shell.LastRefresh;
        shell.NavigateTo("Producción"); await shell.LastRefresh;
        shell.NavigateTo("Catálogo"); await shell.LastRefresh;
        if (shell.Explorer.Assets is { Count: > 0 } assets)
        {
            await shell.Explorer.SelectAsync(((AssetTileViewModel)assets[0]!).Asset);
            await window.Dispatcher.InvokeAsync(() => { window.UpdateLayout(); }, DispatcherPriority.Render);
        }
        if (!Descendants(window).OfType<System.Windows.Controls.TextBlock>().Any(label => label.Text == window.Title &&
                label.Text.StartsWith("RobDev · NAP ", StringComparison.Ordinal)))
            throw new InvalidOperationException("The NAP footer does not show the window's installed version.");
        shell.NavigateTo("Auditor IA"); await shell.LastRefresh;
        shell.Diversity.Question = "¿Cuántos recursos hay?";
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
        var askButton = Descendants(window).OfType<System.Windows.Controls.Button>()
            .Single(button => Equals(button.Content, "Consultar IA"));
        var diversityAssets = shell.Diversity.Report?.TotalAssets ?? 0;
        var diversityEnabled = askButton.IsEnabled;
        if (diversityEnabled != (diversityAssets > 0 && shell.Diversity.AiAvailable))
            throw new InvalidOperationException("The diversity question button disagrees with the personal Host inventory.");
        if (diversityAssets > 0 && !Descendants(window).OfType<System.Windows.Controls.TextBlock>()
                .Any(label => label.Text == "Total del catálogo: " + shell.Diversity.Total))
            throw new InvalidOperationException("The diversity inventory count is missing from WPF.");
        shell.NavigateTo("Inicio"); await shell.LastRefresh;
        await window.Dispatcher.InvokeAsync(() => { window.UpdateLayout(); }, DispatcherPriority.Render);
        if (diagnostics.Errors != 0) throw new InvalidOperationException("WPF binding errors: " + diagnostics.Errors);
        window.VerifyTrayRoundTrip();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        if (image.PixelWidth < 1200 || image.PixelHeight < 700) throw new InvalidOperationException("Remote shell did not render.");
        if (output is not null)
        {
            System.IO.Directory.CreateDirectory(output);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = System.IO.File.Create(System.IO.Path.Combine(output, "automation-wpf.png")); encoder.Save(file);
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { automationConnected = true, bindingErrors = diagnostics.Errors,
            trayVerified = true, rendered = true, settingsManagedByHost = shell.Explorer.SettingsManagedByHost,
            diversityAssets, diversityEnabled }));
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
