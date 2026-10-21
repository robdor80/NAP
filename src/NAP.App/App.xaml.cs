using System.Windows;
using Microsoft.Win32;
using NAP.Core;
using NAP.Presentation;
using NAP.AI;
using System.Net.Http;

namespace NAP.App;

public partial class App : Application
{
    private HttpClient? _auditTransport;
    private RemoteDesktopService? _remoteDesktop;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var automationConfig = e.Args.FirstOrDefault(a => a.StartsWith("--automation-config=", StringComparison.Ordinal))?[20..];
            if (automationConfig is not null)
            {
                var file = new System.IO.FileInfo(automationConfig); if (file.Length > 2 * 1024 * 1024) throw new System.IO.InvalidDataException("Host config exceeds its bound.");
                using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(file.FullName));
                var config = AutomationProtocol.Read<AutomationHostLaunchConfiguration>(document.RootElement); var contexts = config.Contexts();
                var client = new AutomationHostClient(config.Connection); _remoteDesktop = new(client, config.SyntheticAudit);
                var explorer = new ExplorerViewModel(contexts.Select(c => c.Profile), new(System.IO.Path.Combine(config.LaboratoryRoot, "desktop-unused-settings.json")),
                    _remoteDesktop, new FolderPicker(), hostConfigurations: contexts.Select(c => c.Storage).ToArray());
                var confirmation = new WpfConfirmation(); var controls = new AutomationControlViewModel(client, () => explorer.Context, confirmation);
                var remoteShell = new ShellViewModel(explorer, _remoteDesktop, confirmation, normalization: _remoteDesktop, automation: controls);
                var remoteOutput = e.Args.FirstOrDefault(a => a.StartsWith("--smoke-output=", StringComparison.Ordinal))?[15..];
                var remoteWindow = new MainWindow(remoteShell, smokeOutput: remoteOutput, automationSmoke: e.Args.Contains("--smoke-automation", StringComparer.Ordinal));
                MainWindow = remoteWindow; remoteWindow.Show(); return;
            }
            var settings = e.Args.FirstOrDefault(a => a.StartsWith("--settings=", StringComparison.Ordinal))?[11..] ?? LocalUniverseSettingsStore.DefaultPath;
            var smoke = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
            var localProfiles = smoke ? e.Args.FirstOrDefault(a => a.StartsWith("--profiles-root=", StringComparison.Ordinal))?[16..] ?? UniverseProfileStore.DefaultLocalRoot : UniverseProfileStore.DefaultLocalRoot;
            var registry = new UniverseProfileStore(System.IO.Path.Combine(AppContext.BaseDirectory, "config", "universes"), localProfiles);
            var snapshot = await Task.Run(() =>
            {
                IReadOnlyList<UniverseStorageConfig> roots;
                try { roots = new LocalUniverseSettingsStore(settings).Load(); }
                catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) { roots = []; }
                return registry.Discover(roots);
            });
            var vm = new ExplorerViewModel(snapshot.Installed.Select(p => p.Profile), new(settings), new ExplorerService(), new FolderPicker(), registry);
            IAiAuditClient? auditor = null;
            if (!smoke)
            {
                var key = Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY");
                var model = Environment.GetEnvironmentVariable("NAP_GEMINI_MODEL");
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(model))
                    auditor = new GeminiOperationAuditClient(_auditTransport = new HttpClient(), key, model);
            }
            var importSource = smoke ? e.Args.FirstOrDefault(a => a.StartsWith("--smoke-import-profile=", StringComparison.Ordinal))?[23..] : null;
            var shell = new ShellViewModel(vm, new ProfessionalUiService(auditor), new WpfConfirmation(), registry, new ProfilePicker(importSource), snapshot);
            var output = e.Args.FirstOrDefault(a => a.StartsWith("--smoke-output=", StringComparison.Ordinal))?[15..];
            var normalizationProfile = smoke ? e.Args.FirstOrDefault(a => a.StartsWith("--smoke-normalization-profile=", StringComparison.Ordinal))?[30..] : null;
            var window = new MainWindow(shell, smoke, output, importSource is not null, normalizationProfile); MainWindow = window; window.Show();
        }
        catch (Exception ex)
        {
            if (e.Args.Contains("--smoke-test", StringComparer.Ordinal)) { Console.Error.WriteLine(ex); Shutdown(1); return; }
            var error = ExplorerError.From(ex); MessageBox.Show(error.Message + "\nCódigo: " + error.Code, "NAP — Inicio detenido", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _auditTransport?.Dispose(); _remoteDesktop?.Dispose(); base.OnExit(e); }
    private sealed class ProfilePicker(string? diagnosticSource) : IProfileFilePicker
    {
        public string? Pick()
        {
            if (diagnosticSource is not null) return diagnosticSource;
            var dialog = new OpenFileDialog { Title = "Añadir perfil de universo", Filter = "Perfil de universo (JSON)|*.json", CheckFileExists = true, Multiselect = false };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
    private sealed class FolderPicker : IFolderPicker
    {
        public string? Pick(string title, string current)
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            if (System.IO.Directory.Exists(current)) dialog.InitialDirectory = current;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
