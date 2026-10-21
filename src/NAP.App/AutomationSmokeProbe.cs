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
            trayVerified = true, rendered = true, settingsManagedByHost = shell.Explorer.SettingsManagedByHost }));
    }
}
