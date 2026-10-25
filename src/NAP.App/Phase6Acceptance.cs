using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAP.Core;
using NAP.Presentation;

namespace NAP.App;

/// <summary>Acceptance-only entry point. The published build excludes ordinary startup at compile time.
/// Interactive sessions have no persistent store, file picker, network transport or production service.
/// Only the explicitly requested CI self-test writes evidence, inside package/laboratorio.</summary>
internal static class Phase6Acceptance
{
    internal static void Start(App application, string[] args)
    {
        if (args.Distinct(StringComparer.Ordinal).Count() != args.Length ||
            args.Any(a => a is not ("--phase6-preview=acceptance" or "--phase6-self-test" or "--compact")))
            throw new ArgumentException("Este paquete solo admite aceptación sintética. Argumentos rechazados.");
        var probe = args.Contains("--phase6-self-test", StringComparer.Ordinal);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var lab = probe ? PrepareLab() : null;
        var window = new MainWindow(Phase6Preview.Create("exception"), smokeOutput: lab, acceptance: true, acceptanceProbe: probe)
        {
            Title = "NAP 2.0 — ACEPTACIÓN AISLADA — Datos ficticios — Sin producción",
            WindowState = WindowState.Normal, Width = 1440, Height = 900
        };
        if (args.Contains("--compact", StringComparer.Ordinal)) { window.Width = 1200; window.Height = 700; }
        application.MainWindow = window;
        window.Show();
    }

    // No caller-supplied path. Refuse junctions/symlinks, non-local paths and normal NAP locations.
    private static string PrepareLab()
    {
        var lab = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "laboratorio"));
        if (lab.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("El laboratorio debe ser local.");
        foreach (var forbidden in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NAP") })
            if (lab.StartsWith(Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Ubicación de laboratorio no permitida.");
        CheckAncestors(lab);
        Directory.CreateDirectory(lab);
        CheckAncestors(lab);
        return lab;
    }
    private static void CheckAncestors(string path)
    {
        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("El laboratorio no admite enlaces ni puntos de reanálisis.");
    }
    private static string EvidencePath(string lab, string name)
    {
        if (Path.GetFileName(name) != name) throw new IOException("Nombre de evidencia no válido.");
        CheckAncestors(lab);
        var file = Path.Combine(lab, name);
        if (File.Exists(file) || Directory.Exists(file)) throw new IOException("La evidencia debe ser nueva.");
        return file;
    }
    private static void WriteText(string lab, string name, string text)
    {
        using var stream = new FileStream(EvidencePath(lab, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream); writer.Write(text);
    }
    internal static async Task VerifyAsync(MainWindow window, ShellViewModel shell, BindingDiagnostics diagnostics, string lab)
    {
        void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        async Task Capture(string name)
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var content = (FrameworkElement)window.Content;
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new FileStream(EvidencePath(lab, name + ".png"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            Require(diagnostics.Errors == 0, "Errores de binding.");
        }
        Require(shell.Navigation.Count == 5 && shell.Profiles.Count == 2 && shell.Explorer.SettingsManagedByHost, "Contrato sintético inválido.");
        foreach (var item in shell.Navigation)
        {
            shell.Navigate.Execute(item.Name); await shell.LastRefresh;
            Require(shell.CurrentPage.Name == item.Name && shell.CurrentPage.State != UiPageState.Stopped, "Navegación fallida: " + item.Name);
            if (item.Name == "Producción" || item.Name == "Historial")
            {
                var work = item.Name == "Producción" ? shell.Pipeline.Work! : shell.History.Work!;
                Require(work.Rows.Count == 4, "Faltan filas sintéticas.");
                work.Selected = work.Rows[0]; await work.LastDetail;
                Require(work.HasPreview, "Falta comparación sintética.");
                await work.Approve.ExecuteAsync(); // Confirmation adapter always refuses; no writer exists.
                work.Search = "Escena"; await work.Apply.ExecuteAsync();
                Require(work.Rows.Count == 1, "Filtro sintético inválido.");
                work.Search = ""; await work.Apply.ExecuteAsync();
            }
            await Capture(item.Name);
        }
        foreach (var command in new[] { shell.SaveSettings, shell.ImportProfile, shell.PreparePackage, shell.PrepareNormalization,
            shell.ExecutePackage, shell.SaveObjective, shell.CreateBackup, shell.RestoreBackup, shell.RetainBackups,
            shell.InspectGit, shell.CommitGit, shell.PushGit, shell.RecoverGit, shell.RetryGit })
        { Require(!command.CanExecute(null), "Comando productivo disponible."); await command.ExecuteAsync(); }
        // The legacy explorer command independently rejects host-managed roots inside SaveAsync.
        foreach (var tool in shell.Tools)
        {
            shell.Navigate.Execute(tool.Name); await shell.LastRefresh;
            Require(shell.CurrentPage.State != UiPageState.Stopped, "Herramienta sintética fallida: " + tool.Name);
        }
        shell.Navigate.Execute("Ajustes"); await shell.LastRefresh;
        shell.Settings.Section = "Diagnóstico"; await shell.Settings.Diagnostics!.Refresh.ExecuteAsync();
        Require(shell.Settings.Diagnostics.Rows.Count == 4, "Falta diagnóstico sintético.");
        shell.Settings.Diagnostics.Severity = shell.Settings.Diagnostics.Severities.Single(s => s.Value == DiagnosticSeverity.Warning);
        await shell.Settings.Diagnostics.Refresh.ExecuteAsync(); Require(shell.Settings.Diagnostics.Rows.Count == 2, "Filtro diagnóstico inválido.");
        await Capture("Diagnostico");
        shell.Settings.Section = "Licencias"; await shell.Settings.Licenses!.CheckAsync();
        var license = shell.Settings.Licenses.Selected!;
        shell.Settings.Licenses.RenewalDate = license.Value.Expiry.AddYears(1).ToDateTime(TimeOnly.MinValue);
        await shell.Settings.Licenses.Renew.ExecuteAsync();
        Require(shell.Settings.Licenses.Selected!.Value.Expiry > license.Value.Expiry, "Renovación en memoria fallida.");
        await Capture("Licencias");
        shell.Settings.Section = "Histórico sintético";
        var historical = shell.Settings.Historical;
        Require(historical.Rows.Count == 39, "Falta histórico sintético.");
        historical.Search = "001"; Require(historical.Rows.Count == 1, "Búsqueda histórica inválida.");
        historical.Selected = historical.Rows[0]; Require(historical.HasSelection, "Falta detalle histórico.");
        await Capture("Historico");
        shell.SelectedUniverse = shell.Profiles.Last(); await shell.Explorer.UniverseChangeTask; await shell.LastRefresh;
        Require(historical.Rows.Count == 39 && !historical.HasSelection, "El contexto conserva selección anterior.");
        window.VerifyTrayRoundTrip(); await Capture("Bandeja-restaurada");
        Require(diagnostics.Errors == 0, "Errores de binding.");
        WriteText(lab, "aceptacion.json", JsonSerializer.Serialize(new { acceptanceOnlyBuild = App.IsAcceptance,
            navigation = shell.Navigation.Select(n => n.Name), tools = shell.Tools.Select(n => n.Name),
            syntheticDataOnly = true, persistentSessionStore = false, productionCommandsBlocked = true,
            noProfileStore = true, noLocalDiagnosticReader = true, noHostOrNetworkClient = true,
            trayRoundTrip = true, bindingErrors = diagnostics.Errors, evidenceRoot = lab }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("acceptance_probe_passed");
    }
}
