using System.Windows;
using NAP.AI;
using NAP.Core;

namespace NAP.App;

public partial class GeminiSettingsWindow : Window
{
    private readonly AutomationHostClient _host;
    private readonly PersonalGeminiSettings _settings = new();
    public GeminiSettingsWindow(AutomationHostClient host)
    {
        InitializeComponent(); _host = host;
        try { Model.Text = _settings.Load()?.Model ?? Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL") ?? ""; }
        catch { Result.Text = "No se pudo leer la configuración anterior. Introduce de nuevo clave y modelo."; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            var status = AutomationProtocol.Read<AutomationHostSnapshot>(await _host.CallAsync(AutomationOperation.Status, null, new { }, deadline.Token));
            if (status.Universes.Any(u => u.State is AutomationHostState.Running or AutomationHostState.Pausing))
            { Result.Text = "Pausa los universos desde Inicio y espera a que termine la llamada actual antes de aplicar Gemini."; return; }
            var key = ApiKey.Password;
            if (string.IsNullOrWhiteSpace(key)) key = _settings.Load()?.Key ?? Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY") ?? "";
            _settings.Save(key, Model.Text, Consent.IsChecked == true);
            ApiKey.Clear();
            var availability = AutomationProtocol.Read<AiAuditAvailability>(await _host.CallAsync(AutomationOperation.ReloadAuditConfiguration, null, new { }, deadline.Token));
            Result.Text = availability.Configured ? "Gemini configurado. Reanuda el universo desde Inicio. La conexión se comprobará al auditar el próximo recurso." : "Gemini no está disponible. Revisa clave, modelo y autorización.";
        }
        catch { Result.Text = "No se pudo aplicar Gemini. Comprueba clave, modelo, autorización y que el Host esté pausado. Si ya guardaste, vuelve a aplicar al terminar la llamada actual."; }
        finally { SaveButton.IsEnabled = true; }
    }
}
